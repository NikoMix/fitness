using Forge.Domain.Measurement;
using Forge.Domain.Training;
using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

/// <summary>
/// Covers the post-workout comparison.
/// </summary>
/// <remarks>
/// The summary screen opened with "You showed up. Next time Forge will compare this against your
/// previous effort." and nothing ever replaced it, because no delta was computed anywhere. It read
/// as real because the personal-record detection beside it is real and correctly profile-scoped.
/// A comparison is now either calculated or plainly declined.
/// </remarks>
public sealed class WorkoutComparisonTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Bench = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_first_session_says_plainly_that_there_is_nothing_to_compare()
    {
        var session = Session(Now, sets: [(80m, 5)]);

        var comparison = WorkoutComparisonCalculator.Compare(session, []);

        comparison.Basis.ShouldBe(WorkoutComparisonBasis.NoPrevious);
        WorkoutComparisonNarrator.Describe(comparison)
            .ShouldBe("This is the first session Forge has to go on, so there is nothing to compare it against yet. The next one will have this to measure against.");
    }

    [Fact]
    public void The_promise_of_a_future_comparison_is_gone()
    {
        var describedForEveryBasis = new[]
        {
            WorkoutComparisonNarrator.Describe(WorkoutComparison.None),
            WorkoutComparisonNarrator.Describe(WorkoutComparisonCalculator.Compare(
                Session(Now, sets: [(80m, 5)]),
                [Session(Now.AddDays(-3), sets: [(75m, 5)])]))
        };

        foreach (var sentence in describedForEveryBasis)
        {
            sentence.ShouldNotContain("Next time Forge will compare");
        }
    }

    [Fact]
    public void A_planned_session_is_compared_with_the_last_time_that_same_day_was_trained()
    {
        var planDay = Guid.CreateVersion7();
        var current = Session(Now, sets: [(100m, 5), (100m, 5)], planDayId: planDay, planDayName: "Upper A");
        var lastUpperA = Session(Now.AddDays(-7), sets: [(90m, 5), (90m, 5)], planDayId: planDay, planDayName: "Upper A");
        var yesterdayLegs = Session(Now.AddDays(-1), sets: [(200m, 5)], planDayId: Guid.CreateVersion7(), planDayName: "Lower A");

        var comparison = WorkoutComparisonCalculator.Compare(current, [yesterdayLegs, lastUpperA]);

        // Not yesterday's leg day, even though it is the more recent session and far heavier.
        comparison.Basis.ShouldBe(WorkoutComparisonBasis.SamePlanDay);
        comparison.Label.ShouldBe("your last Upper A");
        comparison.VolumeDeltaKilograms.ShouldBe(100m);
        WorkoutComparisonNarrator.Describe(comparison)
            .ShouldBe("Compared with your last Upper A: 100 kg more working volume and the same number of working sets.");
    }

    [Fact]
    public void An_ad_hoc_session_falls_back_to_the_previous_session_and_says_so()
    {
        var current = Session(Now, sets: [(80m, 5)]);
        var previous = Session(Now.AddDays(-2), sets: [(80m, 8)]);

        var comparison = WorkoutComparisonCalculator.Compare(current, [previous]);

        comparison.Basis.ShouldBe(WorkoutComparisonBasis.PreviousSession);
        WorkoutComparisonNarrator.Describe(comparison)
            .ShouldBe("Compared with your previous session: 240 kg less working volume and the same number of working sets.");
    }

    [Fact]
    public void A_plan_day_never_trained_before_falls_back_rather_than_claiming_a_match()
    {
        var current = Session(Now, sets: [(80m, 5)], planDayId: Guid.CreateVersion7(), planDayName: "Upper A");
        var previous = Session(Now.AddDays(-2), sets: [(80m, 5)]);

        var comparison = WorkoutComparisonCalculator.Compare(current, [previous]);

        // This is the state of every device on the first release after the migration: no earlier
        // session carries a plan day, because there was no way to start a workout from a plan.
        comparison.Basis.ShouldBe(WorkoutComparisonBasis.PreviousSession);
        comparison.Label.ShouldBe("your previous session");
    }

    [Fact]
    public void A_later_session_is_never_treated_as_the_previous_one()
    {
        var current = Session(Now, sets: [(80m, 5)]);
        var later = Session(Now.AddDays(1), sets: [(200m, 5)]);

        WorkoutComparisonCalculator.Compare(current, [later]).Basis.ShouldBe(WorkoutComparisonBasis.NoPrevious);
    }

    [Fact]
    public void An_unfinished_session_is_not_a_comparison_candidate()
    {
        var current = Session(Now, sets: [(80m, 5)]);
        var abandoned = Session(Now.AddDays(-1), sets: [(80m, 5)], isCompleted: false);

        WorkoutComparisonCalculator.Compare(current, [abandoned]).Basis.ShouldBe(WorkoutComparisonBasis.NoPrevious);
    }

    [Fact]
    public void Warm_ups_are_excluded_from_both_sides_of_the_comparison()
    {
        var current = Session(Now, sets: [(100m, 5)]);
        current.Sets.Add(Set(current.Id, 40m, 10, isWarmUp: true, at: Now));

        var previous = Session(Now.AddDays(-3), sets: [(100m, 5)]);
        previous.Sets.Add(Set(previous.Id, 40m, 10, isWarmUp: true, at: Now.AddDays(-3)));

        var comparison = WorkoutComparisonCalculator.Compare(current, [previous]);

        comparison.VolumeDeltaKilograms.ShouldBe(0m);
        comparison.CurrentWorkingSets.ShouldBe(1);
        WorkoutComparisonNarrator.Describe(comparison)
            .ShouldBe("Compared with your previous session: the same working volume and the same number of working sets.");
    }

    [Fact]
    public void The_summary_carries_the_comparison_so_the_screen_never_has_to_invent_one()
    {
        var planDay = Guid.CreateVersion7();
        var current = Session(Now, sets: [(100m, 5)], planDayId: planDay, planDayName: "Upper A");
        var previous = Session(Now.AddDays(-7), sets: [(95m, 5)], planDayId: planDay, planDayName: "Upper A");

        var summary = WorkoutSummaryCalculator.Calculate(
            current,
            new Dictionary<Guid, Exercise> { [Bench] = new() { Id = Bench, Name = "Bench press", PrimaryMuscle = "Chest" } },
            Now,
            previousSessions: [previous]);

        summary.Comparison.ShouldNotBeNull();
        summary.Comparison.Basis.ShouldBe(WorkoutComparisonBasis.SamePlanDay);
        summary.Comparison.VolumeDeltaKilograms.ShouldBe(25m);
    }

    [Fact]
    public void A_summary_built_without_earlier_sessions_reports_nothing_to_compare()
    {
        var session = Session(Now, sets: [(100m, 5)]);

        var summary = WorkoutSummaryCalculator.Calculate(session, new Dictionary<Guid, Exercise>(), Now);

        summary.Comparison!.Basis.ShouldBe(WorkoutComparisonBasis.NoPrevious);
    }

    private static WorkoutSession Session(
        DateTimeOffset finishedAt,
        (decimal Load, int Reps)[] sets,
        Guid? planDayId = null,
        string? planDayName = null,
        bool isCompleted = true)
    {
        var session = new WorkoutSession
        {
            Id = Guid.CreateVersion7(),
            UserProfileId = Owner,
            StartedUtc = finishedAt.AddHours(-1),
            CompletedUtc = isCompleted ? finishedAt : null,
            Title = planDayName ?? "Workout",
            PlanDayId = planDayId,
            PlanDayName = planDayName
        };

        foreach (var set in sets)
        {
            session.Sets.Add(Set(session.Id, set.Load, set.Reps, isWarmUp: false, at: finishedAt));
        }

        return session;
    }

    private static SetEntry Set(Guid sessionId, decimal load, int reps, bool isWarmUp, DateTimeOffset at)
        => new()
        {
            UserProfileId = Owner,
            WorkoutSessionId = sessionId,
            ExerciseId = Bench,
            Ordinal = 1,
            Load = Mass.FromKilograms(load),
            Repetitions = reps,
            IsWarmUp = isWarmUp,
            CompletedUtc = at
        };
}
