using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Netora.Domain.Customers;

namespace Netora.Application.Customers.Attachments;

public sealed record AttachmentDto(long Id, string FileName, string FileType, long SizeBytes, string UploadedBy,
    DateTimeOffset UploadedAtUtc);

public enum AttachmentOutcome
{
    Ok,

    /// <summary>The customer or attachment does not exist, or the caller may not see the customer.</summary>
    NotFound,

    /// <summary>Terminated customer and the caller is Staff: view and download only.</summary>
    CustomerLocked,
    EmptyFile,
    TooLarge,
    UnsupportedType,

    /// <summary>The row exists but the file is missing from the storage folder.</summary>
    FileMissing,
}

public sealed record AttachmentResult(AttachmentOutcome Outcome, AttachmentDto? Attachment = null);

public sealed record AttachmentDownload(AttachmentOutcome Outcome, Stream? Content = null, string? FileName = null,
    string? ContentType = null);

public interface ICustomerAttachmentService
{
    long MaxFileSizeBytes { get; }

    Task<AttachmentResult> UploadAsync(int customerId, string? fileName, long declaredLength, Stream content,
        CustomerActor actor, CancellationToken cancellationToken);

    Task<AttachmentDownload> OpenAsync(int customerId, long attachmentId, bool viewerIsManager,
        CancellationToken cancellationToken);

    Task<AttachmentOutcome> DeleteAsync(int customerId, long attachmentId, CustomerActor actor,
        CancellationToken cancellationToken);
}

/// <summary>
/// Attachment rules (03.05), agreed 2026-11-24: JPG, PNG and PDF; size limit from settings (default 10 MB); Staff and
/// Managers may upload and delete for customers they can see; for Terminated customers Staff may only view and
/// download. Deleting hides the attachment and keeps the stored file (retention, 03.04).
/// </summary>
internal sealed class CustomerAttachmentService(
    ICustomerAttachmentRepository repository,
    IAttachmentStore store,
    IOptions<AttachmentOptions> options,
    ILogger<CustomerAttachmentService> logger) : ICustomerAttachmentService
{
    public long MaxFileSizeBytes => options.Value.MaxFileSizeBytes;

    public async Task<AttachmentResult> UploadAsync(int customerId, string? fileName, long declaredLength, Stream content,
        CustomerActor actor, CancellationToken cancellationToken)
    {
        var customer = await repository.FindVisibleCustomerAsync(customerId, actor.IsManager, cancellationToken);
        if (customer is null) return new(AttachmentOutcome.NotFound);
        if (!customer.CanBeChangedBy(actor.IsManager)) return new(AttachmentOutcome.CustomerLocked);

        if (declaredLength == 0) return new(AttachmentOutcome.EmptyFile);
        if (declaredLength > MaxFileSizeBytes) return new(AttachmentOutcome.TooLarge);

        var cleanName = AttachmentFileTypes.CleanFileName(fileName);
        var header = new byte[AttachmentFileTypes.HeaderLength];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        if (read == 0) return new(AttachmentOutcome.EmptyFile);

        var type = AttachmentFileTypes.Detect(cleanName, header.AsSpan(0, read));
        if (type is null) return new(AttachmentOutcome.UnsupportedType);

        // Generated name only; the user's file name is never part of a path.
        var storedName = $"{customerId}/{Guid.NewGuid():N}{type.StoredExtension}";
        var size = await store.SaveAsync(storedName, header.AsMemory(0, read), content, MaxFileSizeBytes, cancellationToken);
        if (size is null) return new(AttachmentOutcome.TooLarge);

        var now = DateTimeOffset.UtcNow;
        var attachment = new CustomerAttachment
        {
            CustomerId = customerId,
            FileName = cleanName,
            StoredFileName = storedName,
            ContentType = type.ContentType,
            SizeBytes = size.Value,
            UploadedByUserId = actor.UserId,
            UploadedByUserName = actor.UserName,
            UploadedAtUtc = now,
        };

        try
        {
            await repository.AddAsync(attachment, Activity(customerId, "AttachmentUploaded", attachment, actor, now), cancellationToken);
        }
        catch
        {
            store.DeleteQuietly(storedName);   // no orphan file without a row
            throw;
        }

        // IDs only; file names can contain personal data (18-logging.md).
        logger.LogInformation("AttachmentUploaded CustomerId={CustomerId} AttachmentId={AttachmentId} Type={Type} Size={Size} ActorUserId={ActorUserId}",
            customerId, attachment.Id, type.Label, attachment.SizeBytes, actor.UserId);
        return new(AttachmentOutcome.Ok, AttachmentFileTypes.ToDto(attachment));
    }

    public async Task<AttachmentDownload> OpenAsync(int customerId, long attachmentId, bool viewerIsManager,
        CancellationToken cancellationToken)
    {
        if (await repository.FindVisibleCustomerAsync(customerId, viewerIsManager, cancellationToken) is null)
            return new(AttachmentOutcome.NotFound);

        var attachment = await repository.FindAsync(customerId, attachmentId, cancellationToken);
        if (attachment is null) return new(AttachmentOutcome.NotFound);

        var stream = store.OpenRead(attachment.StoredFileName);
        if (stream is null)
        {
            logger.LogError("AttachmentFileMissing CustomerId={CustomerId} AttachmentId={AttachmentId}", customerId, attachmentId);
            return new(AttachmentOutcome.FileMissing);
        }
        return new(AttachmentOutcome.Ok, stream, attachment.FileName, attachment.ContentType);
    }

    public async Task<AttachmentOutcome> DeleteAsync(int customerId, long attachmentId, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        var customer = await repository.FindVisibleCustomerAsync(customerId, actor.IsManager, cancellationToken);
        if (customer is null) return AttachmentOutcome.NotFound;

        var attachment = await repository.FindAsync(customerId, attachmentId, cancellationToken);
        if (attachment is null) return AttachmentOutcome.NotFound;
        if (!customer.CanBeChangedBy(actor.IsManager)) return AttachmentOutcome.CustomerLocked;

        var now = DateTimeOffset.UtcNow;
        attachment.IsDeleted = true;
        attachment.DeletedAtUtc = now;
        attachment.DeletedByUserId = actor.UserId;
        attachment.DeletedByUserName = actor.UserName;
        await repository.SaveDeletedAsync(attachment, Activity(customerId, "AttachmentDeleted", attachment, actor, now), cancellationToken);

        logger.LogInformation("AttachmentDeleted CustomerId={CustomerId} AttachmentId={AttachmentId} ActorUserId={ActorUserId}",
            customerId, attachmentId, actor.UserId);
        return AttachmentOutcome.Ok;
    }

    /// <summary>Activity detail names the type and size only, not the file name, which may contain personal data.</summary>
    private static CustomerActivity Activity(int customerId, string action, CustomerAttachment a, CustomerActor actor,
        DateTimeOffset at) => new()
    {
        CustomerId = customerId,
        Action = action,
        Detail = $"{AttachmentFileTypes.Label(a.ContentType)}, {AttachmentFileTypes.FormatSize(a.SizeBytes)}",
        ActorUserId = actor.UserId,
        ActorUserName = actor.UserName,
        Outcome = "Completed",
        OccurredAtUtc = at,
    };
}
