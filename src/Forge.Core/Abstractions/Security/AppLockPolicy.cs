namespace Forge.Core.Abstractions.Security;

/// <summary>Why the app lock is being asked to make a decision.</summary>
/// <remarks>
/// Both members describe the app arriving in the foreground. That is not an oversight: the lock
/// is only ever evaluated on the way in, never on a timer, so it is structurally impossible for
/// a lock screen to appear over a screen the user is currently looking at.
/// </remarks>
public enum AppLockTrigger
{
    /// <summary>The process has just started and is showing its first frame.</summary>
    Launched,

    /// <summary>The app has come back to the foreground within an existing process.</summary>
    Foregrounded,
}

/// <summary>What the app lock should do.</summary>
public enum AppLockDecision
{
    /// <summary>Show Forge's content.</summary>
    Unlock,

    /// <summary>Withhold content until the user authenticates.</summary>
    Lock,

    /// <summary>
    /// Turn the lock off and show content, because this device can no longer authenticate
    /// anyone and keeping the lock on would strand the owner outside their own data.
    /// </summary>
    DisableBecauseUnavailable,
}

/// <summary>Everything the lock decision depends on, with the clock supplied by the caller.</summary>
/// <param name="IsEnabled">Whether the user turned the lock on.</param>
/// <param name="Capability">What the device can do about authentication right now.</param>
/// <param name="Trigger">Why the decision is being made.</param>
/// <param name="Now">The current instant, passed in so the decision is deterministic.</param>
/// <param name="BackgroundedAt">When the app last went to the background, if it has.</param>
/// <param name="GraceDuration">The user's configured background grace period.</param>
/// <param name="RelaxDuringActivity">Whether the grace period is extended during a workout.</param>
/// <param name="IsActivityInProgress">Whether a workout or similar activity is running.</param>
public sealed record AppLockEvaluation(
    bool IsEnabled,
    AppLockCapability Capability,
    AppLockTrigger Trigger,
    DateTimeOffset Now,
    DateTimeOffset? BackgroundedAt,
    TimeSpan GraceDuration,
    bool RelaxDuringActivity,
    bool IsActivityInProgress);

/// <summary>
/// Decides whether Forge should be locked. Pure, deterministic and free of any clock.
/// </summary>
/// <remarks>
/// <para>
/// Kept as a static function over an explicit input so the rules can be exercised exhaustively
/// in ordinary unit tests. A lock that fires at the wrong moment is worse than no lock at all -
/// the user turns it off and loses the protection entirely - so these rules are worth more test
/// coverage than the platform prompt they eventually drive.
/// </para>
/// </remarks>
public static class AppLockPolicy
{
    /// <summary>
    /// The shortest grace period the lock will use while a workout is running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Fifteen minutes is chosen to sit above the longest thing a lifter plausibly does with
    /// their phone mid-session - a screen-off rest between heavy sets, a phone call, a walk to a
    /// different rack - and below the point at which a session has obviously been abandoned. A
    /// phone left face down for a quarter of an hour is a phone whose owner has left, and by
    /// then locking is the right answer again.
    /// </para>
    /// <para>
    /// This floor is applied only when <see cref="IAppLockSettings.RelaxDuringActivity"/> is on,
    /// and it only ever lengthens the user's chosen grace period, never shortens it.
    /// </para>
    /// </remarks>
    public static TimeSpan ActivityGraceFloor { get; } = TimeSpan.FromMinutes(15);

    /// <summary>Decides what the lock should do for one foreground event.</summary>
    /// <param name="evaluation">The inputs, including the current instant.</param>
    /// <returns>The action the caller should take.</returns>
    public static AppLockDecision Decide(AppLockEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        if (!evaluation.IsEnabled)
        {
            return AppLockDecision.Unlock;
        }

        // The no-lockout rule, and the reason it is checked before anything else. If the user
        // has removed their device passcode, or is on a platform too old for the prompt, no
        // authentication will ever succeed. Staying locked would turn Forge into a vault with
        // the key thrown away, holding training history and body measurements that exist
        // nowhere else. Unlocking and switching the setting off is the only honest answer.
        if (evaluation.Capability == AppLockCapability.Unavailable)
        {
            return AppLockDecision.DisableBecauseUnavailable;
        }

        if (evaluation.Trigger == AppLockTrigger.Launched)
        {
            return AppLockDecision.Lock;
        }

        // No recorded background period means nothing happened that the lock is meant to cover.
        //
        // This case is reached more often than it looks. The platform authentication prompt
        // pauses and resumes the host activity, so the foreground event that follows a
        // successful unlock arrives here. Locking on it would re-lock the user immediately
        // after they unlocked, in a loop with no way out.
        if (evaluation.BackgroundedAt is not { } backgroundedAt)
        {
            return AppLockDecision.Unlock;
        }

        var awayFor = evaluation.Now - backgroundedAt;

        // A negative interval means the wall clock moved backwards while Forge was away.
        // Treating that as "no time has passed" would make the lock trivially avoidable by
        // changing the device clock, so it is treated as a long absence instead.
        if (awayFor < TimeSpan.Zero)
        {
            return AppLockDecision.Lock;
        }

        return awayFor >= EffectiveGrace(evaluation) ? AppLockDecision.Lock : AppLockDecision.Unlock;
    }

    /// <summary>
    /// The grace period actually in force, after the workout allowance is applied.
    /// </summary>
    /// <param name="evaluation">The inputs.</param>
    /// <returns>The configured grace period, lengthened during a workout when that is enabled.</returns>
    public static TimeSpan EffectiveGrace(AppLockEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        return evaluation is { RelaxDuringActivity: true, IsActivityInProgress: true }
            ? Max(evaluation.GraceDuration, ActivityGraceFloor)
            : evaluation.GraceDuration;
    }

    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first >= second ? first : second;
}
