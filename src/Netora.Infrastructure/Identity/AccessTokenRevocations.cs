using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Netora.Domain.Security;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Identity;

public sealed class AccessTokenRevocations(NetoraDbContext db, ILogger<AccessTokenRevocations> logger)
{
    public Task<bool> ContainsAsync(Guid id, CancellationToken cancellationToken)
        => db.RevokedAccessTokens.AnyAsync(token => token.Id == id, cancellationToken);

    public async Task RevokeAsync(Guid id, DateTimeOffset retainUntilUtc, CancellationToken cancellationToken)
    {
        var entry = db.RevokedAccessTokens.Add(new RevokedAccessToken { Id = id, RetainUntilUtc = retainUntilUtc });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Another logout request revoked this token first.
            entry.State = EntityState.Detached;
        }

        if (db.Database.IsRelational())
        {
            var now = DateTimeOffset.UtcNow;
            try
            {
                await db.RevokedAccessTokens.Where(token => token.RetainUntilUtc < now)
                    .ExecuteDeleteAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not remove expired token revocations.");
            }
        }
    }
}
