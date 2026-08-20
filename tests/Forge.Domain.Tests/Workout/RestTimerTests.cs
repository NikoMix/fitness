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

    private sealed class TestClock(DateTimeOffset utcNow) : IWorkoutClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan duration) => UtcNow += duration;
    }
}
