using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Catalog;
using Netora.Application.Common;
using Netora.Application.Customers;
using Netora.Infrastructure.Identity;

namespace Netora.WebApi.Controllers;

/// <summary>Internet Plans (05). Everyone signed in reads; only Managers add, edit and deactivate.</summary>
[ApiController]
[Route("api/v1/plans")]
public sealed class PlansController(IPlanService plans) : ControllerBase
{
    /// <summary>All plans. sortBy: plan, fee or profile (anything else: active first, then name); sortDir: asc or desc.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PlanListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? sortBy, [FromQuery] string? sortDir, CancellationToken cancellationToken)
    {
        var column = PlanSort.Columns.Contains(sortBy?.Trim().ToLowerInvariant()) ? sortBy!.Trim().ToLowerInvariant() : null;
        var result = await plans.ListAsync(this.IsManager(), cancellationToken, column,
            string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase));
        return Ok(ApiResponse<PlanListDto>.Ok(result, HttpContext.TraceIdentifier));
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PlanDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PlanDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var plan = await plans.GetAsync(id, this.IsManager(), cancellationToken);
        return plan is null
            ? NotFound(ApiResponse<PlanDetailDto>.Fail("not_found", "Plan not found.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<PlanDetailDto>.Ok(plan, HttpContext.TraceIdentifier));
    }

    [HttpPost]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ProducesResponseType(typeof(ApiResponse<CatalogSavedDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<CatalogSavedDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<CatalogSavedDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] SavePlanRequest request, CancellationToken cancellationToken)
        => CatalogResultMapper.Map(this, await plans.CreateAsync(request, this.Actor(), cancellationToken), "Plan", created: true);

    /// <summary>Send the ConcurrencyStamp read with the plan; a stale one returns 409. Customers and the router are not changed.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ProducesResponseType(typeof(ApiResponse<CatalogSavedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CatalogSavedDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, [FromBody] SavePlanRequest request, CancellationToken cancellationToken)
        => CatalogResultMapper.Map(this, await plans.UpdateAsync(id, request, this.Actor(), cancellationToken), "Plan");

    /// <summary>
    /// Creates the plan's PPP profile on the router if it is missing, or sets its rate-limit to the plan's speeds
    /// (upload/download). Changes nothing else and never deletes. 400 when the plan has no speeds; 502 when the router fails.
    /// </summary>
    [HttpPost("{id:int}/send-to-router")]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ProducesResponseType(typeof(ApiResponse<PlanSentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PlanSentDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PlanSentDto>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SendToRouter(int id, CancellationToken cancellationToken)
    {
        var result = await plans.SendProfileToRouterAsync(id, this.Actor(), cancellationToken);
        var t = HttpContext.TraceIdentifier;
        return result.Outcome switch
        {
            CatalogOutcome.Ok => Ok(ApiResponse<PlanSentDto>.Ok(result.Data!, t)),
            CatalogOutcome.ValidationFailed => BadRequest(ApiResponse<PlanSentDto>.Fail("validation_failed", "One or more validation errors occurred.", t, result.Errors)),
            CatalogOutcome.NotFound => NotFound(ApiResponse<PlanSentDto>.Fail("not_found", "Plan not found.", t)),
            CatalogOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden, ApiResponse<PlanSentDto>.Fail("forbidden", "Only Managers can change this.", t)),
            CatalogOutcome.RouterFailed => StatusCode(StatusCodes.Status502BadGateway, ApiResponse<PlanSentDto>.Fail("router_failed", result.Error ?? "The router failed.", t)),
            _ => throw new InvalidOperationException($"Unhandled outcome {result.Outcome}"),
        };
    }

    /// <summary>Lists the router's PPP profiles and shows which plans' profiles exist and whether their speeds match. Changes nothing.</summary>
    [HttpGet("router-check")]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ProducesResponseType(typeof(ApiResponse<PlanProfileCheckDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RouterCheck(CancellationToken cancellationToken)
        => Ok(ApiResponse<PlanProfileCheckDto>.Ok(await plans.CheckRouterAsync(cancellationToken), HttpContext.TraceIdentifier));
}

/// <summary>NAP Boxes (06). Everyone signed in reads; only Managers add and edit. UNKNOWN is read only.</summary>
[ApiController]
[Route("api/v1/nap-boxes")]
public sealed class NapBoxesController(INapService naps) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<NapListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
        => Ok(ApiResponse<NapListDto>.Ok(await naps.ListAsync(this.IsManager(), cancellationToken), HttpContext.TraceIdentifier));

    /// <summary>One NAP box with its assigned customers (Manager-only customers left out for Staff).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<NapDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<NapDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var nap = await naps.GetAsync(id, this.IsManager(), cancellationToken);
        return nap is null
            ? NotFound(ApiResponse<NapDetailDto>.Fail("not_found", "NAP box not found.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<NapDetailDto>.Ok(nap, HttpContext.TraceIdentifier));
    }

    [HttpPost]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ProducesResponseType(typeof(ApiResponse<CatalogSavedDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<CatalogSavedDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<CatalogSavedDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] SaveNapRequest request, CancellationToken cancellationToken)
        => CatalogResultMapper.Map(this, await naps.CreateAsync(request, this.Actor(), cancellationToken), "NAP box", created: true);

    [HttpPut("{id:int}")]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ProducesResponseType(typeof(ApiResponse<CatalogSavedDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CatalogSavedDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, [FromBody] SaveNapRequest request, CancellationToken cancellationToken)
        => CatalogResultMapper.Map(this, await naps.UpdateAsync(id, request, this.Actor(), cancellationToken), "NAP box");
}

internal static class CatalogControllerExtensions
{
    public static bool IsManager(this ControllerBase c) => c.User.IsInRole(Roles.Manager);

    public static CustomerActor Actor(this ControllerBase c)
    {
        var id = c.User.FindFirst(NetoraClaimTypes.Subject)?.Value ?? string.Empty;
        return new CustomerActor(id, c.User.FindFirst(NetoraClaimTypes.Name)?.Value ?? id, c.IsManager());
    }
}

internal static class CatalogResultMapper
{
    public static IActionResult Map(ControllerBase c, CatalogResult result, string what, bool created = false)
    {
        var t = c.HttpContext.TraceIdentifier;
        return result.Outcome switch
        {
            CatalogOutcome.Ok when created => c.StatusCode(StatusCodes.Status201Created, ApiResponse<CatalogSavedDto>.Ok(new CatalogSavedDto(result.Id!.Value, true), t)),
            CatalogOutcome.Ok => c.Ok(ApiResponse<CatalogSavedDto>.Ok(new CatalogSavedDto(result.Id!.Value, true), t)),
            CatalogOutcome.NoChanges => c.Ok(ApiResponse<CatalogSavedDto>.Ok(new CatalogSavedDto(result.Id!.Value, false), t)),
            CatalogOutcome.ValidationFailed => c.BadRequest(ApiResponse<CatalogSavedDto>.Fail("validation_failed", "One or more validation errors occurred.", t, result.Errors)),
            CatalogOutcome.NotFound => c.NotFound(ApiResponse<CatalogSavedDto>.Fail("not_found", $"{what} not found.", t)),
            CatalogOutcome.Forbidden => c.StatusCode(StatusCodes.Status403Forbidden, ApiResponse<CatalogSavedDto>.Fail("forbidden", "Only Managers can change this.", t)),
            CatalogOutcome.Duplicate => c.Conflict(ApiResponse<CatalogSavedDto>.Fail("duplicate", $"Another {what.ToLowerInvariant()} already uses this name.", t, result.Errors)),
            CatalogOutcome.Conflict => c.Conflict(ApiResponse<CatalogSavedDto>.Fail("concurrency_conflict", $"This {what.ToLowerInvariant()} was changed by someone else. Reload and try again.", t)),
            CatalogOutcome.Locked => c.Conflict(ApiResponse<CatalogSavedDto>.Fail("locked", "UNKNOWN is built in and cannot be changed.", t)),
            _ => throw new InvalidOperationException($"Unhandled outcome {result.Outcome}"),
        };
    }
}
