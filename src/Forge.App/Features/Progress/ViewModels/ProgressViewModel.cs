using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.App.Navigation;

namespace Forge.App.Features.Progress.ViewModels;

public sealed partial class ProgressViewModel(IInsightsDataService dataService) : ObservableObject
{
    public IReadOnlyList<ProgressDestinationViewModel> Destinations { get; } =
    [
        new ProgressDestinationViewModel("Insights", "Trends, training load and explainable analysis.", ForgeRoutes.Insights),
        new ProgressDestinationViewModel("Exercise progress", "Estimated 1RM progression with formula labels and caveats.", ForgeRoutes.ExerciseProgress),
        new ProgressDestinationViewModel("Personal records", "Heaviest loads, reps at load and session-volume records.", ForgeRoutes.PersonalRecords),
        new ProgressDestinationViewModel("Body metrics", "Smoothed weight and measurement trends.", ForgeRoutes.BodyMetrics),
    ];

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private bool hasTrainingData;

    [ObservableProperty]
    private string summary = "Loading your local training history.";

    public bool IsEmpty => !IsLoading && !HasTrainingData;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnHasTrainingDataChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var snapshot = await dataService.LoadAsync(DateOnly.FromDateTime(DateTime.Now), cancellationToken).ConfigureAwait(false);
            var progress = snapshot.Progress;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                HasTrainingData = progress.CompletedSessions > 0 || progress.WorkingSets > 0 || progress.BodyMetricSampleCount > 0;
                Summary = HasTrainingData
                    ? $"{progress.CompletedSessions} completed sessions · {progress.WorkingSets} working sets · {progress.TotalVolumeKilograms:0.##} kg volume"
                    : "No persisted training or body metrics yet.";
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
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

public sealed record ProgressDestinationViewModel(string Title, string Detail, string Route);
