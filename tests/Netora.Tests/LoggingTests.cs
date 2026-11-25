using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Netora.Application.Customers;
using Netora.Application.Logging;
using Netora.Application.Pppoe;
using Netora.Domain.Security;
using Netora.Infrastructure.Identity;
using Netora.Infrastructure.Persistence;
using Netora.Infrastructure.Pppoe;
using Netora.Logging;

namespace Netora.Tests;

/// <summary>Module 18 file logging (18.07).</summary>
public sealed class LoggingTests
{
    private static readonly TimeZoneInfo Manila = TimeZoneInfo.CreateCustomTimeZone("Test+08", TimeSpan.FromHours(8), "Test+08", "Test+08");

    [Fact]
    public async Task Login_success_writes_exactly_one_info_line()
    {
        using var h = new LogHarness();
        await using (var db = new TestDb().NewContext())
            await new SecurityEventWriter(db, NullLogger<SecurityEventWriter>.Instance, h.Log)
                .WriteAsync(SecurityEventTypes.LoginSucceeded, true, "u1", "manager01", "192.168.7.20", null, default);

        var line = Assert.Single(h.Lines());
        Assert.Contains(" [INF] [Authentication] LoginSucceeded actor=manager01 ", line);
        Assert.Contains("target=User:u1 outcome=Success", line);
    }

    [Fact]
    public async Task Login_failure_writes_exactly_one_warning_line_with_the_typed_name_and_reason_class()
    {
        using var h = new LogHarness();
        await using (var db = new TestDb().NewContext())
            await new SecurityEventWriter(db, NullLogger<SecurityEventWriter>.Instance, h.Log)
                .WriteAsync(SecurityEventTypes.LoginFailed, false, null, "nobody here", "10.0.0.9", "UnknownUser", default);

        var line = Assert.Single(h.Lines());
        Assert.Contains(" [WRN] [Authentication] LoginFailed actor=\"nobody here\" ", line);
        Assert.Contains("outcome=Failure", line);
        Assert.Contains("detail=UnknownUser", line);
    }

    [Fact]
    public async Task Customer_create_update_and_terminate_log_ids_and_field_names_but_no_personal_data()
    {
        using var h = new LogHarness();
        using var t = new TestDb(new ActivityLogInterceptor(h.Log));
        var ids = t.Seed();
        int createdId;

        await using (var db = t.NewContext())
        {
            var created = await t.Commands(db).CreateAsync(new CreateCustomerRequest(
                "Zelda", "Quintero", "0917 555 0101", "zelda@example.com", "77 Kalachuchi St", "Gate code 1234",
                "zelda_q", "Fiber 50 Mbps", "NAP-01", null, null, false), TestDb.Manager, default);
            createdId = created.CustomerId!.Value;
        }

        await using (var db = t.NewContext())
        {
            var detail = (await t.Queries(db).GetDetailAsync(createdId, true, default))!;
            var edit = TestDb.EditOf(detail, r => r with { FirstName = "Zenaida", MobileNumber = "0918 444 0202", Address = "9 Ilang-Ilang Ave" });
            Assert.Equal(UpdateCustomerOutcome.Updated, (await t.Commands(db).UpdateAsync(createdId, edit, TestDb.Manager, default)).Outcome);
        }

        await using (var db = t.NewContext())
        {
            var r = await t.PppoeCommands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Terminate", "Moved to Cebu, owes nothing"),
                TestDb.Manager, default);
            Assert.Equal(PppoeOutcome.Ok, r.Outcome);
        }

        var lines = h.Lines();
        Assert.Single(lines, l => l.Contains("[Customers] CustomerCreated actor=manager01") && l.Contains($"target=Customer:{createdId}"));
        var updated = Assert.Single(lines, l => l.Contains("[Customers] CustomerUpdated"));
        Assert.Contains("First Name", updated);
        Assert.Contains("Mobile Number", updated);
        Assert.Single(lines, l => l.Contains("[Approvals] RequestSubmitted") && l.Contains($"Customer:{createdId}"));
        Assert.Single(lines, l => l.Contains("[Approvals] RequestSubmitted") && l.Contains($"TerminatePppoe Customer:{ids.Normal}"));
        Assert.Single(lines, l => l.Contains("[Approvals] RequestApproved") && l.Contains($"TerminatePppoe Customer:{ids.Normal}"));

        var all = string.Join("\n", lines);
        foreach (var personal in new[] { "Zelda", "Zenaida", "Quintero", "0917", "0918", "zelda@", "Kalachuchi", "Ilang", "Gate code",
                     "zelda_q", "Maria", "Santos", "maria_santos", "Test address", "Cebu" })
            Assert.DoesNotContain(personal, all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_value_with_line_breaks_produces_one_line()
    {
        using var h = new LogHarness();
        h.Log.Write(new ActivityEvent(ActivityCategories.Authentication, "LoginFailed", ActivityOutcomes.Failure,
            Actor: "evil\r\n2026-11-24 09:00:00.000 +08:00 [INF] [Users] UserCreated actor=admin\u2028x", Detail: "a\nb"));

        var line = Assert.Single(h.Lines());
        Assert.Contains("actor=\"evil2026-11-24 09:00:00.000 +08:00 [INF] [Users] UserCreated actor=adminx\"", line);
        Assert.Contains("detail=ab", line);
    }

    [Fact]
    public void Values_are_capped_at_256_characters()
    {
        using var h = new LogHarness();
        h.Log.Write(new ActivityEvent(ActivityCategories.Authentication, "LoginFailed", ActivityOutcomes.Failure, Actor: new string('a', 5000)));

        var line = Assert.Single(h.Lines());
        Assert.Contains("actor=" + new string('a', 256) + " ", line);
        Assert.DoesNotContain(new string('a', 257), line);
    }

    [Fact]
    public async Task No_password_token_or_connection_string_is_written()
    {
        const string password = "S3cret#Pppoe99";
        using var h = new LogHarness();
        using var t = new TestDb(new ActivityLogInterceptor(h.Log));
        var ids = t.Seed();

        await using (var db = t.NewContext())
        {
            var d = (await new PppoeQueries(db).GetAsync(ids.Pending, true, default))!;
            var r = await t.PppoeCommands(db).UpdateAsync(ids.Pending,
                new UpdatePppoeRequest(d.PppoeUsername, d.PlanName, password, null, null, null, d.ConcurrencyStamp), TestDb.Staff, default);
            Assert.Equal(PppoeOutcome.Ok, r.Outcome);
        }
        await using (var db = t.NewContext())
        {
            var events = new SecurityEventWriter(db, NullLogger<SecurityEventWriter>.Instance, h.Log);
            await events.WriteAsync(SecurityEventTypes.LoginFailed, false, null, "staff01", "10.0.0.9", "WrongPassword", default);
            await events.WriteAsync(SecurityEventTypes.SettingsUpdated, true, "m1", null, null, "Router settings, password changed", default);
        }

        var lines = h.Lines();
        Assert.Contains(lines, l => l.Contains("[PPPoE] PppoeUpdated actor=staff01") && l.Contains("Changed: Password"));
        var all = string.Join("\n", lines);
        Assert.DoesNotContain(password, all);
        Assert.DoesNotContain("Bearer ", all);
        Assert.DoesNotContain("eyJ", all); // JWT prefix
        Assert.DoesNotContain("Password=", all, StringComparison.OrdinalIgnoreCase); // connection string
        Assert.DoesNotContain("Server=", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_event_after_midnight_goes_to_the_next_dates_file()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 24, 23, 59, 59, 900, TimeSpan.FromHours(8)), Manila);
        using var h = new LogHarness(clock);
        h.Log.Write(new ActivityEvent(ActivityCategories.Customers, "CustomerUpdated", ActivityOutcomes.Success, "Customer:1"));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        h.Log.Write(new ActivityEvent(ActivityCategories.Customers, "CustomerUpdated", ActivityOutcomes.Success, "Customer:2"));
        h.Flush();

        var first = File.ReadAllLines(Path.Combine(h.Directory, "netora-test-2026-11-24.txt"));
        var second = File.ReadAllLines(Path.Combine(h.Directory, "netora-test-2026-11-25.txt"));
        Assert.StartsWith("2026-11-24 23:59:59.900 +08:00 [INF]", Assert.Single(first));
        Assert.Contains("Customer:1", first[0]);
        Assert.StartsWith("2026-11-25 00:00:00.100 +08:00 [INF]", Assert.Single(second));
        Assert.Contains("Customer:2", second[0]);
    }

    [Fact]
    public async Task A_failing_file_write_does_not_fail_the_request()
    {
        // A file where the log folder should be makes every write fail.
        var blocked = Path.Combine(Path.GetTempPath(), "netora-log-blocked-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(blocked, "not a folder");
        var errors = new List<string>();
        try
        {
            var provider = new DailyFileLoggerProvider(Path.Combine(blocked, "logs"), "netora-test", errorSink: errors.Add);
            using (var factory = LoggerFactory.Create(b => b.AddProvider(provider)))
            {
                var log = new ActivityLog(factory);
                using var t = new TestDb();
                await using var db = t.NewContext();
                await new SecurityEventWriter(db, NullLogger<SecurityEventWriter>.Instance, log)
                    .WriteAsync(SecurityEventTypes.LoginSucceeded, true, "u1", "manager01", null, null, default);
                log.Write(new ActivityEvent(ActivityCategories.Customers, "CustomerCreated", ActivityOutcomes.Success, "Customer:1"));

                // The work itself still completed.
                Assert.Single(db.SecurityEvents);
            }
            provider.Dispose();

            Assert.Contains(errors, e => e.Contains("could not write", StringComparison.Ordinal));
            Assert.Single(errors, e => e.Contains("could not write", StringComparison.Ordinal)); // once per failure streak
        }
        finally
        {
            File.Delete(blocked);
        }
    }

    [Fact]
    public void Ordinary_log_lines_use_the_same_layout_and_indent_exceptions()
    {
        using var h = new LogHarness();
        Exception error;
        try { throw new InvalidOperationException("boom\r\nforged line"); }
        catch (Exception ex) { error = ex; }
        h.Factory.CreateLogger("Microsoft.AspNetCore.Something").LogError(error, "Request failed for {Path}", "/x\ny");

        var lines = h.RawLines();
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} [+-]\d\d:\d\d \[ERR\] \[Application\] Log actor=anonymous outcome=- corr=- source=Microsoft.AspNetCore.Something msg=""Request failed for /xy""$", lines[0]);
        Assert.True(lines.Length > 1);
        Assert.All(lines.Skip(1), l => Assert.StartsWith("    ", l));
        Assert.Contains(lines, l => l.Contains("InvalidOperationException: boom"));
    }

    [Fact]
    public async Task An_unhandled_exception_is_logged_once_and_still_reaches_the_error_handler()
    {
        using var h = new LogHarness();
        var middleware = new UnhandledExceptionLoggingMiddleware(_ => throw new InvalidOperationException("db down"), h.Log);
        var http = new DefaultHttpContext();
        http.Request.Method = "GET";
        http.Request.Path = "/api/v1/customers";

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(http));

        var line = Assert.Single(h.Lines());
        Assert.Contains("[ERR] [Application] UnhandledException actor=anonymous target=\"GET /api/v1/customers\" outcome=Failure", line);
        Assert.Contains(h.RawLines(), l => l.StartsWith("    System.InvalidOperationException: db down"));
    }

    [Fact]
    public async Task Correlation_id_is_taken_from_the_header_only_when_safe()
    {
        Assert.Equal("abc-123", await Run("abc-123"));
        Assert.Matches("^[0-9a-f]{12}$", await Run("bad id\r\nx"));
        Assert.Matches("^[0-9a-f]{12}$", await Run(new string('a', 65)));

        static async Task<string> Run(string header)
        {
            var http = new DefaultHttpContext();
            http.Request.Headers[Correlation.HeaderName] = header;
            await new CorrelationIdMiddleware(_ => Task.CompletedTask).InvokeAsync(http);
            Assert.Equal(http.TraceIdentifier, Correlation.Get(http));
            Assert.Equal(http.TraceIdentifier, http.Response.Headers[Correlation.HeaderName].ToString());
            return http.TraceIdentifier;
        }
    }

    [Fact]
    public void Request_context_fills_actor_role_ip_and_correlation()
    {
        using var h = new LogHarness(context: new FixedContext(new ActivityContextSnapshot("staff01", "Staff", "192.168.7.20", "8f3a1c")));
        h.Log.Write(new ActivityEvent(ActivityCategories.Customers, "CustomerAccessDenied", ActivityOutcomes.Denied, "Customer:1042",
            "ManagerOnly", ActivityLevel.Warning));

        var line = Assert.Single(h.Lines());
        Assert.EndsWith("[WRN] [Customers] CustomerAccessDenied actor=staff01 role=Staff target=Customer:1042 outcome=Denied ip=192.168.7.20 corr=8f3a1c detail=ManagerOnly", line);
    }

    private sealed class FixedContext(ActivityContextSnapshot snapshot) : IActivityContext
    {
        public ActivityContextSnapshot Current => snapshot;
    }

    private sealed class FakeClock(DateTimeOffset start, TimeZoneInfo zone) : TimeProvider
    {
        private DateTimeOffset now = start.ToUniversalTime();

        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => zone;

        public void Advance(TimeSpan by) => now += by;
    }

    /// <summary>A file log in its own temporary folder, with the real provider and <see cref="ActivityLog"/>.</summary>
    private sealed class LogHarness : IDisposable
    {
        private readonly DailyFileLoggerProvider provider;
        private bool flushed;

        public LogHarness(TimeProvider? clock = null, IActivityContext? context = null)
        {
            Directory = Path.Combine(Path.GetTempPath(), "netora-log-tests", Guid.NewGuid().ToString("N"));
            provider = new DailyFileLoggerProvider(Directory, "netora-test", clock, context);
            Factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
            Log = new ActivityLog(Factory, context);
        }

        public string Directory { get; }

        public ILoggerFactory Factory { get; }

        public IActivityLog Log { get; }

        /// <summary>Writes everything queued and closes the file.</summary>
        public void Flush()
        {
            if (flushed) return;
            flushed = true;
            provider.Dispose();
        }

        /// <summary>Every line of every file, in file-name order.</summary>
        public string[] RawLines()
        {
            Flush();
            if (!System.IO.Directory.Exists(Directory)) return [];
            return System.IO.Directory.GetFiles(Directory, "*.txt").Order(StringComparer.Ordinal)
                .SelectMany(File.ReadAllLines).ToArray();
        }

        /// <summary>Event lines only (no indented exception lines).</summary>
        public string[] Lines() => RawLines().Where(l => !l.StartsWith(' ')).ToArray();

        public void Dispose()
        {
            Flush();
            Factory.Dispose();
            try { System.IO.Directory.Delete(Directory, recursive: true); } catch (IOException) { }
        }
    }
}
