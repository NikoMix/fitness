using Forge.App.Composition;
using Forge.App.Features.Profile;
using Forge.Core.Abstractions.Data;
using Forge.Core.Abstractions.Preferences;
using Forge.Domain.Analytics;
using Forge.Domain.Common;
using Forge.Domain.Measurement;
using Forge.Domain.Nutrition;
using Forge.Domain.Planning;
using Forge.Domain.Profile;
using Forge.Domain.Recovery;
using Forge.Domain.Training;

namespace Forge.App.Features.Insights.Services;

/// <summary>
/// Reads the local database for the Today, Progress and Insights screens.
/// </summary>
/// <remarks>
/// <para>
/// Every method opens one session, reads only the tables its screen actually shows, and does all
/// aggregation on a background thread. Reads are split per screen rather than shared behind one
/// snapshot because a shared snapshot made every screen pay for every other screen: the body
/// weight chart was running personal-record detection across the whole set history, on a phone,
/// before it could draw a line.
/// </para>
/// <para>
/// Nothing is cached. Someone who finishes a workout and opens Progress expects to see that
/// workout, and a cache short-lived enough to guarantee that would be too short-lived to save any
/// work. Reading less is the honest optimisation; serving stale numbers is not.
/// </para>
/// </remarks>
public interface IInsightsDataService
{
    /// <summary>Loads the Today dashboard summary.</summary>
    /// <param name="today">The user's local date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Today's rings, focus action and recent activity.</returns>
    Task<InsightsDataSnapshot> LoadAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>Loads weekly volume, intensity and consistency for the Progress screen.</summary>
    /// <param name="today">The user's local date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The progress overview.</returns>
    Task<ProgressOverview> LoadProgressAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>Loads muscle and pattern breakdowns plus the sleep association for the Insights screen.</summary>
    /// <param name="today">The user's local date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The insights overview.</returns>
    Task<InsightsOverview> LoadInsightsAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>Loads estimated one-repetition-maximum progression for the most logged exercise.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exercise progress view.</returns>
    Task<ExerciseProgressView> LoadExerciseProgressAsync(CancellationToken cancellationToken);

    /// <summary>Loads detected personal records.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The personal records view.</returns>
    Task<PersonalRecordsView> LoadPersonalRecordsAsync(CancellationToken cancellationToken);

    /// <summary>Loads the smoothed body weight trend.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The body metrics view.</returns>
    Task<BodyMetricsView> LoadBodyMetricsAsync(CancellationToken cancellationToken);

    /// <summary>Records one body measurement against the active profile.</summary>
    /// <param name="entry">The measurement to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What happened, so the screen can say so without inspecting the database again.</returns>
    Task<BodyMetricSaveResult> SaveBodyMetricAsync(BodyMetricEntry entry, CancellationToken cancellationToken);
}

/// <summary>
/// One body measurement as the entry screen collected it.
/// </summary>
/// <remarks>
/// Weight is required and the rest are optional, which matches how people actually weigh
/// themselves: the scale reading is daily, the tape measure is occasional. Every value is already
/// in Forge's canonical storage units - converting from what the user typed is the view model's
/// job, because that is where the unit preference is known.
/// </remarks>
/// <param name="Date">The local date the measurement belongs to.</param>
/// <param name="Weight">Body weight. Must be above zero.</param>
/// <param name="BodyFatPercentage">Optional body-fat percentage.</param>
/// <param name="WaistCircumference">Optional waist circumference.</param>
public sealed record BodyMetricEntry(
    DateOnly Date,
    Mass Weight,
    Percentage? BodyFatPercentage = null,
    Length? WaistCircumference = null);

/// <summary>What a body-metric write did.</summary>
public enum BodyMetricSaveResult
{
    /// <summary>A new entry was added for that date.</summary>
    Added = 0,

    /// <summary>An entry already existed for that date and was replaced.</summary>
    Replaced = 1,

    /// <summary>No profile is active, so nothing was written. Writing anyway would orphan the row.</summary>
    NoActiveProfile = 2,

    /// <summary>The entry carried no usable weight.</summary>
    Rejected = 3
}

/// <summary>What the Today dashboard needs from local storage.</summary>
/// <param name="Today">Today's summary.</param>
public sealed record InsightsDataSnapshot(TodaySummary Today);

/// <summary>Headline counts shared by the Progress and Insights screens.</summary>
/// <param name="CompletedSessions">Sessions marked complete.</param>
/// <param name="WorkingSets">Working sets logged, warm-ups excluded.</param>
/// <param name="TotalVolumeKilograms">Total working volume.</param>
/// <param name="TrainingDays">Distinct local dates containing working sets.</param>
public sealed record ProgressTotals(
    int CompletedSessions,
    int WorkingSets,
    decimal TotalVolumeKilograms,
    int TrainingDays)
{
    /// <summary>Whether anything at all has been logged.</summary>
    public bool HasTraining => CompletedSessions > 0 || WorkingSets > 0;
}

/// <summary>Everything the Progress screen shows.</summary>
/// <param name="Totals">Headline counts.</param>
/// <param name="Consistency">Weekly sessions against the plan.</param>
/// <param name="Weeks">Weekly volume and intensity, ascending.</param>
/// <param name="VolumeReadiness">Whether the volume chart may be drawn.</param>
/// <param name="MeanLoadReadiness">Whether the mean load chart may be drawn.</param>
public sealed record ProgressOverview(
    ProgressTotals Totals,
    ConsistencySummary Consistency,
    IReadOnlyList<TrainingWeek> Weeks,
    SeriesReadinessResult VolumeReadiness,
    SeriesReadinessResult MeanLoadReadiness);

/// <summary>Everything the Insights screen shows.</summary>
/// <param name="Totals">Headline counts.</param>
/// <param name="Consistency">Weekly sessions against the plan.</param>
/// <param name="MuscleGroups">Volume and intensity per muscle group, biggest first.</param>
/// <param name="MovementPatterns">Volume and intensity per movement pattern, biggest first.</param>
/// <param name="SleepAssociation">The association-only sleep result.</param>
public sealed record InsightsOverview(
    ProgressTotals Totals,
    ConsistencySummary Consistency,
    IReadOnlyList<TrainingTrendSlice> MuscleGroups,
    IReadOnlyList<TrainingTrendSlice> MovementPatterns,
    SleepPerformanceInsight SleepAssociation);

/// <summary>Estimated one-repetition-maximum progression for a single exercise.</summary>
/// <param name="ExerciseName">Name of the exercise being charted.</param>
/// <param name="Formula">Which published fit produced the estimates.</param>
/// <param name="EstimatePoints">One point per training day, ascending.</param>
/// <param name="EstimateReadiness">Whether the estimate chart may be drawn.</param>
/// <param name="ExcludedHighRepSets">Working sets left out because the repetition count exceeds the supported range.</param>
public sealed record ExerciseProgressView(
    string ExerciseName,
    OneRepMaxFormula Formula,
    IReadOnlyList<ExerciseEstimatePoint> EstimatePoints,
    SeriesReadinessResult EstimateReadiness,
    int ExcludedHighRepSets);

/// <summary>Detected personal records, newest first.</summary>
/// <param name="Records">The records to show.</param>
/// <param name="Formula">Formula used for estimated records.</param>
public sealed record PersonalRecordsView(IReadOnlyList<PersonalRecordDisplay> Records, OneRepMaxFormula Formula);

/// <summary>The smoothed body weight series and what may be claimed about it.</summary>
/// <param name="Points">Raw and smoothed values per day, ascending.</param>
/// <param name="Trend">Smoothing, trend claim and charting verdict.</param>
public sealed record BodyMetricsView(IReadOnlyList<BodyMetricTrendPoint> Points, SmoothedTrendResult Trend);

/// <summary>One day of body weight, raw and smoothed.</summary>
/// <param name="Date">Local date.</param>
/// <param name="RawKilograms">The entry as recorded.</param>
/// <param name="SmoothedKilograms">The trailing moving average at this date.</param>
/// <param name="IsFullWindow">Whether the average covers a complete window rather than a partial one.</param>
public sealed record BodyMetricTrendPoint(
    DateOnly Date,
    decimal RawKilograms,
    decimal SmoothedKilograms,
    bool IsFullWindow);

/// <summary>One day's best estimated one-repetition maximum for an exercise.</summary>
/// <param name="Date">Local date of the set.</param>
/// <param name="EstimatedOneRepMaxKilograms">The estimate. Never a measured maximum.</param>
/// <param name="Formula">Which published fit produced it.</param>
/// <param name="SourceLoadKilograms">Load of the set the estimate came from.</param>
/// <param name="SourceRepetitions">Repetitions of that set.</param>
public sealed record ExerciseEstimatePoint(
    DateOnly Date,
    decimal EstimatedOneRepMaxKilograms,
    OneRepMaxFormula Formula,
    decimal SourceLoadKilograms,
    int SourceRepetitions);

/// <summary>One personal record, ready to display.</summary>
/// <param name="Title">Record kind, for example "Heaviest load".</param>
/// <param name="ExerciseName">Exercise the record belongs to.</param>
/// <param name="Headline">The achievement, in the units it was achieved in.</param>
/// <param name="Detail">The set that established it, and any caveat.</param>
/// <param name="AchievedUtc">When it was achieved.</param>
/// <param name="IsEstimate">Whether the headline figure is calculated rather than performed.</param>
public sealed record PersonalRecordDisplay(
    string Title,
    string ExerciseName,
    string Headline,
    string Detail,
    DateTimeOffset AchievedUtc,
    bool IsEstimate);

/// <summary>Today's dashboard summary.</summary>
/// <param name="SessionTitle">Name of today's scheduled session, or a no-plan message.</param>
/// <param name="SessionSubtitle">Supporting line for the session title.</param>
/// <param name="HasScheduledSession">Whether a plan scheduled a session today.</param>
/// <param name="Rings">Today's progress rings.</param>
/// <param name="NextActionTitle">Label for the hero action.</param>
/// <param name="NextActionDetail">Why that action was chosen.</param>
/// <param name="RecentActivity">Recent entries, newest first.</param>
public sealed record TodaySummary(
    string SessionTitle,
    string SessionSubtitle,
    bool HasScheduledSession,
    IReadOnlyList<TodayRingData> Rings,
    string NextActionTitle,
    string NextActionDetail,
    IReadOnlyList<TodayActivityData> RecentActivity);

/// <summary>One Today ring.</summary>
/// <param name="Label">What the ring measures.</param>
/// <param name="Progress">Completion from zero to one.</param>
/// <param name="Detail">The real numbers behind the ring.</param>
public sealed record TodayRingData(string Label, double Progress, string Detail);

/// <summary>One recent activity entry.</summary>
/// <param name="Title">What happened.</param>
/// <param name="Detail">Supporting numbers.</param>
/// <param name="WhenUtc">When it happened.</param>
public sealed record TodayActivityData(string Title, string Detail, DateTimeOffset WhenUtc);

internal sealed class InsightsDataService(ForgeStartupService startup, IDataSessionFactory sessions, ProfileStore profiles, IUnitFormatter units) : IInsightsDataService
{
    private const int HydrationTargetMillilitres = 2000;
    private const int MaximumRecordsShown = 30;
    private static readonly OneRepMaxFormula DefaultFormula = OneRepMaxFormula.Epley;

    public Task<InsightsDataSnapshot> LoadAsync(DateOnly today, CancellationToken cancellationToken)
        => ReadAsync(async (session, scope, token) =>
        {
            var sets = await OwnedAsync<SetEntry>(session, scope, token).ConfigureAwait(false);
            var workouts = await OwnedAsync<WorkoutSession>(session, scope, token).ConfigureAwait(false);
            var hydration = await OwnedAsync<HydrationEntry>(session, scope, token).ConfigureAwait(false);
            var plans = await OwnedAsync<TrainingPlan>(session, scope, token).ConfigureAwait(false);

            return new InsightsDataSnapshot(BuildTodaySummary(today, sets, workouts, hydration, plans));
        }, cancellationToken);

    public Task<ProgressOverview> LoadProgressAsync(DateOnly today, CancellationToken cancellationToken)
        => ReadAsync(async (session, scope, token) =>
        {
            var sets = await OwnedAsync<SetEntry>(session, scope, token).ConfigureAwait(false);
            var workouts = await OwnedAsync<WorkoutSession>(session, scope, token).ConfigureAwait(false);
            var plans = await OwnedAsync<TrainingPlan>(session, scope, token).ConfigureAwait(false);

            var weeks = TrainingTrendAggregator.PerWeek(sets);
            var loadedWeeks = weeks.Count(week => week.LoadedWorkingSets > 0);

            return new ProgressOverview(
                BuildTotals(sets, workouts),
                BuildConsistency(today, workouts, plans),
                weeks,
                SparseDataPolicy.Evaluate(weeks.Count, "your weekly training volume"),
                SparseDataPolicy.Evaluate(loadedWeeks, "your weekly mean load"));
        }, cancellationToken);

    public Task<InsightsOverview> LoadInsightsAsync(DateOnly today, CancellationToken cancellationToken)
        => ReadAsync(async (session, scope, token) =>
        {
            var sets = await OwnedAsync<SetEntry>(session, scope, token).ConfigureAwait(false);

            // The exercise catalogue is shared between profiles on purpose, so it is read
            // unscoped. It carries no personal data; the sets that reference it do.
            var exercises = await LiveAsync<Exercise>(session, token).ConfigureAwait(false);
            var workouts = await OwnedAsync<WorkoutSession>(session, scope, token).ConfigureAwait(false);
            var plans = await OwnedAsync<TrainingPlan>(session, scope, token).ConfigureAwait(false);
            var checkIns = await OwnedAsync<MorningCheckIn>(session, scope, token).ConfigureAwait(false);

            var nights = checkIns
                .Where(checkIn => checkIn.SleepHours is > 0m)
                .Select(checkIn => new SleepNight(checkIn.Date, checkIn.SleepHours!.Value))
                .ToList();

            return new InsightsOverview(
                BuildTotals(sets, workouts),
                BuildConsistency(today, workouts, plans),
                TrainingTrendAggregator.PerWeekByMuscleGroup(sets, exercises),
                TrainingTrendAggregator.PerWeekByMovementPattern(sets, exercises),
                SleepPerformancePairing.Analyze(nights, sets));
        }, cancellationToken);

    public Task<ExerciseProgressView> LoadExerciseProgressAsync(CancellationToken cancellationToken)
        => ReadAsync(async (session, scope, token) =>
        {
            var sets = await OwnedAsync<SetEntry>(session, scope, token).ConfigureAwait(false);
            var exercises = await LiveAsync<Exercise>(session, token).ConfigureAwait(false);

            return BuildExerciseProgress(sets, exercises);
        }, cancellationToken);

    public Task<PersonalRecordsView> LoadPersonalRecordsAsync(CancellationToken cancellationToken)
        => ReadAsync(async (session, scope, token) =>
        {
            var sets = await OwnedAsync<SetEntry>(session, scope, token).ConfigureAwait(false);
            var exercises = await LiveAsync<Exercise>(session, token).ConfigureAwait(false);

            return new PersonalRecordsView(BuildPersonalRecords(sets, exercises), DefaultFormula);
        }, cancellationToken);

    public Task<BodyMetricsView> LoadBodyMetricsAsync(CancellationToken cancellationToken)
        => ReadAsync(async (session, scope, token) =>
        {
            var metrics = await OwnedAsync<BodyMetric>(session, scope, token).ConfigureAwait(false);
            return BuildBodyMetrics(metrics);
        }, cancellationToken);

    /// <summary>
    /// Records one body measurement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole owned table is materialised before the date is compared. <c>RecordedUtc</c> is a
    /// <see cref="DateTimeOffset"/>, which SQLite stores as offset-suffixed text, so a predicate
    /// over it throws <see cref="InvalidOperationException"/> at runtime after compiling cleanly -
    /// and the in-memory provider used by most tests does not reproduce it.
    /// </para>
    /// <para>
    /// One entry per date, replaced rather than appended. Somebody who mistypes their weight and
    /// re-enters it expects to have corrected it, not to have moved the day's average halfway
    /// between the typo and the truth. This matches <c>ProfileStore.RecordWeightAsync</c>, which is
    /// the other way a weight reaches this table.
    /// </para>
    /// </remarks>
    public Task<BodyMetricSaveResult> SaveBodyMetricAsync(BodyMetricEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Weight <= Mass.Zero)
        {
            return Task.FromResult(BodyMetricSaveResult.Rejected);
        }

        return WriteAsync(async (session, scope, token) =>
        {
            // Fail closed. An unowned row is invisible to every scoped read, so writing one would
            // look like a successful save that lost the entry.
            if (!scope.IsResolved)
            {
                return BodyMetricSaveResult.NoActiveProfile;
            }

            var metrics = session.Repository<BodyMetric>();
            var owned = await OwnedAsync<BodyMetric>(session, scope, token).ConfigureAwait(false);
            var existing = owned.Find(metric => DateOnly.FromDateTime(metric.RecordedUtc.LocalDateTime) == entry.Date);

            if (existing is null)
            {
                await metrics.AddAsync(
                    new BodyMetric
                    {
                        UserProfileId = scope.ProfileId,
                        RecordedUtc = RecordedAt(entry.Date),
                        Weight = entry.Weight,
                        BodyFatPercentage = entry.BodyFatPercentage,
                        WaistCircumference = entry.WaistCircumference,
                    },
                    token).ConfigureAwait(false);

                await session.SaveChangesAsync(token).ConfigureAwait(false);
                return BodyMetricSaveResult.Added;
            }

            existing.Weight = entry.Weight;

            // Optional fields left blank do not erase what was measured before. Somebody logging a
            // second weigh-in has not thereby retracted this morning's waist measurement.
            existing.BodyFatPercentage = entry.BodyFatPercentage ?? existing.BodyFatPercentage;
            existing.WaistCircumference = entry.WaistCircumference ?? existing.WaistCircumference;

            await metrics.UpdateAsync(existing, token).ConfigureAwait(false);
            await session.SaveChangesAsync(token).ConfigureAwait(false);
            return BodyMetricSaveResult.Replaced;
        }, cancellationToken);
    }

    /// <summary>
    /// The instant to stamp on an entry so it reads back on the date the user chose.
    /// </summary>
    /// <remarks>
    /// Every read groups by the <em>local</em> date of <c>RecordedUtc</c>. Midday is used for a
    /// back-dated entry because midnight in a positive-offset zone converts to the previous day in
    /// UTC, and the entry would then be filed under the day before the one that was picked.
    /// </remarks>
    private static DateTimeOffset RecordedAt(DateOnly date)
    {
        if (date == DateOnly.FromDateTime(DateTime.Now))
        {
            return DateTimeOffset.UtcNow;
        }

        var localMidday = date.ToDateTime(new TimeOnly(12, 0));
        return new DateTimeOffset(localMidday, TimeZoneInfo.Local.GetUtcOffset(localMidday));
    }

    /// <summary>
    /// Runs one read on a background thread over a single session, confined to the active profile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>Task.Run</c> is deliberate. The SQLite read and the aggregation that follows it are
    /// synchronous enough to stutter a scroll if they begin on the UI thread, which on a mid-range
    /// Android device shows up as a dropped frame every time this section is opened.
    /// </para>
    /// <para>
    /// The scope is resolved once, before the session opens, and handed to the read. Resolving it
    /// per query would let a profile switch land between two reads of the same screen and produce a
    /// half-scoped result: this profile's sets against the other profile's sessions.
    /// </para>
    /// </remarks>
    private Task<T> ReadAsync<T>(Func<IDataSession, ProfileScope, CancellationToken, Task<T>> read, CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);
            if (!startup.Succeeded)
            {
                throw new InvalidOperationException("Forge startup did not complete successfully.", startup.Failure);
            }

            var scope = await profiles.GetActiveScopeAsync(cancellationToken).ConfigureAwait(false);

            // One session, one context, one connection. Resolving a repository per entity type
            // from the container would open a separate connection for each of them.
            await using var session = sessions.Create();
            return await read(session, scope, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>
    /// Runs one write on a background thread over a single session, confined to the active profile.
    /// </summary>
    /// <remarks>
    /// The read counterpart's reasoning applies unchanged, and one thing is added: the session is
    /// the only unit of work involved, so the read that finds an existing row and the write that
    /// replaces it share a change tracker. Resolving a repository separately would give the write
    /// its own context, and <c>SaveChangesAsync</c> would then commit an empty tracker and report
    /// success.
    /// </remarks>
    private Task<T> WriteAsync<T>(Func<IDataSession, ProfileScope, CancellationToken, Task<T>> write, CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);
            if (!startup.Succeeded)
            {
                throw new InvalidOperationException("Forge startup did not complete successfully.", startup.Failure);
            }

            var scope = await profiles.GetActiveScopeAsync(cancellationToken).ConfigureAwait(false);

            await using var session = sessions.Create();
            return await write(session, scope, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    private static async Task<List<T>> LiveAsync<T>(IDataSession session, CancellationToken cancellationToken)
        where T : Entity
    {
        var all = await session.Repository<T>().ListAsync(cancellationToken).ConfigureAwait(false);
        return all.Where(entity => !entity.IsDeleted).ToList();
    }

    /// <summary>Reads the live rows of one owned table, confined to a single profile.</summary>
    /// <remarks>
    /// An unresolved scope yields nothing rather than everything, so a screen opened before the
    /// active profile is known renders empty instead of rendering somebody else's training.
    /// </remarks>
    private static async Task<List<T>> OwnedAsync<T>(IDataSession session, ProfileScope scope, CancellationToken cancellationToken)
        where T : Entity, IProfileOwned
    {
        var all = await session.Repository<T>().ListAsync(cancellationToken).ConfigureAwait(false);
        return all.OwnedBy(scope).Where(entity => !entity.IsDeleted).ToList();
    }

    private static ProgressTotals BuildTotals(IReadOnlyList<SetEntry> sets, IReadOnlyList<WorkoutSession> workouts)
    {
        var working = sets.Where(set => !set.IsWarmUp && set.Repetitions > 0).ToList();

        return new ProgressTotals(
            workouts.Count(workout => workout.CompletedUtc is not null),
            working.Count,
            working.Sum(set => set.Volume.Kilograms),
            working.Select(set => DateOnly.FromDateTime(set.CompletedUtc.LocalDateTime)).Distinct().Count());
    }

    private static ConsistencySummary BuildConsistency(
        DateOnly today,
        IReadOnlyList<WorkoutSession> workouts,
        IReadOnlyList<TrainingPlan> plans)
    {
        var completedDates = workouts
            .Where(workout => workout.CompletedUtc is not null)
            .Select(workout => DateOnly.FromDateTime(workout.CompletedUtc!.Value.LocalDateTime))
            .ToList();

        return ConsistencyAnalyzer.Analyze(completedDates, today, WeeklySessionTarget(ActivePlan(plans)));
    }

    /// <summary>
    /// Reads the weekly session target from the active plan, or zero when none is active.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zero is a real answer rather than a missing one. Substituting a default target would let
    /// the app report someone as behind a plan they never chose.
    /// </para>
    /// <para>
    /// A flexible plan's target is its stated sessions per week, which is deliberately not clamped
    /// to the number of distinct days it defines. Running one "Full Body A" day three times a week
    /// is an ordinary programme, and clamping the target to the day count reported it as a
    /// once-weekly plan - which then showed a week containing a single session as full adherence.
    /// Flattering arithmetic is worse than none, because the reader has no way to see it happening.
    /// </para>
    /// </remarks>
    private static int WeeklySessionTarget(TrainingPlan? plan) => plan switch
    {
        null => 0,

        // A fixed-day plan runs each of its days once per week, so the day count is the target.
        { ScheduleMode: PlanScheduleMode.FixedDays } => plan.Days.Count,

        _ => Math.Max(0, plan.TargetSessionsPerWeek)
    };

    private static TrainingPlan? ActivePlan(IEnumerable<TrainingPlan> plans)
    {
        var materialized = plans.ToList();

        return materialized
            .Where(plan => plan.IsActive && !plan.IsTemplate && plan.Days.Count > 0)
            .OrderBy(plan => plan.CreatedUtc)
            .FirstOrDefault()
            ?? materialized
                .Where(plan => plan.IsActive && plan.Days.Count > 0)
                .OrderBy(plan => plan.CreatedUtc)
                .FirstOrDefault();
    }

    private static BodyMetricsView BuildBodyMetrics(IEnumerable<BodyMetric> bodyMetrics)
    {
        var daily = bodyMetrics
            .Where(metric => metric.Weight > Mass.Zero)
            .GroupBy(metric => DateOnly.FromDateTime(metric.RecordedUtc.LocalDateTime))
            .Select(group => new MeasurementPoint(group.Key, decimal.Round(group.Average(metric => metric.Weight.Kilograms), 2)))
            .ToList();

        var trend = SmoothedTrend.Build(daily, "your body weight");

        var points = trend.Points
            .Select(point => new BodyMetricTrendPoint(
                point.Date,
                point.RawValue,
                point.SmoothedValue,
                point.SampleCount >= trend.WindowSize))
            .ToList();

        return new BodyMetricsView(points, trend);
    }

    private static ExerciseProgressView BuildExerciseProgress(
        IReadOnlyList<SetEntry> sets,
        IReadOnlyList<Exercise> exercises)
    {
        var working = sets.Where(set => !set.IsWarmUp && set.Repetitions > 0).ToList();

        var estimable = working
            .Select(set => new { Set = set, Estimate = OneRepMaxEstimator.Estimate(set.Load, set.Repetitions, DefaultFormula) })
            .Where(item => item.Estimate is not null)
            .ToList();

        var exerciseId = estimable
            .GroupBy(item => item.Set.ExerciseId)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .FirstOrDefault()
            ?.Key;

        if (exerciseId is not Guid selected)
        {
            return new ExerciseProgressView(
                "No exercise yet",
                DefaultFormula,
                [],
                SparseDataPolicy.Evaluate(0, "your estimated one-rep max"),
                0);
        }

        var name = exercises.FirstOrDefault(exercise => exercise.Id == selected)?.Name ?? "Most logged exercise";

        var points = estimable
            .Where(item => item.Set.ExerciseId == selected)
            .GroupBy(item => DateOnly.FromDateTime(item.Set.CompletedUtc.LocalDateTime))
            .Select(group =>
            {
                var best = group.OrderByDescending(item => item.Estimate!.Value.Kilograms).First();
                return new ExerciseEstimatePoint(
                    group.Key,
                    best.Estimate!.Value.Kilograms,
                    DefaultFormula,
                    best.Set.Load.Kilograms,
                    best.Set.Repetitions);
            })
            .OrderBy(point => point.Date)
            .ToList();

        // Sets above the supported repetition range are dropped rather than estimated badly. The
        // count travels with the view so the gap in the line has a stated reason.
        var excluded = working.Count(set =>
            set.ExerciseId == selected
            && set.Repetitions > OneRepMaxEstimator.MaximumSupportedRepetitions);

        return new ExerciseProgressView(
            name,
            DefaultFormula,
            points,
            SparseDataPolicy.Evaluate(points.Count, $"estimates for {name}"),
            excluded);
    }

    private List<PersonalRecordDisplay> BuildPersonalRecords(
        IReadOnlyList<SetEntry> sets,
        IReadOnlyList<Exercise> exercises)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var exercise in exercises)
        {
            names[exercise.Id] = exercise.Name;
        }

        return PersonalRecordDetector.DetectAll(sets, DefaultFormula)
            .OrderByDescending(record => record.AchievedUtc)
            .ThenBy(record => record.Type)
            .Take(MaximumRecordsShown)
            .Select(record => new PersonalRecordDisplay(
                FormatRecordType(record.Type),
                names.GetValueOrDefault(record.ExerciseId, "Exercise"),
                FormatHeadline(record),
                FormatDetail(record, DefaultFormula),
                record.AchievedUtc,
                record.Type == PersonalRecordType.EstimatedOneRepMax))
            .ToList();
    }

    /// <summary>
    /// Formats a record in the units it was actually achieved in, and in the units the user reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every record carries its magnitude in a <see cref="Mass"/> so that records of one kind can
    /// be compared, including "most repetitions", where the repetition count is stored in the
    /// kilogram field. Rendering that field as kilograms turned a twelve-repetition record into
    /// "12 kg", which is not a formatting slip but a different quantity. That is also why nothing
    /// here converts <see cref="PersonalRecord.Repetitions"/>: a rep count is not a mass, and
    /// putting it through the unit formatter would turn twelve reps into "26.46 lb".
    /// </para>
    /// <para>
    /// The masses that really are masses go through <see cref="IUnitFormatter"/>. Personal records
    /// were one of the screens that showed kilograms to somebody who had chosen imperial.
    /// </para>
    /// </remarks>
    private string FormatHeadline(PersonalRecord record) => record.Type switch
    {
        PersonalRecordType.HeaviestLoad => Load(record.Load),
        PersonalRecordType.EstimatedOneRepMax => $"≈ {Load(record.Value)}",
        PersonalRecordType.MostRepsAtLoad => $"{record.Repetitions} reps at {Load(record.Load)}",
        PersonalRecordType.GreatestSessionVolume => $"{Load(record.Value)} total volume",
        _ => Load(record.Value)
    };

    private string FormatDetail(PersonalRecord record, OneRepMaxFormula formula) => record.Type switch
    {
        PersonalRecordType.EstimatedOneRepMax =>
            $"Calculated from {Load(record.Load)} × {record.Repetitions} with the {formula} formula. This is an estimate, not a lift you have performed.",
        PersonalRecordType.GreatestSessionVolume =>
            $"Summed across one session's working sets. The heaviest set that day was {Load(record.Load)} × {record.Repetitions}.",
        _ => $"Measured from a working set of {Load(record.Load)} × {record.Repetitions}."
    };

    private string Load(Mass mass) => units.FormatMass((double)mass.Kilograms, 2);

    private static string FormatRecordType(PersonalRecordType type) => type switch
    {
        PersonalRecordType.HeaviestLoad => "Heaviest load",
        PersonalRecordType.EstimatedOneRepMax => "Estimated 1RM",
        PersonalRecordType.MostRepsAtLoad => "Most reps at load",
        PersonalRecordType.GreatestSessionVolume => "Greatest session volume",
        _ => "Personal record"
    };

    private static TodaySummary BuildTodaySummary(
        DateOnly today,
        IReadOnlyList<SetEntry> sets,
        IReadOnlyList<WorkoutSession> workouts,
        IReadOnlyList<HydrationEntry> hydration,
        IReadOnlyList<TrainingPlan> plans)
    {
        var todaySets = sets.Where(set => DateOnly.FromDateTime(set.CompletedUtc.LocalDateTime) == today).ToList();
        var workingSets = todaySets.Count(set => !set.IsWarmUp && set.Repetitions > 0);
        var warmUpSets = todaySets.Count(set => set.IsWarmUp);
        var hydrationMillilitres = hydration
            .Where(entry => DateOnly.FromDateTime(entry.ConsumedUtc.LocalDateTime) == today)
            .Sum(entry => entry.Volume.Millilitres);

        var scheduled = FindScheduledSession(today, plans);
        var plannedWorkingSets = scheduled?.Day.Exercises.Sum(exercise => exercise.WorkingSetCount) ?? 3;
        var plannedWarmUps = Math.Max(1, scheduled?.Day.Exercises.Sum(exercise => exercise.Sets.Count(set => set.IsWarmUp)) ?? 1);
        var inProgress = workouts
            .Where(workout => workout.CompletedUtc is null)
            .OrderByDescending(workout => workout.StartedUtc)
            .FirstOrDefault();

        var rings = new[]
        {
            new TodayRingData("Training", Ratio(workingSets, Math.Max(1, plannedWorkingSets)), $"{workingSets} of {Math.Max(1, plannedWorkingSets)} working sets"),
            new TodayRingData("Mobility", Ratio(warmUpSets, plannedWarmUps), warmUpSets == 0 ? "Warm-up sets will fill this ring" : $"{warmUpSets} warm-up sets logged"),
            new TodayRingData("Hydration", Ratio(hydrationMillilitres, HydrationTargetMillilitres), $"{hydrationMillilitres:0} of {HydrationTargetMillilitres} ml"),
        };

        var sessionTitle = scheduled is null ? "No plan scheduled today" : scheduled.Day.Name;
        var sessionSubtitle = scheduled is null
            ? "Start an open workout or activate a plan."
            : $"{scheduled.Day.Exercises.Count} exercises · {plannedWorkingSets} working sets";

        var nextActionTitle = inProgress is not null
            ? "Continue your active workout"
            : scheduled is not null && workingSets == 0
                ? $"Start {scheduled.Day.Name}"
                : workingSets > 0
                    ? "Review today's logged work"
                    : "Log one working set";

        var nextActionDetail = inProgress is not null
            ? "Forge recovered a local session that has not been completed yet."
            : scheduled is not null
                ? "Today is based on your active plan and the sets already persisted for this date."
                : "Today will become more specific after you choose a plan or log training.";

        return new TodaySummary(
            sessionTitle,
            sessionSubtitle,
            scheduled is not null,
            rings,
            nextActionTitle,
            nextActionDetail,
            BuildRecentActivity(workouts, sets, hydration));
    }

    private static ScheduledPlanSession? FindScheduledSession(DateOnly today, IEnumerable<TrainingPlan> plans)
    {
        var activePlan = ActivePlan(plans);
        if (activePlan is null)
        {
            return null;
        }

        var weekStart = today.AddDays(-(((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7));
        return PlanScheduler.Schedule(activePlan, weekStart, 1).FirstOrDefault(session => session.Date == today);
    }

    private static List<TodayActivityData> BuildRecentActivity(
        IReadOnlyList<WorkoutSession> workouts,
        IReadOnlyList<SetEntry> sets,
        IReadOnlyList<HydrationEntry> hydration)
    {
        var sessionActivity = workouts
            .Where(workout => workout.CompletedUtc is not null)
            .Select(workout => new TodayActivityData(
                workout.Title ?? "Workout",
                $"{sets.Count(set => set.WorkoutSessionId == workout.Id && !set.IsWarmUp)} working sets",
                workout.CompletedUtc!.Value));

        var hydrationActivity = hydration
            .OrderByDescending(entry => entry.ConsumedUtc)
            .Take(3)
            .Select(entry => new TodayActivityData("Hydration", $"{entry.Volume.Millilitres:0} ml logged", entry.ConsumedUtc));

        return sessionActivity
            .Concat(hydrationActivity)
            .OrderByDescending(activity => activity.WhenUtc)
            .Take(5)
            .ToList();
    }

    private static double Ratio(decimal value, decimal target)
        => target <= 0m ? 0d : Math.Clamp((double)(value / target), 0d, 1d);
}
