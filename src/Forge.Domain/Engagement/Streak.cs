using Forge.Domain.Common;
using Forge.Domain.Profile;

namespace Forge.Domain.Engagement;

/// <summary>Why training was deliberately interrupted.</summary>
/// <remarks>
/// Every member describes either correct training or something outside the person's control.
/// None of them is a lapse, and nothing in Forge treats them as one.
/// </remarks>
public enum TrainingInterruption
{
    /// <summary>A prescribed lighter or empty week inside a training block.</summary>
    Deload = 0,

    /// <summary>Illness. Training through it delays recovery and risks worse.</summary>
    Illness = 1,

    /// <summary>Injury, whether or not it is being rehabilitated.</summary>
    Injury = 2,

    /// <summary>Travel, work, caring, or anything else that took the week.</summary>
    LifeHappened = 3,
}

/// <summary>
/// A stretch of days the user asked Forge to leave alone.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the app has an answer other than silence when somebody is ill or deloading.
/// Without it the only signal Forge has is the absence of sessions, and an app that can only see
/// absence will describe recovery in the same words it uses for drift.
/// </para>
/// <para>
/// <see cref="End"/> is nullable on purpose. Nobody knows on the first day of flu when they will
/// train again, and demanding an end date would either produce a guess the app then treats as
/// fact, or discourage recording the period at all.
/// </para>
/// </remarks>
/// <param name="Start">First day covered, in the user's local calendar.</param>
/// <param name="End">Last day covered, or <see langword="null"/> while it is still running.</param>
/// <param name="Reason">What kind of interruption this is.</param>
public sealed record ProtectedPeriod(DateOnly Start, DateOnly? End, TrainingInterruption Reason)
{
    /// <summary>Whether this period is still running.</summary>
    public bool IsOpenEnded => End is null;

    /// <summary>Whether a date falls inside this period.</summary>
    /// <param name="date">The local date to test.</param>
    /// <returns><see langword="true"/> when the date is covered.</returns>
    public bool Covers(DateOnly date) => date >= Start && (End is null || date <= End);

    /// <summary>How the reason is worded to the user.</summary>
    public string ReasonLabel => Reason switch
    {
        TrainingInterruption.Deload => "planned deload",
        TrainingInterruption.Illness => "illness",
        TrainingInterruption.Injury => "injury",
        _ => "time away",
    };
}

/// <summary>
/// One profile's engagement record: whether badges are wanted, and which stretches of time must
/// not be read as missed training.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This type deliberately stores no counter.</strong> It previously held
/// <c>CurrentDays</c>, <c>BestDays</c>, <c>FreezesRemaining</c>, <c>LastCountedDate</c> and a
/// per-day history — the standard daily-streak mechanic. That mechanic was removed rather than
/// tuned, for two reasons.
/// </para>
/// <para>
/// The first is ethical. A number that falls when you rest is, in a training app, an instruction
/// to train while ill, injured or deloading in order to protect it. Rest days are not gaps in
/// training; they are when the adaptation happens. "Freezes" do not fix this: a limited supply of
/// forgiveness still frames recovery as consuming a scarce resource, and it still runs out on the
/// person who needed it most.
/// </para>
/// <para>
/// The second is that a stored counter can be wrong. Anything derived from sessions should be
/// recomputed from sessions every time it is shown, so it cannot drift from the logs and cannot be
/// repaired, gifted or fabricated. What the Streaks screen shows is therefore derived by
/// <see cref="TrainingRhythmAnalyzer"/> from real workout rows. This entity holds only the two
/// things that genuinely are state: the user's preference, and what they told us about their
/// circumstances.
/// </para>
/// <para>
/// The type keeps the name <c>Streak</c> because it is a persisted entity named by
/// <c>ProfileDataAreas</c> and by the reminder service; renaming it is an integration-time concern
/// rather than a behavioural one. See <c>docs/design/engagement-ethics.md</c>.
/// </para>
/// </remarks>
public sealed class Streak : Entity, IProfileOwned
{
    /// <summary>The profile this record belongs to.</summary>
    public Guid UserProfileId { get; init; }

    /// <summary>
    /// Whether the user wants badges and rhythm framing at all.
    /// </summary>
    /// <remarks>
    /// Turning this off changes nothing about logging, plans, nutrition or progress. Engagement
    /// features are decoration over the real record, and anything that broke when they were
    /// switched off would prove they were not.
    /// </remarks>
    public bool GamificationEnabled { get; private set; } = true;

    /// <summary>
    /// Stretches of time that must not be read as missed training.
    /// </summary>
    /// <remarks>
    /// Mapped as a single JSON column, replacing the per-day history the daily streak needed. The
    /// mutable list mirrors how the previous history column was mapped, so EF can rehydrate it
    /// through the same value converter.
    /// </remarks>
    public List<ProtectedPeriod> ProtectedPeriods { get; private set; } = [];

    /// <summary>Turns badges and rhythm framing on or off.</summary>
    /// <param name="enabled">Whether engagement features should be shown.</param>
    public void SetGamificationEnabled(bool enabled)
    {
        GamificationEnabled = enabled;
        ModifiedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Records a protected period, or extends the running one when it has the same reason.
    /// </summary>
    /// <remarks>
    /// Merging rather than appending keeps this idempotent: marking illness on three consecutive
    /// days produces one period, not three, so a screen that re-marks on every open cannot grow
    /// the row without bound.
    /// </remarks>
    /// <param name="period">The period to record.</param>
    /// <exception cref="ArgumentNullException"><paramref name="period"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The period ends before it starts.</exception>
    public void Protect(ProtectedPeriod period)
    {
        ArgumentNullException.ThrowIfNull(period);

        if (period.End is { } end && end < period.Start)
        {
            throw new ArgumentException("A protected period cannot end before it starts.", nameof(period));
        }

        var running = ProtectedPeriods.FindIndex(existing => existing.IsOpenEnded && existing.Reason == period.Reason);
        if (running >= 0)
        {
            var existing = ProtectedPeriods[running];
            ProtectedPeriods[running] = existing with
            {
                Start = period.Start < existing.Start ? period.Start : existing.Start,
                End = period.End,
            };
        }
        else if (!ProtectedPeriods.Contains(period))
        {
            ProtectedPeriods.Add(period);
        }

        ModifiedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Closes every still-running protected period.
    /// </summary>
    /// <remarks>
    /// A period that would end before it began is removed rather than stored. Somebody who marks
    /// illness and then immediately says they are training again has not had a protected period;
    /// keeping a zero-length one would leave the screen reporting a protection they just cancelled.
    /// </remarks>
    /// <param name="lastProtectedDay">The final day the protection covered.</param>
    public void EndProtection(DateOnly lastProtectedDay)
    {
        for (var index = ProtectedPeriods.Count - 1; index >= 0; index--)
        {
            if (!ProtectedPeriods[index].IsOpenEnded)
            {
                continue;
            }

            if (lastProtectedDay < ProtectedPeriods[index].Start)
            {
                ProtectedPeriods.RemoveAt(index);
                continue;
            }

            ProtectedPeriods[index] = ProtectedPeriods[index] with { End = lastProtectedDay };
        }

        ModifiedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Removes every protected period.</summary>
    /// <remarks>Present so a mistaken entry can be corrected without editing the database.</remarks>
    public void ClearProtection()
    {
        ProtectedPeriods.Clear();
        ModifiedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>The protection covering a date, or <see langword="null"/> when there is none.</summary>
    /// <param name="date">The local date to test.</param>
    /// <returns>The covering period, preferring the one that started most recently.</returns>
    public ProtectedPeriod? ProtectionOn(DateOnly date)
        => ProtectedPeriods
            .Where(period => period.Covers(date))
            .OrderByDescending(period => period.Start)
            .FirstOrDefault();

    /// <summary>Whether a date is protected.</summary>
    /// <param name="date">The local date to test.</param>
    /// <returns><see langword="true"/> when some period covers it.</returns>
    public bool IsProtectedOn(DateOnly date) => ProtectionOn(date) is not null;

    /// <summary>
    /// Whether an optional rhythm reminder may be sent today.
    /// </summary>
    /// <remarks>
    /// False during a protected period, which is the whole point. The one day a streak app would
    /// most want to nudge somebody is the day they told it they are ill, and that nudge is the
    /// behaviour this feature exists in order not to have.
    /// </remarks>
    /// <param name="today">The user's local date.</param>
    /// <returns><see langword="true"/> only when engagement is wanted and the day is not protected.</returns>
    public bool AllowsSupportiveReminders(DateOnly today) => GamificationEnabled && !IsProtectedOn(today);
}
