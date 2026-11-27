namespace Netora.Domain.Security;

public sealed class RevokedAccessToken
{
    public Guid Id { get; init; }
    public DateTimeOffset RetainUntilUtc { get; init; }
}
