using Forge.Domain.Planning;
using Shouldly;

namespace Forge.Domain.Tests.Planning;

public sealed class PlanSchedulerTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();

    [Fact]
    public void Fixed_day_schedule_uses_named_weekdays()
    {
        var weekStart = new DateOnly(2026, 8, 17);
        var plan = new TrainingPlan { UserProfileId = Owner, Name = "Fixed", ScheduleMode = PlanScheduleMode.FixedDays };
        plan.Days.Add(new PlanDay { UserProfileId = Owner, Name = "Monday", ScheduledDay = DayOfWeek.Monday, Ordinal = 0 });
        plan.Days.Add(new PlanDay { UserProfileId = Owner, Name = "Wednesday", ScheduledDay = DayOfWeek.Wednesday, Ordinal = 1 });
        plan.Days.Add(new PlanDay { UserProfileId = Owner, Name = "Friday", ScheduledDay = DayOfWeek.Friday, Ordinal = 2 });

        var schedule = PlanScheduler.Schedule(plan, weekStart, 1);

        schedule.Select(session => session.Date).ShouldBe([new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 19), new DateOnly(2026, 8, 21)]);
    }

    [Fact]
    public void Flexible_schedule_spreads_sessions_across_the_week()
    {
        var plan = new TrainingPlan { UserProfileId = Owner, Name = "Flexible", ScheduleMode = PlanScheduleMode.Flexible, TargetSessionsPerWeek = 3 };
        plan.Days.Add(new PlanDay { UserProfileId = Owner, Name = "A", Ordinal = 0 });
        plan.Days.Add(new PlanDay { UserProfileId = Owner, Name = "B", Ordinal = 1 });
        plan.Days.Add(new PlanDay { UserProfileId = Owner, Name = "C", Ordinal = 2 });

        var schedule = PlanScheduler.Schedule(plan, new DateOnly(2026, 8, 17), 1);

        schedule.Select(session => session.Date).ShouldBe([new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 19), new DateOnly(2026, 8, 22)]);
    }

    [Fact]
    public void Missed_session_shifts_plan_forward_without_removing_work()
    {
        var plan = new TrainingPlan { UserProfileId = Owner, Name = "Flexible", ScheduleMode = PlanScheduleMode.Flexible, TargetSessionsPerWeek = 3 };
        plan.Days.Add(new PlanDay { UserProfileId = Owner, Name = "A", Ordinal = 0 });
        plan.Days.Add(new PlanDay { UserProfileId = Owner, Name = "B", Ordinal = 1 });
        plan.Days.Add(new PlanDay { UserProfileId = Owner, Name = "C", Ordinal = 2 });
        var schedule = PlanScheduler.Schedule(plan, new DateOnly(2026, 8, 17), 1);

        var shifted = PlanScheduler.ShiftForMissedSession(schedule, missedSequence: 1, nextAvailableDate: new DateOnly(2026, 8, 20));

        shifted.Count.ShouldBe(schedule.Count);
        shifted[0].Date.ShouldBe(new DateOnly(2026, 8, 17));
        shifted[1].Date.ShouldBe(new DateOnly(2026, 8, 20));
        shifted[2].Date.ShouldBe(new DateOnly(2026, 8, 23));
        shifted[1].WasShifted.ShouldBeTrue();
        shifted[2].WasShifted.ShouldBeTrue();
    }
}
