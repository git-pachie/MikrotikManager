using System.ComponentModel.DataAnnotations;

namespace Netora.Application.Customers.Attachments;

/// <summary>
/// "Attachments" section of the Web API appsettings. Agreed 2026-11-24: storage folder and size limit are
/// configurable; allowed types are JPG, PNG and PDF.
/// </summary>
public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";

    /// <summary>
    /// Folder for uploaded files. A relative path is resolved against the Web API content root. Keep it outside any
    /// web root; files are only ever served through the authorized download endpoint.
    /// </summary>
    [Required]
    public string StoragePath { get; set; } = "App_Data/attachments";

    /// <summary>Largest accepted file, in bytes. Default 10 MB.</summary>
    [Range(1, 1024L * 1024 * 1024)]
    public long MaxFileSizeBytes { get; set; } = 10L * 1024 * 1024;
}
