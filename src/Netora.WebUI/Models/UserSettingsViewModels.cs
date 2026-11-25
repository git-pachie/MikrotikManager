using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Netora.Application.Common;
using Netora.Application.Settings;
using Netora.Application.Users;

namespace Netora.WebUI.Models;

/// <summary>Add and Edit User form. The API validates again and is authoritative.</summary>
public sealed class UserFormViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "Enter a user name.")]
    [RegularExpression(UserRules.UserNamePattern, ErrorMessage = "3 to 40 characters: letters, digits, '.', '_' or '-'.")]
    [Display(Name = "User name")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the display name.")]
    [StringLength(100)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256)]
    public string? Email { get; set; }

    [Required]
    public string Role { get; set; } = "Staff";

    public bool IsActive { get; set; } = true;

    /// <summary>Create only. Never shown again.</summary>
    [StringLength(128)]
    [DataType(DataType.Password)]
    [Display(Name = "Temporary password")]
    public string? TemporaryPassword { get; set; }

    public string? ConcurrencyStamp { get; set; }

    // Display-only, filled from the API on Edit.
    public bool IsEdit => Id is not null;
    public bool IsCurrentUser { get; set; }
    public bool IsLockedOut { get; set; }
    public bool MustChangePassword { get; set; }
    public DateTimeOffset? CreatedAtUtc { get; set; }
    public DateTimeOffset? LastSignInAtUtc { get; set; }
}

public sealed class ProfileViewModel
{
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your display name.")]
    [StringLength(100)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [StringLength(256)]
    public string? Email { get; set; }

    public string? Theme { get; set; }
    public bool MustChangePassword { get; set; }
}

public sealed class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Enter your current password.")]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a new password.")]
    [StringLength(128)]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the new password again.")]
    [Compare(nameof(NewPassword), ErrorMessage = "The two new passwords do not match.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool Forced { get; set; }
}

public sealed class RouterSettingsForm
{
    [StringLength(100)] public string? Name { get; set; }
    [StringLength(253)] public string? Address { get; set; }
    [Range(1, 65535, ErrorMessage = "Enter a port from 1 to 65535.")] public int Port { get; set; } = 443;
    [StringLength(100)] public string? UserName { get; set; }
    [StringLength(128)] [DataType(DataType.Password)] public string? NewPassword { get; set; }
    public bool ClearPassword { get; set; }
    [StringLength(120)] public string? CertificateThumbprint { get; set; }
}

public sealed class EmailSettingsForm
{
    public bool Enabled { get; set; }
    [EmailAddress(ErrorMessage = "Enter a valid sender address.")] [StringLength(256)] public string? From { get; set; }
    [StringLength(100)] public string? FromName { get; set; }
    [StringLength(300)] public string? AppBaseUrl { get; set; }
}

public sealed class SettingsViewModel
{
    public SettingsDto? Data { get; set; }
    public string? ErrorMessage { get; set; }
    public RouterSettingsForm Router { get; set; } = new();
    public EmailSettingsForm Email { get; set; } = new();
    public TestEmailResultDto? TestResult { get; set; }
}

public static class UserLabels
{
    public static string When(DateTimeOffset? value)
        => value?.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture) ?? "Never";

    public static string StatusPill(UserListItemDto u) => !u.IsActive ? "terminated" : u.IsLockedOut ? "disabled" : "active";

    public static string Status(UserListItemDto u) => !u.IsActive ? "Disabled" : u.IsLockedOut ? "Locked" : "Active";
}

/// <summary>Copies API field errors onto a form; errors for unknown fields become form-level messages.</summary>
public static class ApiErrors
{
    public static void Apply(ModelStateDictionary modelState, ApiError? error, Type formType, string fallback, string prefix = "")
    {
        if (error?.Details is { Count: > 0 } details)
        {
            foreach (var (field, messages) in details)
            {
                var key = formType.GetProperty(field) is not null ? prefix + field : string.Empty;
                foreach (var message in messages) modelState.AddModelError(key, message);
            }
            return;
        }
        modelState.AddModelError(string.Empty, error?.Code switch
        {
            "api_unreachable" => fallback + " The Web API is unavailable; try again in a moment.",
            "forbidden" => "Only Managers can do this.",
            _ => fallback + " " + (error?.Message ?? "Unexpected error."),
        });
    }
}
