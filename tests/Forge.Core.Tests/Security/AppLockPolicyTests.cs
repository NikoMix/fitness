using Forge.Core.Abstractions.Security;
using Shouldly;

namespace Forge.Core.Tests.Security;

/// <summary>
/// Exercises the rules that decide whether a user sees their own data.
/// </summary>
/// <remarks>
/// The policy takes the current instant as an argument rather than reading a clock, which is
/// what makes these cases expressible at all: a grace period expiring, a workout running long,
/// and a device clock moved backwards are otherwise untestable without waiting or cheating.
/// </remarks>
public sealed class AppLockPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 14, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_disabled_lock_never_locks()
    {
        var decision = AppLockPolicy.Decide(Evaluation(isEnabled: false, trigger: AppLockTrigger.Launched));

        decision.ShouldBe(AppLockDecision.Unlock);
    }

    [Fact]
    public void Launching_locks_when_the_lock_is_on()
    {
        var decision = AppLockPolicy.Decide(Evaluation(trigger: AppLockTrigger.Launched));

        decision.ShouldBe(AppLockDecision.Lock);
    }

    [Fact]
    public void A_device_that_can_no_longer_authenticate_disables_the_lock_rather_than_trapping_the_user()
    {
        var decision = AppLockPolicy.Decide(Evaluation(
            capability: AppLockCapability.Unavailable,
            trigger: AppLockTrigger.Launched));

        decision.ShouldBe(AppLockDecision.DisableBecauseUnavailable);
    }

    [Fact]
    public void A_temporary_capability_failure_keeps_the_lock_on()
    {
        // The distinction matters: disabling a security control the user asked for, because a
        // sensor was busy for a moment, is a silent downgrade they never consented to.
        var decision = AppLockPolicy.Decide(Evaluation(
            capability: AppLockCapability.TemporarilyUnavailable,
            trigger: AppLockTrigger.Launched));

        decision.ShouldBe(AppLockDecision.Lock);
    }

    [Fact]
    public void A_device_with_no_biometric_but_a_passcode_is_a_supported_configuration()
    {
        var decision = AppLockPolicy.Decide(Evaluation(
            capability: AppLockCapability.DeviceCredentialOnly,
            trigger: AppLockTrigger.Launched));

        decision.ShouldBe(AppLockDecision.Lock);
    }

    [Fact]
    public void Returning_without_a_recorded_absence_does_not_lock()
    {
        // This is the loop guard. The platform prompt pauses and resumes the host activity, so
        // the foreground event that follows a successful unlock arrives with no backgrounding
        // recorded. Locking on it would re-lock the user the instant they got in.
        var decision = AppLockPolicy.Decide(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            backgroundedAt: null));

        decision.ShouldBe(AppLockDecision.Unlock);
    }

    [Fact]
    public void Returning_inside_the_grace_period_does_not_lock()
    {
        var decision = AppLockPolicy.Decide(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            backgroundedAt: Now - TimeSpan.FromSeconds(30),
            grace: TimeSpan.FromMinutes(1)));

        decision.ShouldBe(AppLockDecision.Unlock);
    }

    [Fact]
    public void Returning_after_the_grace_period_locks()
    {
        var decision = AppLockPolicy.Decide(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            backgroundedAt: Now - TimeSpan.FromMinutes(2),
            grace: TimeSpan.FromMinutes(1)));

        decision.ShouldBe(AppLockDecision.Lock);
    }

    [Fact]
    public void The_grace_boundary_locks_rather_than_letting_the_user_through()
    {
        var decision = AppLockPolicy.Decide(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            backgroundedAt: Now - TimeSpan.FromMinutes(1),
            grace: TimeSpan.FromMinutes(1)));

        decision.ShouldBe(AppLockDecision.Lock);
    }

    [Fact]
    public void A_clock_moved_backwards_locks_instead_of_granting_unlimited_grace()
    {
        var decision = AppLockPolicy.Decide(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            backgroundedAt: Now + TimeSpan.FromHours(1),
            grace: TimeSpan.FromMinutes(1)));

        decision.ShouldBe(AppLockDecision.Lock);
    }

    [Fact]
    public void Immediate_locking_still_locks_on_the_shortest_possible_absence()
    {
        var decision = AppLockPolicy.Decide(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            backgroundedAt: Now,
            grace: TimeSpan.Zero));

        decision.ShouldBe(AppLockDecision.Lock);
    }

    [Fact]
    public void A_screen_off_rest_between_sets_does_not_lock_the_user_out_mid_workout()
    {
        // Five minutes away with a one-minute grace: locked normally, not locked during a
        // workout. This is the case the whole workout allowance exists for.
        var away = TimeSpan.FromMinutes(5);

        AppLockPolicy.Decide(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            backgroundedAt: Now - away,
            grace: TimeSpan.FromMinutes(1))).ShouldBe(AppLockDecision.Lock);

        AppLockPolicy.Decide(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            backgroundedAt: Now - away,
            grace: TimeSpan.FromMinutes(1),
            isActivityInProgress: true)).ShouldBe(AppLockDecision.Unlock);
    }

    [Fact]
    public void Lock_immediately_is_still_relaxed_during_a_workout_when_the_allowance_is_on()
    {
        var decision = AppLockPolicy.Decide(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            backgroundedAt: Now - TimeSpan.FromMinutes(2),
            grace: TimeSpan.Zero,
            isActivityInProgress: true));

        decision.ShouldBe(AppLockDecision.Unlock);
    }

    [Fact]
    public void An_abandoned_workout_locks_once_the_activity_floor_has_passed()
    {
        var decision = AppLockPolicy.Decide(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            backgroundedAt: Now - AppLockPolicy.ActivityGraceFloor - TimeSpan.FromSeconds(1),
            grace: TimeSpan.FromMinutes(1),
            isActivityInProgress: true));

        decision.ShouldBe(AppLockDecision.Lock);
    }

    [Fact]
    public void Turning_the_workout_allowance_off_honours_the_chosen_grace_during_a_workout()
    {
        var decision = AppLockPolicy.Decide(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            backgroundedAt: Now - TimeSpan.FromMinutes(5),
            grace: TimeSpan.FromMinutes(1),
            relaxDuringActivity: false,
            isActivityInProgress: true));

        decision.ShouldBe(AppLockDecision.Lock);
    }

    [Fact]
    public void The_workout_allowance_only_ever_lengthens_the_chosen_grace()
    {
        var longerThanTheFloor = AppLockPolicy.ActivityGraceFloor + TimeSpan.FromMinutes(30);

        var effective = AppLockPolicy.EffectiveGrace(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            grace: longerThanTheFloor,
            isActivityInProgress: true));

        effective.ShouldBe(longerThanTheFloor);
    }

    [Fact]
    public void The_effective_grace_is_unchanged_when_no_activity_is_running()
    {
        var effective = AppLockPolicy.EffectiveGrace(Evaluation(
            trigger: AppLockTrigger.Foregrounded,
            grace: TimeSpan.FromMinutes(1)));

        effective.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Every_decision_path_is_a_foreground_event()
    {
        // A structural assertion rather than a behavioural one. The policy has no trigger that
        // means "while the user is looking at the screen", so no configuration of it can put a
        // lock screen over a live session.
        Enum.GetValues<AppLockTrigger>()
            .ShouldBe([AppLockTrigger.Launched, AppLockTrigger.Foregrounded], ignoreOrder: true);
    }

    private static AppLockEvaluation Evaluation(
        bool isEnabled = true,
        AppLockCapability capability = AppLockCapability.Biometric,
        AppLockTrigger trigger = AppLockTrigger.Foregrounded,
        DateTimeOffset? backgroundedAt = null,
        TimeSpan? grace = null,
        bool relaxDuringActivity = true,
        bool isActivityInProgress = false) =>
        new(
            isEnabled,
            capability,
            trigger,
            Now,
            backgroundedAt,
            grace ?? TimeSpan.FromMinutes(1),
            relaxDuringActivity,
            isActivityInProgress);
}
