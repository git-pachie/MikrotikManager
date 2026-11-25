using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Users;
using Netora.Infrastructure.Identity;
using Netora.WebApi.Infrastructure;

namespace Netora.WebApi.Controllers;

/// <summary>User Management (13.01). Managers only.</summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = Policies.ManagerOnly)]
public sealed class UsersController(IUserAdminService users) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<UserListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(ApiResponse<UserListDto>.Ok(await users.ListAsync(cancellationToken), HttpContext.TraceIdentifier));

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var user = await users.GetAsync(id, ActorId, cancellationToken);
        return user is null
            ? NotFound(ApiResponse<UserDetailDto>.Fail("not_found", "User not found.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<UserDetailDto>.Ok(user, HttpContext.TraceIdentifier));
    }

    /// <summary>Creates an account with a temporary password that the user must change at first sign-in.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<UserChangedDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<UserChangedDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UserChangedDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await users.CreateAsync(request, Actor, cancellationToken);
        return result.Outcome == UserOutcome.Ok
            ? StatusCode(StatusCodes.Status201Created, ApiResponse<UserChangedDto>.Ok(new UserChangedDto(result.UserId!, true), HttpContext.TraceIdentifier))
            : UserResultMapper.Failure(this, result);
    }

    /// <summary>Edits display name, email, role and active state. Stale forms (ConcurrencyStamp) return 409.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(ApiResponse<UserChangedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserChangedDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await users.UpdateAsync(id, request, Actor, cancellationToken);
        return result.Outcome is UserOutcome.Ok or UserOutcome.NoChanges
            ? Ok(ApiResponse<UserChangedDto>.Ok(new UserChangedDto(id, result.Outcome == UserOutcome.Ok), HttpContext.TraceIdentifier))
            : UserResultMapper.Failure(this, result);
    }

    /// <summary>Sets a temporary password, unlocks the account and ends its sessions. Not for your own account.</summary>
    [HttpPost("{id}/reset-password")]
    [ProducesResponseType(typeof(ApiResponse<UserChangedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserChangedDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(string id, [FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await users.ResetPasswordAsync(id, request, Actor, cancellationToken);
        return result.Outcome == UserOutcome.Ok
            ? Ok(ApiResponse<UserChangedDto>.Ok(new UserChangedDto(id, true), HttpContext.TraceIdentifier))
            : UserResultMapper.Failure(this, result);
    }

    private string ActorId => User.FindFirst(NetoraClaimTypes.Subject)?.Value ?? string.Empty;

    private ActorInfo Actor => new(ActorId, User.FindFirst(NetoraClaimTypes.Name)?.Value ?? ActorId,
        HttpContext.Connection.RemoteIpAddress?.ToString());
}

public sealed record UserChangedDto(string Id, bool Changed);

public static class UserResultMapper
{
    public static IActionResult Failure(ControllerBase c, UserResult result)
    {
        var t = c.HttpContext.TraceIdentifier;
        return result.Outcome switch
        {
            UserOutcome.ValidationFailed => c.BadRequest(ApiResponse<object>.Fail("validation_failed", "One or more validation errors occurred.", t, result.Errors)),
            UserOutcome.NotFound => c.NotFound(ApiResponse<object>.Fail("not_found", "User not found.", t)),
            UserOutcome.DuplicateUserName => c.Conflict(ApiResponse<object>.Fail("duplicate_user_name", "This user name is already in use.", t, result.Errors)),
            UserOutcome.DuplicateEmail => c.Conflict(ApiResponse<object>.Fail("duplicate_email", "Another user already has this email address.", t, result.Errors)),
            UserOutcome.Conflict => c.Conflict(ApiResponse<object>.Fail("concurrency_conflict", "This user was changed by someone else. Reload and try again.", t)),
            UserOutcome.LastManager => c.Conflict(ApiResponse<object>.Fail("last_manager", "At least one active Manager must remain.", t)),
            UserOutcome.SelfChangeNotAllowed => c.Conflict(ApiResponse<object>.Fail("self_change_not_allowed",
                "You cannot change your own role, deactivate yourself or reset your own password here. Use My Profile for your password.", t)),
            UserOutcome.WrongPassword => c.BadRequest(ApiResponse<object>.Fail("wrong_password", "The current password is not correct.", t, result.Errors)),
            _ => throw new InvalidOperationException($"Unhandled outcome {result.Outcome}"),
        };
    }
}
