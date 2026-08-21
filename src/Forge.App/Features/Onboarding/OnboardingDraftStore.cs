using System.Text.Json;
using Forge.Domain.Onboarding;
using Forge.Domain.Profile;

namespace Forge.App.Features.Onboarding;

/// <summary>Stores a partially completed onboarding draft so setup survives leaving the app.</summary>
public interface IOnboardingDraftStore
{
    /// <summary>Reads the stored draft, or <see langword="null"/> when there is none.</summary>
    /// <returns>The stored answers, or <see langword="null"/>.</returns>
    OnboardingAnswers? Load();

    /// <summary>Whether a draft is waiting to be resumed.</summary>
    /// <returns><see langword="true"/> when a draft exists.</returns>
    bool HasDraft();

    /// <summary>Writes the supplied answers over any existing draft.</summary>
    /// <param name="answers">The answers to persist.</param>
    void Save(OnboardingAnswers answers);

    /// <summary>Removes the draft, used once setup has been persisted properly.</summary>
    void Clear();
}

/// <summary>
/// Persists the onboarding draft to local device preferences.
/// </summary>
/// <remarks>
/// <para>
/// Preferences rather than the database because a draft is not a profile: it is deliberately
/// incomplete, it has not passed the safety guardrails, and writing it as a half-built
/// <see cref="UserProfile"/> would mean every screen in the app had to defend against rows that
/// only exist because someone put their phone down mid-setup.
/// </para>
/// <para>
/// This mirrors <c>ActiveWorkoutDraftStore</c>, which solves the same problem for an interrupted
/// workout.
/// </para>
/// </remarks>
internal sealed class OnboardingDraftStore : IOnboardingDraftStore
{
    private const string DraftKey = "forge.onboarding.draft.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public OnboardingAnswers? Load()
    {
        try
        {
            var json = Preferences.Default.Get(DraftKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var draft = JsonSerializer.Deserialize<OnboardingDraft>(json, JsonOptions);
            return draft?.ToAnswers();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            // A draft that cannot be read is worth strictly less than a clean start, and throwing
            // here would block first run entirely - the caller awaits this from an async void page
            // override. Drop it and let the user begin again.
            Clear();
            return null;
        }
    }

    /// <inheritdoc />
    public bool HasDraft() => !string.IsNullOrWhiteSpace(Preferences.Default.Get(DraftKey, string.Empty));

    /// <inheritdoc />
    public void Save(OnboardingAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        Preferences.Default.Set(DraftKey, JsonSerializer.Serialize(OnboardingDraft.From(answers), JsonOptions));
    }

    /// <inheritdoc />
    public void Clear() => Preferences.Default.Remove(DraftKey);

    // An explicit transport shape rather than serialising OnboardingAnswers directly: the domain
    // type is free to gain computed members without changing what sits in device storage.
    private sealed record OnboardingDraft(
        string DisplayName,
        FitnessGoal Goal,
        double TargetWeightKilograms,
        double TimeframeWeeks,
        double CurrentWeightKilograms,
        double HeightCentimetres,
        double TargetDailyCalories,
        DateOnly? DateOfBirth,
        BiologicalSex BiologicalSex,
        TrainingExperienceLevel ExperienceLevel,
        IReadOnlyList<string> AvailableEquipment,
        string MovementLimitations,
        double TrainingDaysPerWeek)
    {
        public static OnboardingDraft From(OnboardingAnswers answers) => new(
            answers.DisplayName,
            answers.Goal,
            answers.TargetWeightKilograms,
            answers.TimeframeWeeks,
            answers.CurrentWeightKilograms,
            answers.HeightCentimetres,
            answers.TargetDailyCalories,
            answers.DateOfBirth,
            answers.BiologicalSex,
            answers.ExperienceLevel,
            [.. answers.AvailableEquipment],
            answers.MovementLimitations,
            answers.TrainingDaysPerWeek);

        public OnboardingAnswers ToAnswers() => new()
        {
            DisplayName = DisplayName ?? string.Empty,
            Goal = Goal,
            TargetWeightKilograms = TargetWeightKilograms,
            TimeframeWeeks = TimeframeWeeks,
            CurrentWeightKilograms = CurrentWeightKilograms,
            HeightCentimetres = HeightCentimetres,
            TargetDailyCalories = TargetDailyCalories,
            DateOfBirth = DateOfBirth,
            BiologicalSex = BiologicalSex,
            ExperienceLevel = ExperienceLevel,
            AvailableEquipment = [.. AvailableEquipment ?? []],
            MovementLimitations = MovementLimitations ?? string.Empty,
            TrainingDaysPerWeek = TrainingDaysPerWeek,
        };
    }
}
