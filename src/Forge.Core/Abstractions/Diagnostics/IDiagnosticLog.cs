namespace Forge.Core.Abstractions.Diagnostics;

/// <summary>
/// What the diagnostic log currently holds, in terms a settings screen can state plainly.
/// </summary>
/// <param name="TotalBytes">Bytes held by every log file together.</param>
/// <param name="FileCount">How many log files exist.</param>
/// <param name="BudgetBytes">The ceiling those files can never exceed.</param>
/// <param name="LastCrash">The fault that ended a previous launch, if one did.</param>
/// <param name="IsWritable">Whether the sink is still able to write.</param>
public sealed record DiagnosticLogSummary(
    long TotalBytes,
    int FileCount,
    long BudgetBytes,
    CrashBreadcrumb? LastCrash,
    bool IsWritable)
{
    /// <summary>Whether there is anything at all to share.</summary>
    public bool HasContent => FileCount > 0 && TotalBytes > 0;
}

/// <summary>
/// The on-device diagnostic log, as the rest of the app sees it.
/// </summary>
/// <remarks>
/// <para>
/// Forge is local-only: there is no crash-reporting service and no telemetry backend, and adding
/// one would contradict the privacy policy, the Play Data Safety declaration and the store listing
/// at the same time. So the log never leaves the device on its own. The only way it moves is
/// through <see cref="PrepareForSharingAsync"/>, which a person has to choose, having been told
/// first what the file contains.
/// </para>
/// <para>
/// That is why this interface has no "upload" and never will.
/// </para>
/// </remarks>
public interface IDiagnosticLog
{
    /// <summary>Describes what is currently stored.</summary>
    /// <param name="cancellationToken">Cancels the measurement.</param>
    /// <returns>The summary.</returns>
    ValueTask<DiagnosticLogSummary> GetSummaryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Assembles every log file into one file the user can attach to a message.
    /// </summary>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>The absolute path to share, or <see langword="null"/> when there is nothing to send.</returns>
    /// <remarks>
    /// A copy rather than the live file, because rotation could rename the original out from under
    /// the share sheet mid-transfer. The copy lands in the cache directory, which erasure already
    /// reaches.
    /// </remarks>
    ValueTask<string?> PrepareForSharingAsync(CancellationToken cancellationToken);

    /// <summary>Deletes every log file and the crash breadcrumb.</summary>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>Bytes reclaimed.</returns>
    ValueTask<long> DeleteAsync(CancellationToken cancellationToken);

    /// <summary>Forgets a recorded crash, so it is reported once rather than every launch.</summary>
    void AcknowledgeCrash();

    /// <summary>
    /// Stops writing, and closes the file, until the returned handle is disposed.
    /// </summary>
    /// <returns>Dispose to resume writing into a fresh file.</returns>
    /// <remarks>
    /// <para>
    /// Wrap the erasure flow in this. <c>LocalDataErasureService</c> deletes every file under the
    /// app data directory and then removes the directories, and the log sink lives inside that
    /// tree and re-creates its own directory on the next entry. One log line landing between
    /// those two passes leaves a directory that will not delete, and a user who asked to be
    /// forgotten is told instead that some of their data could not be erased.
    /// </para>
    /// <para>
    /// It answers the substantive question too: "delete my data" that left behind a log of the
    /// deleted data would be a breach, not a cosmetic defect.
    /// </para>
    /// </remarks>
    IDisposable SuspendForErasure();
}
