using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Domain.Workout;
using Microsoft.Extensions.Logging;

namespace Forge.App.Features.Workout;

public sealed partial class WorkoutSummaryPageViewModel(
    IWorkoutClock clock,
    IWorkoutPersistenceService persistence,
    ILogger<WorkoutSummaryPageViewModel>? logger = null) : ObservableObject
{
    private static readonly Action<ILogger, Exception?> SummaryFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(1, nameof(SummaryFailed)), "Could not build the workout summary. The session itself was already saved.");

    private static readonly Action<ILogger, Exception?> NavigationFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(2, nameof(NavigationFailed)), "Could not leave the workout summary screen.");

    private static void LogSummaryFailed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            SummaryFailed(logger, exception);
        }
    }

    private static void LogNavigationFailed(ILogger? logger, Exception exception)
    {
        if (logger is not null)
        {
            NavigationFailed(logger, exception);
        }
    }

    public ObservableCollection<SummaryMetricRow> Metrics { get; } = [];

    public ObservableCollection<SummaryMetricRow> MuscleVolume { get; } = [];

    public ObservableCollection<string> Records { get; } = [];

    [ObservableProperty]
    private string title = "Workout complete";

    [ObservableProperty]
    private string comparison = "You showed up. Next time Forge will compare this against your previous effort.";

    [ObservableProperty]
    private bool isBusy;

    [RelayCommand]
#pragma warning disable CA1822 // RelayCommand source generation requires an instance command target for XAML binding.
    private async Task DoneAsync()
    {
        // "Done" is the only way off this screen, and finishing a workout ends here, so this is a
        // mainline path. It crashed the app: Shell threw
        // "Ambiguous routes matched for: .../train/workout-history" with two byte-identical
        // matches, and because AsyncRelayCommand rethrows on the sync context, a failed navigation
        // took the process down rather than doing nothing.
        //
        // The ambiguity is real - workout-history can be pushed onto one tab's stack more than
        // once, since both Train and the active-workout screen navigate to it, and a relative ".."
        // then has two identical candidates to resolve against. Rather than depend on the stack
        // having a shape this screen cannot see, fall back to the destination the user wants
        // anyway. Landing on workout history is right whichever way they arrived.
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            LogNavigationFailed(logger, ex);

            try
            {
                await Shell.Current.GoToAsync($"//{ForgeRoutes.Train}/{ForgeRoutes.WorkoutHistory}");
            }
            catch (Exception fallbackFailure)
            {
                // Never let leaving a screen be the thing that kills the app. A button that does
                // nothing is bad; a crash after a completed workout is worse.
                LogNavigationFailed(logger, fallbackFailure);
            }
        }
    }
#pragma warning restore CA1822

    public async Task LoadAsync(Guid? sessionId, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            Metrics.Clear();
            MuscleVolume.Clear();
            Records.Clear();

            var summary = await persistence.LoadSummaryAsync(sessionId, clock.UtcNow, cancellationToken);
            if (summary is null)
            {
                Metrics.Add(new SummaryMetricRow("Volume", "0 kg"));
                Metrics.Add(new SummaryMetricRow("Duration", "0:00"));
                return;
            }

            Metrics.Add(new SummaryMetricRow("Volume", $"{summary.TotalVolume.Kilograms:0.##} kg"));
            Metrics.Add(new SummaryMetricRow("Working sets", summary.WorkingSetCount.ToString(CultureInfo.CurrentCulture)));
            Metrics.Add(new SummaryMetricRow("Duration", FormatDuration(summary.Duration)));

            foreach (var item in summary.PerMuscleVolume.OrderByDescending(kvp => kvp.Value.Kilograms))
            {
                MuscleVolume.Add(new SummaryMetricRow(item.Key, $"{item.Value.Kilograms:0.##} kg"));
            }

            foreach (var record in summary.PersonalRecords)
            {
                Records.Add($"{record.ExerciseName}: {FormatRecordKind(record.Kind)} {record.CurrentValue:0.##}");
            }

            if (Records.Count == 0)
            {
                Records.Add("No PRs today — consistency still compounds.");
            }
        }
        catch (Exception ex)
        {
            // Deliberately broad. The workout has already been saved by the time this screen runs,
            // so a failure to summarise it must not look like the session was lost.
            //
            // The message is fixed rather than interpolated from the exception. This screen once
            // rendered a raw EF translation failure - LINQ expression text, a parameter name and a
            // Microsoft support URL - to somebody who had just finished training. It said nothing
            // they could act on and it undermined the reassurance in the same sentence. The detail
            // belongs in the log, where it is just as useful and nobody has to read it.
            LogSummaryFailed(logger, ex);
            Comparison = "Your workout was saved. Forge could not put together the summary this time, so the numbers above may be incomplete.";

            // Nothing partial is left on screen claiming to be a comparison.
            MuscleVolume.Clear();
            Records.Clear();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatDuration(TimeSpan duration) => duration.TotalHours >= 1d ? $"{(int)duration.TotalHours}:{duration.Minutes:00}" : $"{duration.Minutes}:{duration.Seconds:00}";

    private static string FormatRecordKind(PersonalRecordKind kind) => kind switch
    {
        PersonalRecordKind.HeaviestLoad => "heaviest load",
        PersonalRecordKind.SetVolume => "best set volume",
        PersonalRecordKind.EstimatedOneRepMax => "estimated 1RM",
        _ => "record"
    };
}
