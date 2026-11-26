using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Netora.Application.Catalog;

namespace Netora.WebUI.Models;

/// <summary>Add and Edit Internet Plan. Quick browser checks only; the API validates again (SavePlanRequestValidator).</summary>
public sealed class PlanFormViewModel
{
    [Required(ErrorMessage = "Enter the plan name.")]
    [StringLength(100)]
    [Display(Name = "Plan name")]
    public string Name { get; set; } = string.Empty;

    [Range(0, 1_000_000, ErrorMessage = "Enter a fee from 0 to 1,000,000.")]
    [Display(Name = "Monthly fee")]
    public decimal? MonthlyFee { get; set; }

    [Range(1, 100_000, ErrorMessage = "Enter 1 to 100,000 Mbps.")]
    [Display(Name = "Download")]
    public int? DownloadMbps { get; set; }

    [Range(1, 100_000, ErrorMessage = "Enter 1 to 100,000 Mbps.")]
    [Display(Name = "Upload")]
    public int? UploadMbps { get; set; }

    [Required(ErrorMessage = "Enter the MikroTik PPP profile name.")]
    [StringLength(100)]
    [Display(Name = "MikroTik profile")]
    public string RouterProfile { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string? ConcurrencyStamp { get; set; }

    // Display only.
    public int? Id { get; set; }
    public bool IsEdit => Id is not null;
    public int Subscribers { get; set; }

    /// <summary>Saved values, to warn when the profile is being changed for a plan that has customers.</summary>
    public string? SavedRouterProfile { get; set; }

    public static PlanFormViewModel From(PlanDetailDto p) => new()
    {
        Id = p.Id, Name = p.Name, MonthlyFee = p.MonthlyFee, DownloadMbps = p.DownloadMbps, UploadMbps = p.UploadMbps,
        RouterProfile = p.RouterProfile, IsActive = p.IsActive, ConcurrencyStamp = p.ConcurrencyStamp, Subscribers = p.Subscribers,
        SavedRouterProfile = p.RouterProfile,
    };

    public SavePlanRequest ToRequest() => new(Name, MonthlyFee, DownloadMbps, UploadMbps, RouterProfile, IsActive, ConcurrencyStamp);
}

public sealed class PlanListViewModel
{
    public PlanListDto? Data { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsManager { get; init; }

    /// <summary>Set after "Check Router" (Managers only).</summary>
    public PlanProfileCheckDto? Check { get; init; }
    public string? CheckError { get; init; }
}

/// <summary>Add NAP Box, and the editable fields on NAP Box Details.</summary>
public sealed class NapFormViewModel
{
    [Required(ErrorMessage = "Enter the NAP code.")]
    [StringLength(50)]
    [RegularExpression(SaveNapRequestValidator.CodePattern,
        ErrorMessage = "1 to 50 characters: letters, digits, spaces, '.', '_' or '-', starting with a letter or digit.")]
    [Display(Name = "NAP code")]
    public string Code { get; set; } = string.Empty;

    [StringLength(100)]
    public string? Area { get; set; }

    [StringLength(200)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public string? ConcurrencyStamp { get; set; }

    public SaveNapRequest ToRequest() => new(Code, Area, Description, IsActive, ConcurrencyStamp);
}

public sealed class NapDetailViewModel
{
    public NapDetailDto Data { get; init; } = null!;
    public NapFormViewModel Form { get; init; } = new();
    public bool IsManager { get; init; }
}

public static class CatalogLabels
{
    private static readonly CultureInfo Peso = CultureInfo.GetCultureInfo("en-PH");

    public static string Fee(decimal? fee) => fee is null ? "—" : "₱" + fee.Value.ToString(fee.Value % 1 == 0 ? "#,0" : "#,0.00", Peso);

    public static string Speed(int? down, int? up) => down is null && up is null ? "—" : $"{down?.ToString() ?? "?"} / {up?.ToString() ?? "?"} Mbps";
}
