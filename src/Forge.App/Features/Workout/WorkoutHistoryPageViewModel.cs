using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Navigation;
using Forge.Domain.Workout;

namespace Forge.App.Features.Workout;

/// <summary>
/// Past sessions, newest first.
/// </summary>
/// <remarks>
/// The list answers "what did I do last time?" in one glance, so each row leads with volume and
/// working sets rather than a title the user probably never typed. Unfinished sessions are
/// flagged rather than hidden: a session left open is usually one the user forgot to close, and
/// silently omitting it makes the history look like the workout never happened.
/// </remarks>
public sealed partial class WorkoutHistoryPageViewModel(
    IWorkoutClock clock,
    IWorkoutPersistenceService persistence) : ObservableObject
{
    private const int MaximumSessions = 100;

    /// <summary>Past sessions, newest first.</summary>
    public ObservableCollection<WorkoutHistoryRow> Sessions { get; } = [];

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSessions))]
    private bool isEmpty;

    [ObservableProperty]
    private string summaryText = string.Empty;

    /// <summary>Whether there is at least one session to show.</summary>
    public bool HasSessions => !IsEmpty;

    /// <summary>Loads the history list.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once the list is populated.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            Sessions.Clear();
            var entries = await persistence.LoadHistoryAsync(MaximumSessions, clock.UtcNow, cancellationToken);

            foreach (var entry in entries)
            {
                Sessions.Add(ToRow(entry));
            }

            IsEmpty = Sessions.Count == 0;
            SummaryText = IsEmpty
                ? string.Empty
                : string.Create(
                    CultureInfo.CurrentCulture,
                    $"{Sessions.Count} session{(Sessions.Count == 1 ? string.Empty : "s")} recorded on this device.");
        }
        catch (Exception ex)
        {
            // Deliberately broad. The local database is the only copy of this data, so a read
            // failure has to be shown as a message on a working screen rather than a crash that
            // takes away the user's only route to backup and export.
            IsEmpty = true;
            SummaryText = $"Forge could not read your history: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static Task OpenSummaryAsync(WorkoutHistoryRow row)
        => row is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{ForgeRoutes.WorkoutSummary}?sessionId={row.WorkoutSessionId}");

    private static WorkoutHistoryRow ToRow(WorkoutHistoryEntry entry)
    {
        var when = (entry.CompletedUtc ?? entry.StartedUtc).ToLocalTime().DateTime;
        var whenText = when.ToString("ddd d MMM, HH:mm", CultureInfo.CurrentCulture);
        var detail = string.Create(
            CultureInfo.CurrentCulture,
            $"{FormatDuration(entry.Duration)} · {entry.WorkingSetCount} working sets · {entry.TotalVolume.Kilograms:0.##} kg volume");
        var exercises = entry.ExerciseNames.Count == 0
            ? "No sets logged"
            : string.Join(" · ", entry.ExerciseNames.Take(4));

        return new WorkoutHistoryRow(
            entry.WorkoutSessionId,
            entry.IsInProgress ? $"{entry.Title} (unfinished)" : entry.Title,
            whenText,
            detail,
            exercises,
            entry.IsInProgress,
            $"{entry.Title}, {whenText}. {detail}. {exercises}.");
    }

    private static string FormatDuration(TimeSpan duration)
        => duration.TotalHours >= 1d
            ? string.Create(CultureInfo.CurrentCulture, $"{(int)duration.TotalHours}h {duration.Minutes:00}m")
            : string.Create(CultureInfo.CurrentCulture, $"{duration.Minutes}m");
}
