using System.Globalization;
using Netora.Application.Customers;

namespace Netora.WebUI.Models;

public sealed class CustomerDetailViewModel
{
    /// <summary>Null when the API call failed; the view then shows <see cref="ErrorMessage"/>.</summary>
    public CustomerDetailDto? Data { get; init; }

    public string? ErrorMessage { get; init; }

    public string FullName => Data is null ? string.Empty : $"{Data.FirstName} {Data.LastName}";

    public string Initials => Data is null
        ? string.Empty
        : string.Concat(Data.FirstName.FirstOrDefault(), Data.LastName.FirstOrDefault()).ToUpperInvariant();

    public string StatusLabel => Data is null ? string.Empty : CustomerListViewModel.StatusLabel(Data.Status);

    public string StatusPillClass => Data?.Status == "PendingApproval" ? "pending" : Data?.Status.ToLowerInvariant() ?? string.Empty;

    public static string Date(DateTimeOffset value)
        => value.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    public static string DateTime(DateTimeOffset value)
        => value.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);

    public static string ActionLabel(string action) => action switch
    {
        "CustomerCreated" => "Customer created",
        "CustomerUpdated" => "Customer updated",
        "AttachmentUploaded" => "Attachment uploaded",
        "AttachmentDeleted" => "Attachment deleted",
        _ => action,
    };

    /// <summary>Shows a dash for empty values instead of leaving a blank cell.</summary>
    public static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
