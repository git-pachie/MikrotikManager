using Netora.Application.Customers;
using Netora.Application.Pppoe;

namespace Netora.Application.Approvals;

internal sealed class ApprovalCommands(IApprovalRepository approvals, IPppoeCommands pppoe) : IApprovalCommands
{
    public async Task<PppoeResult> DecideAsync(long requestId, DecidePppoeRequest decision, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        if (!actor.IsManager) return new(PppoeOutcome.Forbidden);
        var request = await approvals.FindByIdAsync(requestId, cancellationToken);
        if (request is null) return new(PppoeOutcome.NotFound);
        return await pppoe.DecideAsync(request.CustomerId, requestId, decision, actor, cancellationToken);
    }
}
