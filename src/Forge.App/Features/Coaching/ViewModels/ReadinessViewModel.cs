using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Coaching.Services;
using Forge.Domain.Recovery;

namespace Forge.App.Features.Coaching.ViewModels;

public sealed partial class ReadinessViewModel(ICoachingDataService dataService) : ObservableObject
{
    [ObservableProperty]
    private int score;

    [ObservableProperty]
    private double scoreProgress;

    [ObservableProperty]
    private string medicalDisclaimer = ReadinessScoreResult.DefaultMedicalDisclaimer;

    [ObservableProperty]
    private string missingInputs = "Manual check-in works even when health data is unavailable.";

    public ObservableCollection<ReadinessComponentViewModel> Components { get; } = [];

    public IAsyncRelayCommand LoadReadinessCommand => new AsyncRelayCommand(LoadReadinessAsync);

    private async Task LoadReadinessAsync(CancellationToken cancellationToken)
    {
        var result = await dataService.GetReadinessAsync(cancellationToken).ConfigureAwait(false);
        Score = result.Score;
        ScoreProgress = result.Score / 100d;
        MedicalDisclaimer = result.MedicalDisclaimer;
        MissingInputs = result.MissingInputs.Count == 0 ? "All readiness inputs available." : string.Join(" ", result.MissingInputs);
        Components.Clear();
        foreach (var component in result.Components)
        {
            Components.Add(new ReadinessComponentViewModel(
                component.Name,
                component.IsAvailable ? $"{component.RawScore:0.#}/100" : "Missing",
                $"Weight {component.Weight:0.#}% · {component.Explanation}"));
        }
    }
}

public sealed record ReadinessComponentViewModel(string Name, string Value, string Explanation);
