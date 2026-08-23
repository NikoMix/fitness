using System.Text;

namespace Forge.Core.Abstractions.Diagnostics;

/// <summary>
/// A size-capped, rotating text file.
/// </summary>
/// <remarks>
/// <para>
/// The active file is <c>forge.log</c>; older ones are <c>forge.1.log</c> and <c>forge.2.log</c>.
/// Fixed names rather than timestamps, so that the set of files the app can ever have is finite
/// and known: erasure knows exactly what to delete, sharing knows exactly what to attach, and
/// there is no way for a stale file from a build six months ago to survive because its name did
/// not match a glob.
/// </para>
/// <para>
/// Rotation happens on write, before the entry is appended, so the cap is a real ceiling rather
/// than a ceiling plus one entry. Total bytes on disk are therefore bounded by
/// <see cref="DiagnosticLogOptions.MaxFileBytes"/> multiplied by
/// <see cref="DiagnosticLogOptions.RetainedFileCount"/>, with the single exception of one entry
/// longer than the file cap, which is written whole rather than lost.
/// </para>
/// <para>
/// Every operation is guarded by one lock and every write is synchronous. That is deliberate: the
/// caller that matters most is the crash boundary, which is running inside a process that is
/// about to be killed and cannot wait for an asynchronous flush to be scheduled. Contention is
/// nil because the only routine writer is a single background drain loop.
/// </para>
/// <para>
/// Nothing here touches the filesystem until the first write. Constructing this type is free, so
/// it can be built during composition without putting a file open on the startup path.
/// </para>
/// </remarks>
public sealed class RollingLogFile : IDisposable
{
    /// <summary>Name of the file currently being written.</summary>
    public const string ActiveFileName = "forge.log";

    private readonly Lock gate = new();
    private readonly string directory;
    private readonly DiagnosticLogOptions options;
    private readonly Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private FileStream? stream;
    private long bytesWritten;
    private bool disposed;
    private bool faulted;
    private bool suspended;

    /// <summary>Creates a rolling file in <paramref name="directory"/>.</summary>
    /// <param name="directory">Directory the files live in. Created on first write, not now.</param>
    /// <param name="options">The budget and rotation policy.</param>
    public RollingLogFile(string directory, DiagnosticLogOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        this.directory = directory;
        this.options = options;
    }

    /// <summary>Absolute path of the file currently being written.</summary>
    public string ActivePath => Path.Combine(directory, ActiveFileName);

    /// <summary>
    /// Whether a filesystem fault has permanently disabled writing.
    /// </summary>
    /// <remarks>
    /// Once the disk refuses, it is not going to start working within the launch, and retrying on
    /// every entry would turn a full disk into a performance problem on top of a logging one.
    /// </remarks>
    public bool IsFaulted
    {
        get
        {
            lock (gate)
            {
                return faulted;
            }
        }
    }

    /// <summary>
    /// The paths of every log file that exists, newest first.
    /// </summary>
    /// <param name="directory">Directory the files live in.</param>
    /// <param name="options">The policy that decides how many there can be.</param>
    /// <returns>Existing paths only.</returns>
    public static IReadOnlyList<string> ExistingPaths(string directory, DiagnosticLogOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(options);

        var paths = new List<string>();
        for (var index = 0; index < options.RetainedFileCount; index++)
        {
            var path = PathForGeneration(directory, index);
            if (File.Exists(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>Appends one line, rotating first if it would not fit.</summary>
    /// <param name="line">The line to append. Already redacted by the caller.</param>
    /// <returns><see langword="true"/> when the line reached the disk.</returns>
    /// <remarks>
    /// Swallows every filesystem fault. A diagnostic sink that can throw would turn a full disk
    /// into a crash inside the crash handler, which is the one place an exception has nowhere
    /// left to go.
    /// </remarks>
    public bool Write(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        lock (gate)
        {
            if (disposed || faulted || suspended)
            {
                return false;
            }

            try
            {
                var bytes = encoding.GetBytes(line + '\n');
                EnsureOpen();

                // Rotate before appending rather than after, so the cap is never exceeded. An
                // entry larger than the whole cap is written anyway: losing it entirely would be
                // worse than one oversized file, and it is always the interesting one.
                if (stream is not null && bytesWritten > 0 && bytesWritten + bytes.Length > options.MaxFileBytes)
                {
                    Rotate();
                    EnsureOpen();
                }

                if (stream is null)
                {
                    return false;
                }

                stream.Write(bytes);
                stream.Flush();
                bytesWritten += bytes.Length;
                return true;
            }
            catch (IOException)
            {
                faulted = true;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                faulted = true;
                return false;
            }
            catch (NotSupportedException)
            {
                faulted = true;
                return false;
            }
        }
    }

    /// <summary>
    /// Closes the file and refuses writes until <see cref="Resume"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the erasure flow, and it fixes a real hazard rather than a theoretical one.
    /// <c>LocalDataErasureService</c> deletes every file under the app data directory and then
    /// deletes the directories bottom-up. This sink lives inside that tree and re-creates its
    /// directory on the next entry, so a single log line landing between those two passes leaves
    /// a fresh <c>diagnostics/forge.log</c> behind, the non-recursive directory delete fails on a
    /// non-empty directory, and the user is told their data could not be erased.
    /// </para>
    /// <para>
    /// It also settles the more important question. "Delete my data" leaving behind a log of the
    /// deleted data would be a breach, so the sink stops holding a handle, stops writing, and
    /// starts a fresh file afterwards.
    /// </para>
    /// </remarks>
    public void Suspend()
    {
        lock (gate)
        {
            suspended = true;
            CloseStream();
        }
    }

    /// <summary>Allows writing again, starting a new file.</summary>
    public void Resume()
    {
        lock (gate)
        {
            suspended = false;
            bytesWritten = 0;
            faulted = false;
        }
    }

    /// <summary>Pushes anything buffered to the disk.</summary>
    public void Flush()
    {
        lock (gate)
        {
            try
            {
                stream?.Flush(flushToDisk: true);
            }
            catch (IOException)
            {
                faulted = true;
            }
        }
    }

    /// <summary>
    /// Deletes every log file, including the active one.
    /// </summary>
    /// <returns>Bytes reclaimed.</returns>
    /// <remarks>
    /// Log files are user data on the device, so "delete my data" has to reach them. It already
    /// does, because they live under the app data directory that <c>LocalDataErasureService</c>
    /// walks - but a user who wants only the log gone should not have to erase their training
    /// history to get it, so this exists as well.
    /// </remarks>
    public long DeleteAll()
    {
        lock (gate)
        {
            CloseStream();

            long reclaimed = 0;
            for (var index = 0; index < options.RetainedFileCount; index++)
            {
                var path = PathForGeneration(directory, index);
                try
                {
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    reclaimed += new FileInfo(path).Length;
                    File.Delete(path);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            bytesWritten = 0;

            // A fault recorded against a file that no longer exists is stale. Clearing it here is
            // what lets a user who ran out of space free some and get logging back without
            // restarting the app.
            faulted = false;
            return reclaimed;
        }
    }

    /// <summary>Total bytes currently held by every log file.</summary>
    /// <returns>Bytes on disk.</returns>
    public long GetTotalBytes()
    {
        long total = 0;
        for (var index = 0; index < options.RetainedFileCount; index++)
        {
            try
            {
                var path = PathForGeneration(directory, index);
                if (File.Exists(path))
                {
                    total += new FileInfo(path).Length;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return total;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            CloseStream();
            disposed = true;
        }
    }

    private static string PathForGeneration(string directory, int generation) =>
        Path.Combine(directory, generation == 0 ? ActiveFileName : $"forge.{generation}.log");

    private void EnsureOpen()
    {
        // An open handle whose file has been unlinked is not an error on Android - the writes
        // keep succeeding into a file that no longer has a name, and the log silently stops
        // existing. That is not hypothetical here: LocalDataErasureService deletes everything
        // under the app data directory, which is where these files live, and it runs while the
        // app is still going. Re-checking costs one stat per entry and is what makes the sink
        // come back by itself afterwards rather than staying quietly dead for the rest of the
        // launch.
        if (stream is not null && !File.Exists(ActivePath))
        {
            CloseStream();
        }

        if (stream is not null)
        {
            return;
        }

        Directory.CreateDirectory(directory);

        // FileShare.Read so the share sheet, and adb, can read the file while the app still has
        // it open. Without it, exporting the log would need the app to close it first, which is
        // exactly the moment nobody can guarantee. Readers must ask for FileShare.ReadWrite in
        // turn: File.ReadAllText does not, and throws against a perfectly healthy file.
        stream = new FileStream(ActivePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        bytesWritten = stream.Length;
    }

    private void Rotate()
    {
        CloseStream();

        var oldest = PathForGeneration(directory, options.RetainedFileCount - 1);
        try
        {
            if (File.Exists(oldest))
            {
                File.Delete(oldest);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        for (var generation = options.RetainedFileCount - 2; generation >= 0; generation--)
        {
            var from = PathForGeneration(directory, generation);
            var to = PathForGeneration(directory, generation + 1);
            try
            {
                if (File.Exists(from))
                {
                    File.Move(from, to, overwrite: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        bytesWritten = 0;
    }

    private void CloseStream()
    {
        try
        {
            stream?.Dispose();
        }
        catch (IOException)
        {
        }
        finally
        {
            stream = null;
        }
    }
}
