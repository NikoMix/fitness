using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Domain.Workout;

namespace Forge.App.Features.Workout;

public sealed partial class WorkoutSummaryPageViewModel(IWorkoutClock clock, IWorkoutPersistenceService persistence) : ObservableObject
{
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
    private Task DoneAsync() => Shell.Current.GoToAsync("..");
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
            Comparison = $"Your workout was saved, but Forge could not summarise it: {ex.Message}";
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
