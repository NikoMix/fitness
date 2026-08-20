using Forge.Domain.Training;

namespace Forge.Domain.Planning;

/// <summary>Weekly set-volume totals and safety warnings.</summary>
public sealed record VolumeBalanceReport(
    IReadOnlyDictionary<MovementPattern, int> SetsByMovementPattern,
    IReadOnlyDictionary<string, int> SetsByMuscleGroup,
    IReadOnlyList<VolumeBalanceWarning> Warnings);

/// <summary>A programme balance warning.</summary>
public sealed record VolumeBalanceWarning(string Code, string Message, int HigherVolume, int LowerVolume, decimal Ratio);

/// <summary>Computes weekly volume balance for a training plan.</summary>
public static class VolumeBalanceAnalyzer
{
    /// <summary>
    /// Movement balance warning threshold. A 1.5 ratio means one side receives at least 50% more
    /// weekly working-set exposure than its counterpart; sustained differences at that size are
    /// large enough to reinforce posture, shoulder and hip loading asymmetries while still
    /// avoiding noisy warnings for a single accessory set.
    /// </summary>
    public const decimal BadlySkewedMovementRatio = 1.5m;

    /// <summary>Analyzes working-set volume by movement pattern and muscle group.</summary>
    public static VolumeBalanceReport Analyze(TrainingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var patternTotals = new Dictionary<MovementPattern, int>();
        var muscleTotals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var exercise in plan.Days.SelectMany(day => day.Exercises))
        {
            var sets = exercise.WorkingSetCount;
            if (sets == 0)
            {
                continue;
            }

            patternTotals[exercise.Pattern] = patternTotals.GetValueOrDefault(exercise.Pattern) + sets;
            AddMuscle(muscleTotals, exercise.PrimaryMuscle, sets);
            foreach (var muscle in exercise.SecondaryMuscles)
            {
                AddMuscle(muscleTotals, muscle, Math.Max(1, sets / 2));
            }
        }

        var warnings = new List<VolumeBalanceWarning>();
        AddPairWarning(warnings, patternTotals, MovementPattern.Push, MovementPattern.Pull, "PUSH_PULL_IMBALANCE", "Push and pull work are badly skewed.");
        AddPairWarning(warnings, patternTotals, MovementPattern.Squat, MovementPattern.Hinge, "SQUAT_HINGE_IMBALANCE", "Knee-dominant and hip-dominant leg work are badly skewed.");

        return new VolumeBalanceReport(patternTotals, muscleTotals, warnings);
    }

    private static void AddMuscle(Dictionary<string, int> totals, string? muscle, int sets)
    {
        if (string.IsNullOrWhiteSpace(muscle))
        {
            return;
        }

        totals[muscle] = totals.GetValueOrDefault(muscle) + sets;
    }

    private static void AddPairWarning(
        List<VolumeBalanceWarning> warnings,
        IReadOnlyDictionary<MovementPattern, int> totals,
        MovementPattern first,
        MovementPattern second,
        string code,
        string message)
    {
        var firstTotal = totals.GetValueOrDefault(first);
        var secondTotal = totals.GetValueOrDefault(second);
        var lower = Math.Min(firstTotal, secondTotal);
        var higher = Math.Max(firstTotal, secondTotal);
        if (higher == 0 || lower == 0)
        {
            if (higher >= 4)
            {
                warnings.Add(new VolumeBalanceWarning(code, message, higher, lower, decimal.MaxValue));
            }

            return;
        }

        var ratio = decimal.Round((decimal)higher / lower, 2);
        if (ratio > BadlySkewedMovementRatio)
        {
            warnings.Add(new VolumeBalanceWarning(code, message, higher, lower, ratio));
        }
    }
}
