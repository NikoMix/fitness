using Forge.Core.Abstractions.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace Forge.App.Diagnostics;

/// <summary>
/// Gives Forge somewhere to log in a Release build, and something to do when it crashes.
/// </summary>
/// <remarks>
/// <para>
/// Before this existed, <c>MauiProgram</c> configured <c>AddDebug()</c> inside <c>#if DEBUG</c>
/// and nothing else. A Release build - the only configuration a user ever runs - therefore had no
/// logging provider registered at all, and roughly twenty call sites across eleven files were
/// writing into nothing. They are the sites that matter: a failed migration, a failed SQLite
/// integrity check, a failed startup, a workout summary that would not build, a refused unlock.
/// Everything downstream that promises to "log the exception and enter recovery" had nowhere to
/// log.
/// </para>
/// <para>
/// <strong>Startup cost.</strong> This method subscribes three event handlers and allocates two
/// objects. It resolves no path, creates no directory, opens no file and probes nothing - not even
/// <c>FileSystem.AppDataDirectory</c>, which initialises MAUI Essentials and crosses into Java,
/// and which cost 331 ms here when it was called eagerly. All of it moves to the drain thread on
/// the first entry, after the shell is up. The <c>logging-configured</c> phase mark immediately
/// after the call is what keeps that claim honest rather than asserted: a stream took cold start
/// from ~27 s to ~10 s by removing five SQLCipher key derivations, and this is exactly how it gets
/// spent back.
/// </para>
/// </remarks>
internal static class ForgeDiagnostics
{
    /// <summary>Directory name under the app's private data directory.</summary>
    public const string DirectoryName = "diagnostics";

    /// <summary>
    /// Registers the file sink, the diagnostic log service and the crash boundary.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// <see cref="IDiagnosticLog"/> is bound here and nowhere else.
    /// <c>tools/ci/Test-ServiceRegistrations.ps1</c> fails the build if a type is bound in two
    /// files, because <c>IDataErasureService</c> was once bound to a working implementation in one
    /// feature and a throwing one in another, and "delete my account" worked only because one
    /// feature name sorted after the other.
    /// </remarks>
    public static MauiAppBuilder AddForgeDiagnostics(this MauiAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // The directory is resolved by a factory rather than here.
        //
        // FileSystem.AppDataDirectory initialises MAUI Essentials and crosses into Java, and
        // reading it during composition measured 331 ms on an emulator in a Release build - on
        // the critical path to the first frame, for a directory name nothing needs yet. It now
        // happens on the drain thread when the first entry is written, along with creating the
        // channel and opening the file.
        //
        // The location itself is app-private on both platforms, and inside the directory
        // LocalDataErasureService already walks - so "delete my data" reaches the log without
        // erasure needing to know the log exists.
        var provider = new ForgeFileLoggerProvider(
            () => Path.Combine(FileSystem.AppDataDirectory, DirectoryName));

        // AddProvider rather than a DI factory. The provider owns a file handle for the life of
        // the process and must exist before anything else can log, including the composition that
        // follows this call.
        builder.Logging.AddProvider(provider);

        builder.Services.AddSingleton<IDiagnosticLog>(new DiagnosticLog(provider));

        ForgeCrashBoundary.Install(provider);

        return builder;
    }
}
