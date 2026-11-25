using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Netora.Logging;

/// <summary>
/// Builds the one-line text format of 18.03:
/// <c>2026-11-24 09:42:17.123 +08:00 [INF] [Customers] CustomerCreated actor=manager01 role=Manager target=Customer:1042 outcome=Success ip=192.168.7.20 corr=8f3a1c</c>.
/// Every value is cleaned first: control characters and line breaks are removed and the length is capped, so input
/// can never forge an extra line. Exceptions follow on indented lines.
/// </summary>
internal static class LogLine
{
    public const int MaxValueLength = 256;
    public const int MaxMessageLength = 1000;
    private const int MaxExceptionLines = 200;
    private const string Indent = "    ";

    public static string Timestamp(DateTimeOffset at) => at.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);

    public static string Level(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "INF",
    };

    public static string Activity(DateTimeOffset at, LogLevel level, ActivityState a, Exception? exception)
    {
        var sb = Start(at, level, a.Category, a.Event);
        Field(sb, "actor", a.Actor);
        if (!string.IsNullOrEmpty(a.Role)) Field(sb, "role", a.Role);
        if (!string.IsNullOrEmpty(a.Target)) Field(sb, "target", a.Target);
        Field(sb, "outcome", a.Outcome);
        if (!string.IsNullOrEmpty(a.Ip)) Field(sb, "ip", a.Ip);
        Field(sb, "corr", a.CorrelationId);
        if (!string.IsNullOrEmpty(a.Detail)) Field(sb, "detail", a.Detail);
        return End(sb, exception);
    }

    /// <summary>Lines from ordinary <c>ILogger</c> calls (framework warnings, errors), in the same layout.</summary>
    public static string General(DateTimeOffset at, LogLevel level, string source, EventId eventId, string? message,
        Exception? exception, ActivityContextSnapshot context)
    {
        var name = Token(eventId.Name);
        var sb = Start(at, level, "Application", name == "-" ? "Log" : name);
        Field(sb, "actor", context.Actor ?? "anonymous");
        if (!string.IsNullOrEmpty(context.Role)) Field(sb, "role", context.Role);
        Field(sb, "outcome", "-");
        if (!string.IsNullOrEmpty(context.Ip)) Field(sb, "ip", context.Ip);
        Field(sb, "corr", context.CorrelationId);
        Field(sb, "source", source);
        Field(sb, "msg", message, MaxMessageLength);
        return End(sb, exception);
    }

    /// <summary>Removes control characters and Unicode line or paragraph separators, and caps the length.</summary>
    public static string Clean(string? value, int max = MaxValueLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var sb = new StringBuilder(Math.Min(value.Length, max));
        foreach (var ch in value)
        {
            if (char.IsControl(ch)) continue;
            var category = char.GetUnicodeCategory(ch);
            if (category is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator) continue;
            sb.Append(ch);
            if (sb.Length >= max) break;
        }
        return sb.ToString();
    }

    /// <summary>A cleaned value; quoted (with \" and \\ escaped) when it contains a space, quote, backslash or '='.</summary>
    public static string Value(string? value, int max = MaxValueLength)
    {
        var clean = Clean(value, max);
        if (clean.Length == 0) return "-";
        foreach (var ch in clean)
        {
            if (ch is ' ' or '"' or '\\' or '=' || char.IsWhiteSpace(ch))
                return "\"" + clean.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
        return clean;
    }

    /// <summary>Category and event names: letters, digits, '.', '_' and '-' only.</summary>
    public static string Token(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        var sb = new StringBuilder(Math.Min(value.Length, 64));
        foreach (var ch in value)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-') sb.Append(ch);
            if (sb.Length >= 64) break;
        }
        return sb.Length == 0 ? "-" : sb.ToString();
    }

    private static StringBuilder Start(DateTimeOffset at, LogLevel level, string category, string eventName)
    {
        var sb = new StringBuilder(256);
        sb.Append(Timestamp(at)).Append(" [").Append(Level(level)).Append("] [").Append(Token(category)).Append("] ")
            .Append(Token(eventName));
        return sb;
    }

    private static void Field(StringBuilder sb, string key, string? value, int max = MaxValueLength)
        => sb.Append(' ').Append(key).Append('=').Append(Value(value, max));

    private static string End(StringBuilder sb, Exception? exception)
    {
        sb.Append(Environment.NewLine);
        if (exception is not null) AppendException(sb, exception);
        return sb.ToString();
    }

    /// <summary>Exception text and stack trace, one indented line each, so it can never look like a new event.</summary>
    private static void AppendException(StringBuilder sb, Exception exception)
    {
        string text;
        try
        {
            text = exception.ToString();
        }
        catch
        {
            text = exception.GetType().FullName ?? "Exception";
        }

        var count = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = Clean(raw, 2000);
            if (line.Length == 0) continue;
            if (++count > MaxExceptionLines)
            {
                sb.Append(Indent).Append("(stack trace cut)").Append(Environment.NewLine);
                break;
            }
            sb.Append(Indent).Append(line).Append(Environment.NewLine);
        }
    }
}
