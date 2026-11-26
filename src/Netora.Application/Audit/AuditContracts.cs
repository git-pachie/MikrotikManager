namespace Netora.Application.Audit;

/// <summary>Audit Trail filters (12.01). Dates are the server's local calendar days, inclusive.</summary>
public sealed record AuditQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    string? Category = null,
    string? Actor = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25)
{
    public static readonly int[] AllowedPageSizes = [25, 50, 100];

    /// <summary>Deepest page served; older entries are reached by narrowing the date range.</summary>
    public const int MaxPage = 200;
}

/// <param name="EntityLink">Netora page of the entity, when it has one.</param>
/// <param name="Result">Success, Failed, Completed, Pending, Approved or Rejected.</param>
public sealed record AuditEntryDto(
    DateTimeOffset OccurredAtUtc, string Actor, string Category, string Entity, string? EntityLink, string Action,
    string Result, string? Detail);

public sealed record AuditListDto(IReadOnlyList<AuditEntryDto> Items, int TotalMatching, int Page, int PageSize);

public static class AuditCategories
{
    public const string SignIn = "Sign-in";
    public const string Customers = "Customers";
    public const string Pppoe = "PPPoE";
    public const string Approvals = "Approvals";
    public const string Router = "Router";
    public const string PlansAndNap = "Plans & NAP";
    public const string UsersAndSettings = "Users & Settings";

    public static readonly IReadOnlyList<string> All = [SignIn, Customers, Pppoe, Approvals, Router, PlansAndNap, UsersAndSettings];
}

/// <summary>Managers only (agreed 2026-11-26): the trail includes sign-in attempts, IP addresses and Manager-only customers.</summary>
public interface IAuditQueries
{
    Task<AuditListDto> ListAsync(AuditQuery query, CancellationToken cancellationToken);
}

/// <summary>Plain-language actions for the Audit Trail, worded like the approved audit.html mockup.</summary>
public static class AuditLabels
{
    private static readonly Dictionary<string, string> Actions = new(StringComparer.Ordinal)
    {
        ["CustomerCreated"] = "Added customer",
        ["CustomerUpdated"] = "Edited customer",
        ["AttachmentUploaded"] = "Uploaded attachment",
        ["AttachmentDeleted"] = "Deleted attachment",
        ["PppoeUpdated"] = "Edited PPPoE details",
        ["CustomerImported"] = "Imported from router",
        ["RouterProvisioned"] = "Sent to router",
        ["RouterProvisioningFailed"] = "Sending to router failed",
        ["LoginSucceeded"] = "Signed in",
        ["LoginFailed"] = "Sign-in failed",
        ["LockoutTriggered"] = "Account locked",
        ["Logout"] = "Signed out",
        ["PasswordChanged"] = "Changed own password",
        ["ProfileUpdated"] = "Edited own profile",
        ["UserCreated"] = "Added user",
        ["UserUpdated"] = "Edited user",
        ["UserDeleted"] = "Deleted user",
        ["RoleChanged"] = "Changed role",
        ["UserDeactivated"] = "Disabled user",
        ["UserReactivated"] = "Enabled user",
        ["PasswordResetByManager"] = "Reset password",
        ["ManagerBootstrapped"] = "First Manager created",
        ["SettingsUpdated"] = "Changed settings",
        ["PlanCreated"] = "Added plan",
        ["PlanUpdated"] = "Edited plan",
        ["PlanDeactivated"] = "Deactivated plan",
        ["PlanReactivated"] = "Reactivated plan",
        ["ProfileSentToRouter"] = "Sent profile to router",
        ["ProfileSendFailed"] = "Sending profile to router failed",
        ["NapCreated"] = "Added NAP box",
        ["NapUpdated"] = "Edited NAP box",
        ["NapDeactivated"] = "Deactivated NAP box",
        ["NapReactivated"] = "Reactivated NAP box",
    };

    private static readonly (string Op, string Label)[] Operations =
    [
        ("ChangePlan", "plan change"), ("Create", "PPPoE creation"), ("Disable", "PPPoE suspension"),
        ("Enable", "PPPoE reactivation"), ("Terminate", "termination"),
    ];

    private static readonly (string Step, string Verb)[] Steps = [("Requested", "Requested"), ("Approved", "Approved"), ("Rejected", "Rejected")];

    /// <summary>"PppoeChangePlanRequested" → "Requested plan change"; known names from the table; anything else as is.</summary>
    public static string Action(string action)
    {
        if (Actions.TryGetValue(action, out var label)) return label;
        if (ApprovalStep(action) is { } step) return step;
        return action;
    }

    /// <summary>The label for Pppoe{Operation}{Requested|Approved|Rejected} activity names, or null.</summary>
    public static string? ApprovalStep(string action)
    {
        if (!action.StartsWith("Pppoe", StringComparison.Ordinal)) return null;
        foreach (var (step, verb) in Steps)
        {
            if (!action.EndsWith(step, StringComparison.Ordinal)) continue;
            var op = action["Pppoe".Length..^step.Length];
            foreach (var (name, opLabel) in Operations)
                if (op == name) return $"{verb} {opLabel}";
        }
        return null;
    }

    /// <summary>Sign-in events, as opposed to user administration and settings.</summary>
    public static readonly IReadOnlyList<string> SignInEvents =
        ["LoginSucceeded", "LoginFailed", "LockoutTriggered", "Logout", "PasswordChanged", "ProfileUpdated"];

    public static readonly IReadOnlyList<string> CustomerActions = ["CustomerCreated", "CustomerUpdated", "AttachmentUploaded", "AttachmentDeleted"];
    public static readonly IReadOnlyList<string> PppoeActions = ["PppoeUpdated", "CustomerImported"];
    public static readonly IReadOnlyList<string> RouterActions = ["RouterProvisioned", "RouterProvisioningFailed"];

    /// <summary>User administration reasons end with ", by {actor}" (or are just "by {actor}"). Returns the actor and the rest.</summary>
    public static (string? Actor, string? Detail) SplitBy(string? reason)
    {
        if (string.IsNullOrEmpty(reason)) return (null, null);
        var i = reason.LastIndexOf("by ", StringComparison.Ordinal);
        if (i < 0 || (i > 0 && (i < 2 || reason[i - 2] != ','))) return (null, reason);
        var actor = reason[(i + 3)..].Trim();
        var rest = i == 0 ? null : reason[..(i - 2)];
        return (actor.Length == 0 ? null : actor, rest);
    }
}
