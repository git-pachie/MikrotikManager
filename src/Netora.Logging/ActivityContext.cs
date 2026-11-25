using System.Net;
using Microsoft.AspNetCore.Http;
using Netora.Application.Auth;

namespace Netora.Logging;

/// <summary>Who and where for the current request. Values are cleaned again when the line is written.</summary>
public readonly record struct ActivityContextSnapshot(string? Actor, string? Role, string? Ip, string? CorrelationId);

public interface IActivityContext
{
    ActivityContextSnapshot Current { get; }
}

/// <summary>Reads the signed-in user, role, client IP and correlation ID of the current HTTP request.</summary>
public sealed class HttpActivityContext(IHttpContextAccessor accessor) : IActivityContext
{
    public ActivityContextSnapshot Current
    {
        get
        {
            var http = accessor.HttpContext;
            if (http is null) return default;

            var user = http.User;
            string? actor = null, role = null;
            if (user.Identity?.IsAuthenticated == true)
            {
                actor = user.Identity.Name;
                // Manager wins if an account somehow has both roles (same rule as the user list).
                role = user.IsInRole(Roles.Manager) ? Roles.Manager : user.IsInRole(Roles.Staff) ? Roles.Staff : null;
            }

            return new ActivityContextSnapshot(actor, role, Ip(http.Connection.RemoteIpAddress), Correlation.Get(http));
        }
    }

    private static string? Ip(IPAddress? address)
        => address is null ? null : (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
}
