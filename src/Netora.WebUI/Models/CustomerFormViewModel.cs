using System.ComponentModel.DataAnnotations;
using Netora.Application.Customers;

namespace Netora.WebUI.Models;

/// <summary>
/// Add Customer form. These attributes give quick feedback in the browser; the Web API validates again and is
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

    // Display-only data, filled by the controller.
    public IReadOnlyList<string> PlanOptions { get; set; } = [];
    public IReadOnlyList<string> NapOptions { get; set; } = ["UNKNOWN"];
    public bool CanSetManagerOnly { get; set; }
    public string? OptionsError { get; set; }

    public CreateCustomerRequest ToRequest() => new(
        FirstName, LastName, MobileNumber, Email, Address, Notes, PppoeUsername, PlanName, NapBox,
        OnuSerial, OnuModel, CanSetManagerOnly && IsManagerOnly);
}
