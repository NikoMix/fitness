using Forge.App.Composition;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Measurement;
using Forge.Domain.Onboarding;
using Forge.Domain.Profile;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Profile;

// ForgeStartupService is internal and this type is public, so it is resolved from the provider
// rather than injected: a public constructor cannot expose an internal parameter type.

/// <summary>Reads and writes the single local profile and its body-metric history.</summary>
/// <param name="services">Provider used to reach the internal startup service.</param>
/// <param name="sessions">Factory for the shared data session.</param>
public sealed class ProfileStore(IServiceProvider services, IDataSessionFactory sessions)
{
    /// <summary>Whether a profile has been stored on this device.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true"/> when a profile exists.</returns>
    public async Task<bool> HasProfileAsync(CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return snapshot is not null;
    }

    /// <summary>Loads the profile and its body metrics, newest metric first.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshot, or <see langword="null"/> when no profile exists.</returns>
    public async Task<ProfileSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var profiles = session.Repository<UserProfile>();
        var metrics = session.Repository<BodyMetric>();

        var profile = (await profiles.ListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(profile => profile.CreatedUtc)
            .FirstOrDefault();

        if (profile is null)
        {
            return null;
        }

        var bodyMetrics = (await metrics.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(metric => metric.UserProfileId == profile.Id && !metric.IsDeleted)
            .OrderByDescending(metric => metric.RecordedUtc)
            .ToArray();

        return new ProfileSnapshot(profile, bodyMetrics);
    }

    /// <summary>
    /// Creates the minimal profile written when someone skips onboarding.
    /// </summary>
    /// <remarks>
    /// Deliberately sparse: the display name is a placeholder, and no goal, height or weight is
    /// invented. <see cref="ProfileCompletionCalculator"/> reads exactly this shape as "barely
    /// started", which is what lets Today and Profile offer to finish setup instead of rendering
    /// blanks or, worse, plausible-looking defaults the user never chose.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The existing or newly created profile.</returns>
    public async Task<UserProfile> EnsureDefaultProfileAsync(CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var profiles = session.Repository<UserProfile>();

        var profile = (await profiles.ListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(profile => profile.CreatedUtc)
            .FirstOrDefault();

        if (profile is not null)
        {
            return profile;
        }

        profile = new UserProfile
        {
            DisplayName = ProfileCompletionCalculator.PlaceholderDisplayName,
            ExperienceLevel = TrainingExperienceLevel.Unspecified,
            Goal = FitnessGoal.Unspecified,
            AvailableEquipment = "Bodyweight",
            TrainingDaysPerWeek = (int)OnboardingAnswers.DefaultTrainingDaysPerWeek,
        };

        await profiles.AddAsync(profile, cancellationToken).ConfigureAwait(false);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return profile;
    }

    /// <summary>Persists a completed setup, subject to the goal safety guardrails.</summary>
    /// <param name="draft">The answers to persist.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// The safety result. Nothing is written when the result is refused, so the caller can show the
    /// reasoning and let the user adjust without losing anything.
    /// </returns>
    public async Task<GoalSafetyResult> SaveSetupAsync(ProfileSetupDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var profiles = session.Repository<UserProfile>();
        var metrics = session.Repository<BodyMetric>();

        var profile = (await profiles.ListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(profile => profile.CreatedUtc)
            .FirstOrDefault();

        var isNew = profile is null;
        profile ??= new UserProfile { DisplayName = draft.DisplayName };

        profile.DisplayName = draft.DisplayName;
        profile.DateOfBirth = draft.DateOfBirth;
        profile.BiologicalSex = draft.BiologicalSex;
        profile.Height = draft.Height;
        profile.ExperienceLevel = draft.ExperienceLevel;
        profile.Goal = draft.Goal;
        profile.TargetWeight = draft.TargetWeight;
        profile.GoalTimeframeWeeks = draft.GoalTimeframeWeeks;
        profile.TargetDailyCalories = draft.TargetDailyCalories;
        profile.AvailableEquipment = string.Join(", ", draft.AvailableEquipment);
        profile.MovementLimitations = draft.MovementLimitations;
        profile.TrainingDaysPerWeek = draft.TrainingDaysPerWeek;

        // Re-running setup to change, say, training days must not stamp a second weight entry for
        // the same day. A body-metric history full of duplicate points makes every trend line on
        // the Progress screen lie about how often the user actually weighed themselves.
        var today = DateOnly.FromDateTime(DateTime.Now);
        var existingToday = (await metrics.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(metric => metric.UserProfileId == profile.Id && !metric.IsDeleted)
            .FirstOrDefault(metric => DateOnly.FromDateTime(metric.RecordedUtc.LocalDateTime) == today);

        var bodyMetric = existingToday ?? new BodyMetric
        {
            UserProfileId = profile.Id,
            RecordedUtc = DateTimeOffset.UtcNow,
        };

        bodyMetric.Weight = draft.CurrentWeight;

        var safety = GoalSafetyEvaluator.Evaluate(profile.CreateSafetyProposal(bodyMetric));
        if (!safety.IsAccepted)
        {
            // Nothing is saved. The session is disposed without SaveChangesAsync, so the mutations
            // above never reach the database and the caller keeps the user's input to correct.
            return safety;
        }

        if (isNew)
        {
            await profiles.AddAsync(profile, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await profiles.UpdateAsync(profile, cancellationToken).ConfigureAwait(false);
        }

        if (existingToday is null)
        {
            await metrics.AddAsync(bodyMetric, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await metrics.UpdateAsync(bodyMetric, cancellationToken).ConfigureAwait(false);
        }

        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return safety;
    }

    /// <summary>
    /// Records today's body weight against the existing profile.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SaveSetupAsync"/> because logging a weight is a one-field action
    /// people do often, and routing it through full goal setup would re-run every guardrail against
    /// answers they did not touch.
    /// </remarks>
    /// <param name="weight">The weight to record.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the weight was recorded.</returns>
    public async Task<bool> RecordWeightAsync(Mass weight, CancellationToken cancellationToken)
    {
        if (weight <= Mass.Zero)
        {
            return false;
        }

        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var profiles = session.Repository<UserProfile>();
        var metrics = session.Repository<BodyMetric>();

        var profile = (await profiles.ListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(profile => profile.CreatedUtc)
            .FirstOrDefault();

        if (profile is null)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var existingToday = (await metrics.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(metric => metric.UserProfileId == profile.Id && !metric.IsDeleted)
            .FirstOrDefault(metric => DateOnly.FromDateTime(metric.RecordedUtc.LocalDateTime) == today);

        if (existingToday is null)
        {
            await metrics.AddAsync(
                new BodyMetric
                {
                    UserProfileId = profile.Id,
                    RecordedUtc = DateTimeOffset.UtcNow,
                    Weight = weight,
                },
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            existingToday.Weight = weight;
            await metrics.UpdateAsync(existingToday, cancellationToken).ConfigureAwait(false);
        }

        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task EnsureStartupAsync(CancellationToken cancellationToken)
    {
        var startup = services.GetRequiredService<ForgeStartupService>();
        await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);

        if (!startup.Succeeded)
        {
            throw new InvalidOperationException("Forge startup did not complete successfully.", startup.Failure);
        }
    }
}

public sealed record ProfileSnapshot(UserProfile Profile, IReadOnlyList<BodyMetric> BodyMetrics);

/// <summary>
/// Everything first-run setup or a profile edit wants to persist.
/// </summary>
/// <remarks>
/// The optional values are genuinely nullable rather than defaulted. A date of birth of
/// <see cref="DateOnly.MinValue"/> or an energy target of zero is not "unset", it is a wrong
/// answer that the safety evaluator would then refuse for a goal the user never proposed.
/// </remarks>
/// <param name="DisplayName">The name shown inside the app.</param>
/// <param name="DateOfBirth">Date of birth, or <see langword="null"/> when not shared.</param>
/// <param name="BiologicalSex">Biological sex, used only by physiology formulas.</param>
/// <param name="Height">Current height.</param>
/// <param name="CurrentWeight">Today's body weight, stored as a new body metric.</param>
/// <param name="Goal">The primary goal.</param>
/// <param name="TargetWeight">Target body weight, or <see langword="null"/> for non-weight goals.</param>
/// <param name="GoalTimeframeWeeks">Planned timeframe, or <see langword="null"/>.</param>
/// <param name="TargetDailyCalories">Daily energy target, or <see langword="null"/> when unset.</param>
/// <param name="ExperienceLevel">Training background.</param>
/// <param name="AvailableEquipment">Equipment available for training.</param>
/// <param name="MovementLimitations">Free-text injuries or movement limits.</param>
/// <param name="TrainingDaysPerWeek">Weekly training availability.</param>
public sealed record ProfileSetupDraft(
    string DisplayName,
    DateOnly? DateOfBirth,
    BiologicalSex BiologicalSex,
    Length Height,
    Mass CurrentWeight,
    FitnessGoal Goal,
    Mass? TargetWeight,
    int? GoalTimeframeWeeks,
    decimal? TargetDailyCalories,
    TrainingExperienceLevel ExperienceLevel,
    IReadOnlyList<string> AvailableEquipment,
    string MovementLimitations,
    int TrainingDaysPerWeek);
