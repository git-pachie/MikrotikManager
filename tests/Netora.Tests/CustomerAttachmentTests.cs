using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Netora.Application.Customers;
using Netora.Application.Customers.Attachments;
using Netora.Infrastructure.Customers;

namespace Netora.Tests;

public class CustomerAttachmentTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F'];
    private static readonly byte[] Pdf = "%PDF-1.7\n%test document\n"u8.ToArray();
    private static readonly byte[] Html = "<html><script>alert(1)</script></html>"u8.ToArray();

    private readonly string folder = Path.Combine(Path.GetTempPath(), "netora-tests-" + Guid.NewGuid().ToString("N"));

    private (TestDb Db, (int Normal, int ManagerOnly, int Pending, int Terminated) Ids) Setup()
    {
        var t = new TestDb();
        return (t, t.Seed());
    }

    private CustomerAttachmentService Service(Netora.Infrastructure.Persistence.NetoraDbContext db, long maxBytes = 1024)
    {
        var options = Options.Create(new AttachmentOptions { StoragePath = folder, MaxFileSizeBytes = maxBytes });
        var store = new FileSystemAttachmentStore(options, new TestHost(folder), NullLogger<FileSystemAttachmentStore>.Instance);
        return new CustomerAttachmentService(new CustomerAttachmentRepository(db), store, options,
            NullLogger<CustomerAttachmentService>.Instance);
    }

    private static Task<AttachmentResult> Upload(CustomerAttachmentService s, int customerId, string name, byte[] bytes,
        CustomerActor actor) => s.UploadAsync(customerId, name, bytes.Length, new MemoryStream(bytes), actor, default);

    [Theory]
    [InlineData("photo.png", "PNG")]
    [InlineData("scan.JPG", "JPG")]
    [InlineData("scan.jpeg", "JPG")]
    [InlineData("contract.pdf", "PDF")]
    public async Task Accepts_jpg_png_and_pdf_and_stores_the_file(string name, string expectedType)
    {
        var (t, ids) = Setup();
        await using var db = t.NewContext();
        var bytes = expectedType switch { "PNG" => Png, "JPG" => Jpeg, _ => Pdf };

        var result = await Upload(Service(db), ids.Normal, name, bytes, TestDb.Staff);

        Assert.Equal(AttachmentOutcome.Ok, result.Outcome);
        Assert.Equal(expectedType, result.Attachment!.FileType);
        Assert.Equal(bytes.Length, result.Attachment.SizeBytes);

        var row = await db.CustomerAttachments.SingleAsync();
        Assert.StartsWith(ids.Normal + "/", row.StoredFileName);
        Assert.DoesNotContain(Path.GetFileNameWithoutExtension(name), row.StoredFileName);   // user name never in the path
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(folder, row.StoredFileName)));

        var activity = await db.CustomerActivities.SingleAsync();
        Assert.Equal("AttachmentUploaded", activity.Action);
        Assert.DoesNotContain(Path.GetFileNameWithoutExtension(name), activity.Detail);       // no file name in history
    }

    [Theory]
    [InlineData("page.html")]   // not an allowed extension
    [InlineData("fake.pdf")]    // allowed extension, but the content is HTML
    [InlineData("fake.png")]
    public async Task Rejects_other_types_and_disguised_files(string name)
    {
        var (t, ids) = Setup();
        await using var db = t.NewContext();

        var result = await Upload(Service(db), ids.Normal, name, Html, TestDb.Manager);

        Assert.Equal(AttachmentOutcome.UnsupportedType, result.Outcome);
        Assert.Empty(await db.CustomerAttachments.ToListAsync());
    }

    [Fact]
    public async Task A_pdf_named_png_is_rejected()
    {
        var (t, ids) = Setup();
        await using var db = t.NewContext();
        Assert.Equal(AttachmentOutcome.UnsupportedType, (await Upload(Service(db), ids.Normal, "x.png", Pdf, TestDb.Manager)).Outcome);
    }

    [Fact]
    public async Task Rejects_files_over_the_configured_limit_and_leaves_nothing_behind()
    {
        var (t, ids) = Setup();
        await using var db = t.NewContext();
        var big = Pdf.Concat(new byte[2000]).ToArray();

        var declared = await Upload(Service(db, maxBytes: 1024), ids.Normal, "big.pdf", big, TestDb.Manager);

        // A client that under-reports the length is still stopped while the file is written.
        var lying = await Service(db, maxBytes: 1024).UploadAsync(ids.Normal, "big.pdf", 10, new MemoryStream(big), TestDb.Manager, default);

        Assert.Equal(AttachmentOutcome.TooLarge, declared.Outcome);
        Assert.Equal(AttachmentOutcome.TooLarge, lying.Outcome);
        Assert.Empty(await db.CustomerAttachments.ToListAsync());
        Assert.Empty(Directory.Exists(folder) ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories) : []);
    }

    [Fact]
    public async Task Rejects_an_empty_file()
    {
        var (t, ids) = Setup();
        await using var db = t.NewContext();
        Assert.Equal(AttachmentOutcome.EmptyFile, (await Upload(Service(db), ids.Normal, "e.pdf", [], TestDb.Manager)).Outcome);
    }

    [Fact]
    public async Task Path_characters_in_the_file_name_are_removed()
    {
        var (t, ids) = Setup();
        await using var db = t.NewContext();

        var result = await Upload(Service(db), ids.Normal, "../../etc/pass\u0001wd.pdf", Pdf, TestDb.Manager);

        Assert.Equal(AttachmentOutcome.Ok, result.Outcome);
        Assert.Equal("passwd.pdf", result.Attachment!.FileName);
        Assert.Single(Directory.GetFiles(folder, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Staff_cannot_upload_view_or_delete_for_a_manager_only_customer()
    {
        var (t, ids) = Setup();
        await using var db = t.NewContext();
        var uploaded = await Upload(Service(db), ids.ManagerOnly, "a.pdf", Pdf, TestDb.Manager);
        var id = uploaded.Attachment!.Id;

        Assert.Equal(AttachmentOutcome.NotFound, (await Upload(Service(db), ids.ManagerOnly, "b.pdf", Pdf, TestDb.Staff)).Outcome);
        Assert.Equal(AttachmentOutcome.NotFound, (await Service(db).OpenAsync(ids.ManagerOnly, id, false, default)).Outcome);
        Assert.Equal(AttachmentOutcome.NotFound, await Service(db).DeleteAsync(ids.ManagerOnly, id, TestDb.Staff, default));

        var asManager = await Service(db).OpenAsync(ids.ManagerOnly, id, true, default);
        Assert.Equal(AttachmentOutcome.Ok, asManager.Outcome);
        await asManager.Content!.DisposeAsync();
    }

    [Fact]
    public async Task An_attachment_id_from_another_customer_is_not_found()
    {
        var (t, ids) = Setup();
        await using var db = t.NewContext();
        var id = (await Upload(Service(db), ids.Normal, "a.pdf", Pdf, TestDb.Manager)).Attachment!.Id;

        Assert.Equal(AttachmentOutcome.NotFound, (await Service(db).OpenAsync(ids.Pending, id, true, default)).Outcome);
        Assert.Equal(AttachmentOutcome.NotFound, await Service(db).DeleteAsync(ids.Pending, id, TestDb.Manager, default));
    }

    [Fact]
    public async Task Terminated_customer_staff_can_view_but_not_upload_or_delete_managers_can_do_both()
    {
        var (t, ids) = Setup();
        await using var db = t.NewContext();
        var id = (await Upload(Service(db), ids.Terminated, "a.pdf", Pdf, TestDb.Manager)).Attachment!.Id;

        var staffOpen = await Service(db).OpenAsync(ids.Terminated, id, false, default);
        Assert.Equal(AttachmentOutcome.Ok, staffOpen.Outcome);
        await staffOpen.Content!.DisposeAsync();

        Assert.Equal(AttachmentOutcome.CustomerLocked, (await Upload(Service(db), ids.Terminated, "b.pdf", Pdf, TestDb.Staff)).Outcome);
        Assert.Equal(AttachmentOutcome.CustomerLocked, await Service(db).DeleteAsync(ids.Terminated, id, TestDb.Staff, default));
        Assert.Equal(AttachmentOutcome.Ok, await Service(db).DeleteAsync(ids.Terminated, id, TestDb.Manager, default));
    }

    [Fact]
    public async Task Delete_hides_the_attachment_but_keeps_the_record_and_the_file()
    {
        var (t, ids) = Setup();
        await using var db = t.NewContext();
        var id = (await Upload(Service(db), ids.Normal, "a.pdf", Pdf, TestDb.Staff)).Attachment!.Id;

        Assert.Equal(AttachmentOutcome.Ok, await Service(db).DeleteAsync(ids.Normal, id, TestDb.Staff, default));

        await using var check = t.NewContext();
        var detail = await t.Queries(check).GetDetailAsync(ids.Normal, viewerIsManager: false, default);
        Assert.Empty(detail!.Attachments);
        Assert.Contains(detail.Activity, a => a.Action == "AttachmentDeleted" && a.PerformedBy == "staff01");

        var row = await check.CustomerAttachments.SingleAsync();
        Assert.True(row.IsDeleted);
        Assert.Equal("staff-id", row.DeletedByUserId);
        Assert.True(File.Exists(Path.Combine(folder, row.StoredFileName)));
        Assert.Equal(AttachmentOutcome.NotFound, (await Service(db).OpenAsync(ids.Normal, id, true, default)).Outcome);
    }

    [Fact]
    public async Task Details_list_attachments_and_the_limit()
    {
        var (t, ids) = Setup();
        await using var db = t.NewContext();
        await Upload(Service(db), ids.Normal, "a.png", Png, TestDb.Staff);

        await using var check = t.NewContext();
        var detail = await t.Queries(check).GetDetailAsync(ids.Normal, viewerIsManager: false, default);
        var file = Assert.Single(detail!.Attachments);
        Assert.Equal("a.png", file.FileName);
        Assert.Equal("staff01", file.UploadedBy);
        Assert.Equal(10L * 1024 * 1024, detail.MaxAttachmentBytes);   // TestDb uses the default options
    }

    [Fact]
    public void The_store_refuses_paths_outside_its_folder()
    {
        var options = Options.Create(new AttachmentOptions { StoragePath = folder });
        var store = new FileSystemAttachmentStore(options, new TestHost(folder), NullLogger<FileSystemAttachmentStore>.Instance);
        Assert.Throws<InvalidOperationException>(() => store.OpenRead("../outside.pdf"));
    }

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    private sealed class TestHost(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Netora.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
