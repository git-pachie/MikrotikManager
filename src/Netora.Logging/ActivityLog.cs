using System.Collections;
using Microsoft.Extensions.Logging;
using Netora.Application.Logging;

namespace Netora.Logging;

/// <summary>
/// Writes <see cref="ActivityEvent"/>s through <c>ILogger</c> under the category <see cref="CategoryName"/>. The daily
/// file logger turns them into the 18.03 line; other providers (console) get the same fields as structured state.
/// Never throws.
/// </summary>
public sealed class ActivityLog(ILoggerFactory loggerFactory, IActivityContext? context = null) : IActivityLog
{
    public const string CategoryName = "Netora.Activity";

    private readonly ILogger logger = loggerFactory.CreateLogger(CategoryName);

    public void Write(ActivityEvent activity)
    {
        try
        {
            var level = activity.Level switch
            {
                ActivityLevel.Warning => LogLevel.Warning,
                ActivityLevel.Error => LogLevel.Error,
                _ => LogLevel.Information,
            };
            if (!logger.IsEnabled(level)) return;

            var c = context?.Current ?? default;
            var state = new ActivityState(
                activity.Category,
                activity.Event,
                activity.Actor ?? c.Actor ?? "anonymous",
                c.Role,
                activity.Target,
                activity.Outcome,
                c.Ip,
                c.CorrelationId,
                activity.Detail);
            logger.Log(level, new EventId(0, LogLine.Token(activity.Event)), state, activity.Exception, ActivityState.Format);
        }
        catch (Exception ex)
        {
            // Logging must never break the request (18.05).
            try { Console.Error.WriteLine($"Netora activity log failed: {ex.GetType().Name}"); } catch { /* nothing left to do */ }
        }
    }
}

/// <summary>Structured state of one activity event. <see cref="LogLine"/> formats it for the file.</summary>
public sealed class ActivityState(
    string category, string eventName, string actor, string? role, string? target, string outcome, string? ip,
    string? correlationId, string? detail) : IReadOnlyList<KeyValuePair<string, object?>>
{
    public string Category { get; } = category;
    public string Event { get; } = eventName;
    public string Actor { get; } = actor;
    public string? Role { get; } = role;
    public string? Target { get; } = target;
    public string Outcome { get; } = outcome;
    public string? Ip { get; } = ip;
    public string? CorrelationId { get; } = correlationId;
    public string? Detail { get; } = detail;

    internal static readonly Func<ActivityState, Exception?, string> Format = (s, _) => s.ToString();

    private KeyValuePair<string, object?>[]? pairs;

    private KeyValuePair<string, object?>[] Pairs => pairs ??=
    [
        new("Category", Category), new("Event", Event), new("Actor", Actor), new("Role", Role), new("Target", Target),
        new("Outcome", Outcome), new("Ip", Ip), new("CorrelationId", CorrelationId), new("Detail", Detail),
        new("{OriginalFormat}", "[{Category}] {Event} actor={Actor} target={Target} outcome={Outcome} corr={CorrelationId}"),
    ];

    public int Count => 10;

    public KeyValuePair<string, object?> this[int index] => Pairs[index];

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => ((IEnumerable<KeyValuePair<string, object?>>)Pairs).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Message for providers other than the file (console). Cleaned the same way as the file line.</summary>
    public override string ToString()
    {
        var text = $"[{LogLine.Token(Category)}] {LogLine.Token(Event)} actor={LogLine.Value(Actor)}";
        if (!string.IsNullOrEmpty(Role)) text += $" role={LogLine.Value(Role)}";
        if (!string.IsNullOrEmpty(Target)) text += $" target={LogLine.Value(Target)}";
        text += $" outcome={LogLine.Value(Outcome)}";
        if (!string.IsNullOrEmpty(Ip)) text += $" ip={LogLine.Value(Ip)}";
        text += $" corr={LogLine.Value(CorrelationId)}";
        if (!string.IsNullOrEmpty(Detail)) text += $" detail={LogLine.Value(Detail)}";
        return text;
    }
}
