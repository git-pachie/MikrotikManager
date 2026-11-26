using System.Globalization;
using Netora.Application.Audit;

namespace Netora.WebUI.Models;

public sealed class AuditViewModel
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Category { get; init; }
    public string? Actor { get; init; }
    public string? Search { get; init; }
    public int PageSize { get; init; } = 25;

    public AuditListDto? Data { get; set; }
    public string? ErrorMessage { get; set; }

    public bool Filtered => From is not null || To is not null || Category is not null || Actor is not null || Search is not null;

    /// <summary>Pages that can be opened (the API serves at most <see cref="AuditQuery.MaxPage"/>).</summary>
    public int TotalPages => Data is null ? 1 : Math.Clamp((int)Math.Ceiling(Data.TotalMatching / (double)Data.PageSize), 1, AuditQuery.MaxPage);

    public string PageUrl(int page)
    {
        var q = new List<string> { $"page={page}", $"pageSize={PageSize}" };
        if (From is { } f) q.Add("from=" + Date(f));
        if (To is { } t) q.Add("to=" + Date(t));
        if (Category is not null) q.Add("category=" + Uri.EscapeDataString(Category));
        if (Actor is not null) q.Add("actor=" + Uri.EscapeDataString(Actor));
        if (Search is not null) q.Add("search=" + Uri.EscapeDataString(Search));
        return "/audit?" + string.Join("&", q);
    }

    /// <summary>A short window of page links around the current page, plus the first and last.</summary>
    public IEnumerable<int> PageLinks()
    {
        var current = Data?.Page ?? 1;
        return Enumerable.Range(1, TotalPages).Where(p => p == 1 || p == TotalPages || Math.Abs(p - current) <= 2);
    }

    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"Nov 23, 2026 09:42", as in audit.html (server local time).</summary>
    public static string When(DateTimeOffset at) => at.ToLocalTime().ToString("MMM d, yyyy HH:mm", CultureInfo.InvariantCulture);

    public static string ResultPill(string result) => result switch
    {
        "Success" or "Completed" or "Approved" => "active",
        "Pending" => "pending",
        "Failed" or "Rejected" => "terminated",
        _ => "unknown",
    };
}
