using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Insights.Services;
using Forge.App.Navigation;

namespace Forge.App.Features.Insights.ViewModels;

/// <summary>
/// Personal records, each tied to the set and the date that produced it.
/// </summary>
/// <remarks>
/// Estimated records carry a visible "Estimate" marker as well as their wording, because the row
/// that says "≈ 140 kg" sits directly beneath rows showing loads that were genuinely lifted. A
/// reader scanning the list needs to be able to tell those apart without reading every caption.
/// </remarks>
public sealed partial class PersonalRecordsViewModel(IInsightsDataService dataService) : ObservableObject
{
    /// <summary>Detected records, newest first.</summary>
    public ObservableCollection<PersonalRecordItemViewModel> Records { get; } = [];

    [ObservableProperty]
    private bool hasRecords;

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private bool isLoading = true;

    [ObservableProperty]
    private string estimateNote = string.Empty;

    [ObservableProperty]
    private bool hasEstimates;

    /// <summary>Loads the records from local storage.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the screen is populated.</returns>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var view = await dataService.LoadPersonalRecordsAsync(cancellationToken).ConfigureAwait(false);
            var records = view.Records.Select(PersonalRecordItemViewModel.From).ToList();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Records.Clear();
                foreach (var record in records)
                {
                    Records.Add(record);
                }

                HasRecords = records.Count > 0;
                IsEmpty = !HasRecords;
                HasEstimates = records.Exists(record => record.IsEstimate);
                EstimateNote = HasEstimates
                    ? $"Rows marked Estimate are calculated with the {view.Formula} formula from a submaximal set. They are not lifts you have performed, and they are least reliable near ten repetitions."
                    : string.Empty;
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

/// <summary>One record row.</summary>
/// <param name="Title">Record kind.</param>
/// <param name="Exercise">Exercise the record belongs to.</param>
/// <param name="Headline">The achievement in its own units.</param>
/// <param name="Detail">The set that established it.</param>
/// <param name="AchievedOn">Local date the record was achieved.</param>
/// <param name="IsEstimate">Whether the headline figure is calculated rather than performed.</param>
/// <param name="Badge">Short marker shown beside the title, empty for measured records.</param>
public sealed record PersonalRecordItemViewModel(
    string Title,
    string Exercise,
    string Headline,
    string Detail,
    string AchievedOn,
    bool IsEstimate,
    string Badge)
{
    /// <summary>What a screen reader is given for the whole row.</summary>
    public string Description =>
        $"{Title}{(IsEstimate ? ", estimate" : string.Empty)}. {Exercise}. {Headline}. Achieved {AchievedOn}. {Detail}";

    /// <summary>Projects a record into display form.</summary>
    /// <param name="record">The detected record.</param>
    /// <returns>The display model.</returns>
    public static PersonalRecordItemViewModel From(PersonalRecordDisplay record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new PersonalRecordItemViewModel(
            record.Title,
            record.ExerciseName,
            record.Headline,
            record.Detail,
            record.AchievedUtc.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture),
            record.IsEstimate,
            record.IsEstimate ? "Estimate" : "Measured");
    }
}
