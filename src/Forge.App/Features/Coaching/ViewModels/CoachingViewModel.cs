using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Coaching.Services;
using Forge.Domain.Coaching;

namespace Forge.App.Features.Coaching.ViewModels;

public sealed partial class CoachingViewModel(ICoachingDataService dataService) : ObservableObject
{
    [ObservableProperty]
    private string load = "Repeat current load";

    [ObservableProperty]
    private string reps = "Log a session to unlock progression";

    [ObservableProperty]
    private string explanation = "Forge recommendations are local, deterministic and explainable.";

    [ObservableProperty]
    private string overrideNote = "You can override every recommendation.";

    [ObservableProperty]
    private string medicalDisclaimer = "Forge coaching is general fitness guidance and is not medical advice.";

    [ObservableProperty]
    private bool isOverrideVisible = true;

    [ObservableProperty]
    private string status = "Ready";

    public IAsyncRelayCommand LoadRecommendationCommand => new AsyncRelayCommand(LoadRecommendationAsync);

    public IRelayCommand OverrideCommand => new RelayCommand(() => Status = "Override chosen — log what you actually do so future coaching reflects reality.");

    private async Task LoadRecommendationAsync(CancellationToken cancellationToken)
    {
        var recommendation = await dataService.GetNextSessionRecommendationAsync(cancellationToken).ConfigureAwait(false);
        Load = recommendation.Load.Kilograms == 0m ? "Bodyweight or starter load" : $"{recommendation.Load.Kilograms:0.##} kg";
        Reps = $"{recommendation.SetCount} sets × {recommendation.TargetRepsMin}-{recommendation.TargetRepsMax} reps";
        Explanation = recommendation.Explanation;
        OverrideNote = recommendation.OverrideSafetyNote;
        MedicalDisclaimer = recommendation.MedicalDisclaimer;
        IsOverrideVisible = recommendation.IsOverridable;
        Status = recommendation.Status == NextSessionRecommendationStatus.BlockedBySafety ? "Safety blocked" : "Recommendation loaded";
    }
}
