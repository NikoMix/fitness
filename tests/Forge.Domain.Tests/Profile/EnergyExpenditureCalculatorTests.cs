using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Profile;

public sealed class EnergyExpenditureCalculatorTests
{
    [Fact]
    public void Bmr_uses_mifflin_st_jeor_male_coefficient()
    {
        var bmr = EnergyExpenditureCalculator.CalculateBmr(
            Mass.FromKilograms(80m),
            Length.FromCentimetres(180m),
            30,
            BiologicalSex.Male);

        bmr.ShouldBe(1780m);
    }

    [Fact]
    public void Bmr_uses_mifflin_st_jeor_female_coefficient()
    {
        var bmr = EnergyExpenditureCalculator.CalculateBmr(
            Mass.FromKilograms(65m),
            Length.FromCentimetres(165m),
            30,
            BiologicalSex.Female);

        bmr.ShouldBe(1370.25m);
    }

    [Fact]
    public void Tdee_uses_activity_multiplier()
    {
        EnergyExpenditureCalculator.CalculateTdee(1_800m, ActivityLevel.ModeratelyActive).ShouldBe(2_790.00m);
    }
}
