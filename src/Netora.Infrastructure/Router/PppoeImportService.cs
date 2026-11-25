using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Netora.Application.Customers;
using Netora.Application.Pppoe;
using Netora.Application.Router;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Router;

/// <summary>
/// Import of PPP secrets that already exist on the router (04.04). The scan runs only when asked for; nothing is
/// imported until a Manager confirms the selection. Existing usernames are skipped (never duplicated or overwritten),
/// router settings are kept as they are, the router ID is stored, and the NAP is UNKNOWN until reviewed.
/// </summary>
internal sealed class PppoeImportService(
    NetoraDbContext db,
    IRouterClient router,
    IPppoePasswordProtector passwords,
    ILogger<PppoeImportService> logger) : IPppoeImportService
{
    private static readonly Regex UserNamePattern = new(CustomerFieldsValidator<CreateCustomerRequest>.PppoeUsernamePattern);
    private static readonly int[] UniqueViolation = [2601, 2627];

    public async Task<ImportScanDto> ScanAsync(CancellationToken cancellationToken)
    {
        var secrets = await router.ListSecretsAsync(cancellationToken);
        var known = await KnownAsync(cancellationToken);
        var items = secrets.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(s => Classify(s, known)).ToList();
        return new ImportScanDto(router.Mode, items, items.Count(i => i.State == "New"));
    }

    public async Task<ImportResultDto> ImportAsync(ImportRequest request, string actorUserId, string actorUserName, CancellationToken cancellationToken)
    {
        var wanted = new HashSet<string>(request.Names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()), StringComparer.Ordinal);
        var messages = new List<string>();
        if (wanted.Count == 0) return new ImportResultDto(0, 0, ["Select at least one account to import."]);

        // Scan again at confirmation time, so the import uses the router's current values and current SQL Server records.
        var secrets = await router.ListSecretsAsync(cancellationToken);
        var known = await KnownAsync(cancellationToken);
        int imported = 0, skipped = 0;

        foreach (var s in secrets.Where(s => wanted.Contains(s.Name)))
        {
            var candidate = Classify(s, known);
            if (candidate.State != "New")
            {
                skipped++;
                messages.Add($"{s.Name}: skipped ({candidate.Reason ?? candidate.State}).");
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var (first, last) = SplitName(RouterRules.StripMarker(s.Comment), s.Name);
            var customer = new Customer
            {
                FirstName = first,
                LastName = last,
                PppoeUsername = s.Name,
                PlanName = string.IsNullOrWhiteSpace(s.Profile) ? "default" : s.Profile.Trim(),
                NapBox = Customer.UnknownNap,
                Status = s.Disabled ? CustomerStatus.Disabled : CustomerStatus.Active,
                PppoePasswordProtected = string.IsNullOrEmpty(s.Password) ? null : passwords.Protect(s.Password),
                PppoeRemoteAddress = s.RemoteAddress,
                PppoeLocalAddress = s.LocalAddress,
                PppoeComment = Truncate(RouterRules.StripMarker(s.Comment), 200),
                RouterSecretId = s.Id,
                RouterSyncStatus = RouterSyncStatus.Synced,
                CreatedAtUtc = now,
                CreatedByUserId = actorUserId,
            };
            db.Customers.Add(customer);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                db.CustomerActivities.Add(new CustomerActivity
                {
                    CustomerId = customer.Id, Action = "CustomerImported", Detail = $"Imported from the router ({router.Mode}), NAP UNKNOWN",
                    ActorUserId = actorUserId, ActorUserName = actorUserName, Outcome = "Completed", OccurredAtUtc = now,
                });
                await db.SaveChangesAsync(cancellationToken);
                imported++;
                known.Names.Add(s.Name);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && UniqueViolation.Contains(sql.Number))
            {
                db.ChangeTracker.Clear();
                skipped++;
                messages.Add($"{s.Name}: skipped (added by someone else meanwhile).");
            }
        }

        foreach (var missing in wanted.Where(n => secrets.All(s => s.Name != n)))
        {
            skipped++;
            messages.Add($"{missing}: skipped (no longer on the router).");
        }

        logger.LogInformation("PppoeImport Imported={Imported} Skipped={Skipped} ActorUserId={ActorUserId} Mode={Mode}", imported, skipped, actorUserId, router.Mode);
        return new ImportResultDto(imported, skipped, messages);
    }

    private sealed record Known(HashSet<string> Names, Dictionary<string, int> ByName, HashSet<string> SecretIds);

    private async Task<Known> KnownAsync(CancellationToken ct)
    {
        var rows = await db.Customers.AsNoTracking().Select(c => new { c.Id, c.PppoeUsername, c.RouterSecretId }).ToListAsync(ct);
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows) byName.TryAdd(r.PppoeUsername, r.Id);
        return new Known(new HashSet<string>(rows.Select(r => r.PppoeUsername), StringComparer.OrdinalIgnoreCase), byName,
            rows.Where(r => r.RouterSecretId != null).Select(r => r.RouterSecretId!).ToHashSet(StringComparer.Ordinal));
    }

    private static ImportCandidateDto Classify(RouterSecret s, Known known)
    {
        var comment = RouterRules.StripMarker(s.Comment);
        if (known.ByName.TryGetValue(s.Name, out var id) || known.Names.Contains(s.Name))
            return new(s.Name, s.Profile, s.Disabled, comment, "Already imported", "a customer already has this PPPoE username", known.ByName.GetValueOrDefault(s.Name));
        if (known.SecretIds.Contains(s.Id))
            return new(s.Name, s.Profile, s.Disabled, comment, "Already imported", "linked to a customer under another name", null);
        if (!string.IsNullOrEmpty(s.Service) && s.Service is not ("pppoe" or "any"))
            return new(s.Name, s.Profile, s.Disabled, comment, "Not PPPoE", $"service is {s.Service}", null);
        if (!UserNamePattern.IsMatch(s.Name))
            return new(s.Name, s.Profile, s.Disabled, comment, "Cannot import", "username is not 3 to 40 letters, digits, '.', '_' or '-'", null);
        return new(s.Name, s.Profile, s.Disabled, comment, "New", null, null);
    }

    /// <summary>The router comment is often the subscriber's name; otherwise the username stands in until edited.</summary>
    internal static (string First, string Last) SplitName(string? comment, string userName)
    {
        var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (text is null) return (Truncate(userName, 100)!, "(imported)");
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (Truncate(parts[0], 100)!, Truncate(parts[1], 100)!) : (Truncate(parts[0], 100)!, "(imported)");
    }

    private static string? Truncate(string? v, int max) => v is null || v.Length <= max ? v : v[..max];
}
