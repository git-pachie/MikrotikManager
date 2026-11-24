using System.ComponentModel.DataAnnotations;

namespace Netora.WebUI.Models;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Enter your user name.")]
    [StringLength(256)]
    [Display(Name = "User name")]
    public string UserName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your password.")]
    [StringLength(256)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }

    public bool SessionExpired { get; set; }
}
