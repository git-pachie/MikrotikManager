namespace Netora.Application.Health;

public interface IHealthService
{
    Task<HealthStatusDto> GetStatusAsync(CancellationToken cancellationToken);
}
