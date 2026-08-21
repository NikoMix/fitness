using Forge.Domain.Measurement;
using Forge.Domain.Onboarding;
using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Onboarding;

public sealed class GoalSafetyNarratorTests
{
    [Fact]
    public void Accepted_goal_is_narrated_as_inside_the_guardrails()
    {
        var narration = GoalSafetyNarrator.Narrate(Evaluate(
            current: 100m,
            target: 96m,
            weeks: 4,
            calories: 1800m));

        narration.BlocksSaving.ShouldBeFalse();
        narration.IsInformationOnly.ShouldBeTrue();
        narration.Headline.ShouldContain("guardrails");
        narration.Reassurance.ShouldBe(GoalSafetyNarrator.AcceptedReassurance);
    }

    [Fact]
    public void Refusal_states_that_the_users_answers_were_kept()
    {
        var narration = GoalSafetyNarrator.Narrate(Evaluate(
            current: 100m,
            target: 90m,
            weeks: 4,
            calories: 1800m));

        narration.BlocksSaving.ShouldBeTrue();
        narration.Reassurance.ShouldBe(GoalSafetyNarrator.RefusedReassurance);
        narration.Reassurance.ShouldContain("nothing has been discarded", Case.Insensitive);
    }

    [Fact]
    public void Every_blocking_reason_is_surfaced_not_just_the_first()
    {
        // A fast pace, an energy target under the floor, and an underweight target BMI all at once.
        var narration = GoalSafetyNarrator.Narrate(Evaluate(
            current: 70m,
            target: 55m,
            weeks: 8,
            calories: 900m,
            heightCentimetres: 180m));

        narration.Reasons.Count.ShouldBeGreaterThanOrEqualTo(3);
        narration.ReasonText.ShouldContain("% of body weight per week");
        narration.ReasonText.ShouldContain("kcal");
        narration.ReasonText.ShouldContain("BMI");
    }

    [Fact]
    public void Refusal_hides_reassuring_information_advisories()
    {
        var narration = GoalSafetyNarrator.Narrate(Evaluate(
            current: 100m,
            target: 90m,
            weeks: 4,
            calories: 1800m));

        narration.Reasons.ShouldAllBe(reason => !reason.Contains("within Forge's general safety guardrails", StringComparison.Ordinal));
    }

    [Fact]
    public void Signposts_are_deduplicated_and_kept()
    {
        var narration = GoalSafetyNarrator.Narrate(Evaluate(
            current: 70m,
            target: 55m,
            weeks: 8,
            calories: 900m,
            heightCentimetres: 180m));

        narration.Signposts.ShouldNotBeEmpty();
        narration.Signposts.Distinct(StringComparer.Ordinal).Count().ShouldBe(narration.Signposts.Count);
        narration.SignpostText.ShouldContain("clinician");
    }

    [Fact]
    public void Refusal_headline_says_forge_cannot_plan_it_rather_than_blaming_input()
    {
        var narration = GoalSafetyNarrator.Narrate(Evaluate(
            current: 100m,
            target: 90m,
            weeks: 4,
            calories: 1800m));

        narration.Headline.ShouldBe("Forge cannot plan this goal as it stands");
        narration.Headline.ShouldNotContain("invalid", Case.Insensitive);
    }

    [Fact]
    public void Severity_is_carried_through_for_presentation()
    {
        GoalSafetyNarrator.Narrate(Evaluate(100m, 90m, 4, 1800m)).Severity.ShouldBe(SafetySeverity.Refused);
        GoalSafetyNarrator.Narrate(Evaluate(100m, 96m, 4, 1800m)).Severity.ShouldBe(SafetySeverity.Information);
    }

    [Fact]
    public void Narration_always_has_something_to_show()
    {
        GoalSafetyNarrator.Narrate(Evaluate(100m, 96m, 4, 1800m)).HasContent.ShouldBeTrue();
    }

    private static GoalSafetyResult Evaluate(
        decimal current,
        decimal target,
        int weeks,
        decimal calories,
        decimal heightCentimetres = 180m)
        => GoalSafetyEvaluator.Evaluate(new GoalSafetyProposal(
            Mass.FromKilograms(current),
            Length.FromCentimetres(heightCentimetres),
            BiologicalSex.Male,
            Mass.FromKilograms(target),
            weeks,
            calories));
}
