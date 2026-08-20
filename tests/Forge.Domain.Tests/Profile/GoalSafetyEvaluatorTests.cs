using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Profile;

public sealed class GoalSafetyEvaluatorTests
{
    [Fact]
    public void Goal_at_one_percent_bodyweight_change_per_week_is_allowed()
    {
        var result = GoalSafetyEvaluator.Evaluate(new GoalSafetyProposal(
            Mass.FromKilograms(100m),
            Length.FromCentimetres(180m),
            BiologicalSex.Male,
            Mass.FromKilograms(96m),
            TimeframeWeeks: 4,
            TargetDailyCalories: 1800m));

        result.IsAccepted.ShouldBeTrue();
        result.Severity.ShouldBe(SafetySeverity.Information);
    }

    [Fact]
    public void Goal_above_one_percent_bodyweight_change_per_week_is_refused()
    {
        var result = GoalSafetyEvaluator.Evaluate(new GoalSafetyProposal(
            Mass.FromKilograms(100m),
            Length.FromCentimetres(180m),
            BiologicalSex.Male,
            Mass.FromKilograms(95.9m),
            TimeframeWeeks: 4,
            TargetDailyCalories: 1800m));

        result.IsAccepted.ShouldBeFalse();
        result.Advisories.ShouldContain(a => a.Severity == SafetySeverity.Refused && a.Message.Contains("1%", StringComparison.Ordinal));
    }

    [Fact]
    public void Female_energy_floor_is_configurable_and_refused_when_crossed()
    {
        var result = GoalSafetyEvaluator.Evaluate(
            new GoalSafetyProposal(
                Mass.FromKilograms(70m),
                Length.FromCentimetres(165m),
                BiologicalSex.Female,
                TargetDailyCalories: 1199m),
            new GoalSafetyOptions { FemaleDailyCalorieFloor = 1200m });

        result.IsAccepted.ShouldBeFalse();
        result.Advisories.ShouldContain(a => a.Message.Contains("1200", StringComparison.Ordinal));
    }

    [Fact]
    public void Male_energy_floor_is_configurable_and_refused_when_crossed()
    {
        var result = GoalSafetyEvaluator.Evaluate(
            new GoalSafetyProposal(
                Mass.FromKilograms(85m),
                Length.FromCentimetres(180m),
                BiologicalSex.Male,
                TargetDailyCalories: 1499m),
            new GoalSafetyOptions { MaleDailyCalorieFloor = 1500m });

        result.IsAccepted.ShouldBeFalse();
        result.Advisories.ShouldContain(a => a.Message.Contains("1500", StringComparison.Ordinal));
    }

    [Fact]
    public void Energy_floor_boundary_is_allowed()
    {
        var result = GoalSafetyEvaluator.Evaluate(new GoalSafetyProposal(
            Mass.FromKilograms(70m),
            Length.FromCentimetres(165m),
            BiologicalSex.Female,
            TargetDailyCalories: 1200m));

        result.IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void Target_underweight_bmi_is_refused()
    {
        var result = GoalSafetyEvaluator.Evaluate(new GoalSafetyProposal(
            Mass.FromKilograms(70m),
            Length.FromCentimetres(180m),
            BiologicalSex.PreferNotToSay,
            Mass.FromKilograms(58m),
            TimeframeWeeks: 40));

        result.IsAccepted.ShouldBeFalse();
        result.Advisories.ShouldContain(a => a.Message.Contains("18.5", StringComparison.Ordinal));
    }

    [Fact]
    public void Refused_advisories_include_professional_support_signpost()
    {
        var result = GoalSafetyEvaluator.Evaluate(new GoalSafetyProposal(
            Mass.FromKilograms(80m),
            Length.FromCentimetres(180m),
            BiologicalSex.Male,
            TargetDailyCalories: 1000m));

        result.Advisories
            .Where(a => a.Severity == SafetySeverity.Refused)
            .ShouldAllBe(a => !string.IsNullOrWhiteSpace(a.SupportSignpost));
    }

    [Fact]
    public void Safety_copy_is_neutral_and_non_judgemental()
    {
        var result = GoalSafetyEvaluator.Evaluate(new GoalSafetyProposal(
            Mass.FromKilograms(100m),
            Length.FromCentimetres(180m),
            BiologicalSex.Male,
            Mass.FromKilograms(90m),
            TimeframeWeeks: 4,
            TargetDailyCalories: 1000m));

        var copy = string.Join(' ', result.Advisories.Select(a => $"{a.Message} {a.SupportSignpost}"));

        copy.ShouldNotContain("shame", Case.Insensitive);
        copy.ShouldNotContain("bad", Case.Insensitive);
        copy.ShouldNotContain("failure", Case.Insensitive);
        copy.ShouldNotContain("fault", Case.Insensitive);
    }
}
