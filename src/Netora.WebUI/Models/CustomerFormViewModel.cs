using System.ComponentModel.DataAnnotations;
using Netora.Application.Customers;

namespace Netora.WebUI.Models;

/// <summary>
/// Add and Edit Customer form. These attributes give quick feedback in the browser; the Web API validates again and is
/// authoritative (Application/Customers/CreateCustomer.cs).
/// </summary>
public sealed class CustomerFormViewModel
{
    [Required(ErrorMessage = "Enter the first name.")]
    [StringLength(100)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the last name.")]
    [StringLength(100)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = string.Empty;

    [StringLength(30)]
    [RegularExpression(CreateCustomerRequestValidator.MobilePattern,
        ErrorMessage = "Use digits, spaces, +, -, ( or ), 7 to 30 characters.")]
    [Display(Name = "Mobile Number")]
    public string? MobileNumber { get; set; }

    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256)]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Enter the installation address.")]
    [StringLength(300)]
    [Display(Name = "Installation Address")]
    public string Address { get; set; } = string.Empty;

    [StringLength(1000)]
    public string? Notes { get; set; }

    [Required(ErrorMessage = "Enter the PPPoE username.")]
    [RegularExpression(CreateCustomerRequestValidator.PppoeUsernamePattern,
        ErrorMessage = "3 to 40 characters: letters, digits, '.', '_' or '-'.")]
    [Display(Name = "PPPoE Username")]
    public string PppoeUsername { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select an internet plan.")]
    [StringLength(100)]
    [Display(Name = "Internet Plan")]
    public string PlanName { get; set; } = string.Empty;

    [StringLength(50)]
    [Display(Name = "NAP Box")]
    public string? NapBox { get; set; } = "UNKNOWN";

    [StringLength(50)]
    [Display(Name = "ONU Serial Number")]
    public string? OnuSerial { get; set; }

    [StringLength(100)]
    [Display(Name = "ONU Brand / Model")]
    public string? OnuModel { get; set; }

    [Display(Name = "Managers Only (restricted visibility)")]
    public bool IsManagerOnly { get; set; }

    // Edit only. The stamp is the value read when the form was opened; the API rejects the save if it is stale.
    public string? ConcurrencyStamp { get; set; }

    // Display-only data, filled by the controller.
    public int? Id { get; set; }
    public bool IsEdit => Id is not null;
    public string? DisplayId { get; set; }
    public string? StatusLabel { get; set; }

    /// <summary>Active and Disabled customers: PPPoE Username and Plan are shown read-only.</summary>
    public bool ServiceFieldsLocked { get; set; }
    public IReadOnlyList<string> PlanOptions { get; set; } = [];
    public IReadOnlyList<string> NapOptions { get; set; } = ["UNKNOWN"];
    public bool CanSetManagerOnly { get; set; }
    public string? OptionsError { get; set; }

    public static CustomerFormViewModel FromDetail(CustomerDetailDto d) => new()
    {
        Id = d.Id,
        DisplayId = d.DisplayId,
        StatusLabel = CustomerListViewModel.StatusLabel(d.Status),
        ServiceFieldsLocked = d.ServiceFieldsLocked,
        ConcurrencyStamp = d.ConcurrencyStamp,
        FirstName = d.FirstName,
        LastName = d.LastName,
        MobileNumber = d.MobileNumber,
        Email = d.Email,
        Address = d.Address ?? string.Empty,
        Notes = d.Notes,
        PppoeUsername = d.PppoeUsername,
        PlanName = d.PlanName,
        NapBox = d.NapBox,
        OnuSerial = d.OnuSerial,
        OnuModel = d.OnuModel,
        IsManagerOnly = d.IsManagerOnly,
    };

    public UpdateCustomerRequest ToUpdateRequest() => new(
        FirstName, LastName, MobileNumber, Email, Address, Notes, PppoeUsername, PlanName, NapBox,
        OnuSerial, OnuModel, CanSetManagerOnly && IsManagerOnly, ConcurrencyStamp ?? string.Empty);

    public CreateCustomerRequest ToRequest() => new(
        FirstName, LastName, MobileNumber, Email, Address, Notes, PppoeUsername, PlanName, NapBox,
        OnuSerial, OnuModel, CanSetManagerOnly && IsManagerOnly);
}
