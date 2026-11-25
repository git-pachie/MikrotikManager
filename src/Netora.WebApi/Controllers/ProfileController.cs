using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Users;
using Netora.Infrastructure.Identity;

namespace Netora.WebApi.Controllers;

/// <summary>The signed-in user's own account. Available to every role, also while a password change is required.</summary>
[ApiController]
[Route("api/v1/profile")]
public sealed class ProfileController(IProfileService profiles) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ProfileDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var profile = await profiles.GetAsync(UserId, cancellationToken);
        return profile is null
            ? NotFound(ApiResponse<ProfileDto>.Fail("not_found", "Account not found.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<ProfileDto>.Ok(profile, HttpContext.TraceIdentifier));
    }

    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
        => Done(await profiles.UpdateAsync(UserId, request, cancellationToken));

    /// <summary>Saves the theme preference ("light", "dark", or null for none).</summary>
    [HttpPut("theme")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Theme([FromBody] ThemeRequest request, CancellationToken cancellationToken)
        => Done(await profiles.SetThemeAsync(UserId, request, cancellationToken));

    /// <summary>Changes your own password. Returns a new access token, because the old one stops working.</summary>
    [HttpPost("password")]
    [ProducesResponseType(typeof(ApiResponse<LoginResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LoginResultDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await profiles.ChangePasswordAsync(UserId, request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return result.Outcome == UserOutcome.Ok && result.Login is not null
            ? Ok(ApiResponse<LoginResultDto>.Ok(result.Login, HttpContext.TraceIdentifier))
            : UserResultMapper.Failure(this, result);
    }

    private IActionResult Done(UserResult result) => result.Outcome is UserOutcome.Ok or UserOutcome.NoChanges
        ? Ok(ApiResponse<object>.Ok(new { changed = result.Outcome == UserOutcome.Ok }, HttpContext.TraceIdentifier))
        : UserResultMapper.Failure(this, result);

    private string UserId => User.FindFirst(NetoraClaimTypes.Subject)?.Value ?? string.Empty;
}
