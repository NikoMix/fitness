using Forge.Domain.Sensors;
using Forge.Domain.Workout;
using Shouldly;

namespace Forge.Domain.Tests.Workout;

public sealed class RepCountAcceptancePolicyTests
{
    [Fact]
    public void A_calibrating_counter_offers_nothing()
    {
        var suggestion = RepCountAcceptancePolicy.Evaluate(
            new RepetitionCounterReading(0, 0, RepetitionCounterState.Calibrating, 0, 0));

        suggestion.Trust.ShouldBe(RepCountTrust.Calibrating);
        suggestion.CanApplyAutomatically.ShouldBeFalse();
        suggestion.HasCount.ShouldBeFalse();
    }

    [Fact]
    public void A_noisy_signal_is_rejected_even_when_it_produced_a_count()
    {
        var suggestion = RepCountAcceptancePolicy.Evaluate(
            new RepetitionCounterReading(7, 0.3, RepetitionCounterState.SignalTooNoisy, 0.4, 3.1));

        suggestion.Trust.ShouldBe(RepCountTrust.Rejected);
        suggestion.CanApplyAutomatically.ShouldBeFalse();
        suggestion.HasCount.ShouldBeFalse();
        suggestion.Explanation.ShouldContain("manually");
    }

    [Fact]
    public void A_clean_high_confidence_count_may_be_offered_without_confirmation()
    {
        var suggestion = RepCountAcceptancePolicy.Evaluate(
            new RepetitionCounterReading(8, 0.92, RepetitionCounterState.Counting, 0.4, 0.2));

        suggestion.Trust.ShouldBe(RepCountTrust.Trusted);
        suggestion.RepetitionCount.ShouldBe(8);
        suggestion.CanApplyAutomatically.ShouldBeTrue();
    }

    [Fact]
    public void A_count_just_below_the_confidence_bar_requires_confirmation()
    {
        var suggestion = RepCountAcceptancePolicy.Evaluate(
            new RepetitionCounterReading(8, RepCountAcceptancePolicy.DefaultMinimumConfidence - 0.01, RepetitionCounterState.Counting, 0.3, 1.1));

        suggestion.Trust.ShouldBe(RepCountTrust.NeedsConfirmation);
        suggestion.CanApplyAutomatically.ShouldBeFalse();
        suggestion.HasCount.ShouldBeTrue();
    }

    [Fact]
    public void Confidence_exactly_on_the_bar_is_trusted()
    {
        var suggestion = RepCountAcceptancePolicy.Evaluate(
            new RepetitionCounterReading(5, RepCountAcceptancePolicy.DefaultMinimumConfidence, RepetitionCounterState.Counting, 0.3, 0.5));

        suggestion.Trust.ShouldBe(RepCountTrust.Trusted);
    }

    [Fact]
    public void A_ready_counter_with_nothing_seen_yet_offers_nothing()
    {
        var suggestion = RepCountAcceptancePolicy.Evaluate(
            new RepetitionCounterReading(0, 0.85, RepetitionCounterState.Ready, 0, 0));

        suggestion.Trust.ShouldBe(RepCountTrust.Calibrating);
        suggestion.HasCount.ShouldBeFalse();
    }

    [Fact]
    public void Raising_the_confidence_bar_demotes_a_previously_trusted_count()
    {
        var reading = new RepetitionCounterReading(10, 0.85, RepetitionCounterState.Counting, 0.4, 0.3);

        RepCountAcceptancePolicy.Evaluate(reading).Trust.ShouldBe(RepCountTrust.Trusted);
        RepCountAcceptancePolicy.Evaluate(reading, minimumConfidence: 0.95).Trust.ShouldBe(RepCountTrust.NeedsConfirmation);
    }

    [Fact]
    public void A_nonsensical_confidence_bar_is_clamped_into_range()
    {
        var reading = new RepetitionCounterReading(6, 0.5, RepetitionCounterState.Counting, 0.3, 0.9);

        RepCountAcceptancePolicy.Evaluate(reading, minimumConfidence: -5).Trust.ShouldBe(RepCountTrust.Trusted);
        RepCountAcceptancePolicy.Evaluate(reading, minimumConfidence: 5).Trust.ShouldBe(RepCountTrust.NeedsConfirmation);
    }
}
