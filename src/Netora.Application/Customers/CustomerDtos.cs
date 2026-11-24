namespace Netora.Application.Customers;

public sealed record CustomerListQuery(
    string? Search = null,
    string? Status = null,
    string? Nap = null,
    string? Plan = null,
    int Page = 1,
    int PageSize = 10)
{
    public static readonly int[] AllowedPageSizes = [10, 20, 50];
}

public sealed record CustomerListItemDto(
    int Id,
    string Name,
    string PppoeUsername,
    string PlanName,
    string NapBox,
    string? OnuSerial,
    string Status,
    DateTimeOffset CreatedAtUtc);

/// <summary>Counts over every customer the caller may see (Manager-only rows are excluded for Staff).</summary>
public sealed record CustomerStatsDto(int Total, int Active, int Disabled, int Terminated, int UnknownNap);

public sealed record CustomerListDto(
    IReadOnlyList<CustomerListItemDto> Items,
    int TotalMatching,
    int Page,
    int PageSize,
    CustomerStatsDto Stats,
    IReadOnlyList<string> NapOptions,
    IReadOnlyList<string> PlanOptions,
    bool ContainsSampleData);
