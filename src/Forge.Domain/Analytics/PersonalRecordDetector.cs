using Forge.Domain.Measurement;
using Forge.Domain.Training;

namespace Forge.Domain.Analytics;

/// <summary>The kind of personal record detected from a training log.</summary>
public enum PersonalRecordType
{
    HeaviestLoad = 0,
    EstimatedOneRepMax = 1,
    MostRepsAtLoad = 2,
    GreatestSessionVolume = 3
}

/// <summary>A detected record and the set that established it.</summary>
public sealed record PersonalRecord(
    PersonalRecordType Type,
    Guid ExerciseId,
    DateTimeOffset AchievedUtc,
    SetEntry Set,
    Mass Load,
    int Repetitions,
    Mass Value,
    string Explanation);

/// <summary>Detects explainable personal records from completed sets.</summary>
public sealed class PersonalRecordDetector
{
    public static IReadOnlyList<PersonalRecord> DetectAll(IEnumerable<SetEntry> sets, OneRepMaxFormula formula = OneRepMaxFormula.Epley)
    {
        ArgumentNullException.ThrowIfNull(sets);

        var materialized = sets.ToList();
        var records = new List<PersonalRecord>();

        records.AddRange(DetectHeaviestLoads(materialized));
        records.AddRange(DetectEstimatedOneRepMaxes(materialized, formula));
        records.AddRange(DetectMostRepsAtLoad(materialized));
        records.AddRange(DetectGreatestSessionVolumes(materialized));

        return records
            .OrderBy(record => record.ExerciseId)
            .ThenBy(record => record.Type)
            .ThenBy(record => record.AchievedUtc)
            .ToList();
    }

    public static PersonalRecord? Detect(IEnumerable<SetEntry> sets, PersonalRecordType type, OneRepMaxFormula formula = OneRepMaxFormula.Epley)
    {
        ArgumentNullException.ThrowIfNull(sets);

        return type switch
        {
            PersonalRecordType.HeaviestLoad => DetectHeaviestLoads(sets).MaxBy(record => record.Value),
            PersonalRecordType.EstimatedOneRepMax => DetectEstimatedOneRepMaxes(sets, formula).MaxBy(record => record.Value),
            PersonalRecordType.MostRepsAtLoad => DetectMostRepsAtLoad(sets).MaxBy(record => record.Repetitions),
            PersonalRecordType.GreatestSessionVolume => DetectGreatestSessionVolumes(sets).MaxBy(record => record.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown record type.")
        };
    }

    private static IEnumerable<PersonalRecord> DetectHeaviestLoads(IEnumerable<SetEntry> sets)
        => sets.Where(IsWorkingSet)
            .Where(set => set.Load > Mass.Zero)
            .GroupBy(set => set.ExerciseId)
            .Select(group => group.OrderByDescending(set => set.Load).ThenBy(set => set.CompletedUtc).First())
            .Select(set => new PersonalRecord(
                PersonalRecordType.HeaviestLoad,
                set.ExerciseId,
                set.CompletedUtc,
                set,
                set.Load,
                set.Repetitions,
                set.Load,
                $"Heaviest completed working set: {set.Load}."));

    private static IEnumerable<PersonalRecord> DetectEstimatedOneRepMaxes(IEnumerable<SetEntry> sets, OneRepMaxFormula formula)
        => sets.Where(IsWorkingSet)
            .Select(set => new { Set = set, Estimate = OneRepMaxEstimator.Estimate(set.Load, set.Repetitions, formula) })
            .Where(item => item.Estimate is not null)
            .GroupBy(item => item.Set.ExerciseId)
            .Select(group => group.OrderByDescending(item => item.Estimate!.Value).ThenBy(item => item.Set.CompletedUtc).First())
            .Select(item => new PersonalRecord(
                PersonalRecordType.EstimatedOneRepMax,
                item.Set.ExerciseId,
                item.Set.CompletedUtc,
                item.Set,
                item.Set.Load,
                item.Set.Repetitions,
                item.Estimate!.Value,
                $"Estimated 1RM using {formula}; estimates are approximate and less reliable as reps approach ten."));

    private static IEnumerable<PersonalRecord> DetectMostRepsAtLoad(IEnumerable<SetEntry> sets)
        => sets.Where(IsWorkingSet)
            .Where(set => set.Load > Mass.Zero && set.Repetitions > 0)
            .GroupBy(set => new { set.ExerciseId, set.Load })
            .Select(group => group.OrderByDescending(set => set.Repetitions).ThenBy(set => set.CompletedUtc).First())
            .Select(set => new PersonalRecord(
                PersonalRecordType.MostRepsAtLoad,
                set.ExerciseId,
                set.CompletedUtc,
                set,
                set.Load,
                set.Repetitions,
                Mass.FromKilograms(set.Repetitions),
                $"Most reps completed at {set.Load}: {set.Repetitions}."));

    private static IEnumerable<PersonalRecord> DetectGreatestSessionVolumes(IEnumerable<SetEntry> sets)
        => sets.Where(set => !set.IsWarmUp)
            .GroupBy(set => new { set.ExerciseId, set.WorkoutSessionId })
            .Select(group => new
            {
                group.Key.ExerciseId,
                Volume = group.Aggregate(Mass.Zero, (sum, set) => sum + set.Volume),
                RepresentativeSet = group.OrderByDescending(set => set.Volume).ThenBy(set => set.CompletedUtc).First()
            })
            .Where(session => session.Volume > Mass.Zero)
            .GroupBy(session => session.ExerciseId)
            .Select(group => group.OrderByDescending(session => session.Volume).ThenBy(session => session.RepresentativeSet.CompletedUtc).First())
            .Select(session => new PersonalRecord(
                PersonalRecordType.GreatestSessionVolume,
                session.ExerciseId,
                session.RepresentativeSet.CompletedUtc,
                session.RepresentativeSet,
                session.RepresentativeSet.Load,
                session.RepresentativeSet.Repetitions,
                session.Volume,
                $"Greatest working session volume for this exercise: {session.Volume}."));

    private static bool IsWorkingSet(SetEntry set) => !set.IsWarmUp && set.Repetitions > 0;
}
