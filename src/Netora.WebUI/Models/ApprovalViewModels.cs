using System.Globalization;
using Netora.Application.Approvals;

namespace Netora.WebUI.Models;

public sealed class ApprovalListViewModel
{
    public string Status { get; init; } = "pending";
    public string? Search { get; init; }
    public int PageSize { get; init; } = 10;
    public ApprovalListDto? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsManager { get; init; }

    public int TotalPages => Data is null ? 1 : Math.Max(1, (int)Math.Ceiling(Data.TotalMatching / (double)Data.PageSize));

    public string PageUrl(int page)
    {
        var q = new List<string> { $"status={Status}", $"page={page}", $"pageSize={PageSize}" };
        if (!string.IsNullOrWhiteSpace(Search)) q.Add("search=" + Uri.EscapeDataString(Search));
        return "/approvals?" + string.Join("&", q);
    }
}

public sealed class ApprovalDetailViewModel
{
    public required ApprovalDetailDto Detail { get; init; }
    public bool IsManager { get; init; }
}

/// <summary>Display wording for approvals, following the approved approvals.html mockup.</summary>
public static class ApprovalLabels
{
    public static string Operation(string? operation) => operation switch
    {
        "CreatePppoe" => "Create PPPoE",
        "DisablePppoe" => "Suspend account",
        "EnablePppoe" => "Reactivate account",
        "ChangePlan" => "Change plan",
        "TerminatePppoe" => "Terminate account",
        _ => operation ?? string.Empty,
    };

    public static string StatusPill(string status) => status switch
    {
        "Approved" => "active",
        "Rejected" => "terminated",
        _ => "pending",
    };

    public static string Provisioning(string status) => status switch
    {
        "NotSent" => "Not sent to router",
        "Succeeded" => "Done on router",
        "Failed" => "Router failed",
        _ => "Nothing to send",
    };

    public static string ProvisioningPill(string status) => status switch
    {
        "Succeeded" => "active",
        "Failed" => "terminated",
        "NotSent" => "disabled",
        _ => "unknown",
    };

    public static string Date(DateTimeOffset value) => value.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    public static string When(DateTimeOffset? value)
        => value?.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture) ?? "—";

    public static readonly (string Value, string Label)[] StatusFilters =
        [("pending", "Pending"), ("approved", "Approved"), ("rejected", "Rejected"), ("all", "All")];
}
