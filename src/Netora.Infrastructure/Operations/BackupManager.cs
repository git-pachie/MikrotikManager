using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Netora.Application.Operations;
using Netora.Domain.Settings;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Operations;

internal sealed class BackupManager(NetoraDbContext db, ILogger<BackupManager> logger) : IBackupManager
{
    private const string SettingsKey = "Backup.Settings";
    private const string QueueKey = "Backup.ManualRun";

    private sealed record Configuration(string? Destination, TimeOnly? DailyTimeSingapore);

    public async Task<BackupSettingsDto> GetSettingsAsync(CancellationToken ct)
    {
        var settings = await ReadConfigurationAsync(ct);
        var last = await db.BackupRuns.AsNoTracking()
            .OrderByDescending(e => e.Id).FirstOrDefaultAsync(ct);
        var manual = await db.AppSettings.AsNoTracking().AnyAsync(e => e.Key == QueueKey && e.Value != null, ct);
        return new(settings.Destination, settings.DailyTimeSingapore, last?.Status, last?.StartedAtUtc, manual);
    }

    public async Task<string?> SaveSettingsAsync(UpdateBackupSettingsRequest request, string actor, CancellationToken ct)
    {
        var destination = string.IsNullOrWhiteSpace(request.Destination) ? null : request.Destination.Trim();
        if (destination is not null && !IsValidDestination(destination))
            return "Enter an absolute directory on the SQL Server host, such as D:\\NetoraBackups or /mnt/backups.";
        if (destination is null && request.DailyTimeSingapore is not null)
            return "Set a backup directory before enabling the daily schedule.";

        if (!await TryLockAsync(ct)) return "A backup is running. Try saving settings after it finishes.";
        try
        {
            if (await db.AppSettings.AnyAsync(e => e.Key == QueueKey && e.Value != null, ct))
                return "Let the queued backup finish before changing backup settings.";
            await SetAsync(SettingsKey, JsonSerializer.Serialize(new Configuration(destination, request.DailyTimeSingapore)), actor, ct);
            await db.SaveChangesAsync(ct);
            return null;
        }
        finally { await UnlockAsync(); }
    }

    public async Task<string?> QueueAsync(string actor, CancellationToken ct)
    {
        if (!await TryLockAsync(ct)) return "A backup is running.";
        try
        {
            if ((await ReadConfigurationAsync(ct)).Destination is null) return "Save a backup directory first.";
            if (await db.AppSettings.AnyAsync(e => e.Key == QueueKey && e.Value != null, ct)) return "A backup is already queued.";
            await SetAsync(QueueKey, actor, actor, ct);
            await db.SaveChangesAsync(ct);
            return null;
        }
        finally { await UnlockAsync(); }
    }

    public async Task TickAsync(CancellationToken ct)
    {
        var snapshot = await ReadConfigurationAsync(ct);
        if (snapshot.Destination is null) return;
        if (!await TryLockAsync(ct)) return;
        try
        {
            var settings = await ReadConfigurationAsync(ct);
            if (settings.Destination is null) return;
            // Acquiring the exclusive SQL lock proves that a prior running row has no live worker.
            await db.BackupRuns.Where(e => e.Status == "Running")
                .ExecuteUpdateAsync(set => set.SetProperty(e => e.Status, "Interrupted"), ct);
            var queued = await db.AppSettings.SingleOrDefaultAsync(e => e.Key == QueueKey, ct);
            var now = DateTimeOffset.UtcNow;
            var singapore = now.ToOffset(TimeSpan.FromHours(8));
            var today = new DateTimeOffset(singapore.Date, singapore.Offset).ToUniversalTime();
            var due = settings.DailyTimeSingapore is { } at && TimeOnly.FromDateTime(singapore.DateTime) >= at
                && !await db.BackupRuns.AnyAsync(e => e.Scheduled && e.StartedAtUtc >= today, ct);
            if (queued?.Value is null && !due) return;

            var started = new BackupRun
            {
                ActorUserId = queued?.Value ?? "system",
                Scheduled = queued?.Value is null,
                StartedAtUtc = now,
                Status = "Running",
            };
            if (queued is not null) queued.Value = null;
            // Persist the attempt before running SQL so a restart never automatically repeats an interrupted backup.
            db.BackupRuns.Add(started);
            await db.SaveChangesAsync(ct);

            try
            {
                await using var command = Command("""
                    DECLARE @database sysname = DB_NAME();
                    BACKUP DATABASE @database TO DISK = @destination WITH COPY_ONLY, CHECKSUM;
                    """);
                command.CommandTimeout = 1800;
                Parameter(command, "@destination", BackupPath(settings.Destination, now));
                await command.ExecuteNonQueryAsync(ct);
                started.Status = "Completed";
            }
            catch (SqlException ex)
            {
                started.Status = "Failed";
                logger.LogWarning("BackupFailed SqlErrorNumber={Number}", ex.Number);
            }
            await db.SaveChangesAsync(ct);
            logger.LogInformation("BackupRunCompleted RunId={RunId} ActorUserId={ActorUserId} Scheduled={Scheduled} Status={Status}",
                started.Id, started.ActorUserId, started.Scheduled, started.Status);
        }
        finally { await UnlockAsync(); }
    }

    private async Task<Configuration> ReadConfigurationAsync(CancellationToken ct)
    {
        var value = await db.AppSettings.AsNoTracking().Where(e => e.Key == SettingsKey).Select(e => e.Value).SingleOrDefaultAsync(ct);
        return value is null ? new(null, null) : JsonSerializer.Deserialize<Configuration>(value) ?? new(null, null);
    }

    private async Task SetAsync(string key, string value, string actor, CancellationToken ct)
    {
        var row = await db.AppSettings.SingleOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            row = new AppSetting { Key = key };
            db.AppSettings.Add(row);
        }
        row.Value = value;
        row.UpdatedAtUtc = DateTimeOffset.UtcNow;
        row.UpdatedByUserId = actor;
    }

    private async Task<bool> TryLockAsync(CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var command = Command("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = N'Netora.Backup',
                    @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
                SELECT @result;
                """);
            if (await command.ExecuteScalarAsync(ct) is int result && result >= 0) return true;
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }
        await db.Database.CloseConnectionAsync();
        return false;
    }

    private async Task UnlockAsync()
    {
        try
        {
            await using var command = Command("""
                EXEC sys.sp_releaseapplock @Resource = N'Netora.Backup', @LockOwner = 'Session';
                """);
            await command.ExecuteNonQueryAsync();
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private DbCommand Command(string text)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = text;
        command.CommandTimeout = 10;
        return command;
    }

    private static void Parameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    internal static bool IsValidDestination(string path)
    {
        if (path.Length is < 3 or > 180 || path.Any(char.IsControl) || path.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0)
            return false;
        var windows = char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';
        var unix = path[0] == '/' && path[1] != '/';
        return (windows || unix) && !path.Split(['/', '\\']).Any(segment => segment is "." or "..");
    }

    internal static string BackupPath(string directory, DateTimeOffset now)
    {
        var separator = directory[0] == '/' ? '/' : '\\';
        return $"{directory.TrimEnd('/', '\\')}{separator}netora-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.bak";
    }
}

internal sealed class BackupScheduler(IServiceScopeFactory scopes, ILogger<BackupScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await ((BackupManager)scope.ServiceProvider.GetRequiredService<IBackupManager>()).TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("BackupSchedulerFailed ErrorKind={ErrorKind}", ex.GetType().Name);
            }
        }
    }
}
