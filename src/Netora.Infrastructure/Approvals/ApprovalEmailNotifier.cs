using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Netora.Application.Approvals;

namespace Netora.Infrastructure.Approvals;

public enum ApprovalNoticeKind
{
    Submitted,
    Decided,
}

public sealed record QueuedApprovalNotice(ApprovalNoticeKind Kind, ApprovalNotice Notice);

/// <summary>
/// Queues approval emails for <see cref="ApprovalEmailDispatcher"/>, so a request returns without waiting for SMTP.
/// Queued emails are lost if the API stops before they are sent; that is logged, never retried silently.
/// </summary>
internal sealed class ApprovalEmailNotifier(ILogger<ApprovalEmailNotifier> logger) : IApprovalNotifier
{
    private readonly Channel<QueuedApprovalNotice> queue = Channel.CreateBounded<QueuedApprovalNotice>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<QueuedApprovalNotice> Reader => queue.Reader;

    public void RequestSubmitted(ApprovalNotice notice) => Enqueue(ApprovalNoticeKind.Submitted, notice);

    public void RequestDecided(ApprovalNotice notice) => Enqueue(ApprovalNoticeKind.Decided, notice);

    private void Enqueue(ApprovalNoticeKind kind, ApprovalNotice notice)
    {
        if (!queue.Writer.TryWrite(new QueuedApprovalNotice(kind, notice)))
            logger.LogWarning("ApprovalEmailDropped RequestId={RequestId} Kind={Kind} Reason=QueueFull", notice.RequestId, kind);
    }
}
