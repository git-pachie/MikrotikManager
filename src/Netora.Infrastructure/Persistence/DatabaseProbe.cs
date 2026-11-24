using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Netora.Application.Abstractions;

namespace Netora.Infrastructure.Persistence;

internal sealed class DatabaseProbe(NetoraDbContext dbContext, ILogger<DatabaseProbe> logger) : IDatabaseProbe
{
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Database connectivity check failed");
            return false;
        }
    }
}
