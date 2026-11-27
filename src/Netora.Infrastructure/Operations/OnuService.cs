using Microsoft.EntityFrameworkCore;
using Netora.Application.Customers;
using Netora.Application.Operations;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Operations;

internal sealed class OnuService(NetoraDbContext db) : IOnuService
{
    private const int PageSize = 25;

    public async Task<OnuHistoryDto?> ListAsync(int? customerId, string? search, int page, bool isManager,
        CancellationToken cancellationToken)
    {
        var customers = db.Customers.AsNoTracking().Where(c => isManager || !c.IsManagerOnly);
        if (customerId is { } id)
        {
            customers = customers.Where(c => c.Id == id);
            if (!await customers.AnyAsync(cancellationToken)) return null;
        }

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            term = term[..Math.Min(term.Length, 100)];
            customers = customers.Where(c => (c.FirstName + " " + c.LastName).Contains(term) || c.PppoeUsername.Contains(term)
                || (c.OnuSerial != null && c.OnuSerial.Contains(term))
                || db.OnuReplacements.Any(r => r.CustomerId == c.Id
                    && ((r.PreviousSerial != null && r.PreviousSerial.Contains(term)) || (r.NewSerial != null && r.NewSerial.Contains(term)))));
        }

        page = Math.Clamp(page, 1, int.MaxValue / PageSize);
        var offset = (page - 1) * PageSize;
        var devices = await customers.Where(c => customerId != null || c.OnuSerial != null || c.OnuModel != null)
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ThenBy(c => c.Id)
            .Select(c => new OnuDeviceDto(c.Id, c.FirstName + " " + c.LastName, c.PppoeUsername, c.OnuSerial,
                c.OnuModel, c.Nap.Code, c.Status.ToString()))
            .Skip(offset).Take(PageSize + 1).ToListAsync(cancellationToken);
        var history = await (from r in db.OnuReplacements.AsNoTracking()
                             join c in customers on r.CustomerId equals c.Id
                             orderby r.ReplacedOn descending, r.Id descending
                             select new OnuReplacementDto(c.Id, c.FirstName + " " + c.LastName, r.PreviousSerial,
                                 r.PreviousModel, r.NewSerial, r.NewModel, r.ReplacedOn, r.Reason, r.Operator))
            .Skip(offset).Take(PageSize + 1).ToListAsync(cancellationToken);
        return new OnuHistoryDto(devices.Take(PageSize).ToList(), history.Take(PageSize).ToList(), page,
            devices.Count > PageSize, history.Count > PageSize);
    }

    public async Task<UpdateCustomerResult> ReplaceAsync(int customerId, ReplaceOnuRequest request, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId && (actor.IsManager || !c.IsManagerOnly),
            cancellationToken);
        if (customer is null) return new(UpdateCustomerOutcome.NotFound);
        if (!customer.CanBeChangedBy(actor.IsManager)) return new(UpdateCustomerOutcome.Locked);
        if (!string.Equals(customer.ConcurrencyStamp, request.ConcurrencyStamp, StringComparison.Ordinal))
            return new(UpdateCustomerOutcome.Conflict);

        var serial = request.Serial?.Trim();
        var model = request.DeviceModel?.Trim();
        var reason = request.Reason?.Trim();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var errors = new Dictionary<string, string[]>();
        if (!Valid(serial, 50)) errors[nameof(request.Serial)] = ["Enter a serial number of up to 50 characters."];
        if (!Valid(model, 100)) errors[nameof(request.DeviceModel)] = ["Enter a brand/model of up to 100 characters."];
        if (!Valid(reason, 500)) errors[nameof(request.Reason)] = ["Enter a reason of up to 500 characters."];
        if (request.ReplacedOn == default || request.ReplacedOn > today)
            errors[nameof(request.ReplacedOn)] = ["Enter a replacement date on or before today."];
        if (string.Equals(serial, customer.OnuSerial, StringComparison.OrdinalIgnoreCase)
            && string.Equals(model, customer.OnuModel, StringComparison.OrdinalIgnoreCase))
            errors[nameof(request.Serial)] = ["Enter a different device."];
        if (errors.Count > 0) return new(UpdateCustomerOutcome.ValidationFailed, errors);

        db.OnuReplacements.Add(new OnuReplacement
        {
            CustomerId = customer.Id, PreviousSerial = customer.OnuSerial, PreviousModel = customer.OnuModel,
            NewSerial = serial, NewModel = model, ReplacedOn = request.ReplacedOn, Reason = reason!,
            OperatorId = actor.UserId, Operator = actor.UserName,
        });
        var now = DateTimeOffset.UtcNow;
        db.CustomerActivities.Add(new CustomerActivity
        {
            CustomerId = customer.Id, Action = "OnuReplaced", ActorUserId = actor.UserId, ActorUserName = actor.UserName,
            OccurredAtUtc = now,
        });
        customer.OnuSerial = serial;
        customer.OnuModel = model;
        customer.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        customer.UpdatedAtUtc = now;
        customer.UpdatedByUserId = actor.UserId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return new(UpdateCustomerOutcome.Updated);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new(UpdateCustomerOutcome.Conflict);
        }
    }

    private static bool Valid(string? value, int limit)
        => value is { Length: > 0 } && value.Length <= limit && !value.Any(char.IsControl);
}
