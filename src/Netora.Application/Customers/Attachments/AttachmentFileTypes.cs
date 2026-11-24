using Netora.Domain.Customers;

namespace Netora.Application.Customers.Attachments;

public sealed record AttachmentFileType(string Label, string ContentType, string StoredExtension, string[] Extensions, byte[] Signature);

/// <summary>
/// The accepted file types. A file is accepted only when its extension AND its first bytes both match the same type,
/// so a renamed executable or HTML page is rejected even with a ".pdf" name.
/// </summary>
public static class AttachmentFileTypes
{
    public static readonly AttachmentFileType[] All =
    [
        new("JPG", "image/jpeg", ".jpg", [".jpg", ".jpeg"], [0xFF, 0xD8, 0xFF]),
        new("PNG", "image/png", ".png", [".png"], [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
        new("PDF", "application/pdf", ".pdf", [".pdf"], "%PDF-"u8.ToArray()),
    ];

    /// <summary>Bytes needed to recognise every type.</summary>
    public static int HeaderLength => All.Max(t => t.Signature.Length);

    /// <summary>For the file picker's accept attribute and for messages.</summary>
    public static string AcceptList => string.Join(",", All.SelectMany(t => t.Extensions));

    public static string Labels => string.Join(", ", All.Select(t => t.Label));

    public static AttachmentFileType? Detect(string fileName, ReadOnlySpan<byte> header)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        foreach (var type in All)
        {
            if (type.Extensions.Contains(extension) && header.StartsWith(type.Signature))
                return type;
        }
        return null;
    }

    /// <summary>
    /// Makes a browser-supplied name safe to store and show: no folders, no control or path characters, at most
    /// 200 characters, and the extension kept.
    /// </summary>
    public static string CleanFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Where(c => !char.IsControl(c) && !invalid.Contains(c) && c != '/' && c != '\\').ToArray()).Trim();
        if (name.Length == 0 || name.StartsWith('.')) name = "file" + name;
        if (name.Length > 200)
        {
            var ext = Path.GetExtension(name);
            name = name[..(200 - ext.Length)] + ext;
        }
        return name;
    }

    public static AttachmentDto ToDto(CustomerAttachment a) => new(a.Id, a.FileName, Label(a.ContentType), a.SizeBytes,
        a.UploadedByUserName, a.UploadedAtUtc);

    public static string Label(string contentType)
        => All.FirstOrDefault(t => t.ContentType == contentType)?.Label ?? "File";

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };
}
