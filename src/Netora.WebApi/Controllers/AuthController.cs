using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Infrastructure.Identity;

namespace Netora.WebApi.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService authService, IValidator<LoginRequest> loginValidator) : ControllerBase
{
    /// <summary>Validates credentials and returns a short-lived access token. Called by the WebUI server.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<LoginResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LoginResultDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<LoginResultDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<LoginResultDto>), StatusCodes.Status423Locked)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var validation = await loginValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            var details = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            return BadRequest(ApiResponse<LoginResultDto>.Fail("validation_failed",
                "One or more validation errors occurred.", HttpContext.TraceIdentifier, details));
        }

        var result = await authService.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return result.Outcome switch
        {
            LoginOutcome.Succeeded => Ok(ApiResponse<LoginResultDto>.Ok(result.Data!, HttpContext.TraceIdentifier)),
            LoginOutcome.LockedOut => StatusCode(StatusCodes.Status423Locked, ApiResponse<LoginResultDto>.Fail(
                "locked_out", "This account is temporarily locked. Try again later.", HttpContext.TraceIdentifier)),
            _ => Unauthorized(ApiResponse<LoginResultDto>.Fail(
                "invalid_credentials", "Invalid user name or password.", HttpContext.TraceIdentifier)),
        };
    }

    /// <summary>Returns the signed-in user, with the role read from the database.</summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<CurrentUserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(NetoraClaimTypes.Subject)?.Value;
        var user = userId is null ? null : await authService.GetCurrentUserAsync(userId, cancellationToken);
        return user is null
            ? Unauthorized(ApiResponse<CurrentUserDto>.Fail("unauthorized", "Authentication is required.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<CurrentUserDto>.Ok(user, HttpContext.TraceIdentifier));
    }

    /// <summary>Records a logout security event. Tokens are short-lived and also invalidated by a security stamp change.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(NetoraClaimTypes.Subject)?.Value;
        if (userId is not null)
            await authService.RecordLogoutAsync(userId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { }, HttpContext.TraceIdentifier));
    }
}
