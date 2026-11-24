using Netora.Application.Abstractions;

namespace Netora.Application.Health;

internal sealed class HealthService(IDatabaseProbe databaseProbe) : IHealthService
{
    public async Task<HealthStatusDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        var databaseReachable = await databaseProbe.CanConnectAsync(cancellationToken);
        return new HealthStatusDto(
            databaseReachable ? "Healthy" : "Degraded",
            databaseReachable,
            DateTimeOffset.UtcNow);
    }
}
