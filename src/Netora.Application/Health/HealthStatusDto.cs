namespace Netora.Application.Health;

/// <param name="Status">"Healthy" when the API and database are reachable, otherwise "Degraded".</param>
public sealed record HealthStatusDto(string Status, bool DatabaseReachable, DateTimeOffset CheckedAtUtc);
