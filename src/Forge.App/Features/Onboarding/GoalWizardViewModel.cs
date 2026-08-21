using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Profile;
using Forge.App.Navigation;
using Forge.Domain.Measurement;
using Forge.Domain.Profile;

namespace Forge.App.Features.Onboarding;

/// <summary>View model for the onboarding goal wizard.</summary>
public sealed partial class GoalWizardViewModel : ObservableObject
{
    private readonly ProfileStore? profileStore;

    public GoalWizardViewModel()
    {
        Goals = ["Lose weight", "Maintain", "Gain weight", "Build strength", "Improve fitness"];
        SexOptions = ["Prefer not to say", "Female", "Male"];
        ExperienceLevels = ["Beginner", "Intermediate", "Advanced"];
        EquipmentOptions = ["Bodyweight", "Dumbbells", "Barbell", "Machines", "Bands"];
        SelectedGoal = Goals[0];
        SelectedSex = SexOptions[0];
        SelectedExperience = ExperienceLevels[0];
        EvaluateSafety();
    }

    public GoalWizardViewModel(ProfileStore profileStore)
        : this()
    {
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
    }

    /// <summary>Available goal labels.</summary>
    public IReadOnlyList<string> Goals { get; }

    /// <summary>Available biological sex labels.</summary>
    public IReadOnlyList<string> SexOptions { get; }

    /// <summary>Available experience labels.</summary>
    public IReadOnlyList<string> ExperienceLevels { get; }

    /// <summary>Equipment choices shown in the wizard.</summary>
    public IReadOnlyList<string> EquipmentOptions { get; }

    /// <summary>Selected equipment labels.</summary>
    public ObservableCollection<string> SelectedEquipment { get; } = ["Bodyweight"];

    [ObservableProperty]
    private string displayName = "Me";

    [ObservableProperty]
    private string selectedGoal = string.Empty;

    [ObservableProperty]
    private string selectedSex = string.Empty;

    [ObservableProperty]
    private DateTime dateOfBirth = DateTime.Today.AddYears(-30);

    [ObservableProperty]
    private double heightCentimetres = 175;

    [ObservableProperty]
    private double currentWeightKilograms = 80;

    [ObservableProperty]
    private double targetWeightKilograms = 78;

    [ObservableProperty]
    private double targetDailyCalories = 1800;

    [ObservableProperty]
    private double timeframeWeeks = 8;

    [ObservableProperty]
    private string selectedExperience = string.Empty;

    [ObservableProperty]
    private bool hasBodyweight = true;

    [ObservableProperty]
    private bool hasDumbbells;

    [ObservableProperty]
    private bool hasBarbell;

    [ObservableProperty]
    private bool hasMachines;

    [ObservableProperty]
    private bool hasBands;

    [ObservableProperty]
    private string movementLimitations = string.Empty;

    [ObservableProperty]
    private double trainingDaysPerWeek = 3;

    [ObservableProperty]
    private string safetyMessage = string.Empty;

    [ObservableProperty]
    private string safetySignpost = string.Empty;

    [ObservableProperty]
    private bool hasSafetyAdvisory;

    [ObservableProperty]
    private bool isSafetyBlocking;

    [ObservableProperty]
    private bool isSaving;

    partial void OnSelectedSexChanged(string value) => EvaluateSafety();

    partial void OnHeightCentimetresChanged(double value) => EvaluateSafety();

    partial void OnCurrentWeightKilogramsChanged(double value) => EvaluateSafety();

    partial void OnTargetWeightKilogramsChanged(double value) => EvaluateSafety();

    partial void OnTargetDailyCaloriesChanged(double value) => EvaluateSafety();

    partial void OnTimeframeWeeksChanged(double value) => EvaluateSafety();

    partial void OnIsSafetyBlockingChanged(bool value) => FinishCommand.NotifyCanExecuteChanged();

    partial void OnIsSavingChanged(bool value) => FinishCommand.NotifyCanExecuteChanged();

    /// <summary>Toggles an equipment option.</summary>
    [RelayCommand]
    private void ToggleEquipment(string equipment)
    {
        if (SelectedEquipment.Contains(equipment))
        {
            if (SelectedEquipment.Count > 1)
            {
                SelectedEquipment.Remove(equipment);
            }
        }
        else
        {
            SelectedEquipment.Add(equipment);
        }
    }

    /// <summary>Completes onboarding and enters the app.</summary>
    [RelayCommand(CanExecute = nameof(CanFinish))]
    private async Task FinishAsync()
    {
        if (profileStore is null)
        {
            await Microsoft.Maui.Controls.Shell.Current.GoToAsync($"//{ForgeRoutes.Today}");
            return;
        }

        IsSaving = true;
        try
        {
            var result = await profileStore.SaveSetupAsync(CreateDraft(), CancellationToken.None);
            if (!result.IsAccepted)
            {
                ApplySafetyResult(result);
                return;
            }

            await Microsoft.Maui.Controls.Shell.Current.GoToAsync($"//{ForgeRoutes.Today}");
        }
        finally
        {
            IsSaving = false;
        }
    }

    private bool CanFinish() => !IsSafetyBlocking && !IsSaving;

    private void EvaluateSafety()
    {
        if (CurrentWeightKilograms <= 0 || HeightCentimetres <= 0)
        {
            return;
        }

        var result = GoalSafetyEvaluator.Evaluate(new GoalSafetyProposal(
            Mass.FromKilograms((decimal)CurrentWeightKilograms),
            Length.FromCentimetres((decimal)HeightCentimetres),
            SelectedSex switch
            {
                "Female" => BiologicalSex.Female,
                "Male" => BiologicalSex.Male,
                _ => BiologicalSex.PreferNotToSay,
            },
            Mass.FromKilograms((decimal)Math.Max(0.1, TargetWeightKilograms)),
            Math.Max(1, (int)Math.Round(TimeframeWeeks)),
            (decimal)Math.Max(0, TargetDailyCalories)));

        ApplySafetyResult(result);
    }

    private void ApplySafetyResult(GoalSafetyResult result)
    {
        var blocking = result.Advisories.FirstOrDefault(a => a.Severity == SafetySeverity.Refused);
        var advisory = blocking ?? (result.Advisories.Count > 0 ? result.Advisories[0] : null);

        SafetyMessage = advisory?.Message ?? string.Empty;
        SafetySignpost = advisory?.SupportSignpost ?? string.Empty;
        HasSafetyAdvisory = advisory is not null && advisory.Severity != SafetySeverity.Information || blocking is not null;
        IsSafetyBlocking = blocking is not null;
    }

    private ProfileSetupDraft CreateDraft() => new(
        string.IsNullOrWhiteSpace(DisplayName) ? "Me" : DisplayName.Trim(),
        DateOnly.FromDateTime(DateOfBirth),
        SelectedSex switch
        {
            "Female" => BiologicalSex.Female,
            "Male" => BiologicalSex.Male,
            _ => BiologicalSex.PreferNotToSay,
        },
        Length.FromCentimetres((decimal)Math.Max(1, HeightCentimetres)),
        Mass.FromKilograms((decimal)Math.Max(0.1, CurrentWeightKilograms)),
        SelectedGoal switch
        {
            "Lose weight" => FitnessGoal.LoseWeight,
            "Maintain" => FitnessGoal.Maintain,
            "Gain weight" => FitnessGoal.GainWeight,
            "Build strength" => FitnessGoal.BuildStrength,
            "Improve fitness" => FitnessGoal.ImproveFitness,
            _ => FitnessGoal.Unspecified,
        },
        Mass.FromKilograms((decimal)Math.Max(0.1, TargetWeightKilograms)),
        Math.Max(1, (int)Math.Round(TimeframeWeeks)),
        (decimal)Math.Max(0, TargetDailyCalories),
        SelectedExperience switch
        {
            "Beginner" => TrainingExperienceLevel.Beginner,
            "Intermediate" => TrainingExperienceLevel.Intermediate,
            "Advanced" => TrainingExperienceLevel.Advanced,
            _ => TrainingExperienceLevel.Unspecified,
        },
        SelectedEquipmentFromFlags(),
        MovementLimitations.Trim(),
        Math.Clamp((int)Math.Round(TrainingDaysPerWeek), 0, 7));

    private List<string> SelectedEquipmentFromFlags()
    {
        var selected = new List<string>();
        if (HasBodyweight)
        {
            selected.Add("Bodyweight");
        }

        if (HasDumbbells)
        {
            selected.Add("Dumbbells");
        }

        if (HasBarbell)
        {
            selected.Add("Barbell");
        }

        if (HasMachines)
        {
            selected.Add("Machines");
        }

        if (HasBands)
        {
            selected.Add("Bands");
        }

        return selected.Count == 0 ? ["Bodyweight"] : selected;
    }
}
