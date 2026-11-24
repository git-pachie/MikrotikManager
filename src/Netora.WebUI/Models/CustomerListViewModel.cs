using Netora.Application.Customers;

namespace Netora.WebUI.Models;

public sealed class CustomerListViewModel
{
    public string? Search { get; init; }
    public string Status { get; init; } = "All";
    public string Nap { get; init; } = "All";
    public string Plan { get; init; } = "All";
    public int PageSize { get; init; } = 10;

    /// <summary>Null when the API call failed; the view then shows <see cref="ErrorMessage"/>.</summary>
    public CustomerListDto? Data { get; init; }
    public string? ErrorMessage { get; init; }

    public static readonly string[] StatusOptions = ["All", "Active", "Disabled", "Terminated"];

    public int TotalPages => Data is null ? 1 : Math.Max(1, (int)Math.Ceiling(Data.TotalMatching / (double)Data.PageSize));

    /// <summary>Query string for a given page, keeping the current filters.</summary>
    public string PageUrl(int page)
    {
        var q = new List<string> { $"page={page}", $"pageSize={PageSize}" };
        if (!string.IsNullOrWhiteSpace(Search)) q.Add("search=" + Uri.EscapeDataString(Search));
        if (Status != "All") q.Add("status=" + Uri.EscapeDataString(Status));
        if (Nap != "All") q.Add("nap=" + Uri.EscapeDataString(Nap));
        if (Plan != "All") q.Add("plan=" + Uri.EscapeDataString(Plan));
        return "/customers?" + string.Join("&", q);
    }
}
