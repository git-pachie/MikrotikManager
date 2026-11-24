using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Netora.Application.Customers.Attachments;

namespace Netora.Infrastructure.Customers;

/// <summary>
/// Stores attachment files under <see cref="AttachmentOptions.StoragePath"/>. Names come only from
/// <see cref="CustomerAttachmentService"/> ("{customerId}/{guid}.ext"); every path is still checked to stay inside the
/// root, as a second line of defence against path traversal.
/// </summary>
internal sealed class FileSystemAttachmentStore : IAttachmentStore
{
    private readonly string root;
    private readonly ILogger<FileSystemAttachmentStore> logger;

    public FileSystemAttachmentStore(IOptions<AttachmentOptions> options, IHostEnvironment environment,
        ILogger<FileSystemAttachmentStore> logger)
    {
        this.logger = logger;
        var configured = options.Value.StoragePath;
        root = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured));
        Directory.CreateDirectory(root);
    }

    public async Task<long?> SaveAsync(string storedFileName, ReadOnlyMemory<byte> header, Stream content, long maxBytes,
        CancellationToken cancellationToken)
    {
        var path = Resolve(storedFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".part";

        try
        {
            long written;
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await file.WriteAsync(header, cancellationToken);
                written = header.Length;
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    written += read;
                    if (written > maxBytes) break;
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            if (written > maxBytes)
            {
                File.Delete(temp);
                return null;
            }

            File.Move(temp, path);
            return written;
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    public Stream? OpenRead(string storedFileName)
    {
        var path = Resolve(storedFileName);
        return File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
    }

    public void DeleteQuietly(string storedFileName) => TryDelete(Resolve(storedFileName));

    private string Resolve(string storedFileName)
    {
        var full = Path.GetFullPath(Path.Combine(root, storedFileName));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Attachment path is outside the storage folder.");
        return full;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Could not remove temporary attachment file");
        }
    }
}
