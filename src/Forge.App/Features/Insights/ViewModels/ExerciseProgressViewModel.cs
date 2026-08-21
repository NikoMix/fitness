using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.App.Navigation;
using Forge.Domain.Training;

namespace Forge.App.Features.Insights.ViewModels;

/// <summary>
/// Estimated one-repetition-maximum progression for the exercise with the most logged sets.
/// </summary>
/// <remarks>
/// Everything on this screen is worded as an estimate, because it is one. The chart is a trend
/// line rather than a number to train against: the formula is a population fit, its error grows
/// with repetition count, and someone who reads the top of this line as a tested maximum will pick
/// a first working set that is too heavy.
/// </remarks>
public sealed partial class ExerciseProgressViewModel(IInsightsDataService dataService) : ObservableObject
{
    /// <summary>One estimate per training day, oldest first.</summary>
    public ObservableCollection<ExerciseEstimateViewModel> EstimatePoints { get; } = [];

    [ObservableProperty]
    private bool hasData;

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private bool showChart;

    [ObservableProperty]
    private bool showValues;

    [ObservableProperty]
    private string exerciseName = "Most logged exercise";

    [ObservableProperty]
    private string formulaNote = string.Empty;

    [ObservableProperty]
    private string readinessNote = string.Empty;

    [ObservableProperty]
    private string exclusionNote = string.Empty;

    [ObservableProperty]
    private bool hasExclusions;

    [ObservableProperty]
    private string chartSummary = string.Empty;

    /// <summary>Loads the progression from local storage.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the screen is populated.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var view = await dataService.LoadExerciseProgressAsync(cancellationToken).ConfigureAwait(false);

            var points = view.EstimatePoints.Select(ExerciseEstimateViewModel.From).ToList();
            var narration = ChartNarrator.Describe(
                $"Estimated one-rep max for {view.ExerciseName}",
                [.. points.Select(point => new NarratedPoint(point.DateLabel, point.EstimatedOneRepMaxKilograms))],
                "kg");

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                EstimatePoints.Clear();
                foreach (var point in points)
                {
                    EstimatePoints.Add(point);
                }

                ExerciseName = view.ExerciseName;
                HasData = points.Count > 0;
                IsEmpty = !HasData;
                ShowChart = view.EstimateReadiness.CanChart;
                ShowValues = HasData && !ShowChart;
                ReadinessNote = view.EstimateReadiness.CanChart ? string.Empty : view.EstimateReadiness.Explanation;
                ChartSummary = narration;

                FormulaNote = $"Estimated with the {view.Formula} formula from sets of 1 to {OneRepMaxEstimator.MaximumSupportedRepetitions} repetitions. "
                    + "These are calculated figures, not maxima you have lifted, and the error grows as repetitions approach ten.";

                ExclusionNote = view.ExcludedHighRepSets == 0
                    ? string.Empty
                    : $"{Sets(view.ExcludedHighRepSets)} above {OneRepMaxEstimator.MaximumSupportedRepetitions} repetitions were left out. Past that point the formula describes muscular endurance more than maximal strength, so estimating from it would be inventing precision.";
                HasExclusions = view.ExcludedHighRepSets > 0;
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
        }
    }

    private static string Sets(int count) => count == 1 ? "One working set" : $"{count} working sets";

    [RelayCommand]
    private static Task LogWorkoutAsync() => Shell.Current.GoToAsync(ForgeRoutes.Train);
}

/// <summary>One day's estimate, formatted for display.</summary>
/// <param name="DateLabel">Short date label.</param>
/// <param name="EstimatedOneRepMaxKilograms">The estimate.</param>
/// <param name="Detail">The set the estimate came from, and the formula used.</param>
public sealed record ExerciseEstimateViewModel(
    string DateLabel,
    double EstimatedOneRepMaxKilograms,
    string Detail)
{
    /// <summary>Projects an estimate point into display form.</summary>
    /// <param name="point">The estimate point.</param>
    /// <returns>The display model.</returns>
    public static ExerciseEstimateViewModel From(ExerciseEstimatePoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        return new ExerciseEstimateViewModel(
            point.Date.ToString("d MMM", CultureInfo.CurrentCulture),
            (double)point.EstimatedOneRepMaxKilograms,
            string.Create(
                CultureInfo.CurrentCulture,
                $"≈ {point.EstimatedOneRepMaxKilograms:0.##} kg estimated from {point.SourceLoadKilograms:0.##} kg × {point.SourceRepetitions} using {point.Formula}"));
    }
}
