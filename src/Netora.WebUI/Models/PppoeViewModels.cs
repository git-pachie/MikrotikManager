using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Netora.Application.Customers;
using Netora.Application.Pppoe;

namespace Netora.WebUI.Models;

public sealed record PppoePasswordViewModel(int CustomerId, bool PasswordSet);

public sealed class PppoeListViewModel
{
    public string? Search { get; init; }
    public string State { get; init; } = "all";
    public int PageSize { get; init; } = 10;
    public string? SortBy { get; init; }
    public bool Descending { get; init; }
    public PppoeListDto? Data { get; init; }
    public string? ErrorMessage { get; init; }

    public int TotalPages => Data is null ? 1 : Math.Max(1, (int)Math.Ceiling(Data.TotalMatching / (double)Data.PageSize));

    public string SortUrl(string column)
    {
        var next = SortBy == column && !Descending ? "desc" : "asc";
        return Url(1, column, next);
    }

    public string? AriaSort(string column) => SortBy != column ? null : Descending ? "descending" : "ascending";

    private string Url(int page, string? sortBy, string? sortDir)
    {
        var q = new List<string> { $"page={page}", $"pageSize={PageSize}" };
        if (!string.IsNullOrWhiteSpace(Search)) q.Add("search=" + Uri.EscapeDataString(Search));
        if (State != "all") q.Add("state=" + State);
        if (sortBy is not null) { q.Add("sortBy=" + sortBy); q.Add("sortDir=" + sortDir); }
        return "/pppoe?" + string.Join("&", q);
    }
}

/// <summary>Edit PPPoE form. The API validates again and is authoritative (Application/Pppoe/PppoeContracts.cs).</summary>
public sealed class PppoeFormViewModel
{
    [Required(ErrorMessage = "Enter the PPPoE username.")]
    [RegularExpression(CreateCustomerRequestValidator.PppoeUsernamePattern,
        ErrorMessage = "3 to 40 characters: letters, digits, '.', '_' or '-'.")]
    [Display(Name = "PPPoE Username")]
    public string PppoeUsername { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select an internet plan.")]
    [StringLength(100)]
    [Display(Name = "Internet Plan")]
    public string PlanName { get; set; } = string.Empty;

    /// <summary>Write-only. Empty keeps the stored password.</summary>
    [StringLength(UpdatePppoeRequestValidator.PasswordMaxLength, MinimumLength = UpdatePppoeRequestValidator.PasswordMinLength,
        ErrorMessage = "Use 8 to 64 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string? NewPassword { get; set; }

    [StringLength(45)]
    [Display(Name = "Remote Address")]
    public string? RemoteAddress { get; set; }

    [StringLength(45)]
    [Display(Name = "Local Address")]
    public string? LocalAddress { get; set; }

    [StringLength(200)]
    [Display(Name = "Router Comment")]
    public string? Comment { get; set; }

    public string? ConcurrencyStamp { get; set; }

    // Display only, filled by the controller.
    public PppoeDetailDto? Detail { get; set; }
    public IReadOnlyList<string> PlanOptions { get; set; } = [];
    public bool IsManager { get; set; }

    public static PppoeFormViewModel From(PppoeDetailDto d) => new()
    {
        Detail = d,
        PppoeUsername = d.PppoeUsername,
        PlanName = d.PlanName,
        RemoteAddress = d.RemoteAddress,
        LocalAddress = d.LocalAddress,
        Comment = d.Comment,
        ConcurrencyStamp = d.ConcurrencyStamp,
    };

    public UpdatePppoeRequest ToRequest() => new(PppoeUsername, PlanName, NewPassword, RemoteAddress, LocalAddress, Comment,
        ConcurrencyStamp ?? string.Empty);
}

public static class PppoeLabels
{
    public static string State(string state) => state switch
    {
        "NotCreated" => "Not created",
        _ => state,
    };

    public static string StatePill(string state) => state switch
    {
        "Enabled" => "active",
        "Disabled" => "disabled",
        "Terminated" => "terminated",
        "Deleted" => "terminated",
        _ => "unknown",
    };

    public static string Router(string status) => status switch
    {
        "Synced" => "In sync",
        "Failed" => "Failed",
        _ => "Not sent",
    };

    public static string Operation(string? operation) => operation switch
    {
        "DisablePppoe" => "Disable",
        "EnablePppoe" => "Enable",
        "CreatePppoe" => "Create",
        "ChangePlan" => "Change Plan",
        "TerminatePppoe" => "Terminate",
        "DeletePppoe" => "Delete PPPoE",
        _ => operation ?? string.Empty,
    };

    public static string When(DateTimeOffset? value)
        => value?.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture) ?? "—";
}

public sealed record PppoeImportViewModel
{
    public bool IsManager { get; init; }
    public Netora.Application.Settings.RouterSettingsDto? Router { get; init; }
    public Netora.Application.Router.ImportScanDto? Scan { get; init; }
    public string? Error { get; init; }
}
