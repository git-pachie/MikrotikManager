using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Netora.Application.Router;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Router;

/// <summary>
/// A pretend RouterOS router for development (Router:Mode = Mock). Behaves like the REST API for PPP secrets and
/// active sessions, and keeps its state in a JSON file so it survives restarts. On first use it copies the existing
/// Active and Disabled customers as secrets, changes one profile (to show a Configuration Mismatch) and adds two
/// secrets that are not in SQL Server (to show Import). Everything it reports is fictional and labelled "Mock".
/// </summary>
internal sealed class MockRouterClient(
    IServiceScopeFactory scopes,
    IOptionsMonitor<RouterOptions> options,
    IHostEnvironment environment,
    ILogger<MockRouterClient> logger) : IRouterClient
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public string Mode => RouterOptions.MockMode;

    public Task<RouterIdentity> GetIdentityAsync(CancellationToken cancellationToken)
        => Task.FromResult(new RouterIdentity("Mock MikroTik", "7.16 (mock)", "Mock router, no real device"));

    public Task<IReadOnlyList<RouterPppoeLog>> ReadPppoeLogsAsync(string name, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<RouterPppoeLog>>([]);

    public Task<IReadOnlyList<RouterSecret>> ListSecretsAsync(CancellationToken cancellationToken, bool includePasswords = true)
        => WithStateAsync<IReadOnlyList<RouterSecret>>(s => s.Secrets.Select(ToSecret)
            .Select(secret => includePasswords ? secret : secret with { Password = null }).ToList(), save: false, cancellationToken);

    public Task<RouterSecret?> FindSecretAsync(string? id, string name, CancellationToken cancellationToken)
        => WithStateAsync(s =>
        {
            var found = (id is null ? null : s.Secrets.FirstOrDefault(x => x.Id == id))
                        ?? s.Secrets.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.Ordinal));
            return found is null ? null : ToSecret(found);
        }, save: false, cancellationToken);

    public Task<RouterSecret> CreateSecretAsync(RouterSecretWrite secret, CancellationToken cancellationToken)
        => WithStateAsync(s =>
        {
            FailIfConfigured();
            if (s.Secrets.Any(x => string.Equals(x.Name, secret.Name, StringComparison.Ordinal)))
                throw new RouterException(RouterErrorKind.Rejected, "The router refused the change: failure: secret with the same name already exists");
            var created = new MockSecret { Id = $"*{++s.NextId:X}", Service = "pppoe" };
            Apply(created, secret);
            s.Secrets.Add(created);
            return ToSecret(created);
        }, save: true, cancellationToken);

    public Task<RouterSecret> UpdateSecretAsync(string id, RouterSecretWrite secret, CancellationToken cancellationToken)
        => WithStateAsync(s =>
        {
            FailIfConfigured();
            var existing = s.Secrets.FirstOrDefault(x => x.Id == id) ?? throw new RouterException(RouterErrorKind.NotFound, "The router has no such record.");
            if (s.Secrets.Any(x => x.Id != id && string.Equals(x.Name, secret.Name, StringComparison.Ordinal)))
                throw new RouterException(RouterErrorKind.Rejected, "The router refused the change: failure: secret with the same name already exists");
            Apply(existing, secret);
            if (existing.Disabled) existing.Online = false;
            return ToSecret(existing);
        }, save: true, cancellationToken);

    public Task<RouterActiveSession?> FindActiveSessionAsync(string name, CancellationToken cancellationToken)
        => WithStateAsync(s =>
        {
            var x = s.Secrets.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal));
            return x is { Online: true, Disabled: false } ? new RouterActiveSession(x.Name, x.Address, "3d4h12m", "AA:BB:CC:00:00:" + x.Id.TrimStart('*').PadLeft(2, '0')[^2..]) : null;
        }, save: false, cancellationToken);

    public Task DeleteSecretAsync(string id, CancellationToken cancellationToken)
        => WithStateAsync(s =>
        {
            FailIfConfigured();
            if (s.Secrets.RemoveAll(x => x.Id == id) == 0)
                throw new RouterException(RouterErrorKind.NotFound, "The router has no such record.");
            return true;
        }, save: true, cancellationToken);

    /// <summary>
    /// The mock's PPP profiles: those saved when it was seeded, or for an older state file the profiles its secrets
    /// use. RouterOS always has "default".
    /// </summary>
    public Task<IReadOnlyList<RouterPppProfile>> ListProfilesAsync(CancellationToken cancellationToken)
        => WithStateAsync<IReadOnlyList<RouterPppProfile>>(s => PppProfiles(s).Select(ToProfile).ToList(), save: false, cancellationToken);

    public Task<RouterPppProfile> CreateProfileAsync(string name, string rateLimit, CancellationToken cancellationToken)
        => WithStateAsync(s =>
        {
            FailIfConfigured();
            var list = PppProfiles(s);
            if (list.Any(p => string.Equals(p.Name, name, StringComparison.Ordinal)))
                throw new RouterException(RouterErrorKind.Rejected, "The router refused the change: failure: profile with the same name already exists");
            var created = new MockProfile { Id = $"*P{++s.NextId:X}", Name = name, RateLimit = rateLimit };
            list.Add(created);
            return ToProfile(created);
        }, save: true, cancellationToken);

    public Task<RouterPppProfile> UpdateProfileRateLimitAsync(string id, string rateLimit, CancellationToken cancellationToken)
        => WithStateAsync(s =>
        {
            FailIfConfigured();
            var p = PppProfiles(s).FirstOrDefault(x => x.Id == id) ?? throw new RouterException(RouterErrorKind.NotFound, "The router has no such record.");
            p.RateLimit = rateLimit;
            return ToProfile(p);
        }, save: true, cancellationToken);

    /// <summary>
    /// The mock's PPP profiles. State files written before profiles were stored get them from the older name list or
    /// from the profiles their secrets use. RouterOS always has "default".
    /// </summary>
    private static List<MockProfile> PppProfiles(MockState s)
    {
        if (s.PppProfiles is null)
        {
            var names = (s.Profiles ?? s.Secrets.Select(x => x.Profile).OfType<string>().ToList()).Append("default").Distinct(StringComparer.Ordinal);
            s.PppProfiles = names.Select(n => new MockProfile { Id = $"*P{++s.NextId:X}", Name = n }).ToList();
        }
        return s.PppProfiles;
    }

    private static RouterPppProfile ToProfile(MockProfile p) => new(p.Id, p.Name, p.RateLimit);

    private void FailIfConfigured()
    {
        if (options.CurrentValue.Mock.FailWrites)
            throw new RouterException(RouterErrorKind.Unreachable, "Mock router is set to fail (Router:Mock:FailWrites = true).");
    }

    private static void Apply(MockSecret target, RouterSecretWrite s)
    {
        target.Name = s.Name;
        if (s.Password is not null) target.Password = s.Password;
        target.Profile = s.Profile;
        target.Disabled = s.Disabled;
        target.Comment = s.Comment;
        if (!string.IsNullOrWhiteSpace(s.RemoteAddress)) target.RemoteAddress = s.RemoteAddress;
        if (!string.IsNullOrWhiteSpace(s.LocalAddress)) target.LocalAddress = s.LocalAddress;
    }

    private static RouterSecret ToSecret(MockSecret x)
        => new(x.Id, x.Name, x.Password, x.Profile, x.Disabled, x.Comment, x.RemoteAddress, x.LocalAddress, x.Service);

    private async Task<T> WithStateAsync<T>(Func<MockState, T> action, bool save, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var path = StatePath();
            var state = File.Exists(path)
                ? JsonSerializer.Deserialize<MockState>(await File.ReadAllTextAsync(path, cancellationToken)) ?? new MockState()
                : await SeedAsync(path, cancellationToken);
            var result = action(state);
            if (save)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, Json), cancellationToken);
            }
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    private string StatePath()
    {
        var p = options.CurrentValue.Mock.StatePath;
        return Path.IsPathRooted(p) ? p : Path.Combine(environment.ContentRootPath, p);
    }

    private async Task<MockState> SeedAsync(string path, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NetoraDbContext>();
        var customers = await db.Customers.AsNoTracking().Include(c => c.Plan)
            .Where(c => c.Status == CustomerStatus.Active || c.Status == CustomerStatus.Disabled)
            .OrderBy(c => c.Id).ToListAsync(cancellationToken);

        // Every plan's profile exists on the mock (with the plan's speeds when set), so Check Router Profiles finds them.
        var plans = await db.InternetPlans.AsNoTracking().ToListAsync(cancellationToken);
        var state = new MockState { PppProfiles = [] };
        foreach (var p in plans.GroupBy(p => p.RouterProfile, StringComparer.Ordinal).Select(g => g.First()).Append(null))
        {
            if (p is null && state.PppProfiles.Any(x => x.Name == "default")) continue;
            state.PppProfiles.Add(p is null
                ? new MockProfile { Id = $"*P{++state.NextId:X}", Name = "default" }
                : new MockProfile
                {
                    Id = $"*P{++state.NextId:X}", Name = p.RouterProfile,
                    RateLimit = p is { UploadMbps: { } up, DownloadMbps: { } down } ? RouterRules.RateLimit(up, down) : null,
                });
        }
        foreach (var c in customers)
        {
            state.Secrets.Add(new MockSecret
            {
                Id = $"*{++state.NextId:X}", Name = c.PppoeUsername, Password = "mock-password", Profile = c.Plan.RouterProfile,
                Disabled = c.Status != CustomerStatus.Active, Comment = RouterRules.Comment(c.Id, null), Service = "pppoe",
                RemoteAddress = c.PppoeRemoteAddress, LocalAddress = c.PppoeLocalAddress,
                Online = c.Status == CustomerStatus.Active && state.NextId % 4 != 0,
                Address = $"10.20.{state.NextId / 250}.{state.NextId % 250 + 2}",
            });
        }
        // One deliberate difference, so the Configuration Mismatch check has something to show.
        var first = state.Secrets.FirstOrDefault(x => !x.Disabled);
        if (first is not null) first.Profile = first.Profile + " (router)";
        foreach (var (name, comment, profile) in new[] { ("mock_import01", "Rosa Mock", "Fiber 50 Mbps"), ("mock_import02", "Pablo Mock", "Fiber 100 Mbps") })
        {
            state.Secrets.Add(new MockSecret
            {
                Id = $"*{++state.NextId:X}", Name = name, Password = "mock-password", Profile = profile, Comment = comment,
                Service = "pppoe", Online = true, Address = $"10.20.9.{state.NextId}",
            });
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, Json), cancellationToken);
        logger.LogWarning("MockRouterSeeded Secrets={Count} Path={Path}", state.Secrets.Count, path);
        return state;
    }

    private sealed class MockState
    {
        public int NextId { get; set; }
        public List<MockSecret> Secrets { get; set; } = [];

        /// <summary>Profile names from the first module 05 version; replaced by <see cref="PppProfiles"/>.</summary>
        public List<string>? Profiles { get; set; }

        /// <summary>Null in state files written before profiles could be sent to the router.</summary>
        public List<MockProfile>? PppProfiles { get; set; }
    }

    private sealed class MockProfile
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? RateLimit { get; set; }
    }

    private sealed class MockSecret
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Password { get; set; }
        public string? Profile { get; set; }
        public bool Disabled { get; set; }
        public string? Comment { get; set; }
        public string? RemoteAddress { get; set; }
        public string? LocalAddress { get; set; }
        public string? Service { get; set; }
        public bool Online { get; set; }
        public string? Address { get; set; }
    }
}
