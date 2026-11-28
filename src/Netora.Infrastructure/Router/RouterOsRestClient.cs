using System.Globalization;
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
/// strings in RouterOS JSON. The server certificate must be trusted by the host, or match the
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

    public async Task<IReadOnlyList<RouterSecret>> ListSecretsAsync(CancellationToken cancellationToken, bool includePasswords = true)
    {
        var path = includePasswords ? "ppp/secret" : "ppp/secret?.proplist=.id,name,comment";
        var list = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        if (list.ValueKind != JsonValueKind.Array)
            throw new RouterException(RouterErrorKind.Rejected, "The router returned an unexpected PPP secret list.");
        var rows = list.EnumerateArray().Select(ToSecret).ToList();
        if (!includePasswords && rows.Any(s => string.IsNullOrWhiteSpace(s.Name)))
            throw new RouterException(RouterErrorKind.Rejected, "The router returned an incomplete PPP secret list.");
        return rows;
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
        if (byName.ValueKind != JsonValueKind.Array)
            throw new RouterException(RouterErrorKind.Rejected, "The router returned an unexpected PPP secret response.");
        return byName.EnumerateArray().Select(ToSecret).FirstOrDefault();
    }

    public async Task<RouterSecret> CreateSecretAsync(RouterSecretWrite secret, CancellationToken cancellationToken)
        => ToSecret(await SendAsync(HttpMethod.Put, "ppp/secret", Body(secret, creating: true), cancellationToken));

    public async Task<RouterSecret> UpdateSecretAsync(string id, RouterSecretWrite secret, CancellationToken cancellationToken)
    {
        if (!IdPattern().IsMatch(id)) throw new RouterException(RouterErrorKind.Rejected, "The stored router ID is not valid.");
        return ToSecret(await SendAsync(HttpMethod.Patch, $"ppp/secret/{id}", Body(secret, creating: false), cancellationToken));
    }

    public async Task DeleteSecretAsync(string id, CancellationToken cancellationToken)
    {
        if (!IdPattern().IsMatch(id)) throw new RouterException(RouterErrorKind.Rejected, "The stored router ID is not valid.");
        await SendAsync(HttpMethod.Delete, $"ppp/secret/{id}", null, cancellationToken);
    }

    public async Task<RouterActiveSession?> FindActiveSessionAsync(string name, CancellationToken cancellationToken)
    {
        var sessions = await ReadSessionsAsync("&name=" + Uri.EscapeDataString(name), cancellationToken);
        return sessions.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
    }

    public Task<IReadOnlyList<RouterActiveSession>> ListActivePppoeSessionsAsync(CancellationToken cancellationToken)
        => ReadSessionsAsync("", cancellationToken);

    public async Task<PppoeTrafficCounters?> ReadPppoeTrafficAsync(string name, CancellationToken cancellationToken)
    {
        var bindings = await SendAsync(HttpMethod.Get,
            "interface/pppoe-server?running=true&.proplist=.id,name,user,running&user=" + Uri.EscapeDataString(name),
            null, cancellationToken);
        if (bindings.ValueKind != JsonValueKind.Array)
            throw new RouterException(RouterErrorKind.Rejected, "The router returned an unexpected PPPoE interface list.");

        decimal downloadBytes = 0;
        decimal uploadBytes = 0;
        var connections = new List<string>();
        foreach (var binding in bindings.EnumerateArray())
        {
            if (binding.ValueKind != JsonValueKind.Object || Str(binding, "user") is null || Str(binding, "running") is null)
                throw new RouterException(RouterErrorKind.Rejected, "The router returned an incomplete PPPoE interface.");
            if (!string.Equals(Str(binding, "user"), name, StringComparison.Ordinal) || Str(binding, "running") != "true")
                continue;

            var id = Str(binding, ".id");
            var interfaceName = Str(binding, "name");
            if (id is null || !IdPattern().IsMatch(id) || string.IsNullOrEmpty(interfaceName))
                throw new RouterException(RouterErrorKind.Rejected, "The router returned an invalid PPPoE interface.");

            var row = await SendAsync(HttpMethod.Get,
                $"interface/{id}?.proplist=.id,name,running,type,last-link-up-time,link-downs,tx-byte,rx-byte",
                null, cancellationToken);
            if (row.ValueKind != JsonValueKind.Object || Str(row, ".id") != id || Str(row, "name") != interfaceName
                || Str(row, "running") != "true" || Str(row, "type") != "pppoe-in"
                || !ulong.TryParse(Str(row, "tx-byte"), NumberStyles.None, CultureInfo.InvariantCulture, out var tx)
                || !ulong.TryParse(Str(row, "rx-byte"), NumberStyles.None, CultureInfo.InvariantCulture, out var rx))
                throw new RouterException(RouterErrorKind.Rejected, "The PPPoE traffic counters are unavailable.");

            // Router transmit is subscriber download. A new link invalidates comparisons with the previous counters.
            downloadBytes += tx;
            uploadBytes += rx;
            connections.Add($"{id}:{Str(row, "last-link-up-time")}:{Str(row, "link-downs")}");
        }

        if (connections.Count == 0) return null;
        connections.Sort(StringComparer.Ordinal);
        var connectionKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', connections))));
        return new(connectionKey, downloadBytes, uploadBytes);
    }

    private async Task<IReadOnlyList<RouterActiveSession>> ReadSessionsAsync(string filter, CancellationToken cancellationToken)
    {
        const string properties = "ppp/active?service=pppoe&.proplist=.id,name,service,address,uptime,caller-id,session-id";
        var list = await SendAsync(HttpMethod.Get, properties + filter, null, cancellationToken);
        if (list.ValueKind != JsonValueKind.Array)
            throw new RouterException(RouterErrorKind.Rejected, "The router returned an unexpected active session list.");

        var sessions = new List<RouterActiveSession>();
        foreach (var row in list.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(Str(row, "name")) || Str(row, "service") is null)
                throw new RouterException(RouterErrorKind.Rejected, "The router returned an incomplete active session record.");
            if (Str(row, "service") != "pppoe") continue;
            sessions.Add(new(Str(row, "name")!, Str(row, "address"), Str(row, "uptime"), Str(row, "caller-id"),
                Str(row, ".id"), Str(row, "session-id")));
        }
        return sessions;
    }

    public async Task DisconnectPppoeSessionAsync(string id, CancellationToken cancellationToken)
    {
        if (!IdPattern().IsMatch(id)) throw new RouterException(RouterErrorKind.Rejected, "The router session ID is invalid.");
        await SendAsync(HttpMethod.Delete, $"ppp/active/{id}", null, cancellationToken);
    }

    public async Task<IReadOnlyList<RouterPppoeLog>> ReadPppoeLogsAsync(string name, CancellationToken cancellationToken)
    {
        var rows = await SendAsync(HttpMethod.Get, "log?.proplist=time,topics,message", null, cancellationToken);
        if (rows.ValueKind != JsonValueKind.Array)
            throw new RouterException(RouterErrorKind.Rejected, "The router returned an unexpected log response.");

        return rows.EnumerateArray().Select(row => ParsePppoeLog(name, Str(row, "topics"),
                Str(row, "message"), Str(row, "time")))
            .OfType<RouterPppoeLog>().TakeLast(25).Reverse().ToList();
    }

    // Attribute known message formats to the exact username. Return event labels only, never free-form log text.
    internal static RouterPppoeLog? ParsePppoeLog(string name, string? topics, string? message, string? time)
    {
        var kinds = topics?.Split(',', StringSplitOptions.TrimEntries);
        if (kinds is null || (!kinds.Contains("ppp") && !kinds.Contains("pppoe")) || message is null) return null;

        var prefix = $"<pppoe-{name}>: ";
        var eventName = message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..] switch
        {
            "authenticated" => "Authenticated",
            "connected" => "Connected",
            "disconnected" => "Disconnected",
            "authentication failed" => "Authentication failed",
            _ => null,
        } : message == $"user {name} authentication failed" ? "Authentication failed" : null;
        if (eventName is null) return null;
        var displayTime = time is { Length: > 0 and <= 32 }
            && time.All(c => char.IsAsciiDigit(c) || c is '-' or ':' or '.' or ' ' or '+' or 'T' or 'Z')
            ? time : null;
        return new(displayTime, eventName);
    }

    public async Task<IReadOnlyList<RouterPppProfile>> ListProfilesAsync(CancellationToken cancellationToken)
    {
        var list = await SendAsync(HttpMethod.Get, "ppp/profile", null, cancellationToken);
        return list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(ToProfile).Where(p => p.Name.Length > 0).ToList()
            : [];
    }

    public async Task<RouterPppProfile> CreateProfileAsync(string name, string rateLimit, CancellationToken cancellationToken)
        => ToProfile(await SendAsync(HttpMethod.Put, "ppp/profile",
            new Dictionary<string, string> { ["name"] = name, ["rate-limit"] = rateLimit }, cancellationToken));

    public async Task<RouterPppProfile> UpdateProfileRateLimitAsync(string id, string rateLimit, CancellationToken cancellationToken)
    {
        if (!IdPattern().IsMatch(id)) throw new RouterException(RouterErrorKind.Rejected, "The router profile ID is not valid.");
        return ToProfile(await SendAsync(HttpMethod.Patch, $"ppp/profile/{id}",
            new Dictionary<string, string> { ["rate-limit"] = rateLimit }, cancellationToken));
    }

    private static RouterPppProfile ToProfile(JsonElement e)
        => new(Str(e, ".id") ?? string.Empty, Str(e, "name") ?? string.Empty, Str(e, "rate-limit"),
            Str(e, "local-address"), Str(e, "remote-address"));

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

        var certificateRejected = false;
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, cert, _, errors) =>
            {
                var trusted = c.CertificateThumbprint is null
                    ? errors == SslPolicyErrors.None
                    : cert is not null && string.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), c.CertificateThumbprint, StringComparison.OrdinalIgnoreCase);
                certificateRejected = !trusted;
                return trusted;
            },
        };
        using var http = new HttpClient(handler)
        {
            BaseAddress = new UriBuilder(Uri.UriSchemeHttps, c.Address, c.Port, "/rest/").Uri,
            Timeout = TimeSpan.FromSeconds(Math.Clamp(options.Value.TimeoutSeconds, 3, 60)),
        };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{c.UserName}:{c.Password}")));

        using var request = new HttpRequestMessage(method, path);
        // Buffered with a Content-Length: RouterOS's REST server does not read chunked request bodies and answers
        // "Request is empty" (found against RouterOS 7.19.4). JsonContent.Create streams, so it is not used here.
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex) when (certificateRejected)
        {
            throw new RouterException(RouterErrorKind.Certificate,
                "The router's HTTPS certificate is not trusted. Install a trusted certificate on the router, or enter its SHA-256 fingerprint in Settings.", ex);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.SecureConnectionError
                                              || ex.InnerException is AuthenticationException)
        {
            throw new RouterException(RouterErrorKind.Unreachable,
                $"HTTPS negotiation with the router at {c.Address}:{c.Port} failed. Check that RouterOS www-ssl is enabled on this port and allows the Netora server.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new RouterException(RouterErrorKind.Unreachable, $"The router at {c.Address}:{c.Port} could not be reached ({ex.Message}).", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RouterException(RouterErrorKind.Timeout, $"The router at {c.Address}:{c.Port} did not answer in time.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(text)) return default;
                try
                {
                    using var json = JsonDocument.Parse(text);
                    return json.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    throw new RouterException(RouterErrorKind.Rejected, "The router returned invalid JSON.", ex);
                }
            }

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
