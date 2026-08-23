using System.Globalization;
using System.Text;
using Forge.Core.Abstractions.Diagnostics;
using Microsoft.Maui.Storage;

namespace Forge.App.Diagnostics;

/// <summary>
/// The on-device diagnostic log, backed by MAUI's app-private storage.
/// </summary>
/// <remarks>
/// <para>
/// The files live in <c>&lt;AppDataDirectory&gt;/diagnostics</c>. That location is not incidental.
/// It is app-private on both platforms, so no other app and no file manager can read it, and it
/// is inside the directory <c>LocalDataErasureService</c> already walks - so "delete my data"
/// reaches the log without erasure needing to know the log exists. A log of the deleted data
/// surviving the deletion would be a real breach rather than an untidiness.
/// </para>
/// <para>
/// The copy prepared for sharing lands in the cache directory, which erasure also clears.
/// </para>
/// </remarks>
internal sealed class DiagnosticLog(ForgeFileLoggerProvider provider) : IDiagnosticLog
{
    /// <summary>Name of the single file a user shares.</summary>
    public const string SharedFileName = "forge-diagnostics.log";

    private CrashBreadcrumb? lastCrash;
    private bool crashRead;

    /// <summary>
    /// The fault that ended a previous launch, if one did.
    /// </summary>
    /// <remarks>
    /// Read on first access rather than in a field initialiser. The initialiser version ran during
    /// composition, which put a directory resolution and a file probe on the critical path to the
    /// first frame - for an answer nothing needs until somebody opens a settings screen.
    /// </remarks>
    public CrashBreadcrumb? LastCrash
    {
        get
        {
            if (!crashRead)
            {
                crashRead = true;
                lastCrash = CrashBreadcrumb.Read(provider.Directory);
            }

            return lastCrash;
        }
    }

    /// <inheritdoc />
    public ValueTask<DiagnosticLogSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = DiagnosticLogOptions.Default;
        var paths = RollingLogFile.ExistingPaths(provider.Directory, options);

        return ValueTask.FromResult(new DiagnosticLogSummary(
            TotalBytes: provider.File.GetTotalBytes(),
            FileCount: paths.Count,
            BudgetBytes: (long)options.MaxFileBytes * options.RetainedFileCount,
            LastCrash: LastCrash,
            IsWritable: !provider.File.IsFaulted));
    }

    /// <inheritdoc />
    public async ValueTask<string?> PrepareForSharingAsync(CancellationToken cancellationToken)
    {
        // Everything queued has to reach the disk first, or the copy stops just before the entry
        // the user is writing in about.
        provider.Flush(TimeSpan.FromSeconds(2));

        var options = DiagnosticLogOptions.Default;
        var paths = RollingLogFile.ExistingPaths(provider.Directory, options).Reverse().ToArray();
        if (paths.Length == 0)
        {
            return null;
        }

        var target = Path.Combine(FileSystem.CacheDirectory, SharedFileName);
        Directory.CreateDirectory(FileSystem.CacheDirectory);

        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        await writer.WriteLineAsync(BuildHeader()).ConfigureAwait(false);

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // FileShare.ReadWrite because the sink still holds the active file open for
                // writing. The convenience overloads ask for a share mode that excludes an
                // existing writer, so they throw against a perfectly healthy file.
                await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(input);
                await writer.WriteLineAsync($"--- {Path.GetFileName(path)} ---").ConfigureAwait(false);
                await writer.WriteAsync(await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
                await writer.WriteLineAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                await writer.WriteLineAsync($"--- {Path.GetFileName(path)} could not be read ---").ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await writer.WriteLineAsync($"--- {Path.GetFileName(path)} could not be read ---").ConfigureAwait(false);
            }
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        return target;
    }

    /// <inheritdoc />
    public ValueTask<long> DeleteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var reclaimed = provider.File.DeleteAll();
        CrashBreadcrumb.Clear(provider.Directory);
        lastCrash = null;
        crashRead = true;

        try
        {
            var shared = Path.Combine(FileSystem.CacheDirectory, SharedFileName);
            if (File.Exists(shared))
            {
                reclaimed += new FileInfo(shared).Length;
                File.Delete(shared);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return ValueTask.FromResult(reclaimed);
    }

    /// <inheritdoc />
    public void AcknowledgeCrash()
    {
        CrashBreadcrumb.Clear(provider.Directory);
        lastCrash = null;
        crashRead = true;
    }

    /// <inheritdoc />
    public IDisposable SuspendForErasure()
    {
        provider.File.Suspend();
        return new ErasureSuspension(provider);
    }

    private sealed class ErasureSuspension(ForgeFileLoggerProvider provider) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            provider.File.Resume();
        }
    }

    /// <summary>
    /// The plain-language preamble the shared file opens with.
    /// </summary>
    /// <remarks>
    /// Whoever receives this file should be able to see what it is without asking, and so should
    /// the person sending it if they open it first - which they are entitled to do and should be
    /// encouraged to. Saying "no personal data" would be a claim rather than a description, so it
    /// says what was removed and what was deliberately kept instead.
    /// </remarks>
    private static string BuildHeader()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Forge diagnostic log");
        builder.AppendLine(CultureInfo.InvariantCulture, $"Prepared {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC");
        builder.AppendLine();
        builder.AppendLine("This file records what Forge was doing when something went wrong. It stays on your");
        builder.AppendLine("device unless you choose to send it. Forge has no server and never uploads it.");
        builder.AppendLine();
        builder.AppendLine("Removed before anything was written: body measurements, food and drink entries,");
        builder.AppendLine("injuries, notes, names, email addresses, dates, and file paths.");
        builder.AppendLine();
        builder.AppendLine("Deliberately kept: the times Forge was running, error type names, and the internal");
        builder.AppendLine("code locations a fault came from.");
        builder.AppendLine();
        return builder.ToString();
    }
}
