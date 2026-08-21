using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.Core.Abstractions.Preferences;
using Forge.App.Navigation;
using Forge.Domain.Profile;

namespace Forge.App.Features.Profile;

/// <summary>View model for the profile summary surface.</summary>
public sealed partial class ProfileViewModel : ObservableObject
{
    private readonly ProfileStore? profileStore;
    private readonly IUnitFormatter? formatter;

    public ProfileViewModel()
    {
        BodyMetrics =
        [
        ];
    }

    public ProfileViewModel(ProfileStore profileStore, IUnitFormatter formatter)
        : this()
    {
        this.profileStore = profileStore;
        this.formatter = formatter;
    }

    /// <summary>Display name shown on the profile card.</summary>
    [ObservableProperty]
    private string displayName = "Local profile";

    /// <summary>Goal summary.</summary>
    [ObservableProperty]
    private string goalSummary = "No goal set yet";

    /// <summary>Current training setup summary.</summary>
    [ObservableProperty]
    private string trainingSummary = "No training setup yet";

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isEmpty;

    [ObservableProperty]
    private bool hasProfile;

    /// <summary>Latest body metrics for display.</summary>
    public ObservableCollection<ProfileMetric> BodyMetrics { get; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (profileStore is null || formatter is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var snapshot = await profileStore.LoadAsync(cancellationToken);
            BodyMetrics.Clear();
            IsEmpty = snapshot is null;
            HasProfile = snapshot is not null;

            if (snapshot is null)
            {
                DisplayName = "Local profile";
                GoalSummary = "No goal set yet";
                TrainingSummary = "Complete onboarding to personalise training.";
                return;
            }

            DisplayName = snapshot.Profile.DisplayName;
            GoalSummary = FormatGoal(snapshot.Profile);
            TrainingSummary = $"{snapshot.Profile.TrainingDaysPerWeek} days/week · {FormatExperience(snapshot.Profile.ExperienceLevel)} · {snapshot.Profile.AvailableEquipment}";

            var latestMetric = snapshot.BodyMetrics.Count > 0 ? snapshot.BodyMetrics[0] : null;
            if (latestMetric is null)
            {
                return;
            }

            BodyMetrics.Add(new ProfileMetric("Weight", formatter.FormatMass((double)latestMetric.Weight.Kilograms), $"Recorded {latestMetric.RecordedUtc.LocalDateTime:g}"));
            BodyMetrics.Add(new ProfileMetric("Body fat", latestMetric.BodyFatPercentage?.ToString() ?? "Not set", "Optional percentage"));
            BodyMetrics.Add(new ProfileMetric("Waist", latestMetric.WaistCircumference is { } waist ? formatter.FormatLength((double)waist.Centimetres) : "Not set", "Optional circumference"));
        }
        catch (InvalidOperationException)
        {
            BodyMetrics.Clear();
            IsEmpty = true;
            HasProfile = false;
            DisplayName = "Local profile";
            GoalSummary = "Local storage is still starting";
            TrainingSummary = "Try again in a moment.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Opens the Settings route owned by the Settings feature.</summary>
    [RelayCommand]
    private static Task OpenSettingsAsync() => Microsoft.Maui.Controls.Shell.Current.GoToAsync(ForgeRoutes.Settings);

    private static string FormatGoal(UserProfile profile)
    {
        var goal = profile.Goal switch
        {
            FitnessGoal.LoseWeight => "Lose weight",
            FitnessGoal.Maintain => "Maintain",
            FitnessGoal.GainWeight => "Gain weight",
            FitnessGoal.BuildStrength => "Build strength",
            FitnessGoal.ImproveFitness => "Improve fitness",
            _ => "No goal set",
        };

        return profile.TargetWeight is { } target
            ? $"{goal} · target {target.Kilograms:0.#} kg"
            : goal;
    }

    private static string FormatExperience(TrainingExperienceLevel level) => level switch
    {
        TrainingExperienceLevel.Beginner => "Beginner",
        TrainingExperienceLevel.Intermediate => "Intermediate",
        TrainingExperienceLevel.Advanced => "Advanced",
        _ => "Unspecified",
    };
}

/// <summary>A profile metric row.</summary>
public sealed record ProfileMetric(string Name, string Value, string Detail);
