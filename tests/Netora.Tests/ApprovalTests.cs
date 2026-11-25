using Microsoft.EntityFrameworkCore;
using Netora.Application.Approvals;
using Netora.Application.Pppoe;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Approvals;
using Netora.Infrastructure.Persistence;
using Netora.Infrastructure.Pppoe;

namespace Netora.Tests;

public class ApprovalTests
{
    private static ApprovalCommands Approvals(TestDb t, NetoraDbContext db) => new(new ApprovalRepository(db), t.PppoeCommands(db), t.Provisioner);

    private static async Task<ApprovalRequest> PendingOf(TestDb t, int customerId)
    {
        await using var db = t.NewContext();
        return await db.ApprovalRequests.SingleAsync(r => r.CustomerId == customerId && r.Status == ApprovalStatus.Pending);
    }

    private static async Task<Customer> CustomerOf(TestDb t, int id)
    {
        await using var db = t.NewContext();
        return await db.Customers.SingleAsync(c => c.Id == id);
    }

    private static async Task<int> CreateCustomerAsync(TestDb t, string username = "new_user", bool asManager = false)
    {
        await using var db = t.NewContext();
        var result = await t.Commands(db).CreateAsync(TestDb.NewCustomer(username), asManager ? TestDb.Manager : TestDb.Staff, default);
        return result.CustomerId!.Value;
    }

    private static async Task SetPasswordAsync(TestDb t, int customerId)
    {
        await using var db = t.NewContext();
        var d = (await new PppoeQueries(db).GetAsync(customerId, true, default))!;
        var r = await t.PppoeCommands(db).UpdateAsync(customerId,
            new UpdatePppoeRequest(d.PppoeUsername, d.PlanName, "Secret#123", null, null, null, d.ConcurrencyStamp), TestDb.Staff, default);
        Assert.Equal(PppoeOutcome.Ok, r.Outcome);
    }

    // ----- 08.01 creation requests -----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Adding_a_customer_opens_a_pending_creation_request_also_for_managers(bool asManager)
    {
        var t = new TestDb();
        var id = await CreateCustomerAsync(t, asManager: asManager);

        var request = await PendingOf(t, id);
        Assert.Equal(ApprovalOperation.CreatePppoe, request.Operation);
        Assert.Equal("Not created", request.Before);
        Assert.Equal("Enabled, username new_user, plan Fiber 50 Mbps", request.After);
        Assert.Equal(ProvisioningStatus.NotApplicable, request.ProvisioningStatus);
        Assert.Equal(CustomerStatus.PendingApproval, (await CustomerOf(t, id)).Status);
        Assert.Single(t.Notifier.Submitted);
        Assert.Equal(ApprovalOperation.CreatePppoe, t.Notifier.Submitted[0].Operation);
    }

    [Fact]
    public async Task Creation_is_approved_only_after_a_password_is_set()
    {
        var t = new TestDb();
        var id = await CreateCustomerAsync(t);
        var request = await PendingOf(t, id);

        await using (var db = t.NewContext())
        {
            var blocked = await Approvals(t, db).DecideAsync(request.Id, new DecidePppoeRequest(true, null), TestDb.Manager, default);
            Assert.Equal(PppoeOutcome.PasswordRequired, blocked.Outcome);
        }

        await SetPasswordAsync(t, id);
        await using (var db = t.NewContext())
        {
            var ok = await Approvals(t, db).DecideAsync(request.Id, new DecidePppoeRequest(true, "Looks good"), TestDb.Manager, default);
            Assert.Equal(PppoeOutcome.Ok, ok.Outcome);
            Assert.Equal("Approved", ok.Request!.Status);
            Assert.Equal("NotSent", ok.Request.ProvisioningStatus);
        }

        var customer = await CustomerOf(t, id);
        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.Equal(RouterSyncStatus.NotSent, customer.RouterSyncStatus);
        Assert.Single(t.Notifier.Decided);
        Assert.Equal(ApprovalStatus.Approved, t.Notifier.Decided[0].Status);
    }

    [Fact]
    public async Task Creation_approval_uses_details_edited_while_waiting()
    {
        var t = new TestDb();
        var id = await CreateCustomerAsync(t);
        await SetPasswordAsync(t, id);
        await using (var db = t.NewContext())
        {
            var d = (await new PppoeQueries(db).GetAsync(id, true, default))!;
            await t.PppoeCommands(db).UpdateAsync(id,
                new UpdatePppoeRequest("renamed_user", "Fiber 100 Mbps", null, null, null, null, d.ConcurrencyStamp), TestDb.Staff, default);
        }

        var request = await PendingOf(t, id);
        await using (var db = t.NewContext())
        {
            var detail = await new ApprovalQueries(db).GetAsync(request.Id, true, default);
            Assert.Equal("Enabled, username renamed_user, plan Fiber 100 Mbps", detail!.After);
            await Approvals(t, db).DecideAsync(request.Id, new DecidePppoeRequest(true, null), TestDb.Manager, default);
        }

        await using var check = t.NewContext();
        Assert.Equal("Enabled, username renamed_user, plan Fiber 100 Mbps",
            (await check.ApprovalRequests.SingleAsync(r => r.Id == request.Id)).After);
    }

    [Fact]
    public async Task Rejected_creation_keeps_the_customer_pending_and_can_be_resubmitted_once()
    {
        var t = new TestDb();
        var id = await CreateCustomerAsync(t);
        var request = await PendingOf(t, id);

        await using (var db = t.NewContext())
        {
            var rejected = await Approvals(t, db).DecideAsync(request.Id, new DecidePppoeRequest(false, "Wrong plan"), TestDb.Manager, default);
            Assert.Equal("Rejected", rejected.Request!.Status);
            Assert.Equal("NotApplicable", rejected.Request.ProvisioningStatus);
        }
        Assert.Equal(CustomerStatus.PendingApproval, (await CustomerOf(t, id)).Status);

        await using (var db = t.NewContext())
        {
            Assert.True((await new PppoeQueries(db).GetAsync(id, false, default))!.CanResubmitCreate);
            var again = await t.PppoeCommands(db).SubmitAsync(id, new SubmitPppoeRequest("Create", "Plan fixed"), TestDb.Manager, default);
            // Creation always waits for a Manager, even when a Manager resubmits it.
            Assert.Equal("Pending", again.Request!.Status);
            var twice = await t.PppoeCommands(db).SubmitAsync(id, new SubmitPppoeRequest("Create", null), TestDb.Staff, default);
            Assert.Equal(PppoeOutcome.RequestAlreadyPending, twice.Outcome);
        }
    }

    // ----- plan change and termination -----

    [Fact]
    public async Task Staff_plan_change_waits_and_approval_changes_the_plan_with_before_and_after()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using (var db = t.NewContext())
        {
            var r = await t.PppoeCommands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("ChangePlan", "Upgrade", "Fiber 300 Mbps"), TestDb.Staff, default);
            Assert.Equal("Pending", r.Request!.Status);
            Assert.Equal("Plan Fiber 100 Mbps", r.Request.Before);
            Assert.Equal("Plan Fiber 300 Mbps", r.Request.After);
        }
        Assert.Equal("Fiber 100 Mbps", (await CustomerOf(t, ids.Normal)).PlanName);

        var request = await PendingOf(t, ids.Normal);
        await using (var db = t.NewContext())
            Assert.Equal(PppoeOutcome.Ok, (await Approvals(t, db).DecideAsync(request.Id, new DecidePppoeRequest(true, null), TestDb.Manager, default)).Outcome);

        var customer = await CustomerOf(t, ids.Normal);
        Assert.Equal("Fiber 300 Mbps", customer.PlanName);
        Assert.Equal(CustomerStatus.Active, customer.Status);
    }

    [Theory]
    [InlineData("ChangePlan", null)]
    [InlineData("ChangePlan", "Fiber 100 Mbps")]
    [InlineData("Delete", null)]
    public async Task Invalid_requests_are_refused(string operation, string? plan)
    {
        var t = new TestDb(); var ids = t.Seed();
        await using var db = t.NewContext();
        var r = await t.PppoeCommands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest(operation, null, plan), TestDb.Staff, default);
        Assert.Equal(PppoeOutcome.ValidationFailed, r.Outcome);
    }

    [Fact]
    public async Task Plan_change_is_only_for_active_accounts_and_terminate_not_for_pending_ones()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using var db = t.NewContext();
        var onPending = await t.PppoeCommands(db).SubmitAsync(ids.Pending, new SubmitPppoeRequest("ChangePlan", null, "Fiber 50 Mbps"), TestDb.Staff, default);
        var terminatePending = await t.PppoeCommands(db).SubmitAsync(ids.Pending, new SubmitPppoeRequest("Terminate", null), TestDb.Staff, default);
        var createActive = await t.PppoeCommands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Create", null), TestDb.Staff, default);

        Assert.Equal(PppoeOutcome.NotAllowedInCurrentState, onPending.Outcome);
        Assert.Equal(PppoeOutcome.NotAllowedInCurrentState, terminatePending.Outcome);
        Assert.Equal(PppoeOutcome.NotAllowedInCurrentState, createActive.Outcome);
    }

    [Fact]
    public async Task Termination_needs_approval_and_keeps_the_record()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using (var db = t.NewContext())
            await t.PppoeCommands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Terminate", "Moved away"), TestDb.Staff, default);
        Assert.Equal(CustomerStatus.Active, (await CustomerOf(t, ids.Normal)).Status);

        var request = await PendingOf(t, ids.Normal);
        await using (var db = t.NewContext())
            await Approvals(t, db).DecideAsync(request.Id, new DecidePppoeRequest(true, null), TestDb.Manager, default);

        var customer = await CustomerOf(t, ids.Normal);
        Assert.Equal(CustomerStatus.Terminated, customer.Status);
        Assert.Equal("maria_santos", customer.PppoeUsername);
    }

    [Fact]
    public async Task Manager_plan_change_and_termination_are_approved_at_once()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using (var db = t.NewContext())
        {
            var plan = await t.PppoeCommands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("ChangePlan", null, "Fiber 30 Mbps"), TestDb.Manager, default);
            Assert.Equal("Approved", plan.Request!.Status);
            Assert.Equal("NotSent", plan.Request.ProvisioningStatus);
        }
        await using (var db = t.NewContext())
        {
            var end = await t.PppoeCommands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Terminate", null), TestDb.Manager, default);
            Assert.Equal("Approved", end.Request!.Status);
        }

        var customer = await CustomerOf(t, ids.Normal);
        Assert.Equal("Fiber 30 Mbps", customer.PlanName);
        Assert.Equal(CustomerStatus.Terminated, customer.Status);
        Assert.Empty(t.Notifier.Submitted);   // nothing to review, so no email to Managers
    }

    // ----- 08.02 deciding -----

    [Fact]
    public async Task Staff_cannot_decide_and_a_request_is_decided_once()
    {
        var t = new TestDb();
        var id = await CreateCustomerAsync(t);
        var request = await PendingOf(t, id);

        await using var db = t.NewContext();
        Assert.Equal(PppoeOutcome.Forbidden,
            (await Approvals(t, db).DecideAsync(request.Id, new DecidePppoeRequest(false, null), TestDb.Staff, default)).Outcome);
        Assert.Equal(PppoeOutcome.Ok,
            (await Approvals(t, db).DecideAsync(request.Id, new DecidePppoeRequest(false, null), TestDb.Manager, default)).Outcome);
        Assert.Equal(PppoeOutcome.AlreadyDecided,
            (await Approvals(t, db).DecideAsync(request.Id, new DecidePppoeRequest(false, null), TestDb.Manager, default)).Outcome);
        Assert.Equal(PppoeOutcome.NotFound,
            (await Approvals(t, db).DecideAsync(999_999, new DecidePppoeRequest(true, null), TestDb.Manager, default)).Outcome);
    }

    [Fact]
    public async Task Approval_is_refused_when_the_plan_was_already_changed_meanwhile()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using (var db = t.NewContext())
            await t.PppoeCommands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("ChangePlan", null, "Fiber 30 Mbps"), TestDb.Staff, default);
        await using (var db = t.NewContext())
        {
            var c = await db.Customers.SingleAsync(x => x.Id == ids.Normal);
            c.PlanName = "Fiber 30 Mbps";
            await db.SaveChangesAsync();
        }

        var request = await PendingOf(t, ids.Normal);
        await using var check = t.NewContext();
        var detail = await new ApprovalQueries(check).GetAsync(request.Id, true, default);
        Assert.NotNull(detail!.ApproveBlockedReason);
        Assert.Equal(PppoeOutcome.NotAllowedInCurrentState,
            (await Approvals(t, check).DecideAsync(request.Id, new DecidePppoeRequest(true, null), TestDb.Manager, default)).Outcome);
    }

    // ----- 08.02 list and visibility -----

    [Fact]
    public async Task List_filters_by_status_and_hides_manager_only_customers_from_staff()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using (var db = t.NewContext())
        {
            await t.PppoeCommands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Disable", null), TestDb.Staff, default);
            await t.PppoeCommands(db).SubmitAsync(ids.ManagerOnly, new SubmitPppoeRequest("Disable", null), TestDb.Manager, default);
        }
        await using (var db = t.NewContext())
        {
            var mgr = db.Customers.Single(c => c.Id == ids.ManagerOnly);
            Assert.Equal(CustomerStatus.Disabled, mgr.Status);
        }
        await using (var db = t.NewContext())
            await t.PppoeCommands(db).SubmitAsync(ids.ManagerOnly, new SubmitPppoeRequest("Enable", null), TestDb.Manager, default);

        await using var q = t.NewContext();
        var queries = new ApprovalQueries(q);

        var managerAll = await queries.ListAsync(new ApprovalListQuery("all"), viewerIsManager: true, default);
        var staffAll = await queries.ListAsync(new ApprovalListQuery("all"), viewerIsManager: false, default);
        var staffPending = await queries.ListAsync(new ApprovalListQuery(), viewerIsManager: false, default);

        Assert.Equal(3, managerAll.TotalMatching);
        Assert.Equal(new ApprovalCountsDto(1, 2, 0), managerAll.Counts);
        Assert.Equal(1, staffAll.TotalMatching);
        Assert.Equal(new ApprovalCountsDto(1, 0, 0), staffAll.Counts);
        Assert.All(staffAll.Items, i => Assert.Equal(ids.Normal, i.CustomerId));
        Assert.Single(staffPending.Items);
        Assert.Equal("DisablePppoe", staffPending.Items[0].Operation);
    }

    [Fact]
    public async Task Staff_get_not_found_for_requests_of_manager_only_customers()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using (var db = t.NewContext())
            await t.PppoeCommands(db).SubmitAsync(ids.ManagerOnly, new SubmitPppoeRequest("Disable", null), TestDb.Manager, default);

        await using var q = t.NewContext();
        var id = (await q.ApprovalRequests.SingleAsync()).Id;
        Assert.Null(await new ApprovalQueries(q).GetAsync(id, viewerIsManager: false, default));
        Assert.NotNull(await new ApprovalQueries(q).GetAsync(id, viewerIsManager: true, default));
    }

    // ----- 08.03 email notices -----

    [Fact]
    public async Task Notifier_failures_never_change_the_outcome()
    {
        var t = new TestDb();
        t.Notifier.Throw = true;

        var id = await CreateCustomerAsync(t);
        var request = await PendingOf(t, id);
        await using var db = t.NewContext();
        var decided = await Approvals(t, db).DecideAsync(request.Id, new DecidePppoeRequest(false, null), TestDb.Manager, default);

        Assert.Equal(PppoeOutcome.Ok, decided.Outcome);
        Assert.Equal("Rejected", decided.Request!.Status);
    }

    [Theory]
    [InlineData(false, "smtp.gmail.com", "a@gmail.com", "Disabled")]
    [InlineData(true, "", "a@gmail.com", "NotConfigured")]
    [InlineData(true, "smtp.gmail.com", "", "NotConfigured")]
    [InlineData(true, "smtp.gmail.com", "a@gmail.com", null)]
    public void Emails_are_sent_only_when_enabled_and_configured(bool enabled, string host, string from, string? reason)
    {
        var settings = new EmailOptions { Enabled = enabled, From = from, Smtp = new SmtpOptions { Host = host } };
        Assert.Equal(reason, settings.NotReadyReason);
    }

    [Theory]
    [InlineData(true, 587, MailKit.Security.SecureSocketOptions.StartTls)]
    [InlineData(true, 465, MailKit.Security.SecureSocketOptions.SslOnConnect)]
    [InlineData(false, 25, MailKit.Security.SecureSocketOptions.None)]
    public void UseSsl_picks_the_encryption_from_the_port(bool useSsl, int port, MailKit.Security.SecureSocketOptions expected)
        => Assert.Equal(expected, new SmtpOptions { UseSsl = useSsl, Port = port }.SocketOptions);

    [Fact]
    public void Email_settings_default_to_off_and_encrypted()
    {
        var settings = new EmailOptions();
        Assert.False(settings.Enabled);
        Assert.True(settings.Smtp.UseSsl);
        Assert.Equal(587, settings.Smtp.Port);
    }

    [Fact]
    public void Emails_name_the_request_and_customer_id_but_no_personal_data()
    {
        var notice = new ApprovalNotice(12, "REQ-0012", ApprovalOperation.DisablePppoe, ApprovalStatus.Pending, 5, "NET-0005",
            "staff-id", "staff01", null, null);
        var settings = new EmailOptions { AppBaseUrl = "https://netora.example/" };

        var (subject, body) = ApprovalEmailDispatcher.Compose(new QueuedApprovalNotice(ApprovalNoticeKind.Submitted, notice), settings);
        Assert.Equal("[Netora] REQ-0012 Disable PPPoE account for NET-0005 needs approval", subject);
        Assert.Contains("https://netora.example/approvals/12", body);

        var decided = notice with { Status = ApprovalStatus.Rejected, DecidedByUserName = "manager01", DecisionNote = "Not now" };
        var (subject2, body2) = ApprovalEmailDispatcher.Compose(new QueuedApprovalNotice(ApprovalNoticeKind.Decided, decided), settings);
        Assert.Equal("[Netora] REQ-0012 Disable PPPoE account was rejected", subject2);
        Assert.Contains("Note: Not now", body2);
    }
}
