using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Netora.Application.Router;

namespace Netora.Infrastructure.Router;

/// <summary>
/// RouterOS 7 REST API over HTTPS (09.01): GET reads, PUT creates, PATCH updates a record by ".id". All values are
/// strings in RouterOS JSON. Nothing is ever deleted. The server certificate must be trusted by Windows, or match the
/// SHA-256 fingerprint saved in Settings (for a router with a self-signed certificate).
/// </summary>
internal sealed partial class RouterOsRestClient(
    IRouterConnectionProvider connections,
    IOptions<RouterOptions> options,
    ILogger<RouterOsRestClient> logger) : IRouterClient
{
    public string Mode => RouterOptions.RouterOsMode;

    public async Task<RouterIdentity> GetIdentityAsync(CancellationToken cancellationToken)
    {
        var identity = await SendAsync(HttpMethod.Get, "system/identity", null, cancellationToken);
        var resource = await SendAsync(HttpMethod.Get, "system/resource", null, cancellationToken);
        return new RouterIdentity(Str(identity, "name") ?? "(no name)", Str(resource, "version"), Str(resource, "board-name"));
    }

    public async Task<IReadOnlyList<RouterSecret>> ListSecretsAsync(CancellationToken cancellationToken)
    {
        var list = await SendAsync(HttpMethod.Get, "ppp/secret", null, cancellationToken);
        return list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(ToSecret).ToList() : [];
    }

    public async Task<RouterSecret?> FindSecretAsync(string? id, string name, CancellationToken cancellationToken)
    {
        if (id is not null && IdPattern().IsMatch(id))
        {
            try
            {
                return ToSecret(await SendAsync(HttpMethod.Get, $"ppp/secret/{id}", null, cancellationToken));
            }
            catch (RouterException ex) when (ex.Kind == RouterErrorKind.NotFound)
            {
                // Removed or recreated on the router; fall back to the name.
            }
        }
        var byName = await SendAsync(HttpMethod.Get, "ppp/secret?name=" + Uri.EscapeDataString(name), null, cancellationToken);
        return byName.ValueKind == JsonValueKind.Array ? byName.EnumerateArray().Select(ToSecret).FirstOrDefault() : null;
    }

    public async Task<RouterSecret> CreateSecretAsync(RouterSecretWrite secret, CancellationToken cancellationToken)
        => ToSecret(await SendAsync(HttpMethod.Put, "ppp/secret", Body(secret, creating: true), cancellationToken));

    public async Task<RouterSecret> UpdateSecretAsync(string id, RouterSecretWrite secret, CancellationToken cancellationToken)
    {
        if (!IdPattern().IsMatch(id)) throw new RouterException(RouterErrorKind.Rejected, "The stored router ID is not valid.");
        return ToSecret(await SendAsync(HttpMethod.Patch, $"ppp/secret/{id}", Body(secret, creating: false), cancellationToken));
    }

    public async Task<RouterActiveSession?> FindActiveSessionAsync(string name, CancellationToken cancellationToken)
    {
        var list = await SendAsync(HttpMethod.Get, "ppp/active?name=" + Uri.EscapeDataString(name), null, cancellationToken);
        var first = list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().FirstOrDefault() : default;
        return first.ValueKind == JsonValueKind.Object
            ? new RouterActiveSession(Str(first, "name") ?? name, Str(first, "address"), Str(first, "uptime"), Str(first, "caller-id"))
            : null;
    }

    public async Task<IReadOnlyList<string>> ListProfilesAsync(CancellationToken cancellationToken)
    {
        var list = await SendAsync(HttpMethod.Get, "ppp/profile", null, cancellationToken);
        return list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(e => Str(e, "name")).OfType<string>().ToList()
            : [];
    }

    private static Dictionary<string, string> Body(RouterSecretWrite s, bool creating)
    {
        var body = new Dictionary<string, string>
        {
            ["name"] = s.Name,
            ["profile"] = s.Profile,
            ["disabled"] = s.Disabled ? "true" : "false",
            ["comment"] = s.Comment ?? string.Empty,
        };
        if (creating) body["service"] = "pppoe";
        if (s.Password is not null) body["password"] = s.Password;
        if (!string.IsNullOrWhiteSpace(s.RemoteAddress)) body["remote-address"] = s.RemoteAddress;
        if (!string.IsNullOrWhiteSpace(s.LocalAddress)) body["local-address"] = s.LocalAddress;
        return body;
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        var c = await connections.GetAsync(cancellationToken)
                ?? throw new RouterException(RouterErrorKind.NotConfigured, "The router address, user name or password is not set in Settings.");

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
                c.CertificateThumbprint is null
                    ? errors == SslPolicyErrors.None
                    // Pinned: only this exact certificate is accepted (typical for a router's self-signed certificate).
                    : cert is not null && string.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), c.CertificateThumbprint, StringComparison.OrdinalIgnoreCase),
        };
        using var http = new HttpClient(handler)
        {
            BaseAddress = new UriBuilder(Uri.UriSchemeHttps, c.Address, c.Port, "/rest/").Uri,
            Timeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds, 3, 60)),
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.UserName}:{c.Password}")));

        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            throw new RouterException(RouterErrorKind.Certificate,
                "The router's HTTPS certificate is not trusted. Install a trusted certificate on the router, or enter its SHA-256 fingerprint in Settings.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new RouterException(RouterErrorKind.Unreachable, $"The router at {c.Address}:{c.Port} could not be reached ({ex.Message}).", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RouterException(RouterErrorKind.Unreachable, $"The router at {c.Address}:{c.Port} did not answer in time.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
                return string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();

            var detail = ErrorDetail(text);
            logger.LogWarning("RouterCallFailed Method={Method} Path={Path} Status={Status} Detail={Detail}",
                method, path.Split('?')[0], (int)response.StatusCode, detail);
            throw response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new RouterException(RouterErrorKind.Unauthorized,
                    "The router refused the API user name or password, or the user has no REST/API permission."),
                HttpStatusCode.NotFound => new RouterException(RouterErrorKind.NotFound, "The router has no such record."),
                _ => new RouterException(RouterErrorKind.Rejected, "The router refused the change: " + (detail ?? response.ReasonPhrase)),
            };
        }
    }

    private static string? ErrorDetail(string text)
    {
        try
        {
            var e = JsonDocument.Parse(text).RootElement;
            return Str(e, "detail") ?? Str(e, "message");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static RouterSecret ToSecret(JsonElement e) => new(
        Str(e, ".id") ?? string.Empty, Str(e, "name") ?? string.Empty, Str(e, "password"), Str(e, "profile"),
        string.Equals(Str(e, "disabled"), "true", StringComparison.OrdinalIgnoreCase) || Str(e, "disabled") == "yes",
        Str(e, "comment"), Str(e, "remote-address"), Str(e, "local-address"), Str(e, "service"));

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [GeneratedRegex(@"^\*[0-9A-Fa-f]{1,16}$")]
    private static partial Regex IdPattern();
}
