using Netora.Application.Customers;

namespace Netora.WebUI.Models;

public sealed class CustomerListViewModel
{
    public string? Search { get; init; }
    public string Status { get; init; } = "All";
    public string Nap { get; init; } = "All";
    public string Plan { get; init; } = "All";
    public int PageSize { get; init; } = 10;

    /// <summary>One of <see cref="CustomerListQuery.SortColumns"/>, or null for the default order.</summary>
    public string? SortBy { get; init; }
    public bool Descending { get; init; }

    /// <summary>Null when the API call failed; the view then shows <see cref="ErrorMessage"/>.</summary>
    public CustomerListDto? Data { get; init; }
    public string? ErrorMessage { get; init; }

    public static readonly (string Value, string Label)[] StatusOptions =
    [
        ("All", "All"), ("PendingApproval", "Pending"), ("Active", "Active"),
        ("Disabled", "Disabled"), ("Terminated", "Terminated"),
    ];

    public static string StatusLabel(string status) => status == "PendingApproval" ? "Pending Approval" : status;

    public int TotalPages => Data is null ? 1 : Math.Max(1, (int)Math.Ceiling(Data.TotalMatching / (double)Data.PageSize));

    /// <summary>Query string for a given page, keeping the current filters.</summary>
    public string PageUrl(int page)
    {
        var q = new List<string> { $"page={page}", $"pageSize={PageSize}" };
        if (!string.IsNullOrWhiteSpace(Search)) q.Add("search=" + Uri.EscapeDataString(Search));
        if (Status != "All") q.Add("status=" + Uri.EscapeDataString(Status));
        if (Nap != "All") q.Add("nap=" + Uri.EscapeDataString(Nap));
        if (Plan != "All") q.Add("plan=" + Uri.EscapeDataString(Plan));
        if (SortBy is not null) { q.Add("sortBy=" + SortBy); q.Add("sortDir=" + (Descending ? "desc" : "asc")); }
        return "/customers?" + string.Join("&", q);
    }

    /// <summary>Link for a column header: first click sorts ascending, clicking the active column flips the direction. Page resets to 1.</summary>
    public string SortUrl(string column)
    {
        var next = SortBy == column && !Descending ? "desc" : "asc";
        var q = new List<string> { "page=1", $"pageSize={PageSize}" };
        if (!string.IsNullOrWhiteSpace(Search)) q.Add("search=" + Uri.EscapeDataString(Search));
        if (Status != "All") q.Add("status=" + Uri.EscapeDataString(Status));
        if (Nap != "All") q.Add("nap=" + Uri.EscapeDataString(Nap));
        if (Plan != "All") q.Add("plan=" + Uri.EscapeDataString(Plan));
        q.Add("sortBy=" + column);
        q.Add("sortDir=" + next);
        return "/customers?" + string.Join("&", q);
    }

    public string? AriaSort(string column) => SortBy != column ? null : Descending ? "descending" : "ascending";
}
