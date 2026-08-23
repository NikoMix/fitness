namespace Forge.Core.Abstractions.Diagnostics;

/// <summary>
/// The storage budget and rotation policy for Forge's on-device diagnostic log.
/// </summary>
/// <remarks>
/// <para>
/// Forge is local-only by design: no crash reporter, no telemetry backend, and none is planned.
/// The file this configures is therefore the <em>only</em> evidence that will ever exist when
/// something goes wrong for a real user, which is what justifies writing one at all.
/// </para>
/// <para>
/// It is also the reason the budget is small. A fitness app that fills a phone with its own logs
/// has replaced one defect with another, and a log nobody can attach to a message is evidence
/// nobody can send. The defaults are chosen against both of those.
/// </para>
/// </remarks>
public sealed record DiagnosticLogOptions
{
    /// <summary>The default budget, documented in <c>docs/diagnostics/logging.md</c>.</summary>
    public static DiagnosticLogOptions Default { get; } = new();

    /// <summary>
    /// Bytes written to the active file before it is rotated.
    /// </summary>
    /// <remarks>
    /// 512 KiB. A redacted entry measures 120-260 bytes, so one file holds roughly 2,000-4,000 of
    /// them. Forge writes on the order of ten entries per ordinary launch and a burst on failure,
    /// so a single file already spans hundreds of launches.
    /// </remarks>
    public int MaxFileBytes { get; init; } = 512 * 1024;

    /// <summary>
    /// How many files exist at once, including the active one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three, for a total ceiling of 1.5 MiB. The number was chosen from the failure it has to
    /// survive: a crash loop writes the same entry repeatedly, and if the whole budget were one
    /// file the loop would erase the original fault before anyone read it. Keeping two archives
    /// means the launch that first went wrong is still on disk after the noise arrives.
    /// </para>
    /// <para>
    /// 1.5 MiB is also comfortably below what a share sheet, a mail client or a messaging app will
    /// carry without complaint, which matters because a log the user cannot send is not evidence.
    /// </para>
    /// </remarks>
    public int RetainedFileCount { get; init; } = 3;

    /// <summary>Entries below this level are not written.</summary>
    /// <remarks>
    /// Information, not Debug. Every site this feature exists to serve - migration failure,
    /// integrity failure, startup failure, unlock outcome, a summary that would not build - is at
    /// Information or above. Admitting Debug would multiply the volume without adding a single one
    /// of them.
    /// </remarks>
    public DiagnosticLogLevel MinimumLevel { get; init; } = DiagnosticLogLevel.Information;

    /// <summary>Characters kept from a rendered message before it is truncated.</summary>
    public int MaxMessageLength { get; init; } = 512;

    /// <summary>
    /// Characters kept from an exception's own message before it is truncated.
    /// </summary>
    /// <remarks>
    /// Short on purpose. An exception message is the most likely thing in a log to be carrying
    /// text a user typed, and the free text Forge holds - an injury description, a recipe note -
    /// is usually longer than this. Truncation is a blunt second line behind redaction, not a
    /// replacement for it.
    /// </remarks>
    public int MaxExceptionMessageLength { get; init; } = 240;

    /// <summary>Characters kept from a rendered exception, including its stack and inner chain.</summary>
    public int MaxExceptionLength { get; init; } = 4096;

    /// <summary>
    /// Entries held in memory awaiting the writer before new ones are dropped.
    /// </summary>
    /// <remarks>
    /// Logging must never block the app, and it must never grow without bound while a slow disk
    /// or a crash loop outruns the writer. When the queue is full the newest entry is dropped and
    /// counted, and the count is written out once the writer catches up - so the log says it lost
    /// entries rather than quietly having fewer.
    /// </remarks>
    public int QueueCapacity { get; init; } = 1024;

    /// <summary>Throws when a value would make the policy meaningless.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxFileBytes, 1024);
        ArgumentOutOfRangeException.ThrowIfLessThan(RetainedFileCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxMessageLength, 32);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxExceptionMessageLength, 32);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxExceptionLength, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(QueueCapacity, 16);
    }
}

/// <summary>
/// Severity as the diagnostic log records it.
/// </summary>
/// <remarks>
/// Declared here rather than reusing <c>Microsoft.Extensions.Logging.LogLevel</c> so the policy
/// type stays independent of the logging abstraction, which keeps it usable from the redaction
/// tests and from any sink that is not an <c>ILogger</c>. The values line up deliberately.
/// </remarks>
public enum DiagnosticLogLevel
{
    /// <summary>Most detailed. Never written by the default policy.</summary>
    Trace = 0,

    /// <summary>Development detail. Never written by the default policy.</summary>
    Debug = 1,

    /// <summary>Normal progress: startup phases, migration results, unlock outcomes.</summary>
    Information = 2,

    /// <summary>Something recoverable went wrong.</summary>
    Warning = 3,

    /// <summary>Something failed that the user will probably notice.</summary>
    Error = 4,

    /// <summary>The process is going down.</summary>
    Critical = 5,

    /// <summary>Nothing is written.</summary>
    None = 6,
}
