using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Coaching.Services;
using Forge.Core.Abstractions;
using Forge.Core.Abstractions.Preferences;
using Forge.Domain.Coaching;
using Microsoft.Extensions.Logging;

namespace Forge.App.Features.Coaching.ViewModels;

/// <summary>
/// The next-session card.
/// </summary>
/// <remarks>
/// The limitation lines are part of the recommendation rather than decoration beside it. Onboarding
/// echoes a declared limitation back on its review step, which tells the user they were heard; if
/// this screen then recommends a movement without saying whether that sentence was understood, the
/// echo becomes the thing that misleads them.
/// </remarks>
public sealed partial class CoachingViewModel(
    ICoachingDataService dataService,
    IUnitFormatter units,
    ILogger<CoachingViewModel> logger) : ObservableObject
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

    [ObservableProperty]
    private string limitationSummary = string.Empty;

    [ObservableProperty]
    private bool hasLimitationSummary;

    [ObservableProperty]
    private bool hasUninterpretedLimitation;

    public IAsyncRelayCommand LoadRecommendationCommand => new AsyncRelayCommand(LoadRecommendationAsync);

    public IRelayCommand OverrideCommand => new RelayCommand(() => Status = "Override chosen — log what you actually do so future coaching reflects reality.");

    private async Task LoadRecommendationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var advice = await dataService.GetNextSessionRecommendationAsync(cancellationToken).ConfigureAwait(false);
            var recommendation = advice.Recommendation;

            Load = recommendation.Load.Kilograms == 0m
                ? "Bodyweight or starter load"
                : units.FormatMass((double)recommendation.Load.Kilograms, 2);
            Reps = $"{recommendation.SetCount} sets × {recommendation.TargetRepsMin}-{recommendation.TargetRepsMax} reps";
            Explanation = recommendation.Explanation;
            OverrideNote = recommendation.OverrideSafetyNote;
            MedicalDisclaimer = recommendation.MedicalDisclaimer;
            IsOverrideVisible = recommendation.IsOverridable;
            LimitationSummary = advice.LimitationSummary;
            HasLimitationSummary = advice.HasDeclaredLimitation && !string.IsNullOrWhiteSpace(advice.LimitationSummary);
            HasUninterpretedLimitation = advice.HasUninterpretedLimitation;
            Status = recommendation.Status == NextSessionRecommendationStatus.BlockedBySafety ? "Safety blocked" : "Recommendation loaded";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The message is never interpolated into the screen. A LINQ expression and a Microsoft
            // support URL have both reached a user this way before.
            LogRecommendationFailed(logger, exception);
            Status = "Could not load";
            Explanation = ForgeUserFacingException.DescribeFor(
                exception,
                "Forge could not read your training history just now. Nothing was lost — try again in a moment.");
            LimitationSummary = string.Empty;
            HasLimitationSummary = false;
            HasUninterpretedLimitation = false;
        }
    }

    // Source-generated so CA1848 is satisfied; the codebase uses [LoggerMessage] elsewhere for the
    // same reason. See ForgeStartup.cs.
    [LoggerMessage(EventId = 1500, Level = LogLevel.Error, Message = "Loading the next-session recommendation failed.")]
    private static partial void LogRecommendationFailed(ILogger logger, Exception exception);
}
