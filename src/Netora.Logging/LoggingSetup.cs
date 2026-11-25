using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Netora.Application.Logging;

namespace Netora.Logging;

public static class LoggingSetup
{
    public const string PathKey = "Logging:File:Path";

    /// <summary>
    /// Adds the daily file log (18-logging.md) and the <see cref="IActivityLog"/> used for business events.
    /// The folder comes from <c>Logging:File:Path</c> (relative paths are under the content root), the levels from
    /// <c>Logging:File:LogLevel</c>. The folder must not be inside the web root, so it is never served over HTTP.
    /// Call after any <c>ClearProviders()</c>.
    /// </summary>
    /// <param name="filePrefix"><c>netora-api</c> or <c>netora-web</c>.</param>
    public static WebApplicationBuilder AddNetoraFileLogging(this WebApplicationBuilder builder, string filePrefix, string defaultPath)
    {
        var directory = ResolveDirectory(builder.Environment, builder.Configuration[PathKey], defaultPath);

        builder.Services.AddHttpContextAccessor();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IActivityContext, HttpActivityContext>();
        builder.Services.AddSingleton<ILoggerProvider>(sp => new DailyFileLoggerProvider(
            directory, filePrefix, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<IActivityContext>()));
        builder.Services.Replace(ServiceDescriptor.Singleton<IActivityLog, ActivityLog>());
        return builder;
    }

    internal static string ResolveDirectory(IWebHostEnvironment environment, string? configured, string defaultPath)
    {
        var path = string.IsNullOrWhiteSpace(configured) ? defaultPath : configured.Trim();
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path));

        var webRoot = string.IsNullOrEmpty(environment.WebRootPath) ? null : Path.GetFullPath(environment.WebRootPath);
        if (webRoot is not null && IsInside(full, webRoot))
            throw new InvalidOperationException($"{PathKey} must be outside the web root ({webRoot}), so log files are never served over HTTP.");
        return full;
    }

    private static bool IsInside(string path, string folder)
    {
        var f = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var p = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return p.StartsWith(f, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Correlation ID for the request. Register first, before the exception handler.</summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) => app.UseMiddleware<CorrelationIdMiddleware>();

    /// <summary>Logs unhandled exceptions as <c>UnhandledException</c> events. Register just after the exception handler.</summary>
    public static IApplicationBuilder UseUnhandledExceptionLogging(this IApplicationBuilder app)
        => app.UseMiddleware<UnhandledExceptionLoggingMiddleware>();
}
