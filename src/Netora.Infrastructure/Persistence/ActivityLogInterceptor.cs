using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Netora.Application.Logging;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;

namespace Netora.Infrastructure.Persistence;

/// <summary>
/// Writes the Customers, PPPoE and Approvals events of 18.02 to the file log once they are saved:
/// <list type="bullet">
/// <item>New <see cref="CustomerActivity"/> rows: customer created and updated, PPPoE details updated, attachments.
/// Only CustomerUpdated and PppoeUpdated carry a detail, and that is the changed field names.</item>
/// <item>New and decided <see cref="ApprovalRequest"/>s: RequestSubmitted, RequestApproved, RequestRejected.</item>
/// </list>
/// Customer IDs only; never names, phone numbers, addresses, reasons or notes. Router results are logged by the
/// provisioner itself.
/// </summary>
internal sealed class ActivityLogInterceptor(IActivityLog activity) : SaveChangesInterceptor
{
    private readonly List<Func<ActivityEvent?>> pending = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Emit();
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        Emit();
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => pending.Clear();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        pending.Clear();
        return Task.CompletedTask;
    }

    private void Capture(DbContext? context)
    {
        pending.Clear();
        if (context is null) return;
        try
        {
            foreach (var entry in context.ChangeTracker.Entries())
            {
                switch (entry.Entity)
                {
                    case CustomerActivity a when entry.State == EntityState.Added:
                        // Read after the save, when the customer ID is final.
                        pending.Add(() => FromCustomerActivity(a));
                        break;
                    case ApprovalRequest r when entry.State == EntityState.Added:
                        pending.Add(() => Request(r, "RequestSubmitted", ActivityOutcomes.Pending));
                        if (r.Status == ApprovalStatus.Approved) pending.Add(() => Request(r, "RequestApproved", ActivityOutcomes.Success));
                        break;
                    case ApprovalRequest r when entry.State == EntityState.Modified && StatusDecided(entry):
                        pending.Add(() => r.Status == ApprovalStatus.Approved
                            ? Request(r, "RequestApproved", ActivityOutcomes.Success)
                            : Request(r, "RequestRejected", ActivityOutcomes.Rejected));
                        break;
                }
            }
        }
        catch
        {
            // Logging must never break a save.
            pending.Clear();
        }
    }

    private void Emit()
    {
        if (pending.Count == 0) return;
        var events = pending.ToList();
        pending.Clear();
        foreach (var make in events)
        {
            try
            {
                if (make() is { } e) activity.Write(e);
            }
            catch
            {
                // Logging must never break a save.
            }
        }
    }

    private static bool StatusDecided(EntityEntry entry)
    {
        var status = entry.Property(nameof(ApprovalRequest.Status));
        return status.IsModified
               && status.OriginalValue is ApprovalStatus.Pending
               && status.CurrentValue is ApprovalStatus.Approved or ApprovalStatus.Rejected;
    }

    private static ActivityEvent Request(ApprovalRequest r, string name, string outcome)
        => new(ActivityCategories.Approvals, name, outcome, ActivityTargets.Request(r.Id),
            $"{r.Operation} {ActivityTargets.Customer(r.CustomerId)}");

    internal static ActivityEvent? FromCustomerActivity(CustomerActivity a)
    {
        var target = ActivityTargets.Customer(a.CustomerId);
        var outcome = a.Outcome == "Completed" ? ActivityOutcomes.Success : a.Outcome;
        return a.Action switch
        {
            // Detail is "Changed: <field names>" only (never values), see CustomerCommands and PppoeCommands.
            "CustomerUpdated" => new(ActivityCategories.Customers, a.Action, outcome, target, a.Detail, Actor: a.ActorUserName),
            "PppoeUpdated" => new(ActivityCategories.Pppoe, a.Action, outcome, target, a.Detail, Actor: a.ActorUserName),
            "CustomerCreated" or "AttachmentUploaded" or "AttachmentDeleted" or "OnuReplaced"
                => new(ActivityCategories.Customers, a.Action, outcome, target, Actor: a.ActorUserName),
            "CustomerImported" => new(ActivityCategories.Pppoe, a.Action, outcome, target, Actor: a.ActorUserName),
            // Approval steps come from the ApprovalRequest rows; router results from RouterProvisioner.
            _ => null,
        };
    }
}
