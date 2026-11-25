using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Netora.Logging;

namespace Netora.WebUI.Services;

/// <summary>Thrown when the API rejects the signed-in user's token (expired, revoked, account locked or disabled).</summary>
public sealed class ApiUnauthorizedException : Exception
{
    public ApiUnauthorizedException() : base("The Web API rejected the access token.") { }
}

/// <summary>Attaches the signed-in user's access token (stored in the auth cookie) to Web API calls.</summary>
public sealed class BearerTokenHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    public const string AccessTokenName = "access_token";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var token = httpContext is null ? null : await httpContext.GetTokenAsync(AccessTokenName);
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Same correlation ID in the WebUI and API log files, so one action can be followed across both (18.03).
        if (Correlation.Get(httpContext) is { } correlationId)
            request.Headers.TryAddWithoutValidation(Correlation.HeaderName, correlationId);

        var response = await base.SendAsync(request, cancellationToken);

        if (!string.IsNullOrEmpty(token) && response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            throw new ApiUnauthorizedException();
        }

        return response;
    }
}
