using Netora.Application.Customers;
using Netora.Application.Pppoe;
using Netora.Application.Router;
using Netora.Domain.Approvals;

namespace Netora.Application.Approvals;

internal sealed class ApprovalCommands(IApprovalRepository approvals, IPppoeCommands pppoe, IRouterProvisioner provisioner) : IApprovalCommands
{
    public async Task<PppoeResult> DecideAsync(long requestId, DecidePppoeRequest decision, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        if (!actor.IsManager) return new(PppoeOutcome.Forbidden);
        var request = await approvals.FindByIdAsync(requestId, cancellationToken);
        if (request is null) return new(PppoeOutcome.NotFound);
        return await pppoe.DecideAsync(request.CustomerId, requestId, decision, actor, cancellationToken);
    }

    public async Task<RetryResult> RetryAsync(long requestId, CustomerActor actor, CancellationToken cancellationToken)
    {
        if (!actor.IsManager) return new(RetryOutcome.Forbidden);
        var request = await approvals.FindByIdAsync(requestId, cancellationToken);
        if (request is null) return new(RetryOutcome.NotFound);
        if (!CanRetry(request)) return new(RetryOutcome.NotRetryable);
        return new(RetryOutcome.Ok, await provisioner.ProvisionAsync(requestId, actor.UserId, actor.UserName, cancellationToken));
    }

    /// <summary>Only approved changes that did not reach the router yet. A success is never sent again.</summary>
    public static bool CanRetry(ApprovalRequest r)
        => r.Status == ApprovalStatus.Approved && r.ProvisioningStatus is ProvisioningStatus.NotSent or ProvisioningStatus.Failed;
}