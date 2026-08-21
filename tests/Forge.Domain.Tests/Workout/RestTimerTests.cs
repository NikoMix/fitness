using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

public sealed class RestTimerTests
{
    [Fact]
    public void Remaining_time_is_computed_from_wall_clock_after_suspend()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var timer = RestTimer.Start(TimeSpan.FromSeconds(90), clock, notificationId: 42);

        clock.Advance(TimeSpan.FromMinutes(5));

        timer.Remaining(clock.UtcNow).ShouldBe(TimeSpan.Zero);
        timer.Progress(clock.UtcNow).ShouldBe(1d);
    }

    [Fact]
    public void Adjustment_changes_absolute_end_time_not_a_tick_counter()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var timer = RestTimer.Start(TimeSpan.FromSeconds(60), clock, notificationId: 7);

        clock.Advance(TimeSpan.FromSeconds(20));
        timer.Adjust(TimeSpan.FromSeconds(30), clock.UtcNow);

        timer.Remaining(clock.UtcNow).ShouldBe(TimeSpan.FromSeconds(70));
    }

    [Fact]
    public void Ending_rest_early_zeroes_remaining_time_and_marks_state()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var timer = RestTimer.Start(TimeSpan.FromSeconds(60), clock, notificationId: 9);

        clock.Advance(TimeSpan.FromSeconds(10));
        timer.EndEarly(clock.UtcNow);

        timer.Remaining(clock.UtcNow).ShouldBe(TimeSpan.Zero);
        timer.EndedEarly.ShouldBeTrue();
        timer.EndedUtc.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void Remaining_time_survives_a_long_background_gap_without_drifting()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var timer = RestTimer.Start(TimeSpan.FromMinutes(3), clock, notificationId: 11);

        // The app is suspended for 100 seconds; no tick callbacks run while it is away.
        clock.Advance(TimeSpan.FromSeconds(100));

        timer.Remaining(clock.UtcNow).ShouldBe(TimeSpan.FromSeconds(80));
        timer.IsRunning(clock.UtcNow).ShouldBeTrue();
        timer.HasElapsed(clock.UtcNow).ShouldBeFalse();
    }

    [Fact]
    public void Reconciling_the_same_timer_twice_gives_the_same_answer()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var timer = RestTimer.Start(TimeSpan.FromSeconds(90), clock, notificationId: 12);

        clock.Advance(TimeSpan.FromSeconds(30));
        var first = timer.Remaining(clock.UtcNow);
        var second = timer.Remaining(clock.UtcNow);

        first.ShouldBe(second);
        first.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void A_timer_that_ran_out_while_backgrounded_reports_completion_not_skipping()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var timer = RestTimer.Start(TimeSpan.FromSeconds(60), clock, notificationId: 13);

        clock.Advance(TimeSpan.FromMinutes(30));

        timer.HasElapsed(clock.UtcNow).ShouldBeTrue();
        timer.EndedEarly.ShouldBeFalse();
        timer.IsRunning(clock.UtcNow).ShouldBeFalse();
        timer.Progress(clock.UtcNow).ShouldBe(1d);
    }

    [Fact]
    public void Removing_more_time_than_remains_ends_rest_now_rather_than_in_the_past()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var timer = RestTimer.Start(TimeSpan.FromSeconds(60), clock, notificationId: 14);

        clock.Advance(TimeSpan.FromSeconds(10));
        timer.Adjust(TimeSpan.FromSeconds(-300), clock.UtcNow);

        timer.Remaining(clock.UtcNow).ShouldBe(TimeSpan.Zero);
        timer.TargetEndUtc.ShouldBe(clock.UtcNow);
    }

    [Fact]
    public void A_skipped_timer_ignores_further_adjustment()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var timer = RestTimer.Start(TimeSpan.FromSeconds(60), clock, notificationId: 15);

        timer.EndEarly(clock.UtcNow);
        timer.Adjust(TimeSpan.FromSeconds(60), clock.UtcNow);

        timer.Remaining(clock.UtcNow).ShouldBe(TimeSpan.Zero);
        timer.HasElapsed(clock.UtcNow).ShouldBeFalse();
    }

    [Fact]
    public void Progress_before_any_time_passes_is_zero()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));
        var timer = RestTimer.Start(TimeSpan.FromSeconds(60), clock, notificationId: 16);

        timer.Progress(clock.UtcNow).ShouldBe(0d);
    }

    [Fact]
    public void A_non_positive_duration_is_rejected_at_construction()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 8, 20, 10, 0, 0, TimeSpan.Zero));

        Should.Throw<ArgumentOutOfRangeException>(() => RestTimer.Start(TimeSpan.Zero, clock, notificationId: 17));
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IWorkoutClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan duration) => UtcNow += duration;
    }
}
