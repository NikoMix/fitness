using Forge.Core.Abstractions.Diagnostics;

namespace Forge.App.Diagnostics;

/// <summary>
/// Catches the exceptions nothing else will, records them, and lets the next launch say so.
/// </summary>
/// <remarks>
/// <para>
/// Forge has no crash-reporting service and will not get one. Without this, an unhandled exception
/// produced a process that vanished and a user who could describe only a blank screen - and
/// because Release had no logging provider at all, there was nothing on the device to look at
/// either.
/// </para>
/// <para>
/// <strong>What this can and cannot do.</strong> An unhandled exception on the UI thread kills the
/// process whatever Forge does about it. Swallowing it would leave the app running on state
/// nobody can reason about, which is how a crash becomes silent data corruption, so this does not
/// try. What it does instead is make the death informative: the fault is written to the log
/// synchronously, before the runtime tears the process down, and a small breadcrumb is left so
/// that the next launch knows the last one ended badly and can offer the log rather than
/// pretending nothing happened.
/// </para>
/// <para>
/// Unobserved task exceptions are different and are genuinely recoverable: they are marked
/// observed, so a faulted fire-and-forget task is recorded rather than escalated.
/// </para>
/// </remarks>
internal static class ForgeCrashBoundary
{
    private const string Category = "Forge.App.Diagnostics.ForgeCrashBoundary";

    private static ForgeFileLoggerProvider? sink;
    private static bool installed;
    private static Exception? lastCaptured;

    /// <summary>
    /// Subscribes the process-wide handlers.
    /// </summary>
    /// <param name="provider">The sink to write through, and the source of the breadcrumb directory.</param>
    /// <remarks>
    /// Idempotent. The handlers are process-wide and static, so a second subscription would write
    /// every fault twice. Subscribing is all this does: the directory behind the breadcrumb is not
    /// resolved until a crash actually happens, so installing the boundary costs nothing at
    /// startup.
    /// </remarks>
    public static void Install(ForgeFileLoggerProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (installed)
        {
            return;
        }

        installed = true;
        sink = provider;

        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

#if ANDROID
        // The one that actually fires on Android. A managed exception escaping a Java-invoked
        // frame - which is every UI callback - reaches this before AppDomain sees anything, and
        // in several cases instead of it.
        Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += OnAndroidUnhandledException;
#endif
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        => Capture(e.ExceptionObject as Exception, "AppDomain", e.IsTerminating);

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Capture(e.Exception, "TaskScheduler", terminating: false);

        // Marked observed deliberately. A faulted fire-and-forget task - the reminder refresh, a
        // media prefetch - is worth recording and is not worth ending a training session over.
        // The runtime's default is already not to escalate, but stating it here means a future
        // ThrowUnobservedTaskExceptions setting cannot quietly change that.
        e.SetObserved();
    }

#if ANDROID
    private static void OnAndroidUnhandledException(object? sender, Android.Runtime.RaiseThrowableEventArgs e)
    {
        // e.Handled is deliberately left alone. Setting it swallows the exception and lets the
        // app carry on over state nobody can account for, which trades a visible crash for
        // invisible corruption of the only copy of the user's training history.
        Capture(e.Exception, "Android", terminating: !e.Handled);
    }
#endif

    private static void Capture(Exception? exception, string source, bool terminating)
    {
        try
        {
            var provider = sink;
            if (provider is null)
            {
                return;
            }

            // On Android a terminating fault reaches BOTH handlers - AndroidEnvironment first,
            // then AppDomain - and writing it twice was measured, not assumed: a single
            // deliberate crash produced two identical entries with two stack traces. That halves
            // the useful history in a crash loop, which is precisely the case the three-file
            // retention exists for.
            //
            // Reference equality, so only the same exception OBJECT arriving twice is suppressed.
            // Two genuinely different faults are always both recorded, even when their messages
            // and types match. The breadcrumb below is written either way, because it is
            // idempotent and losing it is worse than rewriting it.
            var alreadySeen = ReferenceEquals(lastCaptured, exception) && exception is not null;
            lastCaptured = exception;

            if (!alreadySeen)
            {
                // Immediate, not queued. The drain loop needs a thread-pool continuation to be
                // scheduled, and a process the runtime is already tearing down cannot promise one
                // - so the single entry that explains the crash is the one that must not be
                // queued.
                provider.WriteImmediate(
                    DiagnosticLogLevel.Critical,
                    Category,
                    terminating
                        ? $"Unhandled exception from {source}. The process is going down."
                        : $"Unhandled exception from {source}. The app is continuing.",
                    exception);
            }

            if (terminating)
            {
                CrashBreadcrumb.Write(
                    provider.Directory,
                    new CrashBreadcrumb(
                        DateTimeOffset.UtcNow,
                        source,
                        exception?.GetType().FullName ?? "unknown"));
            }
        }
        catch (Exception)
        {
            // Nothing. This is the last handler in the process: an exception raised here has
            // nowhere left to go, and letting one escape would replace a diagnosable crash with
            // an undiagnosable one.
        }
    }
}
