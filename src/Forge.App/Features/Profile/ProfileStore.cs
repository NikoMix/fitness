using Forge.App.Composition;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Common;
using Forge.Domain.Measurement;
using Forge.Domain.Nutrition;
using Forge.Domain.Nutrition.Recipes;
using Forge.Domain.Onboarding;
using Forge.Domain.Planning;
using Forge.Domain.Profile;
using Forge.Domain.Recovery;
using Forge.Domain.Training;
using Forge.Domain.Workout;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Profile;

// ForgeStartupService is internal and this type is public, so it is resolved from the provider
// rather than injected: a public constructor cannot expose an internal parameter type.

/// <summary>Reads and writes the local profiles on this device and their body-metric history.</summary>
/// <remarks>
/// <para>
/// A device may hold several profiles. Every read and write here goes through the active profile
/// chosen by <see cref="ActiveProfileSelector"/>, never through "the oldest profile that exists",
/// which is what this type used to do and what would otherwise make a switcher cosmetic.
/// </para>
/// <para>
/// Every area of Forge that holds one person's own logging now adopts <see cref="IProfileOwned"/>.
/// What is still shared, and why, is listed in phase 4 of docs/design/multi-profile.md and reported
/// to the user by <see cref="ProfileDataAreas"/>, which derives it from the code rather than from a
/// sentence that would rot.
/// </para>
/// </remarks>
/// <param name="services">Provider used to reach the internal startup service.</param>
/// <param name="sessions">Factory for the shared data session.</param>
public sealed class ProfileStore(IServiceProvider services, IDataSessionFactory sessions)
{
    /// <summary>
    /// The entity types a profile delete actually removes.
    /// </summary>
    /// <remarks>
    /// An explicit list rather than one discovered by reflection, because iOS builds ahead of time:
    /// <c>MakeGenericMethod</c> over an entity type resolved at runtime works on Android and throws
    /// on device. Adding an entry when a feature adopts <see cref="IProfileOwned"/> is one line, and
    /// until it is added the deletion dialog reports that data as retained rather than claiming an
    /// erasure that did not happen.
    /// </remarks>
    public static IReadOnlyList<Type> DeletableEntityTypes { get; } =
    [
        typeof(BodyMetric),
        typeof(WorkoutSession),
        typeof(SetEntry),
        typeof(ActiveWorkoutState),
        typeof(TrainingPlan),
        typeof(PlanDay),
        typeof(PlannedExercise),
        typeof(PlannedSet),
        typeof(FoodLogEntry),
        typeof(HydrationEntry),
        typeof(MorningCheckIn),
        typeof(SorenessEntry),
        typeof(Recipe),
        typeof(ExerciseProfileState),
    ];

    /// <summary>Raised after the active profile changes, so open screens can reload.</summary>
    public event EventHandler? ActiveProfileChanged;

    /// <summary>Whether a profile has been stored on this device.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true"/> when a profile exists.</returns>
    public async Task<bool> HasProfileAsync(CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return snapshot is not null;
    }

    /// <summary>Loads the active profile and its body metrics, newest metric first.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The snapshot, or <see langword="null"/> when no profile exists.</returns>
    public async Task<ProfileSnapshot?> LoadAsync(CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var stored = await session.Repository<UserProfile>().ListAsync(cancellationToken).ConfigureAwait(false);

        var profile = ActiveProfileSelector.SelectActive(stored);
        if (profile is null)
        {
            return null;
        }

        var bodyMetrics = await ReadOwnedMetricsAsync(session, ProfileScope.For(profile), cancellationToken).ConfigureAwait(false);

        return new ProfileSnapshot(profile, bodyMetrics, ActiveProfileSelector.OrderForDisplay(stored).Count);
    }

    /// <summary>Resolves the scope every profile-owned query on this device should run under.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The active scope, or <see cref="ProfileScope.None"/> when no profile exists.</returns>
    public async Task<ProfileScope> GetActiveScopeAsync(CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var stored = await session.Repository<UserProfile>().ListAsync(cancellationToken).ConfigureAwait(false);
        return ActiveProfileSelector.SelectScope(stored);
    }

    /// <summary>Loads every profile on the device together with which one is active.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The roster, which is empty before first-run setup.</returns>
    public async Task<ProfileRoster> LoadRosterAsync(CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var stored = await session.Repository<UserProfile>().ListAsync(cancellationToken).ConfigureAwait(false);
        var ordered = ActiveProfileSelector.OrderForDisplay(stored);
        var active = ActiveProfileSelector.SelectActive(stored);

        var metrics = await session.Repository<BodyMetric>().ListAsync(cancellationToken).ConfigureAwait(false);
        var counts = ordered.ToDictionary(
            profile => profile.Id,
            profile => metrics.OwnedBy(ProfileScope.For(profile)).Count(metric => !metric.IsDeleted));

        return new ProfileRoster(ordered, active?.Id, ActiveProfileSelector.CanAdd(ordered), counts);
    }

    /// <summary>
    /// Creates a profile and makes it active.
    /// </summary>
    /// <remarks>
    /// Switching immediately is deliberate. Somebody adding a profile on a shared device is adding
    /// it because they want to use it now, and leaving them on the previous profile is how the next
    /// thing they log lands on somebody else's record.
    /// </remarks>
    /// <param name="displayName">The name as typed.</param>
    /// <param name="kind">Whether this is a personal or a guest profile.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The name outcome. Nothing is written when the name is rejected.</returns>
    public async Task<ProfileNameResult> CreateProfileAsync(string? displayName, ProfileKind kind, CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var profiles = session.Repository<UserProfile>();
        var stored = await profiles.ListAsync(cancellationToken).ConfigureAwait(false);

        if (!ActiveProfileSelector.CanAdd(stored))
        {
            return ProfileNameResult.Rejected(
                $"This device already holds the maximum of {ActiveProfileSelector.MaximumProfiles} profiles. Delete one before adding another.");
        }

        var name = ProfileNameRules.Validate(displayName, stored);
        if (!name.IsAccepted)
        {
            return name;
        }

        await profiles.AddAsync(
            new UserProfile
            {
                DisplayName = name.Name,
                Kind = kind,
                LastActivatedUtc = NextActivationStamp(stored),
                ExperienceLevel = TrainingExperienceLevel.Unspecified,
                Goal = FitnessGoal.Unspecified,
                AvailableEquipment = "Bodyweight",
                TrainingDaysPerWeek = (int)OnboardingAnswers.DefaultTrainingDaysPerWeek,
            },
            cancellationToken).ConfigureAwait(false);

        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        RaiseActiveProfileChanged();
        return name;
    }

    /// <summary>Renames a profile.</summary>
    /// <param name="profileId">The profile to rename.</param>
    /// <param name="displayName">The name as typed.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The name outcome. Nothing is written when the name is rejected.</returns>
    public async Task<ProfileNameResult> RenameProfileAsync(Guid profileId, string? displayName, CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var profiles = session.Repository<UserProfile>();
        var stored = await profiles.ListAsync(cancellationToken).ConfigureAwait(false);

        var profile = stored.FirstOrDefault(candidate => candidate.Id == profileId && !candidate.IsDeleted);
        if (profile is null)
        {
            return ProfileNameResult.Rejected("That profile is no longer on this device.");
        }

        var name = ProfileNameRules.Validate(displayName, stored, profileId);
        if (!name.IsAccepted)
        {
            return name;
        }

        profile.DisplayName = name.Name;
        await profiles.UpdateAsync(profile, cancellationToken).ConfigureAwait(false);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return name;
    }

    /// <summary>Makes a profile the active one.</summary>
    /// <param name="profileId">The profile to switch to.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the profile exists and is now active.</returns>
    public async Task<bool> SwitchToAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var profiles = session.Repository<UserProfile>();
        var stored = await profiles.ListAsync(cancellationToken).ConfigureAwait(false);

        var profile = stored.FirstOrDefault(candidate => candidate.Id == profileId && !candidate.IsDeleted);
        if (profile is null)
        {
            return false;
        }

        profile.LastActivatedUtc = NextActivationStamp(stored);
        await profiles.UpdateAsync(profile, cancellationToken).ConfigureAwait(false);
        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        RaiseActiveProfileChanged();
        return true;
    }

    /// <summary>Describes precisely what deleting a profile removes and what it leaves behind.</summary>
    /// <param name="profileId">The profile the user selected.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The plan, or <see langword="null"/> when the profile is no longer present.</returns>
    public async Task<ProfileDeletionPlan?> PrepareDeletionAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var stored = await session.Repository<UserProfile>().ListAsync(cancellationToken).ConfigureAwait(false);

        var profile = stored.FirstOrDefault(candidate => candidate.Id == profileId && !candidate.IsDeleted);
        if (profile is null)
        {
            return null;
        }

        var owned = await ReadOwnedMetricsAsync(session, ProfileScope.For(profile), cancellationToken).ConfigureAwait(false);
        var scope = ProfileScope.For(profile);
        var counts = new Dictionary<Type, int>
        {
            [typeof(BodyMetric)] = owned.Count,
            [typeof(WorkoutSession)] = await CountOwnedAsync<WorkoutSession>(session, scope, cancellationToken).ConfigureAwait(false),
            [typeof(SetEntry)] = await CountOwnedAsync<SetEntry>(session, scope, cancellationToken).ConfigureAwait(false),
            [typeof(ActiveWorkoutState)] = await CountOwnedAsync<ActiveWorkoutState>(session, scope, cancellationToken).ConfigureAwait(false),
            [typeof(TrainingPlan)] = await CountOwnedAsync<TrainingPlan>(session, scope, cancellationToken).ConfigureAwait(false),
            [typeof(PlanDay)] = await CountOwnedAsync<PlanDay>(session, scope, cancellationToken).ConfigureAwait(false),
            [typeof(PlannedExercise)] = await CountOwnedAsync<PlannedExercise>(session, scope, cancellationToken).ConfigureAwait(false),
            [typeof(PlannedSet)] = await CountOwnedAsync<PlannedSet>(session, scope, cancellationToken).ConfigureAwait(false),
            [typeof(FoodLogEntry)] = await CountOwnedAsync<FoodLogEntry>(session, scope, cancellationToken).ConfigureAwait(false),
            [typeof(HydrationEntry)] = await CountOwnedAsync<HydrationEntry>(session, scope, cancellationToken).ConfigureAwait(false),
            [typeof(MorningCheckIn)] = await CountOwnedAsync<MorningCheckIn>(session, scope, cancellationToken).ConfigureAwait(false),
            [typeof(SorenessEntry)] = await CountOwnedAsync<SorenessEntry>(session, scope, cancellationToken).ConfigureAwait(false),
            [typeof(Recipe)] = await CountOwnedAsync<Recipe>(session, scope, cancellationToken).ConfigureAwait(false),
            [typeof(ExerciseProfileState)] = await CountOwnedAsync<ExerciseProfileState>(session, scope, cancellationToken).ConfigureAwait(false),
        };

        var refusal = ActiveProfileSelector.CanDelete(stored, profileId)
            ? null
            : "This is the only profile on this device. To remove everything, use Delete my data in Settings, which also destroys the encryption key.";

        return ProfileDeletionPlan.Create(
            profile,
            counts,
            DeletableEntityTypes,
            ActiveProfileSelector.SelectSuccessor(stored, profileId),
            refusal);
    }

    /// <summary>
    /// Deletes a profile and the data owned by it, and nothing else.
    /// </summary>
    /// <remarks>
    /// The rows to remove are chosen by <see cref="ProfileDeletion.Partition{T}"/> rather than
    /// filtered inline, so the one operation in Forge that can destroy another person's data is
    /// decided by code that is tested directly. Records carrying no owner are never touched:
    /// deleting them would take the remaining user's history with them.
    /// </remarks>
    /// <param name="profileId">The profile to delete.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true"/> when the profile was deleted.</returns>
    public async Task<bool> DeleteProfileAsync(Guid profileId, CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var profiles = session.Repository<UserProfile>();
        var stored = await profiles.ListAsync(cancellationToken).ConfigureAwait(false);

        if (!ActiveProfileSelector.CanDelete(stored, profileId))
        {
            return false;
        }

        var profile = stored.First(candidate => candidate.Id == profileId);
        var scope = ProfileScope.For(profile);

        // Written out type by type rather than looped over DeletableEntityTypes with reflection.
        // iOS builds ahead of time, so MakeGenericMethod over an entity type resolved at runtime
        // works on Android and throws on device. MultiProfilePersistenceTests mirrors this list and
        // fails when an owned type is missing from it.
        await SoftDeleteOwnedAsync<BodyMetric>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<WorkoutSession>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<SetEntry>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<ActiveWorkoutState>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<TrainingPlan>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<PlanDay>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<PlannedExercise>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<PlannedSet>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<FoodLogEntry>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<HydrationEntry>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<MorningCheckIn>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<SorenessEntry>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<Recipe>(session, scope, cancellationToken).ConfigureAwait(false);
        await SoftDeleteOwnedAsync<ExerciseProfileState>(session, scope, cancellationToken).ConfigureAwait(false);

        // Extend here, and in DeletableEntityTypes, when another entity adopts IProfileOwned.

        await profiles.SoftDeleteAsync(profileId, cancellationToken).ConfigureAwait(false);

        // The successor is stamped in the same unit of work as the delete. Committing the delete on
        // its own would leave the app momentarily with no active profile, and the fallback would
        // then choose by creation order rather than by what the user was last using.
        var successor = ActiveProfileSelector.SelectSuccessor(stored, profileId);
        if (successor is not null)
        {
            successor.LastActivatedUtc = NextActivationStamp(stored);
            await profiles.UpdateAsync(successor, cancellationToken).ConfigureAwait(false);
        }

        await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        RaiseActiveProfileChanged();
        return true;
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
    /// <returns>The active profile, or a newly created one when the device has none.</returns>
    public async Task<UserProfile> EnsureDefaultProfileAsync(CancellationToken cancellationToken)
    {
        await EnsureStartupAsync(cancellationToken).ConfigureAwait(false);

        await using var session = sessions.Create();
        var profiles = session.Repository<UserProfile>();

        var stored = await profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        var profile = ActiveProfileSelector.SelectActive(stored);

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

    /// <summary>Persists a completed setup for the active profile, subject to the goal safety guardrails.</summary>
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

        var stored = await profiles.ListAsync(cancellationToken).ConfigureAwait(false);
        var profile = ActiveProfileSelector.SelectActive(stored);

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
        var existingToday = (await ReadOwnedMetricsAsync(session, ProfileScope.For(profile), cancellationToken).ConfigureAwait(false))
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
    /// Records today's body weight against the active profile.
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
        var metrics = session.Repository<BodyMetric>();

        var stored = await session.Repository<UserProfile>().ListAsync(cancellationToken).ConfigureAwait(false);
        var profile = ActiveProfileSelector.SelectActive(stored);

        if (profile is null)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var existingToday = (await ReadOwnedMetricsAsync(session, ProfileScope.For(profile), cancellationToken).ConfigureAwait(false))
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

    /// <summary>
    /// Soft-deletes every row of one owned type that belongs to the profile being removed.
    /// </summary>
    /// <remarks>
    /// The rows are chosen by <see cref="ProfileDeletion.Partition{T}"/> rather than filtered
    /// inline, so the one operation in Forge that can destroy another person's data is decided by
    /// code that is tested directly. Records carrying no owner are never touched: deleting them
    /// would take the remaining user's history with them.
    /// </remarks>
    /// <typeparam name="T">The owned entity type to remove.</typeparam>
    /// <param name="session">The unit of work the delete commits through.</param>
    /// <param name="scope">The profile being deleted.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>How many rows were marked for deletion.</returns>
    private static async Task<int> SoftDeleteOwnedAsync<T>(IDataSession session, ProfileScope scope, CancellationToken cancellationToken)
        where T : Entity, IProfileOwned
    {
        var repository = session.Repository<T>();
        var partition = ProfileDeletion.Partition(
            await repository.ListAsync(cancellationToken).ConfigureAwait(false),
            scope);

        foreach (var id in partition.ToDelete)
        {
            await repository.SoftDeleteAsync(id, cancellationToken).ConfigureAwait(false);
        }

        return partition.ToDelete.Count;
    }

    /// <summary>Counts the live rows of one owned type belonging to a profile.</summary>
    /// <typeparam name="T">The owned entity type to count.</typeparam>
    /// <param name="session">The session to read through.</param>
    /// <param name="scope">The profile to count for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The number of live owned rows, which is what the deletion dialog shows.</returns>
    private static async Task<int> CountOwnedAsync<T>(IDataSession session, ProfileScope scope, CancellationToken cancellationToken)
        where T : Entity, IProfileOwned
    {
        var rows = await session.Repository<T>().ListAsync(cancellationToken).ConfigureAwait(false);
        return rows.OwnedBy(scope).Count(row => !row.IsDeleted);
    }

    private static async Task<IReadOnlyList<BodyMetric>> ReadOwnedMetricsAsync(
        IDataSession session,
        ProfileScope scope,
        CancellationToken cancellationToken)
    {
        var metrics = await session.Repository<BodyMetric>().ListAsync(cancellationToken).ConfigureAwait(false);

        return [.. metrics
            .OwnedBy(scope)
            .Where(metric => !metric.IsDeleted)
            .OrderByDescending(metric => metric.RecordedUtc)];
    }

    /// <summary>
    /// Produces an activation stamp strictly newer than every existing one.
    /// </summary>
    /// <remarks>
    /// Two switches inside one clock tick would otherwise be ordered by the tie-break rather than by
    /// what the user did, and the tie-break would sometimes pick the older profile. Forcing the
    /// value forward keeps the active profile a function of the last tap.
    /// </remarks>
    private static DateTimeOffset NextActivationStamp(IEnumerable<UserProfile> stored)
    {
        var now = DateTimeOffset.UtcNow;
        var latest = stored
            .Where(profile => profile.LastActivatedUtc.HasValue)
            .Select(profile => profile.LastActivatedUtc!.Value)
            .DefaultIfEmpty(DateTimeOffset.MinValue)
            .Max();

        return now > latest ? now : latest.AddTicks(1);
    }

    private void RaiseActiveProfileChanged() => ActiveProfileChanged?.Invoke(this, EventArgs.Empty);

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

/// <summary>The active profile, its measurements, and how many profiles share this device.</summary>
/// <param name="Profile">The active profile.</param>
/// <param name="BodyMetrics">Its body metrics, newest first.</param>
/// <param name="ProfileCount">How many profiles are stored on this device.</param>
public sealed record ProfileSnapshot(UserProfile Profile, IReadOnlyList<BodyMetric> BodyMetrics, int ProfileCount)
{
    /// <summary>Whether this device is shared between several profiles.</summary>
    public bool IsShared => ProfileCount > 1;
}

/// <summary>Every profile on the device, with the active one identified.</summary>
/// <param name="Profiles">Live profiles in display order.</param>
/// <param name="ActiveProfileId">The active profile, or <see langword="null"/> before first-run setup.</param>
/// <param name="CanAddProfile">Whether the device is below the profile limit.</param>
/// <param name="OwnedRecordCounts">Live records owned by each profile, keyed by profile identifier.</param>
public sealed record ProfileRoster(
    IReadOnlyList<UserProfile> Profiles,
    Guid? ActiveProfileId,
    bool CanAddProfile,
    IReadOnlyDictionary<Guid, int> OwnedRecordCounts);

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
