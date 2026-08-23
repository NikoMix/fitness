using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Forge.Core.Abstractions.Diagnostics;

/// <summary>
/// Writes Forge's log to a rotating on-device file, in Release as well as Debug.
/// </summary>
/// <remarks>
/// <para>
/// Until this existed, <c>MauiProgram</c> registered <c>AddDebug()</c> inside <c>#if DEBUG</c> and
/// nothing else, so a Release build had <em>no logging provider at all</em>. Around twenty call
/// sites across eleven files were writing to nothing, and they are not incidental ones - they are
/// the places the app already knows something has gone wrong: a failed migration, a failed
/// integrity check, a failed startup, a summary that would not build, an unlock that was refused.
/// Everything downstream that claims to "log the exception and enter recovery" had nowhere to log
/// and nothing to enter.
/// </para>
/// <para>
/// Forge has no crash reporter and no telemetry backend, and will not have one. This file is
/// therefore the only evidence that will ever exist when something goes wrong for a real user.
/// </para>
/// <para>
/// <strong>Nothing here touches the disk on the startup path.</strong> Constructing the provider
/// allocates a channel and nothing else - no directory creation, no file open, no path probe. The
/// drain loop starts on the first entry, on a thread-pool thread, and the file is opened there.
/// Logging a line costs a level check, a redaction pass and a bounded channel write. This matters:
/// a stream took Android cold start from ~27 s to ~10 s by removing five SQLCipher key
/// derivations, and opening a file synchronously during composition is the obvious way to spend
/// some of that back.
/// </para>
/// <para>
/// Scopes are deliberately not written. A scope value is caller-supplied data with no message
/// template constraining it, which makes it one of the easiest ways for a body weight to reach a
/// file. <see cref="ILogger.BeginScope{TState}"/> returns a no-op rather than throwing, so callers
/// that use scopes still work; their values simply do not reach the disk.
/// </para>
/// </remarks>
public sealed class ForgeFileLoggerProvider : ILoggerProvider
{
    private readonly Func<string> directoryFactory;
    private readonly DiagnosticLogOptions options;
    private readonly TimeProvider timeProvider;
    private readonly Lock initGate = new();
    private readonly Lock startGate = new();

    private string? resolvedDirectory;
    private RollingLogFile? file;
    private Channel<string>? queue;
    private Task? drainTask;
    private int droppedEntries;
    private bool disposed;

    /// <summary>Creates a provider writing into <paramref name="directory"/>.</summary>
    /// <param name="directory">Directory the log files live in. Must be app-private.</param>
    /// <param name="options">The budget, rotation and redaction caps.</param>
    /// <param name="timeProvider">Clock for entry timestamps.</param>
    public ForgeFileLoggerProvider(
        string directory,
        DiagnosticLogOptions? options = null,
        TimeProvider? timeProvider = null)
        : this(DirectoryFactoryFor(directory), options, timeProvider)
    {
    }

    /// <summary>
    /// Creates a provider whose directory is not resolved until the first entry.
    /// </summary>
    /// <param name="directoryFactory">Resolves the directory, on the first write and not before.</param>
    /// <param name="options">The budget, rotation and redaction caps.</param>
    /// <param name="timeProvider">Clock for entry timestamps.</param>
    /// <remarks>
    /// <para>
    /// The factory overload is not a convenience. On Android,
    /// <c>FileSystem.AppDataDirectory</c> initialises MAUI Essentials and crosses into Java, and
    /// calling it during composition measured <strong>331 ms</strong> on an emulator - on the
    /// critical path to the first frame, in a Release build, for a directory name nothing needed
    /// yet.
    /// </para>
    /// <para>
    /// With the factory, that call happens on the drain thread when the first entry is written,
    /// which is after the shell is up. So does creating the channel, opening the file and loading
    /// the assemblies behind all three.
    /// </para>
    /// </remarks>
    public ForgeFileLoggerProvider(
        Func<string> directoryFactory,
        DiagnosticLogOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(directoryFactory);

        this.directoryFactory = directoryFactory;
        this.options = options ?? DiagnosticLogOptions.Default;
        this.options.Validate();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The directory the log files live in. Resolving it the first time may be costly.</summary>
    public string Directory => EnsureInitialised().Directory;

    /// <summary>The rotating file this provider writes to.</summary>
    public RollingLogFile File => EnsureInitialised().File;

    /// <summary>Entries dropped because the queue was full.</summary>
    public int DroppedEntries => Volatile.Read(ref droppedEntries);

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new ForgeFileLogger(this, categoryName);

    /// <summary>
    /// Writes an entry immediately, on the calling thread, bypassing the queue.
    /// </summary>
    /// <param name="level">Severity.</param>
    /// <param name="category">Logger category.</param>
    /// <param name="message">Already-rendered message. Redacted here.</param>
    /// <param name="exception">Optional exception to describe.</param>
    /// <remarks>
    /// For the crash boundary. An unhandled exception is followed by the process being killed, and
    /// the drain loop will not necessarily be scheduled again before that happens - so the entry
    /// that explains the crash is the one entry that cannot afford to be queued.
    /// </remarks>
    public void WriteImmediate(DiagnosticLogLevel level, string category, string message, Exception? exception)
    {
        if (disposed || level < options.MinimumLevel || level == DiagnosticLogLevel.None)
        {
            return;
        }

        var state = EnsureInitialised();
        DrainQueuedEntries(state);
        state.File.Write(Format(level, category, eventId: 0, message, exception));
        state.File.Flush();
    }

    /// <summary>
    /// Drains everything queued, waiting up to <paramref name="timeout"/>.
    /// </summary>
    /// <param name="timeout">How long to wait for the writer.</param>
    /// <remarks>
    /// Drains on the calling thread rather than waiting for the background loop. Waiting would be
    /// wrong on the path that matters: the caller is usually a crash handler, and a thread-pool
    /// continuation is not guaranteed to run before the process is killed.
    /// </remarks>
    public void Flush(TimeSpan timeout)
    {
        // Nothing was ever logged, so there is nothing to flush - and forcing initialisation here
        // would open a file for the sake of emptying it.
        if (queue is null)
        {
            return;
        }

        var state = EnsureInitialised();
        var start = timeProvider.GetTimestamp();
        var budget = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(2) : timeout;

        while (state.Queue.Reader.TryRead(out var line))
        {
            state.File.Write(line);

            if (timeProvider.GetElapsedTime(start) > budget)
            {
                break;
            }
        }

        state.File.Flush();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (queue is null)
        {
            return;
        }

        queue.Writer.TryComplete();

        try
        {
            drainTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        var state = EnsureInitialised();
        DrainQueuedEntries(state);
        state.File.Dispose();
    }

    internal bool IsEnabled(DiagnosticLogLevel level) =>
        !disposed && level != DiagnosticLogLevel.None && level >= options.MinimumLevel;

    internal void Enqueue(DiagnosticLogLevel level, string category, int eventId, string message, Exception? exception)
    {
        if (!IsEnabled(level))
        {
            return;
        }

        var line = Format(level, category, eventId, message, exception);
        var state = EnsureInitialised();

        if (!state.Queue.Writer.TryWrite(line))
        {
            Interlocked.Increment(ref droppedEntries);
            return;
        }

        EnsureDraining(state);
    }

    private static Func<string> DirectoryFactoryFor(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return () => directory;
    }

    private SinkState EnsureInitialised()
    {
        if (file is not null && queue is not null && resolvedDirectory is not null)
        {
            return new SinkState(resolvedDirectory, file, queue);
        }

        lock (initGate)
        {
            resolvedDirectory ??= directoryFactory();
            file ??= new RollingLogFile(resolvedDirectory, options);

            // DropWrite, not Wait. Logging must never block the caller, and it must never grow
            // without bound while a slow disk or a crash loop outruns the writer. What is dropped
            // is counted and reported once the writer catches up, so the file says it lost
            // entries rather than quietly having fewer of them.
            queue ??= Channel.CreateBounded<string>(new BoundedChannelOptions(options.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            });

            return new SinkState(resolvedDirectory, file, queue);
        }
    }

    private string Format(DiagnosticLogLevel level, string category, int eventId, string message, Exception? exception)
    {
        var builder = new StringBuilder(256);

        // The timestamp, level, category and event id are written WITHOUT redaction. They are
        // Forge's own vocabulary: constants in this repository, not values from a user. Everything
        // after them is treated as hostile.
        //
        // The timestamp is the one deliberate disclosure in the file. It says when Forge was
        // running, which is a weak signal about when somebody trained, and it is kept because
        // without it the file cannot be correlated with "it happened this morning". The Settings
        // screen states this in as many words before anyone shares it.
        builder
            .Append(timeProvider.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture))
            .Append(' ')
            .Append(Abbreviate(level))
            .Append(' ')
            .Append(category);

        if (eventId != 0)
        {
            builder.Append('[').Append(eventId.ToString(CultureInfo.InvariantCulture)).Append(']');
        }

        var redacted = DiagnosticLogRedactor.Redact(message, options.MaxMessageLength);
        if (redacted.Length > 0)
        {
            builder.Append(' ').Append(redacted);
        }

        var dropped = Interlocked.Exchange(ref droppedEntries, 0);
        if (dropped > 0)
        {
            builder.Append(" (")
                .Append(dropped.ToString(CultureInfo.InvariantCulture))
                .Append(" earlier entries were dropped because the writer fell behind)");
        }

        if (exception is not null)
        {
            builder.Append('\n').Append(DiagnosticLogRedactor.Describe(exception, options));
        }

        // A newline inside an entry would let a redacted message forge a second entry. Collapsing
        // them keeps one entry to one line, except for the exception block, which is appended
        // after this point and is allowed its own lines.
        return builder.ToString().ReplaceLineEndings("\n");
    }

    private static string Abbreviate(DiagnosticLogLevel level) => level switch
    {
        DiagnosticLogLevel.Trace => "TRC",
        DiagnosticLogLevel.Debug => "DBG",
        DiagnosticLogLevel.Information => "INF",
        DiagnosticLogLevel.Warning => "WRN",
        DiagnosticLogLevel.Error => "ERR",
        DiagnosticLogLevel.Critical => "CRT",
        _ => "???",
    };

    private void EnsureDraining(SinkState state)
    {
        if (drainTask is not null)
        {
            return;
        }

        lock (startGate)
        {
            // Started here rather than in the constructor so that composition costs nothing and
            // the first file open happens on a thread-pool thread, after the caller has moved on.
            drainTask ??= Task.Run(() => DrainAsync(state));
        }
    }

    private static async Task DrainAsync(SinkState state)
    {
        try
        {
            await foreach (var line in state.Queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                state.File.Write(line);
            }
        }
        catch (ChannelClosedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static void DrainQueuedEntries(SinkState state)
    {
        while (state.Queue.Reader.TryRead(out var line))
        {
            state.File.Write(line);
        }
    }

    private readonly record struct SinkState(string Directory, RollingLogFile File, Channel<string> Queue);

    private sealed class ForgeFileLogger(ForgeFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(Map(logLevel));

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            var level = Map(logLevel);
            if (!provider.IsEnabled(level))
            {
                return;
            }

            string message;
            try
            {
                message = formatter(state, exception);
            }
            catch (InvalidOperationException)
            {
                // A formatter that throws must not take the app with it, and the entry is still
                // worth having: the category, level and exception are all still known.
                message = "(the log message could not be formatted)";
            }
            catch (FormatException)
            {
                message = "(the log message could not be formatted)";
            }

            provider.Enqueue(level, category, eventId.Id, message, exception);
        }

        private static DiagnosticLogLevel Map(LogLevel level) => level switch
        {
            LogLevel.Trace => DiagnosticLogLevel.Trace,
            LogLevel.Debug => DiagnosticLogLevel.Debug,
            LogLevel.Information => DiagnosticLogLevel.Information,
            LogLevel.Warning => DiagnosticLogLevel.Warning,
            LogLevel.Error => DiagnosticLogLevel.Error,
            LogLevel.Critical => DiagnosticLogLevel.Critical,
            _ => DiagnosticLogLevel.None,
        };

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
