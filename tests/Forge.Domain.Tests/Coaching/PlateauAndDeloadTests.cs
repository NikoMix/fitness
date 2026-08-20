using System.Globalization;
using Forge.Domain.Analytics;
using Forge.Domain.Coaching;
using Forge.Domain.Measurement;
using Shouldly;

namespace Forge.Domain.Tests.Coaching;

public sealed class PlateauAndDeloadTests
{
    [Fact]
    public void Plateau_requires_minimum_sessions()
    {
        var result = PlateauDetector.Detect([Set(new DateOnly(2026, 8, 1), 80m, 8)]);

        result.IsPlateaued.ShouldBeFalse();
        result.Explanation.ShouldContain(PlateauDetector.MinimumSessionsForPlateau.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Plateau_suggests_concrete_interventions_when_stalled()
    {
        var result = PlateauDetector.Detect([
            Set(new DateOnly(2026, 8, 1), 80m, 8),
            Set(new DateOnly(2026, 8, 8), 80m, 8),
            Set(new DateOnly(2026, 8, 15), 80m, 8),
            Set(new DateOnly(2026, 8, 22), 80m, 8)]);

        result.IsPlateaued.ShouldBeTrue();
        result.Interventions.Count.ShouldBeGreaterThan(0);
        result.Interventions.ShouldContain(item => item.Contains("Reduce load", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Deload_triggers_from_accumulated_training_load()
    {
        var points = Enumerable.Range(0, 28)
            .Select(day => new TrainingLoadPoint(new DateOnly(2026, 8, 1).AddDays(day), Mass.FromKilograms(day >= 21 ? 250m : 100m)));

        var result = DeloadRecommender.Recommend(Mass.FromKilograms(100m), 6, 8, 4, points, new DateOnly(2026, 8, 28));

        result.ShouldDeload.ShouldBeTrue();
        result.SuggestedLoad.Kilograms.ShouldBe(90m);
        result.SuggestedSetCount.ShouldBe(3);
        result.MedicalDisclaimer.ShouldContain("not medical advice", Case.Insensitive);
    }

    private static SessionPerformance Set(DateOnly date, decimal load, int reps)
        => new(date, Mass.FromKilograms(load), reps, 2);
}
