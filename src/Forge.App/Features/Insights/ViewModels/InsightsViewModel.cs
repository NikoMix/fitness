using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.App.Navigation;

namespace Forge.App.Features.Insights.ViewModels;

public sealed partial class InsightsViewModel(IInsightsDataService dataService) : ObservableObject
{
    public ObservableCollection<InsightHighlightViewModel> Highlights { get; } =
    [
        new InsightHighlightViewModel("Smoothed body metrics", "Weight trends default to a 7-day moving average so daily noise stays in context."),
        new InsightHighlightViewModel("Explainable records", "Records show the exact set and formula that produced them."),
        new InsightHighlightViewModel("Training load caveat", "Load ratios are descriptive only, never injury predictions."),
    ];

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private string historySummary = "Loading your persisted history.";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var snapshot = await dataService.LoadAsync(DateOnly.FromDateTime(DateTime.Now), cancellationToken).ConfigureAwait(false);
            var progress = snapshot.Progress;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                HistorySummary = progress.CompletedSessions == 0 && progress.BodyMetricSampleCount == 0
                    ? "No local history yet. Forge will keep these screens empty until you log real data."
                    : $"{progress.CompletedSessions} sessions, {progress.WorkingSets} working sets and {progress.BodyMetricSampleCount:0} body metric entries are available for analysis.";
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
        }
    }

    [RelayCommand]
    private static Task OpenExerciseProgressAsync() => Shell.Current.GoToAsync(ForgeRoutes.ExerciseProgress);

    [RelayCommand]
    private static Task OpenPersonalRecordsAsync() => Shell.Current.GoToAsync(ForgeRoutes.PersonalRecords);

    [RelayCommand]
    private static Task OpenBodyMetricsAsync() => Shell.Current.GoToAsync(ForgeRoutes.BodyMetrics);
}

public sealed record InsightHighlightViewModel(string Title, string Detail);
