using System.Globalization;
using Forge.Core.Abstractions.Diagnostics;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace Forge.Core.Tests.Diagnostics;

/// <summary>
/// Pins the budget, the rotation policy and the end-to-end path from <c>ILogger</c> to the file.
/// </summary>
/// <remarks>
/// The redaction tests prove the rules in isolation. These prove the rules are actually
/// <em>wired</em>: a call through the real <c>ILogger</c> surface, carrying a real exception with a
/// body weight and an injury in it, read back off a real file. An isolated redactor that the sink
/// forgets to call is the same defect as no redactor at all.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1848:Use the LoggerMessage delegates",
    Justification = "These tests exist to exercise the extension-method path that ordinary callers use. Substituting a source-generated delegate would test a route the app does not take, which is the whole point of the assertion.")]
public sealed class ForgeFileLoggerProviderTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "forge-log-tests",
        Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Constructing_the_provider_touches_no_files()
    {
        // The startup constraint, expressed as a test. Composition runs on the critical path to
        // the first frame, and a directory probe or a file open there is how a cold-start budget
        // gets spent without anyone noticing.
        using var provider = new ForgeFileLoggerProvider(directory);

        Directory.Exists(directory).ShouldBeFalse();
    }

    [Fact]
    public void The_directory_itself_is_not_resolved_until_the_first_entry()
    {
        // Stronger than the test above, and it is the one that matters on Android. The directory
        // comes from FileSystem.AppDataDirectory, which initialises MAUI Essentials and crosses
        // into Java: calling it during composition measured 331 ms on an emulator in a Release
        // build. Not "cheap to call" - not called at all until something is logged.
        var resolutions = 0;

        using var provider = new ForgeFileLoggerProvider(() =>
        {
            resolutions++;
            return directory;
        });

        resolutions.ShouldBe(0);

        provider.CreateLogger("Forge.App").LogInformation("first entry");
        provider.Flush(TimeSpan.FromSeconds(5));

        resolutions.ShouldBe(1);
    }

    [Fact]
    public void Flushing_a_sink_nothing_ever_logged_to_creates_nothing()
    {
        // Sharing and the settings screen both flush. Neither should be able to bring a log file
        // into existence on a device where nothing has gone wrong.
        var resolutions = 0;

        using var provider = new ForgeFileLoggerProvider(() =>
        {
            resolutions++;
            return directory;
        });

        provider.Flush(TimeSpan.FromSeconds(1));

        resolutions.ShouldBe(0);
        Directory.Exists(directory).ShouldBeFalse();
    }

    [Fact]
    public async Task A_body_weight_and_an_injury_do_not_reach_the_file()
    {
        using var provider = new ForgeFileLoggerProvider(directory);
        var logger = provider.CreateLogger("Forge.App.Features.Progress.BodyMetricsViewModel");

        var failure = new InvalidOperationException(
            "Could not save body weight 82.4 kg for injury: torn left meniscus");

        logger.LogError(failure, "Could not record the entry for profile named Alexandra");
        provider.Flush(TimeSpan.FromSeconds(5));

        var contents = await ReadLogAsync();

        contents.ShouldNotContain("82.4");
        contents.ShouldNotContain("meniscus");
        contents.ShouldNotContain("Alexandra");

        // The parts worth keeping did survive, or the sink would be useless.
        contents.ShouldContain("System.InvalidOperationException");
        contents.ShouldContain("BodyMetricsViewModel");
    }

    [Fact]
    public async Task A_structured_argument_is_redacted_like_everything_else()
    {
        // The template is ours and safe. The argument is not, and this is the shape the accident
        // takes: a perfectly innocent-looking template with a domain value poured into it.
        using var provider = new ForgeFileLoggerProvider(directory);
        var logger = provider.CreateLogger("Forge.App.Features.Nutrition");

        logger.LogWarning("Could not log the meal: {Meal}", "chicken and rice, 640 kcal");
        provider.Flush(TimeSpan.FromSeconds(5));

        var contents = await ReadLogAsync();

        contents.ShouldNotContain("chicken");
        contents.ShouldNotContain("640");
    }

    [Fact]
    public async Task Entries_below_the_minimum_level_are_not_written()
    {
        using var provider = new ForgeFileLoggerProvider(directory);
        var logger = provider.CreateLogger("Forge.App");

        logger.LogDebug("noisy development detail");
        logger.LogInformation("something worth keeping");
        provider.Flush(TimeSpan.FromSeconds(5));

        var contents = await ReadLogAsync();

        contents.ShouldNotContain("noisy development detail");
        contents.ShouldContain("something worth keeping");
    }

    [Fact]
    public async Task An_entry_carries_a_level_a_category_and_an_event_id()
    {
        using var provider = new ForgeFileLoggerProvider(directory);
        var logger = provider.CreateLogger("Forge.App.Composition.ForgeStartupService");

        logger.Log(LogLevel.Error, new EventId(1002), "Forge database startup failed.", null, (state, _) => state);
        provider.Flush(TimeSpan.FromSeconds(5));

        var contents = await ReadLogAsync();

        contents.ShouldContain("ERR");
        contents.ShouldContain("Forge.App.Composition.ForgeStartupService[1002]");
    }

    [Fact]
    public async Task An_immediate_write_reaches_the_file_without_the_drain_loop()
    {
        // The crash path. A queued entry depends on a thread-pool continuation being scheduled,
        // and a process that is about to be killed cannot promise one.
        using var provider = new ForgeFileLoggerProvider(directory);

        provider.WriteImmediate(
            DiagnosticLogLevel.Critical,
            "Forge.App.Diagnostics.ForgeCrashBoundary",
            "Unhandled exception",
            new InvalidOperationException("the thing that went wrong"));

        var contents = await ReadLogAsync();

        contents.ShouldContain("CRT");
        contents.ShouldContain("System.InvalidOperationException");
    }

    [Fact]
    public async Task The_files_never_exceed_the_budget()
    {
        var options = new DiagnosticLogOptions
        {
            MaxFileBytes = 2048,
            RetainedFileCount = 3,
        };

        using var provider = new ForgeFileLoggerProvider(directory, options);
        var logger = provider.CreateLogger("Forge.App");

        for (var i = 0; i < 500; i++)
        {
            logger.LogInformation("entry padded out so that rotation happens more than once over this loop");
        }

        provider.Flush(TimeSpan.FromSeconds(10));
        await Task.Delay(200, TestContext.Current.CancellationToken);
        provider.Flush(TimeSpan.FromSeconds(10));

        var files = Directory.GetFiles(directory);
        files.Length.ShouldBeLessThanOrEqualTo(options.RetainedFileCount);

        var total = files.Sum(path => new FileInfo(path).Length);
        total.ShouldBeLessThanOrEqualTo((long)options.MaxFileBytes * options.RetainedFileCount);
    }

    [Fact]
    public void Rotation_moves_the_active_file_along_and_drops_the_oldest()
    {
        var options = new DiagnosticLogOptions { MaxFileBytes = 1024, RetainedFileCount = 3 };
        using var file = new RollingLogFile(directory, options);

        for (var i = 0; i < 200; i++)
        {
            file.Write(new string('x', 100));
        }

        RollingLogFile.ExistingPaths(directory, options).Count.ShouldBe(3);
        File.Exists(Path.Combine(directory, "forge.3.log")).ShouldBeFalse();
    }

    [Fact]
    public void An_entry_larger_than_the_whole_file_budget_is_still_written()
    {
        // Losing it entirely would be worse than one oversized file, and it is always the
        // interesting one.
        var options = new DiagnosticLogOptions { MaxFileBytes = 1024, RetainedFileCount = 2 };
        using var file = new RollingLogFile(directory, options);

        file.Write(new string('y', 5000)).ShouldBeTrue();
        new FileInfo(file.ActivePath).Length.ShouldBeGreaterThan(4000);
    }

    [Fact]
    public void Deleting_removes_every_generation_and_reports_what_it_reclaimed()
    {
        var options = new DiagnosticLogOptions { MaxFileBytes = 1024, RetainedFileCount = 3 };
        using var file = new RollingLogFile(directory, options);

        for (var i = 0; i < 100; i++)
        {
            file.Write(new string('z', 100));
        }

        var before = file.GetTotalBytes();
        before.ShouldBeGreaterThan(0);

        file.DeleteAll().ShouldBe(before);
        RollingLogFile.ExistingPaths(directory, options).ShouldBeEmpty();
    }

    [Fact]
    public void Writing_again_after_a_delete_recreates_the_file()
    {
        // "Delete my data" runs while the app is still going. A sink that stayed dead afterwards
        // would silently stop logging for the rest of the launch.
        using var file = new RollingLogFile(directory, DiagnosticLogOptions.Default);

        file.Write("before");
        file.DeleteAll();
        file.Write("after").ShouldBeTrue();

        // FileShare.ReadWrite, not File.ReadAllText. The sink holds the file open for writing on
        // purpose so that sharing and adb can read it live, and the convenience overload asks for
        // a share mode that excludes an existing writer - so it throws against a perfectly
        // healthy file. Anything in the app that reads this file has to open it the same way.
        ReadShared(file.ActivePath).ShouldContain("after");
    }

    [Fact]
    public void A_crash_breadcrumb_round_trips_and_clears()
    {
        var breadcrumb = new CrashBreadcrumb(
            new DateTimeOffset(2026, 2, 11, 6, 30, 0, TimeSpan.Zero),
            "AppDomain",
            "System.InvalidOperationException");

        CrashBreadcrumb.Write(directory, breadcrumb).ShouldBeTrue();

        var read = CrashBreadcrumb.Read(directory);
        read.ShouldNotBeNull();
        read.Value.Source.ShouldBe("AppDomain");
        read.Value.ExceptionType.ShouldBe("System.InvalidOperationException");
        read.Value.OccurredAt.ShouldBe(breadcrumb.OccurredAt);

        CrashBreadcrumb.Clear(directory);
        CrashBreadcrumb.Read(directory).ShouldBeNull();
    }

    [Fact]
    public void A_breadcrumb_from_a_launch_that_ended_normally_is_absent()
    {
        CrashBreadcrumb.Read(directory).ShouldBeNull();
    }

    [Fact]
    public void A_budget_that_would_make_the_policy_meaningless_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new DiagnosticLogOptions { MaxFileBytes = 16 }.Validate());
        Should.Throw<ArgumentOutOfRangeException>(() => new DiagnosticLogOptions { RetainedFileCount = 0 }.Validate());
    }

    [Fact]
    public void The_default_budget_is_one_and_a_half_mebibytes()
    {
        // Stated as a test so that raising it is a deliberate act with a diff attached, rather
        // than a default that drifts. The reasoning is in docs/diagnostics/logging.md.
        var options = DiagnosticLogOptions.Default;

        ((long)options.MaxFileBytes * options.RetainedFileCount).ShouldBe(1536 * 1024);
    }

    [Fact]
    public void A_suspended_file_writes_nothing_and_holds_no_handle()
    {
        // The erasure hazard, pinned. LocalDataErasureService deletes the files under the app
        // data directory and then removes the directories; a log line landing between those two
        // passes re-creates this directory, the non-recursive delete fails on it, and a user who
        // asked to be forgotten is told their data could not be erased.
        using var file = new RollingLogFile(directory, DiagnosticLogOptions.Default);

        file.Write("before erasure").ShouldBeTrue();
        file.Suspend();

        file.Write("during erasure").ShouldBeFalse();

        // Nothing holds the file open any more, so erasure can delete it and then delete the
        // directory it sits in.
        File.Delete(file.ActivePath);
        Directory.Delete(directory);
        Directory.Exists(directory).ShouldBeFalse();

        file.Resume();
        file.Write("after erasure").ShouldBeTrue();

        ReadShared(file.ActivePath).ShouldNotContain("during erasure");
        ReadShared(file.ActivePath).ShouldContain("after erasure");

        // The pre-erasure entries are gone with everything else, which is the point.
        ReadShared(file.ActivePath).ShouldNotContain("before erasure");
    }

    private async Task<string> ReadLogAsync()
    {
        var path = Path.Combine(directory, RollingLogFile.ActiveFileName);

        // The drain loop is asynchronous by design, so a read straight after a write can race it.
        for (var attempt = 0; attempt < 40 && !File.Exists(path); attempt++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        File.Exists(path).ShouldBeTrue($"nothing was written to {path}");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
