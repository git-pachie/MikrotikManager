using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Settings;
using Netora.Infrastructure.Identity;

namespace Netora.WebApi.Controllers;

/// <summary>Settings (13.02). Managers only. Secrets are never returned.</summary>
[ApiController]
[Route("api/v1/settings")]
[Authorize(Policy = Policies.ManagerOnly)]
public sealed class SettingsController(ISettingsService settings, Netora.Application.Router.IRouterConnectionTester tester) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<SettingsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(ApiResponse<SettingsDto>.Ok(await settings.GetAsync(cancellationToken), HttpContext.TraceIdentifier));

    /// <summary>Saves router details. NewPassword is write-only; leave it empty to keep the stored one.</summary>
    [HttpPut("router")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateRouter([FromBody] UpdateRouterSettingsRequest request, CancellationToken cancellationToken)
        => Map(await settings.UpdateRouterAsync(request, ActorId, cancellationToken));

    /// <summary>Reads the router identity and RouterOS version with the saved details, and remembers the result.</summary>
    [HttpPost("router/test")]
    [ProducesResponseType(typeof(ApiResponse<Netora.Application.Router.RouterTestResultDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> TestRouter(CancellationToken cancellationToken)
        => Ok(ApiResponse<Netora.Application.Router.RouterTestResultDto>.Ok(await tester.TestAsync(ActorId, cancellationToken), HttpContext.TraceIdentifier));

    [HttpPut("email")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateEmail([FromBody] UpdateEmailSettingsRequest request, CancellationToken cancellationToken)
        => Map(await settings.UpdateEmailAsync(request, ActorId, cancellationToken));

    /// <summary>Sends a test email to your own address now. 200 with Sent=false and the reason when sending fails.</summary>
    [HttpPost("email/test")]
    [ProducesResponseType(typeof(ApiResponse<TestEmailResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<TestEmailResultDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TestEmail(CancellationToken cancellationToken)
    {
        var result = await settings.SendTestEmailAsync(ActorId, cancellationToken);
        var t = HttpContext.TraceIdentifier;
        return result.Outcome switch
        {
            SettingsOutcome.Ok => Ok(ApiResponse<TestEmailResultDto>.Ok(result.Test!, t)),
            SettingsOutcome.NoRecipient => Conflict(ApiResponse<TestEmailResultDto>.Fail("no_recipient", "Your account has no email address. Add one in My Profile.", t)),
            SettingsOutcome.NotConfigured => Conflict(ApiResponse<TestEmailResultDto>.Fail("not_configured", "Set the SMTP server and the sender address first.", t)),
            _ => Map(result),
        };
    }

    private IActionResult Map(SettingsResult result) => result.Outcome == SettingsOutcome.Ok
        ? Ok(ApiResponse<object>.Ok(new { saved = true }, HttpContext.TraceIdentifier))
        : BadRequest(ApiResponse<object>.Fail("validation_failed", "One or more validation errors occurred.", HttpContext.TraceIdentifier, result.Errors));

    private string ActorId => User.FindFirst(NetoraClaimTypes.Subject)?.Value ?? string.Empty;
}
