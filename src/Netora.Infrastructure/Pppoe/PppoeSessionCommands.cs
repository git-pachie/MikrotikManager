using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Netora.Application.Approvals;
using Netora.Application.Customers;
using Netora.Application.Pppoe;
using Netora.Application.Router;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Pppoe;

internal sealed class PppoeSessionCommands(NetoraDbContext db, IRouterClient router, IApprovalNotifier notifier,
    ILogger<PppoeSessionCommands> logger) : IPppoeSessionCommands
{
    public async Task<DisconnectPppoeResult> DisconnectAsync(int customerId, DisconnectPppoeRequest request,
        CustomerActor actor, CancellationToken ct)
    {
        if (!actor.IsManager) return new(DisconnectPppoeOutcome.Forbidden);
        var username = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId && c.PppoeDeletedAtUtc == null)
            .Select(c => c.PppoeUsername).SingleOrDefaultAsync(ct);
        if (username is null) return new(DisconnectPppoeOutcome.NotFound);
        if (request.SessionReference is not { Length: > 0 and <= 128 })
            return new(DisconnectPppoeOutcome.SessionChanged);

        // Once the command starts, keep its result and audit independent of the browser connection.
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        ct.ThrowIfCancellationRequested();
        var outcome = DisconnectPppoeOutcome.Disconnected;
        try
        {
            var current = await router.FindActiveSessionAsync(username, budget.Token);
            if (current is not { Id: { } id, Reference: { } reference }
                || !string.Equals(current.Name, username, StringComparison.Ordinal)
                || !string.Equals($"{router.Mode}:{reference}", request.SessionReference, StringComparison.Ordinal))
                return new(DisconnectPppoeOutcome.SessionChanged, router.Mode);

            await router.DisconnectPppoeSessionAsync(id, budget.Token);
        }
        catch (RouterException ex)
        {
            outcome = ex.Kind == RouterErrorKind.NotFound ? DisconnectPppoeOutcome.SessionChanged : DisconnectPppoeOutcome.RouterError;
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            outcome = DisconnectPppoeOutcome.RouterError;
        }

        var now = DateTimeOffset.UtcNow;
        var completed = outcome == DisconnectPppoeOutcome.Disconnected;
        db.CustomerActivities.Add(new CustomerActivity
        {
            CustomerId = customerId,
            Action = "PppoeSessionDisconnected",
            Detail = $"{router.Mode} · {outcome}",
            ActorUserId = actor.UserId,
            ActorUserName = actor.UserName,
            OccurredAtUtc = now,
            Outcome = completed ? "Completed" : "Failed",
        });
        try
        {
            using var logBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await db.SaveChangesAsync(logBudget.Token);
        }
        catch (Exception ex) when (ex is DbUpdateException or OperationCanceledException)
        {
            logger.LogError("PppoeSessionAuditFailed CustomerId={CustomerId} ActorUserId={ActorUserId} Outcome={Outcome} Mode={Mode}",
                customerId, actor.UserId, outcome, router.Mode);
        }

        if (completed)
            notifier.PppoeSessionDisconnected(new(customerId, username, actor.UserName, router.Mode, now));
        return new(outcome, router.Mode);
    }
}
