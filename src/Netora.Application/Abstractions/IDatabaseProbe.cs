namespace Netora.Application.Abstractions;

/// <summary>Lets Application check database reachability without referencing EF Core.</summary>
public interface IDatabaseProbe
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
