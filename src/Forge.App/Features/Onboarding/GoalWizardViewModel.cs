using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Forge.App.Features.Profile;
using Forge.App.Navigation;
using Forge.Domain.Measurement;
using Forge.Domain.Onboarding;
using Forge.Domain.Profile;

namespace Forge.App.Features.Onboarding;

/// <summary>
/// Drives the step-by-step first-run goal wizard.
/// </summary>
/// <remarks>
/// <para>
/// The wizard shows one step at a time. The previous single-page form gave no sense of length, no
/// way to stop and come back, and - worst - reported a safety refusal at the bottom of a screen
/// whose cause was a field far above it.
/// </para>
/// <para>
/// Two rules shape the behaviour. First, the continue action is never disabled for validation
/// reasons: a greyed-out button that will not say why is the least helpful thing a form can do, so
/// pressing Continue on an incomplete step reveals what is missing instead. Second, nothing the
/// user typed is ever thrown away - not on a validation failure, not on a safety refusal, and not
/// when the app is killed mid-setup, because every step change writes the draft to local storage.
/// </para>
/// </remarks>
public sealed partial class GoalWizardViewModel : ObservableObject
{
    // Twelve weeks is long enough for any allowed rate of change to clear the safety guardrails,
    // so the wizard opens on a value that does not refuse itself before anything has been typed.
    private const double DefaultTimeframeWeeks = 12;

    private readonly ProfileStore? profileStore;
    private readonly IOnboardingDraftStore? draftStore;
    private readonly OnboardingAnswers answers = new();
    private bool suppressSync;

    /// <summary>Initialises an instance with no persistence, used by the XAML designer.</summary>
    public GoalWizardViewModel()
    {
        Goals = ProfileLabels.Goals;
        SexOptions = ProfileLabels.Sexes;
        ExperienceLevels = ProfileLabels.ExperienceLevels;
        EquipmentOptions = ProfileLabels.Equipment;

        ApplyAnswersToProperties();
        Evaluate();
    }

    /// <summary>Initialises the wizard.</summary>
    /// <param name="profileStore">Persistence for the finished profile.</param>
    /// <param name="draftStore">Persistence for a partially completed draft.</param>
    public GoalWizardViewModel(ProfileStore profileStore, IOnboardingDraftStore draftStore)
        : this()
    {
        this.profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        this.draftStore = draftStore ?? throw new ArgumentNullException(nameof(draftStore));
    }

    /// <summary>Available goal labels.</summary>
    public IReadOnlyList<string> Goals { get; }

    /// <summary>Available biological sex labels.</summary>
    public IReadOnlyList<string> SexOptions { get; }

    /// <summary>Available experience labels.</summary>
    public IReadOnlyList<string> ExperienceLevels { get; }

    /// <summary>Equipment choices shown in the wizard.</summary>
    public IReadOnlyList<string> EquipmentOptions { get; }

    /// <summary>Everything the user has answered, read back on the final step.</summary>
    public ObservableCollection<WizardReviewLine> ReviewLines { get; } = [];

    /// <summary>Outstanding issues on the current step, shown once the user tries to continue.</summary>
    public ObservableCollection<string> ValidationMessages { get; } = [];

    [ObservableProperty]
    private OnboardingStep currentStep = OnboardingStep.Goal;

    [ObservableProperty]
    private string stepTitle = string.Empty;

    [ObservableProperty]
    private string stepDescription = string.Empty;

    [ObservableProperty]
    private int stepNumber = 1;

    [ObservableProperty]
    private int stepCount = OnboardingFlow.StepCount;

    [ObservableProperty]
    private bool isGoalStep = true;

    [ObservableProperty]
    private bool isBodyMetricsStep;

    [ObservableProperty]
    private bool isExperienceStep;

    [ObservableProperty]
    private bool isEquipmentStep;

    [ObservableProperty]
    private bool isAvailabilityStep;

    [ObservableProperty]
    private bool isReviewStep;

    [ObservableProperty]
    private bool showsWeightTarget = true;

    [ObservableProperty]
    private bool showsWeightTargetHint;

    [ObservableProperty]
    private bool hasValidationMessages;

    [ObservableProperty]
    private string validationHeadline = string.Empty;

    [ObservableProperty]
    private string validationSummary = string.Empty;

    [ObservableProperty]
    private string primaryActionText = "Continue";

    [ObservableProperty]
    private string backActionText = "Back";

    [ObservableProperty]
    private string displayName = string.Empty;

    [ObservableProperty]
    private string selectedGoal = string.Empty;

    [ObservableProperty]
    private string selectedSex = "Prefer not to say";

    [ObservableProperty]
    private bool sharesDateOfBirth;

    [ObservableProperty]
    private DateTime dateOfBirth = DateTime.Today.AddYears(-30);

    [ObservableProperty]
    private double heightCentimetres;

    [ObservableProperty]
    private double currentWeightKilograms;

    [ObservableProperty]
    private double targetWeightKilograms;

    [ObservableProperty]
    private double targetDailyCalories;

    [ObservableProperty]
    private double timeframeWeeks = DefaultTimeframeWeeks;

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
    private double trainingDaysPerWeek = OnboardingAnswers.DefaultTrainingDaysPerWeek;

    [ObservableProperty]
    private string safetyHeadline = string.Empty;

    [ObservableProperty]
    private string safetyMessage = string.Empty;

    [ObservableProperty]
    private string safetySignpost = string.Empty;

    [ObservableProperty]
    private string safetyReassurance = string.Empty;

    [ObservableProperty]
    private bool hasSafetyAdvisory;

    [ObservableProperty]
    private bool isSafetyBlocking;

    [ObservableProperty]
    private bool isSaving;

    /// <summary>Loads any saved draft, falling back to the persisted profile when one exists.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once the wizard is populated.</returns>
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        var restored = TryLoadDraft();

        if (restored is null && profileStore is not null)
        {
            try
            {
                var snapshot = await profileStore.LoadAsync(cancellationToken).ConfigureAwait(false);
                if (snapshot is not null)
                {
                    restored = FromSnapshot(snapshot);
                }
            }
            catch (InvalidOperationException)
            {
                // Local storage is still starting. An empty wizard is still usable, and the finish
                // action reports the failure properly if storage is still not ready by then.
            }
        }

        // Everything below raises PropertyChanged on bound editors and rebuilds ReviewLines, which
        // a BindableLayout turns into view creation. The database read above resumes on a
        // thread-pool thread, so the rebuild is marshalled explicitly rather than relying on the
        // caller having a synchronisation context.
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (restored is not null)
            {
                CopyInto(answers, restored);
                ApplyAnswersToProperties();
                CurrentStep = OnboardingFlow.FirstIncompleteStep(answers);
            }

            Evaluate();
        }).ConfigureAwait(false);
    }

    /// <summary>Writes the current answers to the draft store.</summary>
    /// <remarks>Called when the page disappears so an interrupted setup can be resumed.</remarks>
    public void PersistDraft()
    {
        SyncAnswers();
        draftStore?.Save(answers);
    }

    /// <summary>Moves back one step when there is one.</summary>
    /// <returns><see langword="true"/> when a step change was handled inside the page.</returns>
    public bool TryGoBack()
    {
        if (OnboardingFlow.Previous(CurrentStep) is not { } previous)
        {
            return false;
        }

        ClearValidation();
        CurrentStep = previous;
        PersistDraft();
        return true;
    }

    partial void OnCurrentStepChanged(OnboardingStep value) => Evaluate();

    partial void OnDisplayNameChanged(string value) => Evaluate();

    partial void OnSelectedGoalChanged(string value) => Evaluate();

    partial void OnSelectedSexChanged(string value) => Evaluate();

    partial void OnSharesDateOfBirthChanged(bool value) => Evaluate();

    partial void OnDateOfBirthChanged(DateTime value) => Evaluate();

    partial void OnHeightCentimetresChanged(double value) => Evaluate();

    partial void OnCurrentWeightKilogramsChanged(double value) => Evaluate();

    partial void OnTargetWeightKilogramsChanged(double value) => Evaluate();

    partial void OnTargetDailyCaloriesChanged(double value) => Evaluate();

    partial void OnTimeframeWeeksChanged(double value) => Evaluate();

    partial void OnSelectedExperienceChanged(string value) => Evaluate();

    partial void OnHasBodyweightChanged(bool value) => Evaluate();

    partial void OnHasDumbbellsChanged(bool value) => Evaluate();

    partial void OnHasBarbellChanged(bool value) => Evaluate();

    partial void OnHasMachinesChanged(bool value) => Evaluate();

    partial void OnHasBandsChanged(bool value) => Evaluate();

    partial void OnMovementLimitationsChanged(string value) => Evaluate();

    partial void OnTrainingDaysPerWeekChanged(double value) => Evaluate();

    partial void OnIsSavingChanged(bool value) => ContinueCommand.NotifyCanExecuteChanged();

    /// <summary>Validates the current step and advances, or reveals what is still needed.</summary>
    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task ContinueAsync()
    {
        SyncAnswers();

        var validation = OnboardingFlow.Validate(CurrentStep, answers);
        if (!validation.IsValid)
        {
            ShowIssues(validation);
            return;
        }

        ClearValidation();

        if (OnboardingFlow.Next(CurrentStep) is { } next)
        {
            CurrentStep = next;
            PersistDraft();
            return;
        }

        await FinishAsync().ConfigureAwait(false);
    }

    /// <summary>Moves back one step, or leaves the wizard entirely from the first step.</summary>
    [RelayCommand]
    private async Task BackAsync()
    {
        if (TryGoBack())
        {
            return;
        }

        PersistDraft();
        await Shell.Current.GoToAsync("..").ConfigureAwait(true);
    }

    /// <summary>Jumps straight to the step that collects a particular answer.</summary>
    /// <param name="step">The step to show.</param>
    [RelayCommand]
    private void GoToStep(OnboardingStep step)
    {
        ClearValidation();
        CurrentStep = step;
    }

    private bool CanContinue() => !IsSaving;

    private async Task FinishAsync()
    {
        if (profileStore is null)
        {
            await Shell.Current.GoToAsync($"//{ForgeRoutes.Today}").ConfigureAwait(true);
            return;
        }

        IsSaving = true;
        try
        {
            // The save resumes on a thread-pool thread, so every UI touch below is marshalled.
            var result = await profileStore.SaveSetupAsync(CreateDraft(), CancellationToken.None).ConfigureAwait(false);
            if (!result.IsAccepted)
            {
                // Refused. Everything entered stays exactly where it is, the narration explains
                // every reason, and the wizard returns to the step that owns the numbers the
                // guardrails objected to rather than leaving the user stranded on a summary they
                // cannot edit.
                var narration = GoalSafetyNarrator.Narrate(result);
                var step = OnboardingFlow.StepForRefusal(answers);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ApplyNarration(narration);
                    CurrentStep = step;
                }).ConfigureAwait(false);

                PersistDraft();
                return;
            }

            draftStore?.Clear();
            await MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync($"//{ForgeRoutes.Today}")).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            PersistDraft();
            await MainThread.InvokeOnMainThreadAsync(() => ShowBlockingMessage(
                "Nothing was saved yet",
                "Forge is still preparing local storage. Everything you entered is kept exactly as it is - try again in a moment."))
                .ConfigureAwait(false);
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsSaving = false).ConfigureAwait(false);
        }
    }

    private void ShowIssues(OnboardingStepValidation validation)
    {
        ValidationMessages.Clear();
        foreach (var issue in validation.Issues)
        {
            ValidationMessages.Add(issue.Message);
        }

        ValidationHeadline = "Still needed on this step";
        ValidationSummary = string.Join(Environment.NewLine + Environment.NewLine, ValidationMessages);
        HasValidationMessages = ValidationMessages.Count > 0;
    }

    private void ShowBlockingMessage(string headline, string message)
    {
        ValidationMessages.Clear();
        ValidationMessages.Add(message);
        ValidationHeadline = headline;
        ValidationSummary = message;
        HasValidationMessages = true;
    }

    private void ClearValidation()
    {
        ValidationMessages.Clear();
        ValidationHeadline = string.Empty;
        ValidationSummary = string.Empty;
        HasValidationMessages = false;
    }

    /// <summary>
    /// Reads the stored draft without ever throwing.
    /// </summary>
    /// <remarks>
    /// <see cref="InitialiseAsync"/> is awaited from an <c>async void</c> page override, where an
    /// escaping exception has no caller to observe it and terminates the process. A draft that
    /// cannot be read is worth less than a clean start, so failure degrades to an empty wizard.
    /// </remarks>
    private OnboardingAnswers? TryLoadDraft()
    {
        try
        {
            return draftStore?.Load();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void Evaluate()
    {
        if (suppressSync)
        {
            return;
        }

        SyncAnswers();

        StepNumber = OnboardingFlow.PositionOf(CurrentStep);
        StepCount = OnboardingFlow.StepCount;
        StepTitle = OnboardingFlow.TitleOf(CurrentStep);
        StepDescription = OnboardingFlow.DescriptionOf(CurrentStep);

        IsGoalStep = CurrentStep == OnboardingStep.Goal;
        IsBodyMetricsStep = CurrentStep == OnboardingStep.BodyMetrics;
        IsExperienceStep = CurrentStep == OnboardingStep.Experience;
        IsEquipmentStep = CurrentStep == OnboardingStep.Equipment;
        IsAvailabilityStep = CurrentStep == OnboardingStep.Availability;
        IsReviewStep = CurrentStep == OnboardingStep.Review;
        ShowsWeightTarget = answers.GoalUsesWeightTarget;
        ShowsWeightTargetHint = IsGoalStep && !answers.GoalUsesWeightTarget && answers.Goal != FitnessGoal.Unspecified;
        PrimaryActionText = IsReviewStep ? "Save and start" : "Continue";
        BackActionText = IsGoalStep ? "Back to welcome" : "Back";

        if (IsReviewStep)
        {
            BuildReview();
        }

        EvaluateSafety();
    }

    private void EvaluateSafety()
    {
        if (OnboardingFlow.CreateSafetyProposal(answers) is not { } proposal)
        {
            HasSafetyAdvisory = false;
            IsSafetyBlocking = false;
            return;
        }

        ApplyNarration(GoalSafetyNarrator.Narrate(GoalSafetyEvaluator.Evaluate(proposal)));
    }

    private void ApplyNarration(GoalSafetyNarration narration)
    {
        SafetyHeadline = narration.Headline;
        SafetyMessage = narration.ReasonText;
        SafetySignpost = narration.SignpostText;
        SafetyReassurance = narration.Reassurance;
        IsSafetyBlocking = narration.BlocksSaving;

        // A reassuring "this is fine" belongs on the review step, where the user is deciding
        // whether to commit. Repeating it under every editor turns it into noise that the real
        // refusals then have to compete with.
        HasSafetyAdvisory = narration.HasContent && (narration.BlocksSaving || IsReviewStep);
    }

    private void BuildReview()
    {
        ReviewLines.Clear();
        ReviewLines.Add(new WizardReviewLine("Name", Display(answers.DisplayName), OnboardingStep.Goal));
        ReviewLines.Add(new WizardReviewLine("Goal", ProfileLabels.Describe(answers.Goal), OnboardingStep.Goal));

        if (answers.GoalUsesWeightTarget)
        {
            ReviewLines.Add(new WizardReviewLine("Target weight", Kilograms(answers.TargetWeightKilograms), OnboardingStep.Goal));
            ReviewLines.Add(new WizardReviewLine("Timeframe", Weeks(answers.TimeframeWeeks), OnboardingStep.Goal));
        }

        ReviewLines.Add(new WizardReviewLine("Current weight", Kilograms(answers.CurrentWeightKilograms), OnboardingStep.BodyMetrics));
        ReviewLines.Add(new WizardReviewLine("Height", Centimetres(answers.HeightCentimetres), OnboardingStep.BodyMetrics));
        ReviewLines.Add(new WizardReviewLine("Daily energy target", Calories(answers.TargetDailyCalories), OnboardingStep.BodyMetrics));
        ReviewLines.Add(new WizardReviewLine(
            "Date of birth",
            answers.DateOfBirth is { } dateOfBirth
                ? dateOfBirth.ToString("d MMM yyyy", CultureInfo.CurrentCulture)
                : "Not shared",
            OnboardingStep.BodyMetrics));
        ReviewLines.Add(new WizardReviewLine("Sex", ProfileLabels.Describe(answers.BiologicalSex), OnboardingStep.BodyMetrics));
        ReviewLines.Add(new WizardReviewLine("Experience", ProfileLabels.Describe(answers.ExperienceLevel), OnboardingStep.Experience));
        ReviewLines.Add(new WizardReviewLine("Equipment", Display(string.Join(", ", answers.AvailableEquipment)), OnboardingStep.Equipment));
        ReviewLines.Add(new WizardReviewLine("Movement limits", Display(answers.MovementLimitations, "None noted"), OnboardingStep.Equipment));
        ReviewLines.Add(new WizardReviewLine("Training days", Days(answers.TrainingDaysPerWeek), OnboardingStep.Availability));
    }

    private void SyncAnswers()
    {
        answers.DisplayName = DisplayName.Trim();
        answers.Goal = ProfileLabels.ParseGoal(SelectedGoal);
        answers.BiologicalSex = ProfileLabels.ParseSex(SelectedSex);
        answers.DateOfBirth = SharesDateOfBirth ? DateOnly.FromDateTime(DateOfBirth) : null;
        answers.HeightCentimetres = HeightCentimetres;
        answers.CurrentWeightKilograms = CurrentWeightKilograms;
        answers.TargetWeightKilograms = TargetWeightKilograms;
        answers.TargetDailyCalories = TargetDailyCalories;
        answers.TimeframeWeeks = TimeframeWeeks;
        answers.ExperienceLevel = ProfileLabels.ParseExperience(SelectedExperience);
        answers.MovementLimitations = MovementLimitations.Trim();
        answers.TrainingDaysPerWeek = TrainingDaysPerWeek;

        answers.AvailableEquipment.Clear();
        AddEquipment(HasBodyweight, "Bodyweight");
        AddEquipment(HasDumbbells, "Dumbbells");
        AddEquipment(HasBarbell, "Barbell");
        AddEquipment(HasMachines, "Machines");
        AddEquipment(HasBands, "Bands");

        void AddEquipment(bool selected, string label)
        {
            if (selected)
            {
                answers.AvailableEquipment.Add(label);
            }
        }
    }

    private void ApplyAnswersToProperties()
    {
        suppressSync = true;
        try
        {
            DisplayName = answers.DisplayName;
            SelectedGoal = answers.Goal == FitnessGoal.Unspecified ? string.Empty : ProfileLabels.Describe(answers.Goal);
            SelectedSex = ProfileLabels.Describe(answers.BiologicalSex);
            SharesDateOfBirth = answers.DateOfBirth is not null;
            DateOfBirth = answers.DateOfBirth?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today.AddYears(-30);
            HeightCentimetres = answers.HeightCentimetres;
            CurrentWeightKilograms = answers.CurrentWeightKilograms;
            TargetWeightKilograms = answers.TargetWeightKilograms;
            TargetDailyCalories = answers.TargetDailyCalories;
            TimeframeWeeks = answers.TimeframeWeeks > 0 ? answers.TimeframeWeeks : DefaultTimeframeWeeks;
            SelectedExperience = answers.ExperienceLevel == TrainingExperienceLevel.Unspecified
                ? string.Empty
                : ProfileLabels.Describe(answers.ExperienceLevel);
            MovementLimitations = answers.MovementLimitations;
            TrainingDaysPerWeek = answers.TrainingDaysPerWeek;

            HasBodyweight = answers.AvailableEquipment.Contains("Bodyweight");
            HasDumbbells = answers.AvailableEquipment.Contains("Dumbbells");
            HasBarbell = answers.AvailableEquipment.Contains("Barbell");
            HasMachines = answers.AvailableEquipment.Contains("Machines");
            HasBands = answers.AvailableEquipment.Contains("Bands");
        }
        finally
        {
            suppressSync = false;
        }
    }

    private ProfileSetupDraft CreateDraft() => new(
        string.IsNullOrWhiteSpace(answers.DisplayName)
            ? ProfileCompletionCalculator.PlaceholderDisplayName
            : answers.DisplayName,
        answers.DateOfBirth,
        answers.BiologicalSex,
        Length.FromCentimetres((decimal)answers.HeightCentimetres),
        Mass.FromKilograms((decimal)answers.CurrentWeightKilograms),
        answers.Goal,
        answers.GoalUsesWeightTarget && answers.TargetWeightKilograms > 0
            ? Mass.FromKilograms((decimal)answers.TargetWeightKilograms)
            : null,
        answers.GoalUsesWeightTarget && answers.TimeframeWeeks >= 1 ? (int)Math.Round(answers.TimeframeWeeks) : null,
        answers.TargetDailyCalories > 0 ? (decimal)answers.TargetDailyCalories : null,
        answers.ExperienceLevel,
        [.. answers.AvailableEquipment],
        answers.MovementLimitations,
        (int)Math.Round(answers.TrainingDaysPerWeek));

    private static OnboardingAnswers FromSnapshot(ProfileSnapshot snapshot)
    {
        var profile = snapshot.Profile;
        var latest = snapshot.BodyMetrics.Count > 0 ? snapshot.BodyMetrics[0] : null;

        return new OnboardingAnswers
        {
            DisplayName = string.Equals(profile.DisplayName, ProfileCompletionCalculator.PlaceholderDisplayName, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : profile.DisplayName,
            Goal = profile.Goal,
            TargetWeightKilograms = (double)(profile.TargetWeight?.Kilograms ?? 0m),
            TimeframeWeeks = profile.GoalTimeframeWeeks ?? 0,
            CurrentWeightKilograms = (double)(latest?.Weight.Kilograms ?? 0m),
            HeightCentimetres = (double)profile.Height.Centimetres,
            TargetDailyCalories = (double)(profile.TargetDailyCalories ?? 0m),
            DateOfBirth = profile.DateOfBirth,
            BiologicalSex = profile.BiologicalSex,
            ExperienceLevel = profile.ExperienceLevel,
            AvailableEquipment =
            [
                .. profile.AvailableEquipment.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            ],
            MovementLimitations = profile.MovementLimitations,
            TrainingDaysPerWeek = profile.TrainingDaysPerWeek,
        };
    }

    private static void CopyInto(OnboardingAnswers target, OnboardingAnswers source)
    {
        target.DisplayName = source.DisplayName;
        target.Goal = source.Goal;
        target.TargetWeightKilograms = source.TargetWeightKilograms;
        target.TimeframeWeeks = source.TimeframeWeeks;
        target.CurrentWeightKilograms = source.CurrentWeightKilograms;
        target.HeightCentimetres = source.HeightCentimetres;
        target.TargetDailyCalories = source.TargetDailyCalories;
        target.DateOfBirth = source.DateOfBirth;
        target.BiologicalSex = source.BiologicalSex;
        target.ExperienceLevel = source.ExperienceLevel;
        target.MovementLimitations = source.MovementLimitations;
        target.TrainingDaysPerWeek = source.TrainingDaysPerWeek;

        target.AvailableEquipment.Clear();
        foreach (var equipment in source.AvailableEquipment)
        {
            target.AvailableEquipment.Add(equipment);
        }
    }

    private static string Display(string value, string fallback = "Not set")
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Kilograms(double value)
        => value > 0 ? string.Create(CultureInfo.CurrentCulture, $"{value:0.#} kg") : "Not set";

    private static string Centimetres(double value)
        => value > 0 ? string.Create(CultureInfo.CurrentCulture, $"{value:0.#} cm") : "Not set";

    private static string Calories(double value)
        => value > 0 ? string.Create(CultureInfo.CurrentCulture, $"{value:0} kcal") : "Not set";

    private static string Weeks(double value)
        => value >= 1 ? string.Create(CultureInfo.CurrentCulture, $"{value:0} weeks") : "Not set";

    private static string Days(double value)
        => string.Create(CultureInfo.CurrentCulture, $"{value:0} days per week");
}

/// <summary>One answer read back on the review step, with the step that can change it.</summary>
/// <param name="Label">What the answer is.</param>
/// <param name="Value">The answer as it will be saved.</param>
/// <param name="Step">The step that collects it.</param>
public sealed record WizardReviewLine(string Label, string Value, OnboardingStep Step);
