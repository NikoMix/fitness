using Forge.App.Composition;
using Forge.Core.Abstractions.Data;
using Forge.Domain.Analytics;
using Forge.Domain.Nutrition;
using Forge.Domain.Planning;
using Forge.Domain.Profile;
using Forge.Domain.Training;
using Microsoft.Extensions.DependencyInjection;

namespace Forge.App.Features.Insights.Services;

public interface IInsightsDataService
{
    Task<InsightsDataSnapshot> LoadAsync(DateOnly today, CancellationToken cancellationToken);
}

public sealed record InsightsDataSnapshot(
    IReadOnlyList<BodyMetricTrendPoint> BodyMetricPoints,
    TrendResult BodyWeightTrend,
    IReadOnlyList<ExerciseEstimatePoint> ExerciseEstimatePoints,
    string ExerciseName,
    IReadOnlyList<PersonalRecordDisplay> PersonalRecords,
    ProgressSummary Progress,
    TodaySummary Today);

public sealed record BodyMetricTrendPoint(DateOnly Date, decimal RawKilograms, decimal SmoothedKilograms);

public sealed record ExerciseEstimatePoint(DateOnly Date, decimal EstimatedOneRepMaxKilograms, OneRepMaxFormula Formula);

public sealed record PersonalRecordDisplay(string Title, string ExerciseName, string Detail, DateTimeOffset AchievedUtc);

public sealed record ProgressSummary(int CompletedSessions, int WorkingSets, decimal TotalVolumeKilograms, decimal BodyMetricSampleCount);

public sealed record TodaySummary(
    string SessionTitle,
    string SessionSubtitle,
    bool HasScheduledSession,
    IReadOnlyList<TodayRingData> Rings,
    string NextActionTitle,
    string NextActionDetail,
    IReadOnlyList<TodayActivityData> RecentActivity);

public sealed record TodayRingData(string Label, double Progress, string Detail);

public sealed record TodayActivityData(string Title, string Detail, DateTimeOffset WhenUtc);

internal sealed class InsightsDataService(ForgeStartupService startup, IDataSessionFactory sessions) : IInsightsDataService
{
    private const int HydrationTargetMillilitres = 2000;
    private static readonly OneRepMaxFormula DefaultFormula = OneRepMaxFormula.Epley;

    public Task<InsightsDataSnapshot> LoadAsync(DateOnly today, CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            await startup.InitialiseAsync(cancellationToken).ConfigureAwait(false);
            if (!startup.Succeeded)
            {
                throw new InvalidOperationException("Forge startup did not complete successfully.", startup.Failure);
            }

            // One session, one context, one connection. Resolving a repository per entity type
            // from the container would open six separate connections for a single screen.
            await using var session = sessions.Create();

            var sets = (await session.Repository<SetEntry>().ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(set => !set.IsDeleted)
                .ToList();
            var workoutSessions = (await session.Repository<WorkoutSession>().ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(workout => !workout.IsDeleted)
                .ToList();
            var exercises = (await session.Repository<Exercise>().ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(exercise => !exercise.IsDeleted)
                .ToList();
            var bodyMetrics = (await session.Repository<BodyMetric>().ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(metric => !metric.IsDeleted)
                .ToList();
            var hydration = (await session.Repository<HydrationEntry>().ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(entry => !entry.IsDeleted)
                .ToList();
            var plans = (await session.Repository<TrainingPlan>().ListAsync(cancellationToken).ConfigureAwait(false))
                .Where(plan => !plan.IsDeleted)
                .ToList();

            return BuildSnapshot(today, sets, workoutSessions, exercises, bodyMetrics, hydration, plans);
        }, cancellationToken);

    private static InsightsDataSnapshot BuildSnapshot(
        DateOnly today,
        IReadOnlyList<SetEntry> sets,
        IReadOnlyList<WorkoutSession> sessions,
        IReadOnlyList<Exercise> exercises,
        IReadOnlyList<BodyMetric> bodyMetrics,
        IReadOnlyList<HydrationEntry> hydration,
        IReadOnlyList<TrainingPlan> plans)
    {
        var bodyPoints = BuildBodyMetricPoints(bodyMetrics);
        var bodyTrend = TrendAnalyzer.Analyze(bodyPoints.Select(point => new MeasurementPoint(point.Date, point.SmoothedKilograms)));
        var estimatePoints = BuildExerciseEstimatePoints(sets, out var exerciseId);
        var exerciseName = exercises.FirstOrDefault(exercise => exercise.Id == exerciseId)?.Name ?? "Most logged exercise";
        var records = BuildPersonalRecords(sets, exercises);
        var todaySummary = BuildTodaySummary(today, sets, sessions, hydration, plans);

        return new InsightsDataSnapshot(
            bodyPoints,
            bodyTrend,
            estimatePoints,
            exerciseName,
            records,
            new ProgressSummary(
                sessions.Count(session => session.CompletedUtc is not null),
                sets.Count(set => !set.IsWarmUp && set.Repetitions > 0),
                VolumeAggregator.PerWeek(sets).Sum(point => point.Volume.Kilograms),
                bodyPoints.Count),
            todaySummary);
    }

    private static List<BodyMetricTrendPoint> BuildBodyMetricPoints(IEnumerable<BodyMetric> bodyMetrics)
    {
        var dailyPoints = bodyMetrics
            .Where(metric => metric.Weight > Domain.Measurement.Mass.Zero)
            .GroupBy(metric => DateOnly.FromDateTime(metric.RecordedUtc.LocalDateTime))
            .Select(group => new MeasurementPoint(group.Key, decimal.Round(group.Average(metric => metric.Weight.Kilograms), 2)))
            .OrderBy(point => point.Date)
            .ToList();

        return MovingAverage.Smooth(dailyPoints)
            .Select(point => new BodyMetricTrendPoint(point.Date, point.RawValue, point.SmoothedValue))
            .ToList();
    }

    private static List<ExerciseEstimatePoint> BuildExerciseEstimatePoints(IReadOnlyList<SetEntry> sets, out Guid? exerciseId)
    {
        var estimates = sets
            .Where(set => !set.IsWarmUp)
            .Select(set => new { Set = set, Estimate = OneRepMaxEstimator.Estimate(set.Load, set.Repetitions, DefaultFormula) })
            .Where(item => item.Estimate is not null)
            .ToList();

        exerciseId = estimates
            .GroupBy(item => item.Set.ExerciseId)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .FirstOrDefault()
            ?.Key;

        if (exerciseId is not Guid selectedExerciseId)
        {
            return [];
        }

        return estimates
            .Where(item => item.Set.ExerciseId == selectedExerciseId)
            .GroupBy(item => DateOnly.FromDateTime(item.Set.CompletedUtc.LocalDateTime))
            .Select(group => new ExerciseEstimatePoint(
                group.Key,
                group.Max(item => item.Estimate!.Value.Kilograms),
                DefaultFormula))
            .OrderBy(point => point.Date)
            .ToList();
    }

    private static List<PersonalRecordDisplay> BuildPersonalRecords(IReadOnlyList<SetEntry> sets, IReadOnlyList<Exercise> exercises)
    {
        var exerciseNames = exercises.ToDictionary(exercise => exercise.Id, exercise => exercise.Name);
        return PersonalRecordDetector.DetectAll(sets)
            .OrderByDescending(record => record.AchievedUtc)
            .ThenBy(record => record.Type)
            .Take(30)
            .Select(record => new PersonalRecordDisplay(
                FormatRecordType(record.Type),
                exerciseNames.GetValueOrDefault(record.ExerciseId, "Exercise"),
                $"{record.Value.Kilograms:0.##} kg · {record.Load.Kilograms:0.##} kg × {record.Repetitions} · {record.Explanation}",
                record.AchievedUtc))
            .ToList();
    }

    private static TodaySummary BuildTodaySummary(
        DateOnly today,
        IReadOnlyList<SetEntry> sets,
        IReadOnlyList<WorkoutSession> sessions,
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
        var inProgress = sessions.Where(session => session.CompletedUtc is null).OrderByDescending(session => session.StartedUtc).FirstOrDefault();

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
            BuildRecentActivity(sessions, sets, hydration));
    }

    private static ScheduledPlanSession? FindScheduledSession(DateOnly today, IEnumerable<TrainingPlan> plans)
    {
        var activePlan = plans
            .Where(plan => plan.IsActive && !plan.IsTemplate && plan.Days.Count > 0)
            .OrderBy(plan => plan.CreatedUtc)
            .FirstOrDefault()
            ?? plans
                .Where(plan => plan.IsActive && plan.Days.Count > 0)
                .OrderBy(plan => plan.CreatedUtc)
                .FirstOrDefault();

        if (activePlan is null)
        {
            return null;
        }

        var weekStart = today.AddDays(-(((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7));
        return PlanScheduler.Schedule(activePlan, weekStart, 1).FirstOrDefault(session => session.Date == today);
    }

    private static List<TodayActivityData> BuildRecentActivity(
        IReadOnlyList<WorkoutSession> sessions,
        IReadOnlyList<SetEntry> sets,
        IReadOnlyList<HydrationEntry> hydration)
    {
        var sessionActivity = sessions
            .Where(session => session.CompletedUtc is not null)
            .Select(session => new TodayActivityData(
                session.Title ?? "Workout",
                $"{sets.Count(set => set.WorkoutSessionId == session.Id && !set.IsWarmUp)} working sets",
                session.CompletedUtc!.Value));

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

    private static string FormatRecordType(PersonalRecordType type) => type switch
    {
        PersonalRecordType.HeaviestLoad => "Heaviest load",
        PersonalRecordType.EstimatedOneRepMax => "Estimated 1RM",
        PersonalRecordType.MostRepsAtLoad => "Most reps at load",
        PersonalRecordType.GreatestSessionVolume => "Greatest session volume",
        _ => "Personal record"
    };

    private static double Ratio(decimal value, decimal target)
        => target <= 0m ? 0d : Math.Clamp((double)(value / target), 0d, 1d);
}
