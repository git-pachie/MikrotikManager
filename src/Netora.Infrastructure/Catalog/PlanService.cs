using FluentValidation;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Netora.Application.Catalog;
using Netora.Application.Customers;
using Netora.Application.Logging;
using Netora.Application.Router;
using Netora.Domain.Plans;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Catalog;

/// <summary>
/// Internet Plans (05.01). Managers change the catalogue; editing a plan never touches customers or the router:
/// moving a customer to another plan is a Manager-approved Change Plan request (05.02), and a changed profile shows up
/// as a Configuration Mismatch on the customers already on the plan instead of being pushed silently.
/// </summary>
internal sealed class PlanService(
    NetoraDbContext db,
    IValidator<SavePlanRequest> validator,
    IRouterClient router,
    ILogger<PlanService> logger,
    IActivityLog? activity = null) : IPlanService
{
    private IActivityLog Log => activity ?? NullActivityLog.Instance;

    public async Task<PlanListDto> ListAsync(bool viewerIsManager, CancellationToken cancellationToken)
    {
        var visible = Visible(viewerIsManager);
        var items = await db.InternetPlans.AsNoTracking()
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.Name)
            .Select(p => new PlanListItemDto(p.Id, p.Name, p.MonthlyFee, p.DownloadMbps, p.UploadMbps, p.RouterProfile, p.IsActive,
                visible.Count(c => c.PlanId == p.Id)))
            .ToListAsync(cancellationToken);
        return new PlanListDto(items);
    }

    public async Task<PlanDetailDto?> GetAsync(int id, bool viewerIsManager, CancellationToken cancellationToken)
    {
        var visible = Visible(viewerIsManager);
        return await db.InternetPlans.AsNoTracking().Where(p => p.Id == id)
            .Select(p => new PlanDetailDto(p.Id, p.Name, p.MonthlyFee, p.DownloadMbps, p.UploadMbps, p.RouterProfile, p.IsActive,
                visible.Count(c => c.PlanId == p.Id), p.ConcurrencyStamp, p.CreatedAtUtc, p.UpdatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CatalogResult> CreateAsync(SavePlanRequest request, CustomerActor actor, CancellationToken cancellationToken)
    {
        if (!actor.IsManager) return new CatalogResult(CatalogOutcome.Forbidden);
        var invalid = await ValidateAsync(request, cancellationToken);
        if (invalid is not null) return invalid;

        var name = request.Name.Trim();
        if (await NameTakenAsync(name, null, cancellationToken)) return Duplicate();

        var plan = new InternetPlan
        {
            Name = name,
            MonthlyFee = request.MonthlyFee,
            DownloadMbps = request.DownloadMbps,
            UploadMbps = request.UploadMbps,
            RouterProfile = request.RouterProfile.Trim(),
            IsActive = request.IsActive,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = actor.UserId,
        };
        db.InternetPlans.Add(plan);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Duplicate();
        }

        logger.LogInformation("PlanCreated PlanId={PlanId} ActorUserId={ActorUserId}", plan.Id, actor.UserId);
        Log.Write(new ActivityEvent(ActivityCategories.Plans, "PlanCreated", ActivityOutcomes.Success, ActivityTargets.Plan(plan.Id),
            plan.IsActive ? null : "Inactive", Actor: actor.UserName));
        return new CatalogResult(CatalogOutcome.Ok, plan.Id);
    }

    public async Task<CatalogResult> UpdateAsync(int id, SavePlanRequest request, CustomerActor actor, CancellationToken cancellationToken)
    {
        if (!actor.IsManager) return new CatalogResult(CatalogOutcome.Forbidden);
        var invalid = await ValidateAsync(request, cancellationToken);
        if (invalid is not null) return invalid;

        var plan = await db.InternetPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is null) return new CatalogResult(CatalogOutcome.NotFound);
        if (!string.Equals(request.ConcurrencyStamp, plan.ConcurrencyStamp, StringComparison.Ordinal))
            return new CatalogResult(CatalogOutcome.Conflict);

        var name = request.Name.Trim();
        if (!string.Equals(name, plan.Name, StringComparison.Ordinal) && await NameTakenAsync(name, id, cancellationToken))
            return Duplicate();

        var changed = new List<string>();
        void Set<T>(string label, T current, T next, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, next)) return;
            assign(next);
            changed.Add(label);
        }

        var wasActive = plan.IsActive;
        Set("Name", plan.Name, name, v => plan.Name = v);
        Set("Monthly Fee", plan.MonthlyFee, request.MonthlyFee, v => plan.MonthlyFee = v);
        Set("Download", plan.DownloadMbps, request.DownloadMbps, v => plan.DownloadMbps = v);
        Set("Upload", plan.UploadMbps, request.UploadMbps, v => plan.UploadMbps = v);
        Set("MikroTik Profile", plan.RouterProfile, request.RouterProfile.Trim(), v => plan.RouterProfile = v);
        Set("Status", plan.IsActive, request.IsActive, v => plan.IsActive = v);
        if (changed.Count == 0) return new CatalogResult(CatalogOutcome.NoChanges, id);

        // The UPDATE only matches if nobody saved in between.
        db.Entry(plan).Property(p => p.ConcurrencyStamp).OriginalValue = plan.ConcurrencyStamp;
        plan.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        plan.UpdatedAtUtc = DateTimeOffset.UtcNow;
        plan.UpdatedByUserId = actor.UserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new CatalogResult(CatalogOutcome.Conflict);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Duplicate();
        }

        logger.LogInformation("PlanUpdated PlanId={PlanId} Fields={Fields} ActorUserId={ActorUserId}", id, string.Join(",", changed), actor.UserId);
        Log.Write(new ActivityEvent(ActivityCategories.Plans, "PlanUpdated", ActivityOutcomes.Success, ActivityTargets.Plan(id),
            "Changed: " + string.Join(", ", changed), Actor: actor.UserName));
        if (wasActive != plan.IsActive)
            Log.Write(new ActivityEvent(ActivityCategories.Plans, plan.IsActive ? "PlanReactivated" : "PlanDeactivated",
                ActivityOutcomes.Success, ActivityTargets.Plan(id), Actor: actor.UserName));
        return new CatalogResult(CatalogOutcome.Ok, id);
    }

    public async Task<PlanProfileCheckDto> CheckRouterAsync(CancellationToken cancellationToken)
    {
        var plans = await db.InternetPlans.AsNoTracking().OrderByDescending(p => p.IsActive).ThenBy(p => p.Name).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<string> profiles;
        try
        {
            profiles = await router.ListProfilesAsync(cancellationToken);
        }
        catch (RouterException ex)
        {
            Log.Write(new ActivityEvent(ActivityCategories.MikroTik, "RouterError", ActivityOutcomes.Failure,
                Detail: $"step=ProfileCheck kind={ex.Kind} mode={router.Mode}", Level: ActivityLevel.Warning));
            return new PlanProfileCheckDto(router.Mode, false, ex.Message, now,
                plans.Select(p => new PlanProfileStatusDto(p.Id, p.Name, p.RouterProfile, p.IsActive, false)).ToList(), []);
        }

        var onRouter = new HashSet<string>(profiles, StringComparer.Ordinal);
        var mapped = new HashSet<string>(plans.Select(p => p.RouterProfile), StringComparer.Ordinal);
        return new PlanProfileCheckDto(router.Mode, true, null, now,
            plans.Select(p => new PlanProfileStatusDto(p.Id, p.Name, p.RouterProfile, p.IsActive, onRouter.Contains(p.RouterProfile))).ToList(),
            profiles.Where(x => !mapped.Contains(x)).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Customers the viewer may count: Manager-only customers are left out for Staff.</summary>
    private IQueryable<Domain.Customers.Customer> Visible(bool viewerIsManager)
        => viewerIsManager ? db.Customers.AsNoTracking() : db.Customers.AsNoTracking().Where(c => !c.IsManagerOnly);

    private async Task<CatalogResult?> ValidateAsync(SavePlanRequest request, CancellationToken ct)
    {
        var v = await validator.ValidateAsync(request, ct);
        return v.IsValid ? null : new CatalogResult(CatalogOutcome.ValidationFailed, Errors:
            v.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
    }

    private Task<bool> NameTakenAsync(string name, int? exceptId, CancellationToken ct)
    {
        var upper = name.ToUpperInvariant();
        return db.InternetPlans.AnyAsync(p => p.Name.ToUpper() == upper && p.Id != exceptId, ct);
    }

    private static CatalogResult Duplicate() => new(CatalogOutcome.Duplicate, Errors: new Dictionary<string, string[]>
    {
        [nameof(SavePlanRequest.Name)] = ["Another plan already has this name."],
    });

    internal static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2601 or 2627 };
}
