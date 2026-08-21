using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.App.Navigation;

namespace Forge.App.Features.Insights.ViewModels;

/// <summary>
/// The smoothed body weight trend, with the raw entries kept alongside it.
/// </summary>
/// <remarks>
/// The smoothed line is drawn as a line and the raw entries as points, rather than as two lines in
/// different colours. Two colours alone would leave a colour-blind reader, or anyone looking at a
/// phone in bright sun, unable to tell which line was the average. Different shapes survive both.
/// </remarks>
public sealed partial class BodyMetricsViewModel(IInsightsDataService dataService) : ObservableObject
{
    /// <summary>Raw and smoothed weight per day, oldest first.</summary>
    public ObservableCollection<BodyMetricPointViewModel> Points { get; } = [];

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
    private string smoothingLabel = string.Empty;

    [ObservableProperty]
    private string trendSummary = "Forge waits for enough entries before describing a trend.";

    [ObservableProperty]
    private string readinessNote = string.Empty;

    [ObservableProperty]
    private string partialWindowNote = string.Empty;

    [ObservableProperty]
    private string chartSummary = string.Empty;

    /// <summary>Loads the trend from local storage.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the screen is populated.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var view = await dataService.LoadBodyMetricsAsync(cancellationToken).ConfigureAwait(false);

            var points = view.Points.Select(BodyMetricPointViewModel.From).ToList();
            var narration = ChartNarrator.Describe(
                "Smoothed body weight",
                [.. points.Select(point => new NarratedPoint(point.DateLabel, point.SmoothedKilograms))],
                "kg");

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Points.Clear();
                foreach (var point in points)
                {
                    Points.Add(point);
                }

                HasData = points.Count > 0;
                IsEmpty = !HasData;
                ShowChart = view.Trend.Readiness.CanChart;

                // Below the charting threshold the entries are listed instead. Two weigh-ins are
                // real information; the straight line drawn between them is not.
                ShowValues = HasData && !ShowChart;

                SmoothingLabel = $"Smoothed view · {view.Trend.WindowSize}-day moving average";
                ReadinessNote = view.Trend.Readiness.CanChart ? string.Empty : view.Trend.Readiness.Explanation;
                PartialWindowNote = view.Trend.PartialWindowNote;
                ChartSummary = narration;

                TrendSummary = view.Trend.Trend.Direction == Domain.Analytics.TrendDirection.NoClaim
                    ? view.Trend.Trend.Explanation
                    : string.Create(
                        CultureInfo.CurrentCulture,
                        $"{Direction(view.Trend.Trend.Direction)} by about {Math.Abs(view.Trend.Trend.MagnitudePerDay):0.###} kg per day across {view.Trend.Trend.SampleCount} entries, measured from the smoothed line.");
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
        }
    }

    private static string Direction(Domain.Analytics.TrendDirection direction) => direction switch
    {
        Domain.Analytics.TrendDirection.Increasing => "Trending up",
        Domain.Analytics.TrendDirection.Decreasing => "Trending down",
        Domain.Analytics.TrendDirection.Stable => "Holding steady",
        _ => "No trend claimed"
    };

    [RelayCommand]
    private static Task AddBodyMetricAsync() => Shell.Current.GoToAsync(ForgeRoutes.Profile);
}

/// <summary>One day of body weight, formatted for display.</summary>
/// <param name="DateLabel">Short date label.</param>
/// <param name="RawKilograms">The entry as recorded.</param>
/// <param name="SmoothedKilograms">The trailing moving average.</param>
/// <param name="Detail">Both figures in words, with a note when the average is still filling.</param>
public sealed record BodyMetricPointViewModel(
    string DateLabel,
    double RawKilograms,
    double SmoothedKilograms,
    string Detail)
{
    /// <summary>Projects a trend point into display form.</summary>
    /// <param name="point">The trend point.</param>
    /// <returns>The display model.</returns>
    public static BodyMetricPointViewModel From(BodyMetricTrendPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        var detail = string.Create(
            CultureInfo.CurrentCulture,
            $"{point.RawKilograms:0.##} kg recorded · {point.SmoothedKilograms:0.##} kg averaged{(point.IsFullWindow ? string.Empty : " (partial window)")}");

        return new BodyMetricPointViewModel(
            point.Date.ToString("d MMM", CultureInfo.CurrentCulture),
            (double)point.RawKilograms,
            (double)point.SmoothedKilograms,
            detail);
    }
}
