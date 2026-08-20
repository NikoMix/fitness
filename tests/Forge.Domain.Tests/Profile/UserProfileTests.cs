using Forge.Domain.Measurement;
using Forge.Domain.Profile;
using Shouldly;

namespace Forge.Domain.Tests.Profile;

public sealed class UserProfileTests
{
    [Fact]
    public void CreateSafetyProposal_uses_persisted_goal_and_latest_metric()
    {
        var profile = new UserProfile
        {
            DisplayName = "Avery",
            BiologicalSex = BiologicalSex.Female,
            Height = Length.FromCentimetres(165m),
            TargetWeight = Mass.FromKilograms(60m),
            GoalTimeframeWeeks = 12,
            TargetDailyCalories = 1600m,
        };
        var latestMetric = new BodyMetric
        {
            UserProfileId = profile.Id,
            Weight = Mass.FromKilograms(66m),
        };

        var proposal = profile.CreateSafetyProposal(latestMetric);

        proposal.CurrentWeight.ShouldBe(latestMetric.Weight);
        proposal.Height.ShouldBe(profile.Height);
        proposal.BiologicalSex.ShouldBe(BiologicalSex.Female);
        proposal.TargetWeight.ShouldBe(profile.TargetWeight);
        proposal.TimeframeWeeks.ShouldBe(12);
        proposal.TargetDailyCalories.ShouldBe(1600m);
    }
}
