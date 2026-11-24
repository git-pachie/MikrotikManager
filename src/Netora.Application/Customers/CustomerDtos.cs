namespace Netora.Application.Customers;

public sealed record CustomerListQuery(
    string? Search = null,
    string? Status = null,
    string? Nap = null,
    string? Plan = null,
    int Page = 1,
    int PageSize = 10,
    string? SortBy = null,
    string? SortDir = null)
{
    public static readonly int[] AllowedPageSizes = [10, 20, 50];

    /// <summary>Columns the list can be sorted by. Anything else is ignored, never passed to the database.</summary>
    public static readonly string[] SortColumns = ["name", "username", "plan", "nap", "onu", "status", "created"];

    public bool Descending => string.Equals(SortDir, "desc", StringComparison.OrdinalIgnoreCase);
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

public sealed record CustomerActivityDto(
    DateTimeOffset OccurredAtUtc,
    string Action,
    string? Detail,
    string PerformedBy,
    string Outcome);

/// <summary>Everything the Customer Details and Edit Customer pages need.</summary>
public sealed record CustomerDetailDto(
    int Id,
    string DisplayId,
    string FirstName,
    string LastName,
    string? MobileNumber,
    string? Email,
    string? Address,
    string? Notes,
    string PppoeUsername,
    string PlanName,
    string NapBox,
    string? OnuSerial,
    string? OnuModel,
    string Status,
    bool IsManagerOnly,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    string ConcurrencyStamp,
    // IsEditable: whether this viewer may edit the customer and add or delete its attachments.
    bool IsEditable,
    bool ServiceFieldsLocked,
    IReadOnlyList<CustomerActivityDto> Activity,
    IReadOnlyList<Attachments.AttachmentDto> Attachments,
    long MaxAttachmentBytes);
