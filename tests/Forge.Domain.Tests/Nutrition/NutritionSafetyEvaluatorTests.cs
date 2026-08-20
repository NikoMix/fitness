using Forge.Domain.Nutrition;
using Shouldly;

namespace Forge.Domain.Tests.Nutrition;

public sealed class NutritionSafetyEvaluatorTests
{
    [Fact]
    public void Targets_below_female_floor_are_refused()
    {
        var advisory = NutritionSafetyEvaluator.Evaluate(1199m, 2000m, NutritionSafetySex.Female, hideCalorieNumbers: false);

        advisory.Severity.ShouldBe(NutritionAdvisorySeverity.High);
        advisory.CanProceed.ShouldBeFalse();
        advisory.Message.ShouldContain("1200");
        advisory.SupportSignpost.ShouldNotBeNull();
    }

    [Fact]
    public void Female_floor_boundary_is_allowed_when_deficit_is_not_steep()
    {
        var advisory = NutritionSafetyEvaluator.Evaluate(1200m, 1500m, NutritionSafetySex.Female, hideCalorieNumbers: false);

        advisory.Severity.ShouldBe(NutritionAdvisorySeverity.None);
        advisory.CanProceed.ShouldBeTrue();
    }

    [Fact]
    public void Targets_below_male_floor_are_refused()
    {
        var advisory = NutritionSafetyEvaluator.Evaluate(1499m, 2200m, NutritionSafetySex.Male, hideCalorieNumbers: false);

        advisory.Severity.ShouldBe(NutritionAdvisorySeverity.High);
        advisory.CanProceed.ShouldBeFalse();
        advisory.Message.ShouldContain("1500");
    }

    [Fact]
    public void Unspecified_sex_uses_more_protective_floor()
    {
        var advisory = NutritionSafetyEvaluator.Evaluate(1499m, 2200m, NutritionSafetySex.Unspecified, hideCalorieNumbers: false);

        advisory.Severity.ShouldBe(NutritionAdvisorySeverity.High);
        advisory.Message.ShouldContain("1500");
    }

    [Fact]
    public void Deficits_above_twenty_five_percent_are_flagged()
    {
        var advisory = NutritionSafetyEvaluator.Evaluate(1600m, 2200m, NutritionSafetySex.Female, hideCalorieNumbers: false);

        advisory.Severity.ShouldBe(NutritionAdvisorySeverity.Caution);
        advisory.CanProceed.ShouldBeTrue();
        advisory.SupportSignpost.ShouldNotBeNull();
    }

    [Fact]
    public void Deficit_fraction_boundary_is_allowed()
    {
        var advisory = NutritionSafetyEvaluator.Evaluate(1800m, 2400m, NutritionSafetySex.Female, hideCalorieNumbers: false);

        advisory.Severity.ShouldBe(NutritionAdvisorySeverity.None);
    }

    [Fact]
    public void Absolute_deficits_above_one_thousand_kilocalories_are_flagged()
    {
        var advisory = NutritionSafetyEvaluator.Evaluate(2100m, 3200m, NutritionSafetySex.Male, hideCalorieNumbers: false);

        advisory.Severity.ShouldBe(NutritionAdvisorySeverity.Caution);
        advisory.Message.ShouldContain("1100");
    }

    [Fact]
    public void Hide_calorie_numbers_omits_numeric_feedback()
    {
        var advisory = NutritionSafetyEvaluator.Evaluate(1600m, 2200m, NutritionSafetySex.Female, hideCalorieNumbers: true);

        advisory.DisplayEnergyKilocalories.ShouldBeNull();
        advisory.Message.ShouldNotContain("1600");
        advisory.Message.ShouldNotContain("600");
    }

    [Theory]
    [InlineData("shame")]
    [InlineData("cheat")]
    [InlineData("failed")]
    [InlineData("streak")]
    public void Advisory_language_avoids_shame_and_streak_pressure(string bannedWord)
    {
        var advisory = NutritionSafetyEvaluator.Evaluate(1199m, 2200m, NutritionSafetySex.Female, hideCalorieNumbers: true);

        advisory.Message.ShouldNotContain(bannedWord, Case.Insensitive);
        advisory.SupportSignpost.ShouldNotBeNull().ShouldNotContain(bannedWord, Case.Insensitive);
    }
}
