using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Forge.App.Features.Insights.Services;
using Forge.Core.Abstractions.Preferences;
using Microsoft.Extensions.Logging;

namespace Forge.App.Features.Insights.ViewModels;

/// <summary>
/// The smoothed body weight trend, with the raw entries kept alongside it.
/// </summary>
/// <remarks>
/// <para>
/// The smoothed line is drawn as a line and the raw entries as points, rather than as two lines in
/// different colours. Two colours alone would leave a colour-blind reader, or anyone looking at a
/// phone in bright sun, unable to tell which line was the average. Different shapes survive both.
/// </para>
/// <para>
/// Everything on this screen is plotted and worded in the user's own mass unit.
/// <see cref="IUnitFormatter"/> supplies both the number and the suffix, so the plotted series, the
/// narration and the per-entry detail cannot disagree with each other the way a hard-coded "kg"
/// beside a converted value does.
/// </para>
/// </remarks>
public sealed partial class BodyMetricsViewModel : ObservableObject
{
    private readonly IInsightsDataService dataService;
    private readonly IUnitFormatter units;

    /// <summary>Initialises the view model.</summary>
    /// <param name="dataService">Reads and writes body metrics locally.</param>
    /// <param name="units">Formats stored kilograms in the user's chosen unit.</param>
    /// <param name="logger">Receives exceptions raised while saving.</param>
    public BodyMetricsViewModel(
        IInsightsDataService dataService,
        IUnitFormatter units,
        ILogger<BodyMetricsViewModel> logger)
    {
        this.dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        this.units = units ?? throw new ArgumentNullException(nameof(units));

        ArgumentNullException.ThrowIfNull(logger);
        Entry = new BodyMetricEntryForm(dataService, units, logger, LoadAsync);
    }

    /// <summary>Raw and smoothed weight per day, oldest first.</summary>
    public ObservableCollection<BodyMetricPointViewModel> Points { get; } = [];

    /// <summary>The form that records a new measurement.</summary>
    public BodyMetricEntryForm Entry { get; }

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

            // The unit is read once per load rather than per point, so every number on the screen
            // is stated in the same unit even if the preference changes while this is rendering.
            var suffix = units.MassUnitSuffix;
            var points = view.Points.Select(point => BodyMetricPointViewModel.From(point, units)).ToList();
            var narration = ChartNarrator.Describe(
                "Smoothed body weight",
                [.. points.Select(point => new NarratedPoint(point.DateLabel, point.SmoothedWeight))],
                suffix);

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
                Entry.RefreshUnitLabels();

                TrendSummary = view.Trend.Trend.Direction == Domain.Analytics.TrendDirection.NoClaim
                    ? view.Trend.Trend.Explanation
                    : string.Create(
                        CultureInfo.CurrentCulture,
                        $"{Direction(view.Trend.Trend.Direction)} by about {units.ToDisplayMass(Math.Abs((double)view.Trend.Trend.MagnitudePerDay)):0.###} {suffix} per day across {view.Trend.Trend.SampleCount} entries, measured from the smoothed line.");
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
}

/// <summary>One day of body weight, formatted for display.</summary>
/// <param name="DateLabel">Short date label.</param>
/// <param name="RawWeight">The entry as recorded, in the user's chosen unit.</param>
/// <param name="SmoothedWeight">The trailing moving average, in the user's chosen unit.</param>
/// <param name="Detail">Both figures in words, with a note when the average is still filling.</param>
public sealed record BodyMetricPointViewModel(
    string DateLabel,
    double RawWeight,
    double SmoothedWeight,
    string Detail)
{
    /// <summary>Projects a trend point into display form.</summary>
    /// <param name="point">The trend point.</param>
    /// <param name="units">Converts and formats the stored kilograms.</param>
    /// <returns>The display model.</returns>
    /// <remarks>
    /// The plotted values are converted as well as the text. A chart that plots kilograms under a
    /// "lb" narration is the same defect as the wrong suffix and harder to see, because the shape
    /// of the line stays right.
    /// </remarks>
    public static BodyMetricPointViewModel From(BodyMetricTrendPoint point, IUnitFormatter units)
    {
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(units);

        var raw = units.FormatMass((double)point.RawKilograms, 2);
        var smoothed = units.FormatMass((double)point.SmoothedKilograms, 2);
        var detail = $"{raw} recorded · {smoothed} averaged{(point.IsFullWindow ? string.Empty : " (partial window)")}";

        return new BodyMetricPointViewModel(
            point.Date.ToString("d MMM", CultureInfo.CurrentCulture),
            units.ToDisplayMass((double)point.RawKilograms),
            units.ToDisplayMass((double)point.SmoothedKilograms),
            detail);
    }
}
