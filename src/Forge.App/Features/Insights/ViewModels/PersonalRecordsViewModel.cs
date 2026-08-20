using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.App.Navigation;

namespace Forge.App.Features.Insights.ViewModels;

public sealed partial class PersonalRecordsViewModel(IInsightsDataService dataService) : ObservableObject
{
    public ObservableCollection<PersonalRecordItemViewModel> Records { get; } = [];

    [ObservableProperty]
    private bool hasRecords;

    [ObservableProperty]
    private bool isEmpty = true;

    [ObservableProperty]
    private bool isLoading = true;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        IsEmpty = false;
        try
        {
            var snapshot = await dataService.LoadAsync(DateOnly.FromDateTime(DateTime.Now), cancellationToken).ConfigureAwait(false);
            var records = snapshot.PersonalRecords
                .Select(record => new PersonalRecordItemViewModel(
                    record.Title,
                    record.ExerciseName,
                    record.Detail,
                    record.AchievedUtc.ToLocalTime().ToString("d", CultureInfo.CurrentCulture)))
                .ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Records.Clear();
                foreach (var record in records)
                {
                    Records.Add(record);
                }

                HasRecords = Records.Count > 0;
                IsEmpty = !HasRecords;
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

public sealed record PersonalRecordItemViewModel(string Title, string Exercise, string Detail, string AchievedOn);
