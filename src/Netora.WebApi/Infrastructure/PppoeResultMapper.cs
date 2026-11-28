using Microsoft.AspNetCore.Mvc;
using Netora.Application.Common;
using Netora.Application.Pppoe;

namespace Netora.WebApi.Infrastructure;

/// <summary>Maps PPPoE and approval outcomes to HTTP results in the standard envelope. Shared by both controllers.</summary>
public static class PppoeResultMapper
{
    public static IActionResult Failure(ControllerBase controller, PppoeResult result, string notFoundMessage = "PPPoE account not found.")
    {
        var t = controller.HttpContext.TraceIdentifier;
        return result.Outcome switch
        {
            PppoeOutcome.ValidationFailed => controller.BadRequest(ApiResponse<object>.Fail("validation_failed", "One or more validation errors occurred.", t, result.Errors)),
            PppoeOutcome.NotFound => controller.NotFound(ApiResponse<object>.Fail("not_found", notFoundMessage, t)),
            PppoeOutcome.AccountActive => controller.Conflict(ApiResponse<object>.Fail("account_active", "Disable the account first. Active accounts cannot be edited.", t)),
            PppoeOutcome.CustomerLocked => controller.Conflict(ApiResponse<object>.Fail("customer_locked", "Terminated customers are view-only for Staff.", t)),
            PppoeOutcome.ServiceFieldsLocked => controller.Conflict(ApiResponse<object>.Fail("service_fields_locked", "PPPoE Username and Internet Plan cannot be changed for a terminated account.", t, result.Errors)),
            PppoeOutcome.DuplicatePppoeUsername => controller.Conflict(ApiResponse<object>.Fail("duplicate_pppoe_username", "This PPPoE Username is already in use.", t, result.Errors)),
            PppoeOutcome.Conflict => controller.Conflict(ApiResponse<object>.Fail("concurrency_conflict", "This account was changed by someone else. Reload and try again.", t)),
            PppoeOutcome.NotAllowedInCurrentState => controller.Conflict(ApiResponse<object>.Fail("not_allowed_in_current_state", "This request does not fit the account's current state.", t)),
            PppoeOutcome.RequestAlreadyPending => controller.Conflict(ApiResponse<object>.Fail("request_already_pending", "A request for this account is already waiting for a Manager.", t)),
            PppoeOutcome.AlreadyDecided => controller.Conflict(ApiResponse<object>.Fail("already_decided", "This request was already approved or rejected.", t)),
            PppoeOutcome.Forbidden => controller.StatusCode(StatusCodes.Status403Forbidden, ApiResponse<object>.Fail("forbidden", "This action requires the Manager role.", t)),
            PppoeOutcome.DeletionInProgress => controller.Conflict(ApiResponse<object>.Fail("pppoe_deletion_in_progress", "A deletion request already exists for this account. Open its approval details to see the result or retry.", t)),
            PppoeOutcome.PasswordUnavailable => controller.Conflict(ApiResponse<object>.Fail("password_unavailable", "Netora has no readable saved password for this PPPoE account.", t)),
            _ => throw new InvalidOperationException($"Unhandled outcome {result.Outcome}"),
        };
    }
}
