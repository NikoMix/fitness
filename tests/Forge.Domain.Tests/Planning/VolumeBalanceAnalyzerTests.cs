using Forge.Domain.Planning;
using Forge.Domain.Training;
using Shouldly;

namespace Forge.Domain.Tests.Planning;

public sealed class VolumeBalanceAnalyzerTests
{
    private static readonly Guid Owner = Guid.CreateVersion7();

    [Fact]
    public void Analyzer_counts_weekly_sets_by_pattern_and_muscle()
    {
        var plan = PlanWith((MovementPattern.Push, "Chest", 3), (MovementPattern.Pull, "Back", 3));

        var report = VolumeBalanceAnalyzer.Analyze(plan);

        report.SetsByMovementPattern[MovementPattern.Push].ShouldBe(3);
        report.SetsByMovementPattern[MovementPattern.Pull].ShouldBe(3);
        report.SetsByMuscleGroup["Chest"].ShouldBe(3);
        report.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void Analyzer_warns_when_push_exceeds_pull_by_more_than_threshold()
    {
        var plan = PlanWith((MovementPattern.Push, "Chest", 8), (MovementPattern.Pull, "Back", 4));

        var warning = VolumeBalanceAnalyzer.Analyze(plan).Warnings.Single();

        warning.Code.ShouldBe("PUSH_PULL_IMBALANCE");
        warning.Ratio.ShouldBe(2.0m);
        VolumeBalanceAnalyzer.BadlySkewedMovementRatio.ShouldBe(1.5m);
    }

    [Fact]
    public void Analyzer_warns_when_counterpart_volume_is_missing()
    {
        var report = VolumeBalanceAnalyzer.Analyze(PlanWith((MovementPattern.Push, "Chest", 4)));

        report.Warnings.Single().LowerVolume.ShouldBe(0);
    }

    private static TrainingPlan PlanWith(params (MovementPattern Pattern, string Muscle, int Sets)[] exercises)
    {
        var plan = new TrainingPlan { UserProfileId = Owner, Name = "Test" };
        var day = new PlanDay { UserProfileId = Owner, Name = "Day" };
        foreach (var item in exercises)
        {
            var exercise = new PlannedExercise { UserProfileId = Owner, ExerciseName = item.Pattern.ToString(), Pattern = item.Pattern, PrimaryMuscle = item.Muscle };
            for (var index = 1; index <= item.Sets; index++)
            {
                exercise.Sets.Add(new PlannedSet { UserProfileId = Owner, Ordinal = index, TargetRepsMin = 8, TargetRepsMax = 10 });
            }

            day.Exercises.Add(exercise);
        }

        plan.Days.Add(day);
        return plan;
    }
}
