using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Infrastructure.Identity;
using Netora.WebApi.Infrastructure;

namespace Netora.WebApi.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService authService, IValidator<LoginRequest> loginValidator,
    AccessTokenRevocations revocations) : ControllerBase
{
    /// <summary>Validates credentials and returns a short-lived access token. Called by the WebUI server.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(ApiResponse<LoginResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LoginResultDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<LoginResultDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<LoginResultDto>), StatusCodes.Status423Locked)]
    [ProducesResponseType(typeof(ApiResponse<LoginResultDto>), StatusCodes.Status429TooManyRequests)]
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

    /// <summary>Revokes the calling access token and records a logout security event.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(NetoraClaimTypes.Subject)?.Value;
        if (userId is null || !Guid.TryParseExact(User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value, "N", out var tokenId)
            || !long.TryParse(User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var expiration))
            return Unauthorized(ApiResponse<object>.Fail("unauthorized", "The access token is invalid.", HttpContext.TraceIdentifier));

        await revocations.RevokeAsync(tokenId, DateTimeOffset.FromUnixTimeSeconds(expiration) + AuthenticationSetup.TokenClockSkew,
            cancellationToken);
        await authService.RecordLogoutAsync(userId, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return Ok(ApiResponse<object>.Ok(new { }, HttpContext.TraceIdentifier));
    }
}
