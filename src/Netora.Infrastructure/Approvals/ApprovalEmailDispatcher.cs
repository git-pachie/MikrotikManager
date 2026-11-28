using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;
using Netora.Application.Approvals;
using Netora.Application.Auth;
using Netora.Application.Logging;
using Netora.Application.Pppoe;
using Netora.Application.Settings;
using Netora.Domain.Approvals;
using Netora.Infrastructure.Identity;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Approvals;

/// <summary>
/// Sends queued approval emails (08.03): every active Manager with an email address when a request is submitted,
/// and the requester when it is decided. Failures are logged and never change the approval outcome.
/// Logs carry request IDs and recipient counts only, never addresses.
/// </summary>
internal sealed class ApprovalEmailDispatcher(
    ApprovalEmailNotifier notifier,
    IServiceScopeFactory scopes,
    ILogger<ApprovalEmailDispatcher> logger,
    IPppoePasswordProtector passwords,
    IActivityLog? activity = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in notifier.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await SendAsync(item, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    await RecordDeliveryAsync(item, "Email failed", stoppingToken);
                    var error = item.Kind == ApprovalNoticeKind.PppoeCreationApproved ? ex.GetType().Name : SmtpEmailSender.Describe(ex);
                    logger.LogInformation("ApprovalEmailFailed RequestId={RequestId} Kind={Kind} Error={Error}",
                        item.Notice.RequestId, item.Kind, error);
                    (activity ?? NullActivityLog.Instance).Write(new ActivityEvent(ActivityCategories.Application,
                        "EmailDeliveryFailed", ActivityOutcomes.Failure, ActivityTargets.Request(item.Notice.RequestId),
                        $"email={item.Kind} error={ex.GetType().Name}", ActivityLevel.Warning, Actor: "system"));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            if (notifier.Reader.Count > 0)
                logger.LogWarning("ApprovalEmailsNotSent Count={Count} Reason=Shutdown", notifier.Reader.Count);
        }
    }

    private async Task SendAsync(QueuedApprovalNotice item, CancellationToken cancellationToken)
    {
        EmailOptions settings;
        using (var scope = scopes.CreateScope())
            settings = await scope.ServiceProvider.GetRequiredService<IEmailSettingsProvider>().GetAsync(cancellationToken);
        var notice = item.Notice;
        if (settings.NotReadyReason is { } reason)
        {
            await RecordDeliveryAsync(item, "Email skipped", cancellationToken);
            logger.LogInformation("ApprovalEmailSkipped RequestId={RequestId} Kind={Kind} Reason={Reason}", notice.RequestId, item.Kind, reason);
            return;
        }
        if (item.Kind == ApprovalNoticeKind.PppoeCreationApproved && settings.Smtp.EffectiveSecurity == SmtpSecurityMode.None)
        {
            await RecordDeliveryAsync(item, "Email skipped", cancellationToken);
            logger.LogWarning("ApprovalEmailSkipped RequestId={RequestId} Kind={Kind} Reason=SmtpTlsRequired", notice.RequestId, item.Kind);
            return;
        }

        var recipients = await RecipientsAsync(item, cancellationToken);
        if (recipients.Count == 0)
        {
            await RecordDeliveryAsync(item, "No email recipients", cancellationToken);
            logger.LogInformation("ApprovalEmailSkipped RequestId={RequestId} Kind={Kind} Reason=NoRecipients", notice.RequestId, item.Kind);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.From!));
        // Recipients are sent as BCC so Managers do not see each other's addresses (no To header, nothing sent back to the sender).
        foreach (var address in recipients) message.Bcc.Add(MailboxAddress.Parse(address));
        (message.Subject, var text) = Compose(item, settings, passwords);
        message.Body = new TextPart("plain") { Text = text };

        await SmtpEmailSender.SendAsync(settings, message, cancellationToken);
        await RecordDeliveryAsync(item, "Accepted by SMTP", cancellationToken);

        logger.LogInformation("ApprovalEmailSent RequestId={RequestId} Kind={Kind} Recipients={Count}", notice.RequestId, item.Kind, recipients.Count);
    }

    private async Task RecordDeliveryAsync(QueuedApprovalNotice item, string status, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NetoraDbContext>();
            db.ApprovalEmailDeliveries.Add(new ApprovalEmailDelivery
            {
                RequestId = item.Notice.RequestId,
                Kind = item.Kind.ToString(),
                Status = status,
                OccurredAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning("ApprovalEmailHistoryUnavailable RequestId={RequestId} ErrorKind={ErrorKind}",
                item.Notice.RequestId, ex.GetType().Name);
        }
    }

    private async Task<List<string>> RecipientsAsync(QueuedApprovalNotice item, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (item.Kind == ApprovalNoticeKind.PppoeCreationApproved)
        {
            var admin = await users.FindByNameAsync("admin");
            return admin is { IsActive: true } && !string.IsNullOrWhiteSpace(admin.Email)
                && await users.IsInRoleAsync(admin, Roles.Manager) ? [admin.Email!] : [];
        }

        if (item.Kind is ApprovalNoticeKind.Submitted or ApprovalNoticeKind.PppoeDeleted)
        {
            var managers = await users.GetUsersInRoleAsync(Roles.Manager);
            return managers
                .Where(u => u.IsActive && !string.IsNullOrWhiteSpace(u.Email)
                    && (item.Kind == ApprovalNoticeKind.PppoeDeleted || u.Id != item.Notice.RequestedByUserId))
                .Select(u => u.Email!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var requester = await users.FindByIdAsync(item.Notice.RequestedByUserId);
        return requester is { IsActive: true } && !string.IsNullOrWhiteSpace(requester.Email) ? [requester.Email!] : [];
    }

    internal static (string Subject, string Body) Compose(QueuedApprovalNotice item, EmailOptions settings,
        IPppoePasswordProtector? passwords = null)
    {
        var n = item.Notice;
        var operation = OperationLabel(n.Operation);
        var link = string.IsNullOrWhiteSpace(settings.AppBaseUrl)
            ? null
            : $"{settings.AppBaseUrl.TrimEnd('/')}/approvals/{n.RequestId}";

        if (item.Kind == ApprovalNoticeKind.PppoeCreationApproved && item.Details is { } details)
        {
            var password = (passwords ?? throw new InvalidOperationException("PPPoE password protection is unavailable."))
                .Unprotect(details.ProtectedPassword);
            var body = $"PPPoE account creation was approved in Netora ({n.RequestDisplayId}).\n\n" +
                       $"Account Name: {details.Name}\nAddress: {details.Address ?? "—"}\n" +
                       $"Mobile: {details.Mobile ?? "—"}\nPPPoE Username: {details.Username}\n" +
                       $"Password: {password}\nRemote IP: {details.RemoteAddress ?? "Assigned by the router"}\n\n" +
                       (link is null ? "Check Netora for the router provisioning status." : $"Check router provisioning: {link}");
            return ($"[Netora] PPPoE account approved for {n.CustomerDisplayId}", body);
        }

        if (item.Kind == ApprovalNoticeKind.PppoeDeleted)
        {
            var router = item.RouterMode == "Mock" ? "the mock router" : "MikroTik";
            var body = $"{item.Actor} completed the PPPoE account deletion for {n.CustomerDisplayId}.\n\n" +
                       $"Netora removed the account and confirmed the PPPoE secret is absent on {router}.\n" +
                       $"Request: {n.RequestDisplayId}\n" +
                       (link is null ? "See the request in Netora for details." : $"Details: {link}");
            return ($"[Netora] PPPoE account deleted for {n.CustomerDisplayId}", body);
        }

        if (item.Kind == ApprovalNoticeKind.Submitted)
        {
            var body = $"{n.RequestedByUserName} submitted a request that needs a Manager's approval.\n\n" +
                       $"Request: {n.RequestDisplayId}\nChange: {operation}\nCustomer: {n.CustomerDisplayId}\n\n" +
                       (link is null ? "Review it under Pending Approvals in Netora." : $"Review it: {link}") +
                       "\n\nNothing is changed until a Manager approves the request.";
            return ($"[Netora] {n.RequestDisplayId} {operation} for {n.CustomerDisplayId} needs approval", body);
        }

        var decision = n.Status == ApprovalStatus.Approved ? "approved" : "rejected";
        var decided = $"Your request {n.RequestDisplayId} ({operation} for {n.CustomerDisplayId}) was {decision} by {n.DecidedByUserName}.";
        if (!string.IsNullOrWhiteSpace(n.DecisionNote)) decided += $"\nNote: {n.DecisionNote}";
        if (n.Status == ApprovalStatus.Approved)
            decided += "\n\nThe change is saved in Netora. It is sent to the MikroTik router separately; check the request for the router status.";
        if (link is not null) decided += $"\n\nDetails: {link}";
        return ($"[Netora] {n.RequestDisplayId} {operation} was {decision}", decided);
    }

    internal static string OperationLabel(ApprovalOperation operation) => operation switch
    {
        ApprovalOperation.CreatePppoe => "Create PPPoE account",
        ApprovalOperation.DisablePppoe => "Disable PPPoE account",
        ApprovalOperation.EnablePppoe => "Enable PPPoE account",
        ApprovalOperation.ChangePlan => "Change plan",
        ApprovalOperation.TerminatePppoe => "Terminate account",
        ApprovalOperation.DeletePppoe => "Delete PPPoE account",
        _ => operation.ToString(),
    };


}
