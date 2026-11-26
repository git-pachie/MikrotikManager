using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Netora.Application.Logging;
using Netora.Application.Pppoe;
using Netora.Application.Router;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Router;

/// <summary>
/// Sends approved requests to the router (04.02, 04.03, 08.04) and records the outcome on the request (04.05).
/// <list type="bullet">
/// <item>Desired state, not replay: the router secret is set to what SQL Server holds now, so sending twice changes
/// nothing more. A request that already succeeded is never sent again.</item>
/// <item>Create never overwrites a secret Netora did not create: an existing secret is adopted only when its comment
/// carries Netora's marker for this customer (left by an earlier attempt whose answer was lost).</item>
/// <item>Terminate disables the secret; nothing is ever deleted.</item>
/// <item>One request at a time per API instance, so two retries cannot race.</item>
/// </list>
/// </summary>
internal sealed class RouterProvisioner(
    NetoraDbContext db,
    IRouterClient router,
    IPppoePasswordProtector passwords,
    ILogger<RouterProvisioner> logger,
    IActivityLog? activity = null) : IRouterProvisioner
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<ProvisioningResult> ProvisionAsync(long requestId, string actorUserId, string actorUserName, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            db.ChangeTracker.Clear();
            var request = await db.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
            if (request is null) return new ProvisioningResult(nameof(ProvisioningStatus.NotApplicable), "Request not found.", 0);
            if (request.Status != ApprovalStatus.Approved || request.ProvisioningStatus == ProvisioningStatus.Succeeded)
                return new ProvisioningResult(request.ProvisioningStatus.ToString(), request.ProvisioningError, request.ProvisioningAttempts);

            var customer = await db.Customers.Include(c => c.Plan).FirstAsync(c => c.Id == request.CustomerId, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            string? error = null;
            string? errorKind = null;
            string detail;
            Exception? crash = null;
            try
            {
                detail = await SendAsync(request, customer, cancellationToken);
            }
            catch (RouterException ex)
            {
                error = ex.Message;
                errorKind = ex.Kind.ToString();
                detail = ex.Message;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                crash = ex;
                error = "Unexpected error while talking to the router. See the API log.";
                errorKind = "Unexpected";
                detail = error;
            }

            request.ProvisioningAttempts++;
            request.ProvisioningLastAttemptAtUtc = now;
            request.ProvisioningStatus = error is null ? ProvisioningStatus.Succeeded : ProvisioningStatus.Failed;
            request.ProvisioningError = error is null ? null : Truncate(error, 500);
            customer.RouterSyncStatus = error is null ? RouterSyncStatus.Synced : RouterSyncStatus.Failed;
            db.CustomerActivities.Add(new CustomerActivity
            {
                CustomerId = customer.Id,
                Action = error is null ? "RouterProvisioned" : "RouterProvisioningFailed",
                Detail = Truncate($"{ApprovalRequest.DisplayId(request.Id)} {request.Operation}: {detail} ({router.Mode})", 500),
                ActorUserId = actorUserId,
                ActorUserName = actorUserName,
                Outcome = error is null ? "Completed" : "Failed",
                OccurredAtUtc = now,
            });
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation("RouterProvisioning RequestId={RequestId} CustomerId={CustomerId} Operation={Operation} Status={Status} Attempt={Attempt} Mode={Mode}",
                request.Id, customer.Id, request.Operation, request.ProvisioningStatus, request.ProvisioningAttempts, router.Mode);
            Log(request, customer.Id, errorKind, crash);
            return new ProvisioningResult(request.ProvisioningStatus.ToString(), request.ProvisioningError, request.ProvisioningAttempts);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Makes the router match SQL Server for this customer. Returns a short description of what was done.</summary>
    private async Task<string> SendAsync(ApprovalRequest request, Customer customer, CancellationToken ct)
    {
        var desired = new RouterSecretWrite(
            customer.PppoeUsername,
            customer.PppoePasswordProtected is null ? null : passwords.Unprotect(customer.PppoePasswordProtected),
            // The plan's mapped PPP profile (module 05). Netora never creates profiles; a missing one fails on the router.
            customer.Plan.RouterProfile,
            Disabled: customer.Status is CustomerStatus.Disabled or CustomerStatus.Terminated,
            RouterRules.Comment(customer.Id, customer.PppoeComment),
            customer.PppoeRemoteAddress,
            customer.PppoeLocalAddress);

        var existing = await router.FindSecretAsync(customer.RouterSecretId, customer.PppoeUsername, ct);

        if (request.Operation == ApprovalOperation.CreatePppoe)
        {
            if (existing is null)
            {
                if (desired.Password is null) throw new RouterException(RouterErrorKind.Rejected, "No PPPoE password is set for this customer.");
                var created = await router.CreateSecretAsync(desired, ct);
                customer.RouterSecretId = created.Id;
                return $"Secret {created.Name} created";
            }
            if (existing.Comment?.Contains(RouterRules.Marker(customer.Id), StringComparison.Ordinal) != true)
            {
                throw new RouterException(RouterErrorKind.Rejected,
                    $"A PPPoE secret named {existing.Name} already exists on the router and was not created by Netora. " +
                    "It was not changed. Import it, or choose another PPPoE username.");
            }
            // Created by an earlier attempt whose answer was lost: adopt it and set the desired values.
            var adopted = await router.UpdateSecretAsync(existing.Id, desired, ct);
            customer.RouterSecretId = adopted.Id;
            return $"Secret {adopted.Name} already existed from an earlier attempt; values updated";
        }

        if (existing is null)
        {
            if (request.Operation == ApprovalOperation.TerminatePppoe)
                return "No secret on the router, so there was nothing to disable";
            throw new RouterException(RouterErrorKind.NotFound,
                $"No PPPoE secret named {customer.PppoeUsername} exists on the router. Import it or check the username.");
        }

        var updated = await router.UpdateSecretAsync(existing.Id, desired, ct);
        customer.RouterSecretId = updated.Id;
        return request.Operation switch
        {
            ApprovalOperation.DisablePppoe => $"Secret {updated.Name} disabled",
            ApprovalOperation.EnablePppoe => $"Secret {updated.Name} enabled",
            ApprovalOperation.ChangePlan => $"Secret {updated.Name} profile set to {updated.Profile}",
            ApprovalOperation.TerminatePppoe => $"Secret {updated.Name} disabled (terminated, not deleted)",
            _ => $"Secret {updated.Name} updated",
        };
    }

    /// <summary>
    /// File log (18.02): the provisioning result under Approvals, and on success the PPPoE account event. A failure
    /// carries the error kind only (Unreachable, Unauthorized, ...), never the router's message or credentials.
    /// </summary>
    private void Log(ApprovalRequest request, int customerId, string? errorKind, Exception? crash)
    {
        var log = activity ?? NullActivityLog.Instance;
        var detail = $"{request.Operation} {ActivityTargets.Customer(customerId)} attempt={request.ProvisioningAttempts} mode={router.Mode}";
        if (errorKind is not null)
        {
            log.Write(new ActivityEvent(ActivityCategories.Approvals, "ProvisioningFailed", ActivityOutcomes.Failure,
                ActivityTargets.Request(request.Id), $"{detail} error={errorKind}",
                crash is null ? ActivityLevel.Warning : ActivityLevel.Error, Exception: crash));
            if (crash is null)
                log.Write(new ActivityEvent(ActivityCategories.MikroTik, "RouterError", ActivityOutcomes.Failure,
                    ActivityTargets.Request(request.Id), $"kind={errorKind} mode={router.Mode}", ActivityLevel.Warning));
            return;
        }

        log.Write(new ActivityEvent(ActivityCategories.Approvals, "ProvisioningSucceeded", ActivityOutcomes.Success,
            ActivityTargets.Request(request.Id), detail));
        var accountEvent = request.Operation switch
        {
            ApprovalOperation.CreatePppoe => "AccountCreated",
            ApprovalOperation.DisablePppoe => "AccountSuspended",
            ApprovalOperation.EnablePppoe => "AccountReactivated",
            ApprovalOperation.ChangePlan => "PlanChanged",
            ApprovalOperation.TerminatePppoe => "AccountTerminated",
            _ => null,
        };
        if (accountEvent is not null)
            log.Write(new ActivityEvent(ActivityCategories.Pppoe, accountEvent, ActivityOutcomes.Success,
                ActivityTargets.Customer(customerId), $"{ApprovalRequest.DisplayId(request.Id)} mode={router.Mode}"));
    }

    private static string Truncate(string v, int max) => v.Length <= max ? v : v[..max];
}
