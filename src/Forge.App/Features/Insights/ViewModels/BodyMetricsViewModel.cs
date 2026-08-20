using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.App.Navigation;
using Forge.Domain.Analytics;

namespace Forge.App.Features.Insights.ViewModels;

public sealed partial class BodyMetricsViewModel(IInsightsDataService dataService) : ObservableObject
{
    public ObservableCollection<BodyMetricPointViewModel> Points { get; } = [];

    public string SmoothingLabel { get; } = $"Smoothed view · {MovingAverage.DefaultWindowSize}-day moving average";

    public string RawSeriesLabel { get; } = "Raw entries are available for detail, but the smoothed line is primary to reduce day-to-day noise.";

    [ObservableProperty]
    private bool hasData;

    [ObservableProperty]
    private bool isEmpty = true;

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private string trendSummary = "Forge will wait for enough entries before claiming a trend.";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        IsEmpty = false;
        try
        {
            var snapshot = await dataService.LoadAsync(DateOnly.FromDateTime(DateTime.Now), cancellationToken).ConfigureAwait(false);
            var points = snapshot.BodyMetricPoints
                .Select(point => new BodyMetricPointViewModel(
                    point.Date.ToString("MMM d", CultureInfo.CurrentCulture),
                    (double)point.RawKilograms,
                    (double)point.SmoothedKilograms))
                .ToList();
            var trend = snapshot.BodyWeightTrend;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Points.Clear();
                foreach (var point in points)
                {
                    Points.Add(point);
                }

                HasData = Points.Count > 0;
                IsEmpty = !HasData;
                TrendSummary = trend.Direction == TrendDirection.NoClaim
                    ? trend.Explanation
                    : $"{trend.Direction} at {trend.MagnitudePerDay:0.###} kg/day from the smoothed series.";
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
        }
    }

    [RelayCommand]
    private static Task AddBodyMetricAsync() => Shell.Current.GoToAsync(ForgeRoutes.Profile);
}

public sealed record BodyMetricPointViewModel(string DateLabel, double RawKilograms, double SmoothedKilograms);
