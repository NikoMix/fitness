using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.App.Navigation;

namespace Forge.App.Features.Insights.ViewModels;

public sealed partial class ExerciseProgressViewModel(IInsightsDataService dataService) : ObservableObject
{
    public ObservableCollection<ExerciseEstimatePointViewModel> EstimatePoints { get; } = [];

    public string FormulaNote { get; } = "Estimated 1RM uses Epley by default; Forge always labels formulae because estimates carry a meaningful error margin, especially near ten reps.";

    [ObservableProperty]
    private bool hasData;

    [ObservableProperty]
    private bool isEmpty = true;

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private string exerciseName = "Most logged exercise";

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        IsEmpty = false;
        try
        {
            var snapshot = await dataService.LoadAsync(DateOnly.FromDateTime(DateTime.Now), cancellationToken).ConfigureAwait(false);
            var points = snapshot.ExerciseEstimatePoints
                .Select(point => new ExerciseEstimatePointViewModel(
                    point.Date.ToString("MMM d", CultureInfo.CurrentCulture),
                    (double)point.EstimatedOneRepMaxKilograms,
                    point.Formula.ToString(),
                    "Approximate; error grows as reps approach ten."))
                .ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                EstimatePoints.Clear();
                foreach (var point in points)
                {
                    EstimatePoints.Add(point);
                }

                ExerciseName = snapshot.ExerciseName;
                HasData = EstimatePoints.Count > 0;
                IsEmpty = !HasData;
            });
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoading = false);
        }
    }

    [RelayCommand]
    private static Task LogWorkoutAsync() => Shell.Current.GoToAsync(ForgeRoutes.Train);
}

public sealed record ExerciseEstimatePointViewModel(string DateLabel, double EstimatedOneRepMaxKilograms, string Formula, string ErrorMarginNote);
