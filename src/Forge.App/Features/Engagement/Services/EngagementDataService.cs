using Forge.App.Composition;
using Forge.App.Features.Profile;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Common;
using Forge.Domain.Engagement;
using Forge.Domain.Planning;
using Forge.Domain.Profile;
using Forge.Domain.Recovery;
using Forge.Domain.Training;

namespace Forge.App.Features.Engagement.Services;

/// <summary>Everything the two engagement screens show, for one profile.</summary>
/// <param name="HasProfile">Whether an active profile could be resolved at all.</param>
/// <param name="GamificationEnabled">Whether the user wants badges and rhythm framing.</param>
/// <param name="Rhythm">The weekly picture, derived from logged sessions.</param>
/// <param name="Metrics">The counts the achievement rules read.</param>
/// <param name="Achievements">Every badge and where the user stands with it.</param>
/// <param name="NewlyEarned">Badges awarded by this refresh, empty on every later refresh.</param>
public sealed record EngagementSnapshot(
    bool HasProfile,
    bool GamificationEnabled,
    TrainingRhythm Rhythm,
    EngagementMetrics Metrics,
    IReadOnlyList<AchievementStatus> Achievements,
    IReadOnlyList<AchievementDefinition> NewlyEarned)
{
    /// <summary>An honest empty snapshot for a device with no resolvable profile.</summary>
    /// <param name="today">The user's local date.</param>
    /// <returns>A snapshot claiming nothing.</returns>
    public static EngagementSnapshot Empty(DateOnly today) => new(
        false,
        true,
        TrainingRhythmAnalyzer.Analyze([], today, 0, []),
        EngagementMetrics.Empty,
        [],
        []);
}

/// <summary>Reads and updates one profile's engagement state.</summary>
public interface IEngagementDataService
{
    /// <summary>Recomputes the rhythm and badges, awarding anything newly earned.</summary>
    /// <param name="today">The user's local date.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The snapshot both engagement screens render.</returns>
    Task<EngagementSnapshot> RefreshAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>Turns badges and rhythm framing on or off for the active profile.</summary>
    /// <param name="enabled">Whether engagement features should be shown.</param>
    /// <param name="today">The user's local date.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The snapshot after the change.</returns>
    Task<EngagementSnapshot> SetGamificationEnabledAsync(bool enabled, DateOnly today, CancellationToken cancellationToken);

    /// <summary>Marks a running stretch of days as not to be measured.</summary>
    /// <param name="reason">Why training is interrupted.</param>
    /// <param name="from">First day covered.</param>
    /// <param name="today">The user's local date.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The snapshot after the change.</returns>
    Task<EngagementSnapshot> ProtectFromAsync(TrainingInterruption reason, DateOnly from, DateOnly today, CancellationToken cancellationToken);

    /// <summary>Closes any running protected period.</summary>
    /// <param name="today">The user's local date, used as the final protected day.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The snapshot after the change.</returns>
    Task<EngagementSnapshot> EndProtectionAsync(DateOnly today, CancellationToken cancellationToken);
}

/// <summary>
/// The only path between the engagement screens and the database.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="IDataSession"/> per operation, so every repository shares a change tracker and a
/// single save commits everything together. Resolving <c>IRepository&lt;T&gt;</c> per entity type
/// from the container would give each one its own context, and the save would then commit an empty
/// tracker with no exception and no failing test.
/// </para>
/// <para>
/// Every read is confined to the active profile, and the scope is resolved once before the session
/// opens. Resolving it per query would let a profile switch land between two reads and produce a
/// snapshot mixing one person's sessions with another person's badges.
/// </para>
/// <para>
/// Nothing is ordered in the database. SQLite has no <c>DateTimeOffset</c>, so EF stores one as
/// offset-suffixed text and any <c>ORDER BY</c> over it throws at runtime — a failure the
/// in-memory provider does not reproduce. Rows are materialised first and ordered in memory.
/// </para>
/// </remarks>
internal sealed class EngagementDataService(ForgeStartupService startup, IDataSessionFactory sessions, ProfileStore profiles)
    : IEngagementDataService
{
    /// <inheritdoc />
    public Task<EngagementSnapshot> RefreshAsync(DateOnly today, CancellationToken cancellationToken)
        => WriteAsync(today, static _ => { }, cancellationToken);

    /// <inheritdoc />
    public Task<EngagementSnapshot> SetGamificationEnabledAsync(bool enabled, DateOnly today, CancellationToken cancellationToken)
        => WriteAsync(today, streak => streak.SetGamificationEnabled(enabled), cancellationToken);

    /// <inheritdoc />
    public Task<EngagementSnapshot> ProtectFromAsync(
        TrainingInterruption reason,
        DateOnly from,
        DateOnly today,
        CancellationToken cancellationToken)
        => WriteAsync(today, streak => streak.Protect(new ProtectedPeriod(from, null, reason)), cancellationToken);

    /// <inheritdoc />
    public Task<EngagementSnapshot> EndProtectionAsync(DateOnly today, CancellationToken cancellationToken)
        => WriteAsync(today, streak => streak.EndProtection(today), cancellationToken);

    /// <summary>
    /// Runs one operation on a background thread over a single session.
    /// </summary>
    /// <remarks>
    /// Reading and aggregating a training history is synchronous enough to drop a frame if it
    /// starts on the UI thread, which on a mid-range Android device is visible every time either
    /// engagement screen is opened.
    /// </remarks>
    private Task<EngagementSnapshot> WriteAsync(
        DateOnly today,
        Action<Streak> mutate,
        CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);
            if (!startup.Succeeded)
            {
                throw new InvalidOperationException("Forge startup did not complete successfully.", startup.Failure);
            }

            var scope = await profiles.GetActiveScopeAsync(cancellationToken).ConfigureAwait(false);

            // Fail-closed. Without a resolvable profile there is nobody to attribute a badge or a
            // protected period to, and writing one anyway would create a row owned by nobody that
            // a scoped read can never return again.
            if (!scope.IsResolved)
            {
                return EngagementSnapshot.Empty(today);
            }

            await using var session = sessions.Create();

            var streak = await GetOrCreateStreakAsync(session, scope, cancellationToken).ConfigureAwait(false);
            mutate(streak);
            await session.Repository<Streak>().UpdateAsync(streak, cancellationToken).ConfigureAwait(false);

            var snapshot = await BuildAsync(session, scope, streak, today, cancellationToken).ConfigureAwait(false);
            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return snapshot;
        }, cancellationToken);

    private static async Task<Streak> GetOrCreateStreakAsync(IDataSession session, ProfileScope scope, CancellationToken cancellationToken)
    {
        var existing = await OwnedAsync<Streak>(session, scope, cancellationToken).ConfigureAwait(false);

        // Ordered in memory. CreatedUtc is a DateTimeOffset and SQLite refuses to sort one.
        var streak = existing.OrderBy(record => record.CreatedUtc).FirstOrDefault();
        if (streak is not null)
        {
            return streak;
        }

        streak = new Streak { UserProfileId = scope.ProfileId };
        await session.Repository<Streak>().AddAsync(streak, cancellationToken).ConfigureAwait(false);
        return streak;
    }

    private static async Task<EngagementSnapshot> BuildAsync(
        IDataSession session,
        ProfileScope scope,
        Streak streak,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var workouts = await OwnedAsync<WorkoutSession>(session, scope, cancellationToken).ConfigureAwait(false);
        var sets = await OwnedAsync<SetEntry>(session, scope, cancellationToken).ConfigureAwait(false);
        var plans = await OwnedAsync<TrainingPlan>(session, scope, cancellationToken).ConfigureAwait(false);
        var checkIns = await OwnedAsync<MorningCheckIn>(session, scope, cancellationToken).ConfigureAwait(false);
        var achievements = await OwnedAsync<Achievement>(session, scope, cancellationToken).ConfigureAwait(false);

        // The exercise catalogue is shared between profiles on purpose. It carries no personal
        // data; the sets that reference it do.
        var exercises = await LiveAsync<Exercise>(session, cancellationToken).ConfigureAwait(false);

        var sessionDates = workouts
            .Where(workout => workout.CompletedUtc is not null)
            .Select(workout => DateOnly.FromDateTime(workout.CompletedUtc!.Value.LocalDateTime))
            .ToList();

        var rhythm = TrainingRhythmAnalyzer.Analyze(
            sessionDates,
            today,
            WeeklySessionTarget(ActivePlan(plans)),
            streak.ProtectedPeriods);

        var patterns = exercises
            .GroupBy(exercise => exercise.Id)
            .ToDictionary(group => group.Key, group => group.First().Pattern);

        var metrics = EngagementMetricsBuilder.Build(
            rhythm,
            sessionDates,
            sets
                .Where(set => !set.IsWarmUp && set.Repetitions > 0)
                .Select(set => new EngagementSet(
                    set.ExerciseId,
                    DateOnly.FromDateTime(set.CompletedUtc.LocalDateTime),
                    patterns.GetValueOrDefault(set.ExerciseId, MovementPattern.Unspecified),
                    set.Load,
                    set.Repetitions,
                    set.RepsInReserve.HasValue)),
            checkIns.Count);

        var unlockedUtcByCode = achievements
            .Where(achievement => achievement.UnlockedUtc is not null)
            .GroupBy(achievement => achievement.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Min(achievement => achievement.UnlockedUtc!.Value),
                StringComparer.OrdinalIgnoreCase);

        var newlyEarned = AchievementEvaluator.Evaluate(metrics, unlockedUtcByCode.Keys, streak.GamificationEnabled);
        var now = DateTimeOffset.UtcNow;

        foreach (var definition in newlyEarned)
        {
            var awarded = new Achievement
            {
                UserProfileId = scope.ProfileId,
                Code = definition.Code,
                Title = definition.Title,
                EncouragingDescription = definition.Description,
                Category = definition.Category,
            };

            awarded.MarkUnlocked(now);
            await session.Repository<Achievement>().AddAsync(awarded, cancellationToken).ConfigureAwait(false);
            unlockedUtcByCode[definition.Code] = now;
        }

        return new EngagementSnapshot(
            true,
            streak.GamificationEnabled,
            rhythm,
            metrics,
            AchievementEvaluator.Describe(metrics, unlockedUtcByCode, streak.GamificationEnabled),
            newlyEarned);
    }

    private static async Task<List<T>> LiveAsync<T>(IDataSession session, CancellationToken cancellationToken)
        where T : Entity
    {
        var all = await session.Repository<T>().ListAsync(cancellationToken).ConfigureAwait(false);
        return [.. all.Where(entity => !entity.IsDeleted)];
    }

    /// <summary>Reads the live rows of one owned table, confined to a single profile.</summary>
    private static async Task<List<T>> OwnedAsync<T>(IDataSession session, ProfileScope scope, CancellationToken cancellationToken)
        where T : Entity, IProfileOwned
    {
        var all = await session.Repository<T>().ListAsync(cancellationToken).ConfigureAwait(false);
        return [.. all.OwnedBy(scope).Where(entity => !entity.IsDeleted)];
    }

    /// <summary>
    /// Reads the weekly session target from the active plan, or zero when none is active.
    /// </summary>
    /// <remarks>
    /// Matches <c>InsightsDataService</c> deliberately. Two screens deriving "your weekly target"
    /// differently would show the same person two different adherence figures with no way to tell
    /// which was true. Zero is a real answer: without a plan there is no target, and inventing one
    /// would report somebody as behind a plan they never chose.
    /// </remarks>
    private static int WeeklySessionTarget(TrainingPlan? plan) => plan switch
    {
        null => 0,
        { ScheduleMode: PlanScheduleMode.FixedDays } => plan.Days.Count,
        _ => Math.Max(0, plan.TargetSessionsPerWeek),
    };

    private static TrainingPlan? ActivePlan(IEnumerable<TrainingPlan> plans)
    {
        var materialised = plans.ToList();

        return materialised
            .Where(plan => plan.IsActive && !plan.IsTemplate && plan.Days.Count > 0)
            .OrderBy(plan => plan.CreatedUtc)
            .FirstOrDefault()
            ?? materialised
                .Where(plan => plan.IsActive && plan.Days.Count > 0)
                .OrderBy(plan => plan.CreatedUtc)
                .FirstOrDefault();
    }
}
