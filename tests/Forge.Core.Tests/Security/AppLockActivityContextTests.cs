using Forge.Core.Abstractions.Security;
using Shouldly;

namespace Forge.Core.Tests.Security;

/// <summary>Exercises the counter that tells the lock a workout is running.</summary>
public sealed class AppLockActivityContextTests
{
    [Fact]
    public void Nothing_is_in_progress_to_begin_with()
    {
        new AppLockActivityContext().IsActivityInProgress.ShouldBeFalse();
    }

    [Fact]
    public void A_scope_marks_an_activity_for_its_lifetime()
    {
        var context = new AppLockActivityContext();

        using (context.BeginActivity())
        {
            context.IsActivityInProgress.ShouldBeTrue();
        }

        context.IsActivityInProgress.ShouldBeFalse();
    }

    [Fact]
    public void Nested_scopes_do_not_end_the_outer_activity_early()
    {
        // A rest timer inside a workout is the real case. If the inner scope ended the workout,
        // the lock would start firing between sets - the exact failure the allowance exists to
        // prevent.
        var context = new AppLockActivityContext();

        var workout = context.BeginActivity();
        var restTimer = context.BeginActivity();

        restTimer.Dispose();
        context.IsActivityInProgress.ShouldBeTrue();

        workout.Dispose();
        context.IsActivityInProgress.ShouldBeFalse();
    }

    [Fact]
    public void Disposing_a_scope_twice_does_not_leave_the_lock_permanently_relaxed()
    {
        var context = new AppLockActivityContext();
        var outer = context.BeginActivity();
        var inner = context.BeginActivity();

        inner.Dispose();
        inner.Dispose();

        context.IsActivityInProgress.ShouldBeTrue();

        outer.Dispose();
        context.IsActivityInProgress.ShouldBeFalse();
    }

    [Fact]
    public async Task Scopes_opened_and_closed_from_several_threads_settle_correctly()
    {
        var context = new AppLockActivityContext();

        await Parallel.ForAsync(0, 200, TestContext.Current.CancellationToken, async (_, token) =>
        {
            using (context.BeginActivity())
            {
                await Task.Yield();
            }
        }).ConfigureAwait(true);

        context.IsActivityInProgress.ShouldBeFalse();
    }
}
