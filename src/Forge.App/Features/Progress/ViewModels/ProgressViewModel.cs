using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.App.Features.Insights.ViewModels;
using Forge.App.Navigation;
using Forge.Domain.Analytics;

namespace Forge.App.Features.Progress.ViewModels;

/// <summary>
/// The Progress overview: how consistently you have trained, and how volume and intensity moved.
/// </summary>
/// <remarks>
/// Volume and mean load are drawn as two separate charts rather than as two series sharing one
/// frame with twin axes. Volume runs in thousands of kilograms and mean load in tens, so a shared
/// axis would flatten one of them, and scaling them independently onto one frame invents a visual
/// relationship whose apparent strength is decided entirely by the scaling chosen.
/// </remarks>
public sealed partial class ProgressViewModel(IInsightsDataService dataService) : ObservableObject
{
    /// <summary>Focused screens reachable from Progress.</summary>
    public IReadOnlyList<ProgressDestinationViewModel> Destinations { get; } =
    [
        new ProgressDestinationViewModel("Insights", "Muscle and pattern breakdowns, and what your own data does and does not support.", ForgeRoutes.Insights),
        new ProgressDestinationViewModel("Exercise progress", "Estimated 1RM progression, labelled with the formula that produced it.", ForgeRoutes.ExerciseProgress),
        new ProgressDestinationViewModel("Personal records", "Heaviest loads, reps at a load and session-volume records, each with its date.", ForgeRoutes.PersonalRecords),
        new ProgressDestinationViewModel("Body metrics", "Smoothed weight trend with the raw entries kept visible.", ForgeRoutes.BodyMetrics),
    ];

    /// <summary>Weekly volume and intensity, oldest first.</summary>
    public ObservableCollection<TrainingWeekViewModel> Weeks { get; } = [];

    /// <summary>Sessions per week against the plan, oldest first.</summary>
    public ObservableCollection<ConsistencyWeekViewModel> ConsistencyWeeks { get; } = [];

    /// <summary>Why bodyweight sets do not appear in the mean load line.</summary>
    public string MeanLoadCaveat { get; } = TrainingTrendAggregator.MeanLoadCaveat;

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private bool hasTrainingData;

    [ObservableProperty]
    private string summary = "Reading your local training history.";

    [ObservableProperty]
    private string consistencyHeadline = string.Empty;

    [ObservableProperty]
    private string consistencyDetail = string.Empty;

    [ObservableProperty]
    private string consistencyStats = string.Empty;

    [ObservableProperty]
    private string consistencyNote = string.Empty;

    [ObservableProperty]
    private string consistencySummary = string.Empty;

    [ObservableProperty]
    private bool showConsistencyChart;

    [ObservableProperty]
    private bool showConsistencyValues;

    [ObservableProperty]
    private bool hasConsistencyStats;

    [ObservableProperty]
    private bool hasConsistencyNote;

    [ObservableProperty]
    private string volumeNote = string.Empty;

    [ObservableProperty]
    private string volumeSummary = string.Empty;

    [ObservableProperty]
    private bool showVolumeChart;

    [ObservableProperty]
    private bool showVolumeNote;

    [ObservableProperty]
    private string meanLoadNote = string.Empty;

    [ObservableProperty]
    private string meanLoadSummary = string.Empty;

    [ObservableProperty]
    private bool showMeanLoadChart;

    [ObservableProperty]
    private bool showMeanLoadNote;

    [ObservableProperty]
    private bool showWeeklyValues;

    /// <summary>Whether nothing has been logged yet.</summary>
    public bool IsEmpty => !IsLoading && !HasTrainingData;

    /// <summary>Whether the training cards have anything to show.</summary>
    public bool HasTraining => !IsLoading && HasTrainingData;

    partial void OnIsLoadingChanged(bool value) => RaiseDerived();

    partial void OnHasTrainingDataChanged(bool value) => RaiseDerived();

    /// <summary>Loads the overview from local storage.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the screen is populated.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var overview = await dataService
                .LoadProgressAsync(DateOnly.FromDateTime(DateTime.Now), cancellationToken)
                .ConfigureAwait(false);

            var weeks = overview.Weeks.Select(TrainingWeekViewModel.From).ToList();
            var consistencyWeeks = overview.Consistency.Weeks.Select(ConsistencyWeekViewModel.From).ToList();

            var volumeNarration = ChartNarrator.Describe(
                "Weekly volume",
                [.. weeks.Select(week => new NarratedPoint(week.WeekLabel, week.VolumeKilograms))],
                "kg");

            var loadNarration = ChartNarrator.Describe(
                "Weekly mean load",
                [.. weeks.Where(week => week.HasLoadedSets).Select(week => new NarratedPoint(week.WeekLabel, week.MeanLoadKilograms))],
                "kg");

            var sessionNarration = ChartNarrator.Describe(
                "Sessions per week",
                [.. consistencyWeeks.Select(week => new NarratedPoint(week.WeekLabel, week.SessionsCompleted))],
                string.Empty);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Replace(Weeks, weeks);
                Replace(ConsistencyWeeks, consistencyWeeks);

                HasTrainingData = overview.Totals.HasTraining;
                Summary = HasTrainingData
                    ? $"{overview.Totals.CompletedSessions} completed sessions · {overview.Totals.WorkingSets} working sets · {overview.Totals.TotalVolumeKilograms:0.##} kg total volume"
                    : "Nothing has been logged on this device yet.";

                ApplyConsistency(overview.Consistency, sessionNarration);

                ShowVolumeChart = overview.VolumeReadiness.CanChart;
                ShowVolumeNote = !ShowVolumeChart;
                VolumeNote = overview.VolumeReadiness.Explanation;
                VolumeSummary = volumeNarration;

                ShowMeanLoadChart = overview.MeanLoadReadiness.CanChart;
                ShowMeanLoadNote = !ShowMeanLoadChart;
                MeanLoadNote = overview.MeanLoadReadiness.Explanation;
                MeanLoadSummary = loadNarration;

                // One list of the weekly figures backs both charts whenever either is too sparse
                // to draw, so a new user still sees every number Forge actually holds.
                ShowWeeklyValues = weeks.Count > 0 && !(ShowVolumeChart && ShowMeanLoadChart);
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
        }
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasTraining));
    }

    private void ApplyConsistency(ConsistencySummary consistency, string narration)
    {
        ConsistencyHeadline = consistency.Headline;
        ConsistencyDetail = consistency.Detail;
        ConsistencyNote = consistency.Readiness.CanChart ? string.Empty : consistency.Readiness.Explanation;
        ConsistencySummary = narration;
        ShowConsistencyChart = consistency.Readiness.CanChart;
        ShowConsistencyValues = consistency.Weeks.Count > 0 && !consistency.Readiness.CanChart;

        var parts = new List<string>();

        if (consistency.CurrentActiveWeekStreak > 0)
        {
            parts.Add(consistency.CurrentActiveWeekStreak == 1
                ? "1 week in a row with training"
                : $"{consistency.CurrentActiveWeekStreak} weeks in a row with training");
        }

        if (consistency.LongestActiveWeekStreak > consistency.CurrentActiveWeekStreak)
        {
            parts.Add($"best run {consistency.LongestActiveWeekStreak} weeks");
        }

        if (consistency.HasAdherenceClaim)
        {
            var percent = Math.Round(consistency.AdherenceRatio * 100m, MidpointRounding.AwayFromZero);
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"{percent:0}% of planned sessions"));
            parts.Add($"{consistency.WeeksMeetingPlan} of {consistency.CompletedWeeksAnalysed} full weeks on target");
        }

        ConsistencyStats = parts.Count == 0 ? string.Empty : string.Join(" · ", parts);
        HasConsistencyStats = ConsistencyStats.Length > 0;
        HasConsistencyNote = ConsistencyNote.Length > 0;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    [RelayCommand]
    private static Task StartLoggingAsync() => Shell.Current.GoToAsync(ForgeRoutes.Train);

    [RelayCommand]
    private static Task OpenDestinationAsync(ProgressDestinationViewModel destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return Shell.Current.GoToAsync(destination.Route);
    }
}

/// <summary>One link out of the Progress overview.</summary>
/// <param name="Title">Destination name.</param>
/// <param name="Detail">What the destination shows.</param>
/// <param name="Route">Shell route to navigate to.</param>
public sealed record ProgressDestinationViewModel(string Title, string Detail, string Route);

/// <summary>One week of sessions against the plan, formatted for display.</summary>
/// <param name="WeekLabel">Short label for the week.</param>
/// <param name="SessionsCompleted">Sessions completed in the week.</param>
/// <param name="Detail">One line describing the week against its target.</param>
public sealed record ConsistencyWeekViewModel(string WeekLabel, double SessionsCompleted, string Detail)
{
    /// <summary>Projects an analysed week into display form.</summary>
    /// <param name="week">The analysed week.</param>
    /// <returns>The display model.</returns>
    public static ConsistencyWeekViewModel From(ConsistencyWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);

        var sessions = week.SessionsCompleted == 1 ? "1 session" : $"{week.SessionsCompleted} sessions";
        var detail = week switch
        {
            { IsCurrentWeek: true, SessionsPlanned: > 0 } => $"{sessions} so far · target {week.SessionsPlanned} · this week is still open",
            { IsCurrentWeek: true } => $"{sessions} so far · this week is still open",
            { SessionsPlanned: > 0 } => $"{sessions} of {week.SessionsPlanned} planned{(week.MetPlan ? " · target met" : string.Empty)}",
            _ => sessions
        };

        return new ConsistencyWeekViewModel(
            week.WeekStarting.ToString("d MMM", CultureInfo.CurrentCulture),
            week.SessionsCompleted,
            detail);
    }
}
