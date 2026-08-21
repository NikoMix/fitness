using Forge.Domain.Recovery;
using Forge.Domain.Training;

namespace Forge.Domain.Analytics;

/// <summary>One night of recorded sleep, keyed to the local date it was reported on.</summary>
/// <param name="Date">Local date of the morning the sleep was reported.</param>
/// <param name="Hours">Hours slept.</param>
public sealed record SleepNight(DateOnly Date, decimal Hours);

/// <summary>An association result together with the caveats that must travel with it.</summary>
/// <param name="Association">The underlying association-only verdict.</param>
/// <param name="PairedDays">Days that had both a sleep figure and a training session.</param>
/// <param name="SleepNightsRecorded">Sleep figures available before pairing.</param>
/// <param name="TrainingDaysRecorded">Training days available before pairing.</param>
public sealed record SleepPerformanceInsight(
    SleepPerformanceAssociationResult Association,
    int PairedDays,
    int SleepNightsRecorded,
    int TrainingDaysRecorded)
{
    /// <summary>Whether an association may be described at all.</summary>
    public bool HasClaim => Association.HasClaim;

    /// <summary>The association sentence, or the reason no association is being described.</summary>
    public string Message => Association.Message;

    /// <summary>How many further paired days are needed before anything can be said.</summary>
    public int PairedDaysStillNeeded =>
        Math.Max(0, SleepPerformanceAssociationAnalyzer.MinimumSampleSize - PairedDays);
}

/// <summary>
/// Pairs recorded sleep with the training that followed it, so an association can be described.
/// </summary>
/// <remarks>
/// <para>
/// This never says one thing caused another, and the shape of the data is the reason why. These
/// are self-selected observations from a single person living an ordinary life, with no control
/// condition and no randomisation. A bad week at work lowers sleep and lowers training at the same
/// time without either causing the other, and the arithmetic cannot tell that apart from an effect.
/// Wording that implies otherwise would push someone toward real decisions about their training on
/// evidence that does not support them.
/// </para>
/// <para>
/// Pairing only days that contain training is a correctness requirement, not a nicety. Treating a
/// rest day as zero performance would manufacture the very association the screen claims to be
/// testing: poor sleep makes people skip sessions, skipped sessions have no volume, and the result
/// would be a confident finding produced entirely by the missing rows.
/// </para>
/// </remarks>
public static class SleepPerformancePairing
{
    /// <summary>The caveat that must accompany any association shown to the reader.</summary>
    /// <remarks>
    /// Names the specific confounds rather than gesturing at uncertainty, because a vague hedge
    /// reads as legal cover and gets skipped, whereas a concrete alternative explanation is
    /// something the reader can actually check against their own week.
    /// </remarks>
    public const string NonCausationCaveat =
        "Association is not causation. These are your own observations rather than a controlled experiment, so anything that moves both numbers at once - illness, work stress, travel, or simply which session fell on which day - can produce this pattern without sleep having changed your training at all.";

    /// <summary>Explains what the performance figure is, and what it cannot separate.</summary>
    public const string PerformanceMeasureCaveat =
        "Performance here is the working volume you logged that day. A heavy lower-body session and a short accessory session produce very different volumes whatever your sleep did, so read this as a rough signal rather than a measurement of how strong you were.";

    /// <summary>Builds paired samples from recorded sleep and logged training.</summary>
    /// <param name="nights">Recorded sleep. Entries without usable hours should be filtered out by the caller.</param>
    /// <param name="sets">Logged sets. Warm-ups and sets without repetitions are ignored.</param>
    /// <returns>One sample per local date that had both sleep and training, ascending.</returns>
    public static IReadOnlyList<SleepPerformanceSample> BuildSamples(
        IEnumerable<SleepNight> nights,
        IEnumerable<SetEntry> sets)
    {
        ArgumentNullException.ThrowIfNull(nights);
        ArgumentNullException.ThrowIfNull(sets);

        var volumeByDate = sets
            .Where(set => !set.IsWarmUp && set.Repetitions > 0)
            .GroupBy(set => DateOnly.FromDateTime(set.CompletedUtc.LocalDateTime))
            .ToDictionary(group => group.Key, group => group.Sum(set => set.Volume.Kilograms));

        return nights
            .Where(night => night.Hours > 0m)
            .GroupBy(night => night.Date)
            .Select(group => new { Date = group.Key, Hours = group.Average(night => night.Hours) })
            .Where(night => volumeByDate.ContainsKey(night.Date))
            .Select(night => new SleepPerformanceSample(night.Date, night.Hours, volumeByDate[night.Date]))
            .OrderBy(sample => sample.Date)
            .ToList();
    }

    /// <summary>Builds the samples and asks the association analyzer what, if anything, may be said.</summary>
    /// <param name="nights">Recorded sleep.</param>
    /// <param name="sets">Logged sets.</param>
    /// <param name="sleepThresholdHours">Hours separating the rested and less-rested groups.</param>
    /// <returns>The association verdict and the pairing counts behind it.</returns>
    public static SleepPerformanceInsight Analyze(
        IEnumerable<SleepNight> nights,
        IEnumerable<SetEntry> sets,
        decimal sleepThresholdHours = 7m)
    {
        ArgumentNullException.ThrowIfNull(nights);
        ArgumentNullException.ThrowIfNull(sets);

        var materializedNights = nights.Where(night => night.Hours > 0m).ToList();
        var trainingDays = sets
            .Where(set => !set.IsWarmUp && set.Repetitions > 0)
            .Select(set => DateOnly.FromDateTime(set.CompletedUtc.LocalDateTime))
            .Distinct()
            .Count();

        var samples = BuildSamples(materializedNights, sets);

        return new SleepPerformanceInsight(
            SleepPerformanceAssociationAnalyzer.Analyze(samples, sleepThresholdHours),
            samples.Count,
            materializedNights.Select(night => night.Date).Distinct().Count(),
            trainingDays);
    }
}
