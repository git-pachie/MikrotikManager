using Microsoft.AspNetCore.Http;

namespace Netora.Logging;

/// <summary>
/// One correlation ID per request (12.03, 18.03), so a WebUI action can be followed into the API log. The WebUI sends
/// its ID to the API in the <see cref="HeaderName"/> header; anything that is not 1 to 64 letters, digits or '-' is
/// replaced with a new ID.
/// </summary>
public static class Correlation
{
    public const string HeaderName = "X-Correlation-ID";
    private const string ItemKey = "Netora.CorrelationId";

    public static string? Get(HttpContext? http)
        => http?.Items.TryGetValue(ItemKey, out var value) == true ? value as string : null;

    internal static void Set(HttpContext http, string id) => http.Items[ItemKey] = id;

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
        foreach (var ch in value)
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '-') return false;
        return true;
    }
}

/// <summary>
/// Sets the request's correlation ID, also as <see cref="HttpContext.TraceIdentifier"/> so the <c>traceId</c> in API
/// responses is the same value, and returns it in the response header. Register it first in the pipeline.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[Correlation.HeaderName].ToString();
        var id = Correlation.IsValid(incoming) ? incoming : Correlation.NewId();
        Correlation.Set(context, id);
        context.TraceIdentifier = id;
        context.Response.Headers[Correlation.HeaderName] = id;
        return next(context);
    }
}
