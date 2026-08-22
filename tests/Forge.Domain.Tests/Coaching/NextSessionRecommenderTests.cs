using Forge.Domain.Coaching;
using Forge.Domain.Measurement;
using Forge.Domain.Recovery;
using Shouldly;

namespace Forge.Domain.Tests.Coaching;

public sealed class NextSessionRecommenderTests
{
    [Fact]
    public void Caps_session_to_session_load_increase()
    {
        var result = NextSessionRecommender.Recommend(Request(currentLoad: 80m, rir: 8));

        result.Status.ShouldBe(NextSessionRecommendationStatus.Recommended);
        result.Load.Kilograms.ShouldBe(84m);
        result.Reasons.ShouldContain(reason => reason.Contains("capped", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Blocks_injured_primary_muscle_but_keeps_override_visible()
    {
        var result = NextSessionRecommender.Recommend(Request(contraindications:
            [new TrainingContraindication("Quadriceps", "knee pain flare")]
        ));

        result.Status.ShouldBe(NextSessionRecommendationStatus.BlockedBySafety);
        result.IsOverridable.ShouldBeTrue();
        result.Explanation.ShouldContain("injured", Case.Insensitive);
        result.Explanation.ShouldContain("knee pain flare");
        result.MedicalDisclaimer.ShouldContain("not medical advice", Case.Insensitive);
    }

    [Fact]
    public void Blocks_severe_soreness_for_target_muscle()
    {
        var result = NextSessionRecommender.Recommend(Request(soreness:
            [new SorenessEntry { UserProfileId = Guid.CreateVersion7(), MuscleGroup = "Quadriceps", Level = SorenessTracker.SevereSorenessLevel }]
        ));

        result.Status.ShouldBe(NextSessionRecommendationStatus.BlockedBySafety);
        result.Explanation.ShouldContain("soreness", Case.Insensitive);
    }

    [Fact]
    public void Explains_rpe_based_progression()
    {
        var result = NextSessionRecommender.Recommend(Request(currentLoad: 80m, reps: 8, rir: 3));

        result.Explanation.ShouldContain("80 kg for 8 reps");
        result.Explanation.ShouldContain("3 reps in reserve");
        result.IsOverridable.ShouldBeTrue();
    }

    private static NextSessionRecommendationRequest Request(
        decimal currentLoad = 80m,
        int reps = 8,
        int? rir = 3,
        IReadOnlyList<TrainingContraindication>? contraindications = null,
        IReadOnlyList<SorenessEntry>? soreness = null)
        => new(
            Guid.CreateVersion7(),
            "Back squat",
            "Quadriceps",
            ["Glutes"],
            Mass.FromKilograms(currentLoad),
            6,
            8,
            3,
            [new SessionPerformance(new DateOnly(2026, 8, 13), Mass.FromKilograms(currentLoad), reps, rir)],
            contraindications,
            soreness);
}
