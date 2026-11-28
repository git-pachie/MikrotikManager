using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Netora.Application.Approvals;
using Netora.Application.Logging;

namespace Netora.Infrastructure.Approvals;

public enum ApprovalNoticeKind
{
    Submitted,
    Decided,
    PppoeDeleted,
    PppoeCreationApproved,
    PppoeSessionDisconnected,
}

public sealed record QueuedApprovalNotice(ApprovalNoticeKind Kind, ApprovalNotice? Notice, string? RouterMode = null,
    string? Actor = null, ApprovedPppoeDetails? Details = null, PppoeDisconnectNotice? Disconnect = null)
{
    public string? Target => Notice is { } request ? ActivityTargets.Request(request.RequestId)
        : Disconnect is { } session ? ActivityTargets.Customer(session.CustomerId) : null;
}

/// <summary>
/// Queues approval emails for <see cref="ApprovalEmailDispatcher"/>, so a request returns without waiting for SMTP.
/// Queued emails are lost if the API stops before they are sent; that is logged, never retried silently.
/// </summary>
internal sealed class ApprovalEmailNotifier(ILogger<ApprovalEmailNotifier> logger) : IApprovalNotifier
{
    private readonly Channel<QueuedApprovalNotice> queue = Channel.CreateBounded<QueuedApprovalNotice>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ChannelReader<QueuedApprovalNotice> Reader => queue.Reader;

    public void RequestSubmitted(ApprovalNotice notice) => Enqueue(ApprovalNoticeKind.Submitted, notice);

    public void RequestDecided(ApprovalNotice notice) => Enqueue(ApprovalNoticeKind.Decided, notice);

    public void PppoeDeleted(ApprovalNotice notice, string routerMode, string actor)
        => Enqueue(ApprovalNoticeKind.PppoeDeleted, notice, routerMode, actor);

    public void PppoeCreationApproved(ApprovalNotice notice, ApprovedPppoeDetails details)
        => Enqueue(ApprovalNoticeKind.PppoeCreationApproved, notice, details: details);

    public void PppoeSessionDisconnected(PppoeDisconnectNotice notice)
        => Enqueue(new QueuedApprovalNotice(ApprovalNoticeKind.PppoeSessionDisconnected, null, Disconnect: notice));

    private void Enqueue(ApprovalNoticeKind kind, ApprovalNotice notice, string? routerMode = null, string? actor = null,
        ApprovedPppoeDetails? details = null)
        => Enqueue(new QueuedApprovalNotice(kind, notice, routerMode, actor, details));

    private void Enqueue(QueuedApprovalNotice item)
    {
        if (!queue.Writer.TryWrite(item))
            logger.LogWarning("ApprovalEmailDropped Target={Target} Kind={Kind} Reason=QueueFull", item.Target, item.Kind);
    }
}
