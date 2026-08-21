using Forge.Core.Abstractions.Security;
using Shouldly;

namespace Forge.Core.Tests.Security;

/// <summary>
/// Exercises the lock state transitions, including the ones that must never happen.
/// </summary>
public sealed class AppLockStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_machine_is_not_locked()
    {
        new AppLockStateMachine().State.ShouldBe(AppLockState.Disabled);
    }

    [Fact]
    public void Launching_with_the_lock_on_locks()
    {
        var machine = new AppLockStateMachine();

        var decision = EnterForeground(machine);

        decision.ShouldBe(AppLockDecision.Lock);
        machine.State.ShouldBe(AppLockState.Locked);
    }

    [Fact]
    public void Launching_with_the_lock_off_leaves_it_disabled()
    {
        var machine = new AppLockStateMachine();

        EnterForeground(machine, isEnabled: false).ShouldBe(AppLockDecision.Unlock);

        machine.State.ShouldBe(AppLockState.Disabled);
    }

    [Fact]
    public void A_foreground_event_can_never_clear_an_existing_lock()
    {
        // The attack this closes: the platform prompt pauses and resumes the host activity, so
        // cancelling it produces a foreground event with no recorded absence. If that were
        // allowed to unlock, tapping "Cancel" on the fingerprint dialog would open the app.
        var machine = new AppLockStateMachine();
        EnterForeground(machine);
        machine.State.ShouldBe(AppLockState.Locked);

        EnterForeground(machine).ShouldBe(AppLockDecision.Unlock);

        machine.State.ShouldBe(AppLockState.Locked);
    }

    [Fact]
    public void A_short_background_trip_while_locked_leaves_it_locked()
    {
        var machine = new AppLockStateMachine();
        EnterForeground(machine);
        machine.RecordBackgrounded(Now);

        EnterForeground(machine, now: Now + TimeSpan.FromSeconds(2)).ShouldBe(AppLockDecision.Unlock);

        machine.State.ShouldBe(AppLockState.Locked);
    }

    [Fact]
    public void Only_authentication_can_clear_a_lock()
    {
        var machine = new AppLockStateMachine();
        EnterForeground(machine);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            EnterForeground(machine);
            machine.ApplyAuthentication(AppLockAuthenticationResult.Cancelled);
            machine.State.ShouldBe(AppLockState.Locked);
        }

        machine.ApplyAuthentication(AppLockAuthenticationResult.Success).ShouldBeTrue();
        machine.State.ShouldBe(AppLockState.Unlocked);
    }

    [Fact]
    public void A_successful_authentication_unlocks()
    {
        var machine = new AppLockStateMachine();
        EnterForeground(machine);

        machine.ApplyAuthentication(AppLockAuthenticationResult.Success).ShouldBeTrue();

        machine.State.ShouldBe(AppLockState.Unlocked);
    }

    [Theory]
    [InlineData(AppLockAuthenticationOutcome.Cancelled)]
    [InlineData(AppLockAuthenticationOutcome.Failed)]
    [InlineData(AppLockAuthenticationOutcome.TemporarilyLockedOut)]
    [InlineData(AppLockAuthenticationOutcome.Unavailable)]
    public void A_failed_authentication_never_grants_access(AppLockAuthenticationOutcome outcome)
    {
        var machine = new AppLockStateMachine();
        EnterForeground(machine);

        machine.ApplyAuthentication(new AppLockAuthenticationResult(outcome, "nope")).ShouldBeFalse();

        machine.State.ShouldBe(AppLockState.Locked);
    }

    [Fact]
    public void Repeated_failures_change_nothing_except_staying_locked()
    {
        // There is deliberately no attempt counter, no escalating delay and no wipe. The
        // platform already rate-limits its own sensor, and anything Forge added on top could
        // only ever punish the person who owns the phone.
        var machine = new AppLockStateMachine();
        EnterForeground(machine);

        for (var attempt = 0; attempt < 25; attempt++)
        {
            machine.ApplyAuthentication(AppLockAuthenticationResult.Failed("no")).ShouldBeFalse();
        }

        machine.State.ShouldBe(AppLockState.Locked);
        machine.ApplyAuthentication(AppLockAuthenticationResult.Success).ShouldBeTrue();
        machine.State.ShouldBe(AppLockState.Unlocked);
    }

    [Fact]
    public void Unlocking_then_returning_without_backgrounding_stays_unlocked()
    {
        // The prompt itself pauses and resumes the host activity, so this exact sequence
        // happens on every successful unlock. Getting it wrong is an inescapable lock loop.
        var machine = new AppLockStateMachine();
        EnterForeground(machine);
        machine.ApplyAuthentication(AppLockAuthenticationResult.Success);

        EnterForeground(machine).ShouldBe(AppLockDecision.Unlock);

        machine.State.ShouldBe(AppLockState.Unlocked);
    }

    [Fact]
    public void Returning_after_a_long_absence_locks_again()
    {
        var machine = new AppLockStateMachine();
        EnterForeground(machine);
        machine.ApplyAuthentication(AppLockAuthenticationResult.Success);

        machine.RecordBackgrounded(Now);

        EnterForeground(machine, now: Now + TimeSpan.FromMinutes(5)).ShouldBe(AppLockDecision.Lock);
        machine.State.ShouldBe(AppLockState.Locked);
    }

    [Fact]
    public void Only_the_first_backgrounding_of_a_session_counts()
    {
        // Otherwise a burst of background and foreground events would keep resetting the timer
        // and quietly extend the grace period well past what the user chose.
        var machine = new AppLockStateMachine();
        EnterForeground(machine);
        machine.ApplyAuthentication(AppLockAuthenticationResult.Success);

        machine.RecordBackgrounded(Now);
        machine.RecordBackgrounded(Now + TimeSpan.FromMinutes(4));

        machine.BackgroundedAt.ShouldBe(Now);
    }

    [Fact]
    public void Judging_an_absence_clears_it()
    {
        var machine = new AppLockStateMachine();
        EnterForeground(machine);
        machine.ApplyAuthentication(AppLockAuthenticationResult.Success);
        machine.RecordBackgrounded(Now);

        EnterForeground(machine, now: Now + TimeSpan.FromSeconds(5));

        machine.BackgroundedAt.ShouldBeNull();
    }

    [Fact]
    public void An_unavailable_device_ends_up_unlocked_rather_than_stuck()
    {
        var machine = new AppLockStateMachine();

        var decision = EnterForeground(machine, capability: AppLockCapability.Unavailable);

        decision.ShouldBe(AppLockDecision.DisableBecauseUnavailable);
        machine.State.ShouldBe(AppLockState.Disabled);
    }

    [Fact]
    public void Enabling_does_not_lock_the_user_out_of_the_screen_they_are_standing_on()
    {
        var machine = new AppLockStateMachine();

        machine.Enable();

        machine.State.ShouldBe(AppLockState.Unlocked);
    }

    [Fact]
    public void Disabling_unlocks()
    {
        var machine = new AppLockStateMachine();
        EnterForeground(machine);

        machine.Disable();

        machine.State.ShouldBe(AppLockState.Disabled);
    }

    [Fact]
    public void A_workout_survives_a_background_that_would_otherwise_lock()
    {
        var machine = new AppLockStateMachine();
        EnterForeground(machine);
        machine.ApplyAuthentication(AppLockAuthenticationResult.Success);
        machine.RecordBackgrounded(Now);

        var decision = EnterForeground(
            machine,
            now: Now + TimeSpan.FromMinutes(6),
            isActivityInProgress: true);

        decision.ShouldBe(AppLockDecision.Unlock);
        machine.State.ShouldBe(AppLockState.Unlocked);
    }

    private static AppLockDecision EnterForeground(
        AppLockStateMachine machine,
        bool isEnabled = true,
        AppLockCapability capability = AppLockCapability.Biometric,
        DateTimeOffset? now = null,
        TimeSpan? grace = null,
        bool isActivityInProgress = false) =>
        machine.EnterForeground(
            isEnabled,
            capability,
            now ?? Now,
            grace ?? TimeSpan.FromMinutes(1),
            relaxDuringActivity: true,
            isActivityInProgress);
}
