using Netora.Domain.Security;

namespace Netora.Application.Abstractions;

public interface ISecurityEventWriter
{
    /// <summary>Persists a security event. Must not throw for logging problems.</summary>
    Task WriteAsync(string eventType, bool succeeded, string? userId, string? typedUserName,
        string? ipAddress, string? reason, CancellationToken cancellationToken);
}
