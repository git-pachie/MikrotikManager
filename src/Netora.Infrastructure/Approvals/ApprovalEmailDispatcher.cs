using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;
using Netora.Application.Approvals;
using Netora.Application.Auth;
using Netora.Domain.Approvals;
using Netora.Infrastructure.Identity;

namespace Netora.Infrastructure.Approvals;

/// <summary>
/// Sends queued approval emails (08.03): every active Manager with an email address when a request is submitted,
/// and the requester when it is decided. Failures are logged and never change the approval outcome.
/// Logs carry request IDs and recipient counts only, never addresses.
/// </summary>
internal sealed class ApprovalEmailDispatcher(
    ApprovalEmailNotifier notifier,
    IServiceScopeFactory scopes,
    ILogger<ApprovalEmailDispatcher> logger) : BackgroundService
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
                    logger.LogWarning("ApprovalEmailFailed RequestId={RequestId} Kind={Kind} Error={Error}",
                        item.Notice.RequestId, item.Kind, SmtpEmailSender.Describe(ex));
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
            logger.LogInformation("ApprovalEmailSkipped RequestId={RequestId} Kind={Kind} Reason={Reason}", notice.RequestId, item.Kind, reason);
            return;
        }

        var recipients = await RecipientsAsync(item, cancellationToken);
        if (recipients.Count == 0)
        {
            logger.LogInformation("ApprovalEmailSkipped RequestId={RequestId} Kind={Kind} Reason=NoRecipients", notice.RequestId, item.Kind);
            return;
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.From!));
        // Recipients are sent as BCC so Managers do not see each other's addresses (no To header, nothing sent back to the sender).
        foreach (var address in recipients) message.Bcc.Add(MailboxAddress.Parse(address));
        (message.Subject, var text) = Compose(item, settings);
        message.Body = new TextPart("plain") { Text = text };

        await SmtpEmailSender.SendAsync(settings, message, cancellationToken);

        logger.LogInformation("ApprovalEmailSent RequestId={RequestId} Kind={Kind} Recipients={Count}", notice.RequestId, item.Kind, recipients.Count);
    }

    private async Task<List<string>> RecipientsAsync(QueuedApprovalNotice item, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        if (item.Kind == ApprovalNoticeKind.Submitted)
        {
            var managers = await users.GetUsersInRoleAsync(Roles.Manager);
            return managers
                .Where(u => u.IsActive && !string.IsNullOrWhiteSpace(u.Email) && u.Id != item.Notice.RequestedByUserId)
                .Select(u => u.Email!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var requester = await users.FindByIdAsync(item.Notice.RequestedByUserId);
        return requester is { IsActive: true } && !string.IsNullOrWhiteSpace(requester.Email) ? [requester.Email!] : [];
    }

    internal static (string Subject, string Body) Compose(QueuedApprovalNotice item, EmailOptions settings)
    {
        var n = item.Notice;
        var operation = OperationLabel(n.Operation);
        var link = string.IsNullOrWhiteSpace(settings.AppBaseUrl)
            ? null
            : $"{settings.AppBaseUrl.TrimEnd('/')}/approvals/{n.RequestId}";

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
        _ => operation.ToString(),
    };


}
