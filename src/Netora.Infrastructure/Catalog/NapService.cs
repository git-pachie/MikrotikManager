using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Netora.Application.Catalog;
using Netora.Application.Customers;
using Netora.Application.Logging;
using Netora.Domain.Customers;
using Netora.Domain.Network;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Catalog;

/// <summary>
/// NAP Boxes (06.01). Managers change the list; UNKNOWN is built in and read only. Customer counts and the assigned
/// customers are filtered by the viewer's role first, so Staff never see or count Manager-only customers.
/// </summary>
internal sealed class NapService(
    NetoraDbContext db,
    IValidator<SaveNapRequest> validator,
    ILogger<NapService> logger,
    IActivityLog? activity = null) : INapService
{
    /// <summary>Cap on the Assigned Customers table. NAP boxes serve far fewer customers in practice.</summary>
    public const int MaxAssignedShown = 500;

    private IActivityLog Log => activity ?? NullActivityLog.Instance;

    public async Task<NapListDto> ListAsync(bool viewerIsManager, CancellationToken cancellationToken)
    {
        var visible = Visible(viewerIsManager);
        var items = await db.NapBoxes.AsNoTracking()
            .OrderBy(n => n.IsSystem).ThenByDescending(n => n.IsActive).ThenBy(n => n.Code)
            .Select(n => new NapListItemDto(n.Id, n.Code, n.Area, n.Description, n.IsActive, n.IsSystem,
                visible.Count(c => c.NapBoxId == n.Id)))
            .ToListAsync(cancellationToken);
        return new NapListDto(items);
    }

    public async Task<NapDetailDto?> GetAsync(int id, bool viewerIsManager, CancellationToken cancellationToken)
    {
        var nap = await db.NapBoxes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (nap is null) return null;

        var rows = await Visible(viewerIsManager).Where(c => c.NapBoxId == id)
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ThenBy(c => c.Id)
            .Take(MaxAssignedShown)
            .Select(c => new { c.Id, c.FirstName, c.LastName, c.PppoeUsername, PlanName = c.Plan.Name, c.Status, c.ConcurrencyStamp })
            .ToListAsync(cancellationToken);
        var customers = rows.Select(c => new NapCustomerDto(
                c.Id, Customer.DisplayId(c.Id), string.IsNullOrWhiteSpace(c.LastName) ? c.FirstName : $"{c.FirstName} {c.LastName}",
                c.PppoeUsername, c.PlanName, c.Status.ToString(), c.ConcurrencyStamp,
                CanMove: c.Status != CustomerStatus.Terminated || viewerIsManager))
            .ToList();

        // Moving to UNKNOWN is allowed (unassign); inactive NAP boxes take no new customers.
        var targets = await db.NapBoxes.AsNoTracking()
            .Where(n => n.Id != id && (n.IsActive || n.IsSystem))
            .OrderBy(n => n.IsSystem).ThenBy(n => n.Code).Select(n => n.Code)
            .ToListAsync(cancellationToken);

        return new NapDetailDto(nap.Id, nap.Code, nap.Area, nap.Description, nap.IsActive, nap.IsSystem, nap.ConcurrencyStamp,
            nap.CreatedAtUtc, nap.UpdatedAtUtc, customers, targets);
    }

    public async Task<CatalogResult> CreateAsync(SaveNapRequest request, CustomerActor actor, CancellationToken cancellationToken)
    {
        if (!actor.IsManager) return new CatalogResult(CatalogOutcome.Forbidden);
        var invalid = await ValidateAsync(request, cancellationToken);
        if (invalid is not null) return invalid;

        var code = request.Code.Trim();
        if (await CodeTakenAsync(code, null, cancellationToken)) return Duplicate();

        var nap = new NapBox
        {
            Code = code,
            Area = Clean(request.Area),
            Description = Clean(request.Description),
            IsActive = request.IsActive,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = actor.UserId,
        };
        db.NapBoxes.Add(nap);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PlanService.IsUniqueViolation(ex))
        {
            return Duplicate();
        }

        await AuditTrail.RecordAsync(db, logger, AuditTrail.New(actor, Domain.Audit.AuditEntityTypes.NapBox, nap.Id, nap.Code, "NapCreated",
            nap.IsActive ? null : "Inactive"), cancellationToken);
        logger.LogInformation("NapCreated NapBoxId={NapBoxId} ActorUserId={ActorUserId}", nap.Id, actor.UserId);
        Log.Write(new ActivityEvent(ActivityCategories.Nap, "NapCreated", ActivityOutcomes.Success, ActivityTargets.Nap(nap.Id),
            nap.IsActive ? null : "Inactive", Actor: actor.UserName));
        return new CatalogResult(CatalogOutcome.Ok, nap.Id);
    }

    public async Task<CatalogResult> UpdateAsync(int id, SaveNapRequest request, CustomerActor actor, CancellationToken cancellationToken)
    {
        if (!actor.IsManager) return new CatalogResult(CatalogOutcome.Forbidden);

        var nap = await db.NapBoxes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (nap is null) return new CatalogResult(CatalogOutcome.NotFound);
        if (nap.IsSystem) return new CatalogResult(CatalogOutcome.Locked);

        var invalid = await ValidateAsync(request, cancellationToken);
        if (invalid is not null) return invalid;
        if (!string.Equals(request.ConcurrencyStamp, nap.ConcurrencyStamp, StringComparison.Ordinal))
            return new CatalogResult(CatalogOutcome.Conflict);

        var code = request.Code.Trim();
        if (!string.Equals(code, nap.Code, StringComparison.Ordinal) && await CodeTakenAsync(code, id, cancellationToken))
            return Duplicate();

        var changed = new List<string>();
        void Set<T>(string label, T current, T next, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, next)) return;
            assign(next);
            changed.Add(label);
        }

        var wasActive = nap.IsActive;
        Set("NAP Code", nap.Code, code, v => nap.Code = v);
        Set("Area", nap.Area, Clean(request.Area), v => nap.Area = v);
        Set("Description", nap.Description, Clean(request.Description), v => nap.Description = v);
        Set("Status", nap.IsActive, request.IsActive, v => nap.IsActive = v);
        if (changed.Count == 0) return new CatalogResult(CatalogOutcome.NoChanges, id);

        nap.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        nap.UpdatedAtUtc = DateTimeOffset.UtcNow;
        nap.UpdatedByUserId = actor.UserId;
        db.AuditEvents.Add(AuditTrail.New(actor, Domain.Audit.AuditEntityTypes.NapBox, id, nap.Code, "NapUpdated", "Changed: " + string.Join(", ", changed)));
        if (wasActive != nap.IsActive)
            db.AuditEvents.Add(AuditTrail.New(actor, Domain.Audit.AuditEntityTypes.NapBox, id, nap.Code, nap.IsActive ? "NapReactivated" : "NapDeactivated", null));
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new CatalogResult(CatalogOutcome.Conflict);
        }
        catch (DbUpdateException ex) when (PlanService.IsUniqueViolation(ex))
        {
            return Duplicate();
        }

        logger.LogInformation("NapUpdated NapBoxId={NapBoxId} Fields={Fields} ActorUserId={ActorUserId}", id, string.Join(",", changed), actor.UserId);
        Log.Write(new ActivityEvent(ActivityCategories.Nap, "NapUpdated", ActivityOutcomes.Success, ActivityTargets.Nap(id),
            "Changed: " + string.Join(", ", changed), Actor: actor.UserName));
        if (wasActive != nap.IsActive)
            Log.Write(new ActivityEvent(ActivityCategories.Nap, nap.IsActive ? "NapReactivated" : "NapDeactivated",
                ActivityOutcomes.Success, ActivityTargets.Nap(id), Actor: actor.UserName));
        return new CatalogResult(CatalogOutcome.Ok, id);
    }

    private IQueryable<Customer> Visible(bool viewerIsManager)
        => viewerIsManager ? db.Customers.AsNoTracking() : db.Customers.AsNoTracking().Where(c => !c.IsManagerOnly);

    private async Task<CatalogResult?> ValidateAsync(SaveNapRequest request, CancellationToken ct)
    {
        var v = await validator.ValidateAsync(request, ct);
        return v.IsValid ? null : new CatalogResult(CatalogOutcome.ValidationFailed, Errors:
            v.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
    }

    private Task<bool> CodeTakenAsync(string code, int? exceptId, CancellationToken ct)
    {
        var upper = code.ToUpperInvariant();
        return db.NapBoxes.AnyAsync(n => n.Code.ToUpper() == upper && n.Id != exceptId, ct);
    }

    private static CatalogResult Duplicate() => new(CatalogOutcome.Duplicate, Errors: new Dictionary<string, string[]>
    {
        [nameof(SaveNapRequest.Code)] = ["Another NAP box already has this code."],
    });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
