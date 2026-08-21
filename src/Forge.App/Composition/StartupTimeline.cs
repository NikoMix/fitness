using System.Diagnostics;
using System.Globalization;

namespace Forge.App.Composition;

/// <summary>
/// Emits startup phase marks so cold start can be attributed rather than guessed at.
/// </summary>
/// <remarks>
/// <para>
/// Forge budgeted cold start at under 2.0 s from its first commit and then never measured it.
/// A budget with no instrument behind it is a wish, and the first thing anyone investigating a
/// slow launch needs is not a total but a breakdown: how much went on the native runtime before
/// managed code ran, how much on building the container, how much on the DevExpress registration
/// chain. These marks are what make that breakdown readable off a device with nothing attached
/// but adb.
/// </para>
/// <para>
/// Marks are emitted in Release as well as Debug, deliberately. A release-only startup regression
/// is the normal kind - it is usually linking, AOT or assembly load that moves, none of which a
/// Debug build can show you - so instrumentation that switches itself off in the configuration
/// that ships would only ever measure the configuration nobody runs. The payload is phase names
/// and durations, with nothing about the user in it.
/// </para>
/// <para>
/// The cost of the instrument is measured rather than asserted. <c>MauiProgram</c> emits two
/// marks back to back at entry, so the gap between <c>program-enter</c> and <c>timeline-probe</c>
/// is the cost of exactly one <see cref="Mark"/> call on that device. If that number is ever
/// large enough to matter, the report shows it instead of hiding it inside another phase.
/// </para>
/// <para>
/// Output is one line per mark under the <c>ForgePerf</c> tag, formatted for machine reading by
/// <c>tools/perf/Measure-ColdStart.ps1</c>:
/// <code>phase=container-built t=412.7 proc=618.3 req=741.2</code>
/// </para>
/// </remarks>
internal static class StartupTimeline
{
    /// <summary>Logcat tag the performance harness filters on.</summary>
    internal const string LogTag = "ForgePerf";

    // Eight marks are emitted before the flush today. The cap exists so a future caller cannot
    // turn a diagnostic buffer into an unbounded allocation on the startup path; overflow falls
    // back to writing directly rather than losing the mark.
    private const int MaxBufferedMarks = 24;

    private static readonly (string Phase, double ElapsedMs)[] BufferedMarks = new (string, double)[MaxBufferedMarks];
    private static readonly Lock BufferLock = new();
    private static int bufferedCount;
    private static volatile bool flushed;

    private static readonly double ProcessAgeAtOriginMs;
    private static readonly double LaunchRequestAgeAtOriginMs;
    private static readonly long OriginTimestamp;

    /// <summary>
    /// Captures the timeline origin.
    /// </summary>
    /// <remarks>
    /// An explicit static constructor, rather than field initialisers, because the order matters
    /// and is not obvious. The process ages are read FIRST and the stopwatch origin is taken
    /// LAST, so that whatever those reads cost is excluded from every phase delta. The first
    /// version of this class had it the other way round, over a path that parsed procfs, and the
    /// failed parse charged 160 ms of its own overhead to the first phase - an instrument that
    /// reported mostly itself.
    /// </remarks>
    static StartupTimeline()
    {
        ProcessAgeAtOriginMs = ReadProcessAgeMilliseconds();
        LaunchRequestAgeAtOriginMs = ReadLaunchRequestAgeMilliseconds();
        OriginTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>Records that a startup phase has been reached.</summary>
    /// <param name="phase">
    /// Stable machine-readable phase name. Changing one silently breaks the trend comparison
    /// against previously recorded results, so treat these as an interface rather than log text.
    /// </param>
    /// <remarks>
    /// Marks taken before <see cref="FlushInBackground"/> are buffered in memory rather than
    /// written to logcat. That is not premature caution: writing the first one directly was
    /// measured at 136 ms on a Release build, because the first call has to resolve and warm the
    /// whole Android logging path, and it landed squarely on the critical path to the first
    /// frame. Buffering reduces a mark to a timestamp read and an array write, and moves the
    /// logging cost onto a background thread that runs after the shell is up.
    /// </remarks>
    public static void Mark(string phase)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(OriginTimestamp).TotalMilliseconds;

        if (flushed)
        {
            Write(Format(phase, elapsedMs));
            return;
        }

        lock (BufferLock)
        {
            if (!flushed && bufferedCount < MaxBufferedMarks)
            {
                BufferedMarks[bufferedCount++] = (phase, elapsedMs);
                return;
            }
        }

        Write(Format(phase, elapsedMs));
    }

    /// <summary>
    /// Writes the buffered marks to logcat from a background thread.
    /// </summary>
    /// <remarks>
    /// Called by <c>ForgeStartupService</c>, which already runs off the UI thread after the shell
    /// has been handed to the window. Dispatched rather than run inline because that service is
    /// also awaited directly by the first data-backed screen, and on that path the caller may be
    /// the UI thread - which is exactly where this work must not happen.
    /// </remarks>
    public static void FlushInBackground()
    {
        if (flushed)
        {
            return;
        }

        _ = Task.Run(Flush);
    }

    private static void Flush()
    {
        (string Phase, double ElapsedMs)[] snapshot;
        int count;

        lock (BufferLock)
        {
            if (flushed)
            {
                return;
            }

            count = bufferedCount;
            snapshot = BufferedMarks;
            flushed = true;
        }

        // The anchor is what lets the harness put the buffered marks back onto real time. Every
        // buffered line carries an elapsed offset but is written long after the moment it
        // describes, so its own logcat timestamp is meaningless. This line reports the elapsed
        // offset AT THE MOMENT IT IS WRITTEN, so its logcat timestamp and its offset together
        // pin the timeline origin to the device clock - and from there every other mark can be
        // compared against the system's own 'Displayed' event.
        var anchorMs = Stopwatch.GetElapsedTime(OriginTimestamp).TotalMilliseconds;
        Write(Format("timeline-anchor", anchorMs));

        for (var i = 0; i < count; i++)
        {
            Write(Format(snapshot[i].Phase, snapshot[i].ElapsedMs));
        }
    }

    private static string Format(string phase, double elapsedMs) =>
        // Invariant formatting is not cosmetic here. The harness parses these numbers, and on a
        // machine with a comma decimal separator - which is where this was developed - culture
        // formatting turns 412.7 into "412,7" and every phase silently fails to parse.
        string.Create(
            CultureInfo.InvariantCulture,
            $"phase={phase} t={elapsedMs:F1} proc={ProcessAgeAtOriginMs:F1} req={LaunchRequestAgeAtOriginMs:F1}");

    private static void Write(string message)
    {
#if ANDROID
        // Discarded because Log.Info returns the written byte count, and IDE0058 treats an unused
        // expression value as an error under EnforceCodeStyleInBuild.
        _ = Android.Util.Log.Info(LogTag, message);
#else
        Console.WriteLine($"{LogTag}: {message}");
#endif
    }

    /// <summary>
    /// Returns how long the OS process had been alive when the timeline started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only way to see the cost of everything that happens before Forge's own code:
    /// the zygote fork, loading libmonodroid, starting the runtime and mapping or JITting the
    /// assemblies. On a MAUI app that segment is routinely a large share of cold start, and a
    /// breakdown that begins at <c>CreateMauiApp</c> would attribute all of it to nothing at all.
    /// </para>
    /// <para>
    /// Uses the platform API rather than <see cref="Process.StartTime"/>, which throws on Android
    /// for want of permissions, and rather than parsing <c>/proc/self/stat</c>, which was the
    /// first attempt here and cost more to fail than the phase it was measuring.
    /// </para>
    /// </remarks>
    /// <returns>Process age in milliseconds, or -1 where the platform cannot report it.</returns>
    private static double ReadProcessAgeMilliseconds()
    {
#if ANDROID
        try
        {
            return Android.OS.SystemClock.ElapsedRealtime() - Android.OS.Process.StartElapsedRealtime;
        }
        catch (Exception)
        {
            // Diagnostics must never be able to break startup.
            return -1;
        }
#else
        return -1;
#endif
    }

    /// <summary>
    /// Returns how long ago the system was asked to start this process.
    /// </summary>
    /// <remarks>
    /// Sits earlier than the process itself: it covers the time Android spent handling the launch
    /// intent before there was a process at all. The difference between this and the process age
    /// is overhead Forge cannot influence, which is worth separating out before anyone spends a
    /// day trying to optimise it. Requires API 33, so it reports unknown on Android 8 to 12
    /// despite Forge supporting them.
    /// </remarks>
    /// <returns>Milliseconds since the launch request, or -1 where the platform cannot report it.</returns>
    private static double ReadLaunchRequestAgeMilliseconds()
    {
#if ANDROID
        try
        {
            // API 33, not 30 - the analyzer caught that guess. Reporting unknown on older
            // devices is better than reporting a duration measured from a different epoch and
            // letting someone act on it.
            if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                return -1;
            }

            return Android.OS.SystemClock.ElapsedRealtime() - Android.OS.Process.StartRequestedElapsedRealtime;
        }
        catch (Exception)
        {
            return -1;
        }
#else
        return -1;
#endif
    }
}
