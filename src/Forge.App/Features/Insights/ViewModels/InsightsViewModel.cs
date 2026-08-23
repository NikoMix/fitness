using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.App.Navigation;
using Forge.Core.Abstractions.Preferences;
using Forge.Domain.Analytics;

namespace Forge.App.Features.Insights.ViewModels;

/// <summary>
/// The Insights hub: where training volume went, and what the data does and does not support.
/// </summary>
/// <remarks>
/// The muscle and pattern breakdowns are always listed as numbers and only additionally charted
/// once a few training days exist. A single session produces a perfectly accurate breakdown that
/// nonetheless reads as a statement about how someone trains, and the bar chart is what makes it
/// read that way; the list says the same thing without the implied verdict.
/// </remarks>
public sealed partial class InsightsViewModel(IInsightsDataService dataService, IUnitFormatter units) : ObservableObject
{
    /// <summary>Training days required before a breakdown is drawn rather than only listed.</summary>
    public const int MinimumTrainingDaysForBreakdown = 3;

    private const int MaximumSlicesShown = 8;

    /// <summary>Volume per muscle group, biggest first.</summary>
    public ObservableCollection<TrainingSliceViewModel> MuscleGroups { get; } = [];

    /// <summary>Volume per movement pattern, biggest first.</summary>
    public ObservableCollection<TrainingSliceViewModel> MovementPatterns { get; } = [];

    /// <summary>Weekly volume for the slice currently in focus, oldest first.</summary>
    public ObservableCollection<TrainingWeekViewModel> FocusWeeks { get; } = [];

    /// <summary>Why per-muscle volumes add up to more than the total.</summary>
    public string MuscleOverlapCaveat { get; } = TrainingTrendAggregator.MuscleGroupOverlapCaveat;

    /// <summary>The standing rule for every association shown on this screen.</summary>
    public string NonCausationCaveat { get; } = SleepPerformancePairing.NonCausationCaveat;

    /// <summary>What the performance figure in the association is, and what it cannot separate.</summary>
    public string PerformanceMeasureCaveat { get; } = SleepPerformancePairing.PerformanceMeasureCaveat;

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private bool hasTrainingData;

    [ObservableProperty]
    private string historySummary = "Reading your persisted history.";

    [ObservableProperty]
    private string consistencyHeadline = string.Empty;

    [ObservableProperty]
    private string consistencyDetail = string.Empty;

    [ObservableProperty]
    private string muscleSummary = string.Empty;

    [ObservableProperty]
    private string muscleNote = string.Empty;

    [ObservableProperty]
    private bool showMuscleChart;

    [ObservableProperty]
    private string patternSummary = string.Empty;

    [ObservableProperty]
    private bool showPatternChart;

    [ObservableProperty]
    private string sleepMessage = string.Empty;

    [ObservableProperty]
    private string sleepCounts = string.Empty;

    [ObservableProperty]
    private bool hasSleepClaim;

    [ObservableProperty]
    private bool hasFocusSlice;

    [ObservableProperty]
    private string focusLabel = string.Empty;

    [ObservableProperty]
    private string focusSummary = string.Empty;

    [ObservableProperty]
    private string focusNote = string.Empty;

    [ObservableProperty]
    private bool showFocusChart;

    [ObservableProperty]
    private bool showFocusValues;

    /// <summary>Whether nothing has been logged yet.</summary>
    public bool IsEmpty => !IsLoading && !HasTrainingData;

    /// <summary>Whether the breakdown cards have anything to show.</summary>
    public bool HasBreakdown => !IsLoading && HasTrainingData;

    partial void OnIsLoadingChanged(bool value) => RaiseDerived();

    partial void OnHasTrainingDataChanged(bool value) => RaiseDerived();

    /// <summary>Loads the hub from local storage.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the screen is populated.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var overview = await dataService
                .LoadInsightsAsync(DateOnly.FromDateTime(DateTime.Now), cancellationToken)
                .ConfigureAwait(false);

            var muscles = Project(overview.MuscleGroups);
            var patterns = Project(overview.MovementPatterns);
            var chartable = overview.Totals.TrainingDays >= MinimumTrainingDaysForBreakdown;

            // Read once so the two narrations and the focus card cannot end up quoting different
            // units within the same render.
            var suffix = units.MassUnitSuffix;

            var muscleNarration = ChartNarrator.Describe(
                "Volume by muscle group",
                [.. muscles.Select(slice => new NarratedPoint(slice.Label, slice.Volume))],
                suffix);

            var patternNarration = ChartNarrator.Describe(
                "Volume by movement pattern",
                [.. patterns.Select(slice => new NarratedPoint(slice.Label, slice.Volume))],
                suffix);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Replace(MuscleGroups, muscles);
                Replace(MovementPatterns, patterns);

                HasTrainingData = overview.Totals.HasTraining;
                HistorySummary = HasTrainingData
                    ? $"{overview.Totals.CompletedSessions} sessions and {overview.Totals.WorkingSets} working sets across {overview.Totals.TrainingDays} training days are available for analysis."
                    : "No local history yet. Forge keeps these screens empty until you log real data.";

                ConsistencyHeadline = overview.Consistency.Headline;
                ConsistencyDetail = overview.Consistency.Detail;

                ShowMuscleChart = chartable && muscles.Count > 0;
                ShowPatternChart = chartable && patterns.Count > 0;
                MuscleSummary = muscleNarration;
                PatternSummary = patternNarration;
                MuscleNote = chartable
                    ? MuscleOverlapCaveat
                    : $"Based on {Days(overview.Totals.TrainingDays)} of training. Forge lists the figures but does not chart a balance picture until there are at least {MinimumTrainingDaysForBreakdown}, because one session's split is not how you train. {MuscleOverlapCaveat}";

                ApplySleep(overview.SleepAssociation);

                // Focus on the biggest contributor by default, so the time series is populated on
                // arrival and the reader can switch rather than having to discover the feature.
                Focus(muscles.Count > 0 ? muscles[0] : patterns.FirstOrDefault());
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
        }
    }

    private void ApplySleep(SleepPerformanceInsight insight)
    {
        HasSleepClaim = insight.HasClaim;
        SleepMessage = insight.Message;
        SleepCounts = insight.PairedDays == 0
            ? $"Forge pairs a morning check-in that records sleep with the training you logged that same day. It has {insight.SleepNightsRecorded} sleep entries and {insight.TrainingDaysRecorded} training days, and no day yet has both."
            : $"{insight.PairedDays} days have both a sleep entry and logged training, out of {insight.SleepNightsRecorded} sleep entries and {insight.TrainingDaysRecorded} training days. Days without training are left out entirely rather than counted as zero, which would invent the very pattern this is looking for.";
    }

    private List<TrainingSliceViewModel> Project(IEnumerable<TrainingTrendSlice> slices)
        => slices.Take(MaximumSlicesShown).Select(slice => TrainingSliceViewModel.From(slice, units)).ToList();

    /// <summary>Puts one muscle group or movement pattern into the weekly trend card.</summary>
    /// <param name="slice">The slice to focus, or <see langword="null"/> to clear the card.</param>
    [RelayCommand]
    private void Focus(TrainingSliceViewModel? slice)
    {
        FocusWeeks.Clear();
        HasFocusSlice = slice is not null;

        if (slice is null)
        {
            FocusLabel = string.Empty;
            FocusSummary = string.Empty;
            FocusNote = string.Empty;
            ShowFocusChart = false;
            ShowFocusValues = false;
            return;
        }

        foreach (var week in slice.Weeks)
        {
            FocusWeeks.Add(week);
        }

        // The same threshold the rest of the app uses: a slice trained twice does not get a line
        // drawn through it just because it happens to be the biggest slice.
        var readiness = SparseDataPolicy.Evaluate(slice.Weeks.Count, $"weekly volume for {slice.Label}");

        FocusLabel = slice.Label;
        ShowFocusChart = readiness.CanChart;
        ShowFocusValues = slice.Weeks.Count > 0 && !readiness.CanChart;
        FocusNote = readiness.CanChart ? string.Empty : readiness.Explanation;
        FocusSummary = ChartNarrator.Describe(
            $"Weekly volume for {slice.Label}",
            [.. slice.Weeks.Select(week => new NarratedPoint(week.WeekLabel, week.Volume))],
            units.MassUnitSuffix);
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasBreakdown));
    }

    private static string Days(int count) => count == 1 ? "one day" : $"{count} days";

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    [RelayCommand]
    private static Task OpenExerciseProgressAsync() => Shell.Current.GoToAsync(ForgeRoutes.ExerciseProgress);

    [RelayCommand]
    private static Task OpenPersonalRecordsAsync() => Shell.Current.GoToAsync(ForgeRoutes.PersonalRecords);

    [RelayCommand]
    private static Task OpenBodyMetricsAsync() => Shell.Current.GoToAsync(ForgeRoutes.BodyMetrics);

    [RelayCommand]
    private static Task LogWorkoutAsync() => Shell.Current.GoToAsync(ForgeRoutes.Train);
}

/// <summary>One muscle group or movement pattern, formatted for display.</summary>
/// <param name="Label">Muscle group or pattern name.</param>
/// <param name="VolumeKilograms">Total working volume attributed to it, in canonical kilograms.</param>
/// <param name="Volume">The same volume in the unit the user reads, which is what the chart plots.</param>
/// <param name="Detail">Sets, weeks and mean load behind the figure.</param>
/// <param name="Weeks">Weekly volume and intensity for this slice, oldest first.</param>
public sealed record TrainingSliceViewModel(
    string Label,
    double VolumeKilograms,
    double Volume,
    string Detail,
    IReadOnlyList<TrainingWeekViewModel> Weeks)
{
    /// <summary>What a screen reader is given for the row.</summary>
    public string Description => $"{Label}. {Detail}";

    /// <summary>Projects an aggregated slice into display form.</summary>
    /// <param name="slice">The aggregated slice.</param>
    /// <param name="units">Converts and formats the stored kilograms.</param>
    /// <returns>The display model.</returns>
    public static TrainingSliceViewModel From(TrainingTrendSlice slice, IUnitFormatter units)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(units);

        var weeks = slice.Weeks.Count == 1 ? "1 week" : $"{slice.Weeks.Count} weeks";
        var sets = slice.TotalWorkingSets == 1 ? "1 working set" : $"{slice.TotalWorkingSets} working sets";
        var latest = slice.Weeks.Count == 0 ? null : slice.Weeks[^1];
        var intensity = latest is { LoadedWorkingSets: > 0 }
            ? string.Create(
                CultureInfo.CurrentCulture,
                $" · mean load {units.FormatMass((double)latest.MeanLoad.Kilograms, 2)} in the week of {latest.WeekStarting:d MMM}")
            : string.Empty;

        return new TrainingSliceViewModel(
            slice.Label,
            (double)slice.TotalVolume.Kilograms,
            units.ToDisplayMass((double)slice.TotalVolume.Kilograms),
            $"{units.FormatMass((double)slice.TotalVolume.Kilograms, 2)} · {sets} across {weeks}{intensity}",
            [.. slice.Weeks.Select(week => TrainingWeekViewModel.From(week, units))]);
    }
}
