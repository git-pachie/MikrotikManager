using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Netora.Logging;

/// <summary>
/// Daily text log files (18.01): <c>{prefix}-yyyy-MM-dd.txt</c> in one folder, dated by the server's local time.
/// The file is chosen from each event's own timestamp, so the first event after midnight starts the next day's file
/// and no event is ever appended to another date's file. Old files are never deleted (retention is manual, 18.05).
/// <para>
/// Lines are queued and written by one background task, so a slow disk does not slow requests. The queue is flushed
/// on shutdown. If a file cannot be written the event is dropped, the failure goes to the console (stderr), and the
/// request carries on.
/// </para>
/// Filtering uses the standard configuration under the alias "File": <c>Logging:File:LogLevel</c>.
/// </summary>
[ProviderAlias("File")]
public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private const int QueueCapacity = 10_000;

    private readonly string directory;
    private readonly string filePrefix;
    private readonly TimeProvider time;
    private readonly Action<string> errorSink;
    private readonly Channel<Entry> queue;
    private readonly Task writerTask;

    private StreamWriter? writer;
    private string? currentFile;
    private bool failing;
    private int dropped;
    private int disposed;

    /// <param name="directory">Absolute folder; created when missing.</param>
    /// <param name="filePrefix">For example <c>netora-api</c>.</param>
    /// <param name="errorSink">Where write failures are reported. Defaults to stderr.</param>
    public DailyFileLoggerProvider(string directory, string filePrefix, TimeProvider? time = null,
        IActivityContext? context = null, Action<string>? errorSink = null)
    {
        this.directory = directory;
        this.filePrefix = filePrefix;
        this.time = time ?? TimeProvider.System;
        Context = context;
        this.errorSink = errorSink ?? (message => Console.Error.WriteLine(message));
        queue = Channel.CreateBounded<Entry>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait, // TryWrite then fails instead of blocking the caller.
        });
        writerTask = Task.Run(WriteLoopAsync);
    }

    public string Directory => directory;

    internal IActivityContext? Context { get; }

    public ILogger CreateLogger(string categoryName) => new DailyFileLogger(this, categoryName);

    /// <summary>The file name for an event at <paramref name="at"/> (local time).</summary>
    public string FileNameFor(DateTimeOffset at) => $"{filePrefix}-{at:yyyy-MM-dd}.txt";

    internal DateTimeOffset Now => time.GetLocalNow();

    internal void Enqueue(DateTimeOffset at, string text)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        if (!queue.Writer.TryWrite(new Entry(at, text)) && Interlocked.Increment(ref dropped) == 1)
            Report("Netora file log: queue full, events are being dropped.");
    }

    internal void Report(string message)
    {
        try { errorSink(message); } catch { /* the console itself failed; nothing left to do */ }
    }

    private async Task WriteLoopAsync()
    {
        var reader = queue.Reader;
        try
        {
            while (await reader.WaitToReadAsync().ConfigureAwait(false))
            {
                while (reader.TryRead(out var entry)) Write(entry);
                Flush();
            }
        }
        catch (Exception ex)
        {
            Report($"Netora file log stopped: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Close();
        }
    }

    private void Write(Entry entry)
    {
        try
        {
            var name = FileNameFor(entry.At);
            if (writer is null || !string.Equals(name, currentFile, StringComparison.Ordinal))
            {
                Close();
                System.IO.Directory.CreateDirectory(directory);
                var stream = new FileStream(Path.Combine(directory, name), FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                currentFile = name;
            }

            writer.Write(entry.Text);
            if (failing)
            {
                failing = false;
                Report($"Netora file log: writing to {directory} works again.");
            }
        }
        catch (Exception ex)
        {
            // Report once per failure streak, not once per event.
            if (!failing) Report($"Netora file log: could not write to {directory}: {ex.GetType().Name}: {ex.Message}");
            failing = true;
            Close();
        }
    }

    private void Flush()
    {
        try
        {
            writer?.Flush();
        }
        catch (Exception ex)
        {
            if (!failing) Report($"Netora file log: could not write to {directory}: {ex.GetType().Name}: {ex.Message}");
            failing = true;
            Close();
        }
    }

    private void Close()
    {
        try { writer?.Dispose(); } catch { /* already failing; the next event reopens the file */ }
        writer = null;
        currentFile = null;
    }

    /// <summary>Stops accepting events, writes what is queued (up to 5 seconds) and closes the file.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        queue.Writer.TryComplete();
        try
        {
            if (!writerTask.Wait(TimeSpan.FromSeconds(5)))
                Report("Netora file log: shutdown timed out, some events may not be written.");
        }
        catch (Exception ex)
        {
            Report($"Netora file log: shutdown failed: {ex.GetType().Name}");
        }
    }

    private readonly record struct Entry(DateTimeOffset At, string Text);
}

internal sealed class DailyFileLogger(DailyFileLoggerProvider provider, string category) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        try
        {
            var now = provider.Now;
            var text = state is ActivityState activity
                ? LogLine.Activity(now, logLevel, activity, exception)
                : LogLine.General(now, logLevel, category, eventId, formatter(state, exception), exception,
                    provider.Context?.Current ?? default);
            provider.Enqueue(now, text);
        }
        catch (Exception ex)
        {
            provider.Report($"Netora file log: could not format an event: {ex.GetType().Name}");
        }
    }
}
