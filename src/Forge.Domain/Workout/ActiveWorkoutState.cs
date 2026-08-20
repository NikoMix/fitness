using Forge.Domain.Common;
using Forge.Domain.Measurement;
using Forge.Domain.Training;

namespace Forge.Domain.Workout;

/// <summary>Recoverable in-progress workout aggregate used to rebuild the active screen.</summary>
public sealed class ActiveWorkoutState : Entity
{
    public required Guid WorkoutSessionId { get; init; }

    public DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset? CompletedUtc { get; set; }

    public Guid? CurrentExerciseId { get; set; }

    public string CurrentExerciseName { get; set; } = "Workout";

    public List<ActiveWorkoutExercise> ExerciseQueue { get; set; } = [];

    public List<CompletedWorkoutSet> CompletedSets { get; set; } = [];

    public RestTimer? ActiveRestTimer { get; set; }

    public bool IsCompleted => CompletedUtc is not null;

    public TimeSpan Elapsed(DateTimeOffset now) => (CompletedUtc ?? now) - StartedUtc;

    public static ActiveWorkoutState Start(Guid workoutSessionId, DateTimeOffset startedUtc, ActiveWorkoutExercise firstExercise)
    {
        ArgumentNullException.ThrowIfNull(firstExercise);
        return new ActiveWorkoutState
        {
            WorkoutSessionId = workoutSessionId,
            StartedUtc = startedUtc,
            CurrentExerciseId = firstExercise.ExerciseId,
            CurrentExerciseName = firstExercise.Name,
            ExerciseQueue = [firstExercise]
        };
    }

    public CompletedWorkoutSet LogSet(
        Mass load,
        int repetitions,
        bool isWarmUp,
        bool toFailure,
        int? repsInReserve,
        DateTimeOffset completedUtc,
        string? primaryMuscle = null)
    {
        EnsureNotCompleted();
        ArgumentOutOfRangeException.ThrowIfLessThan(repetitions, 0);

        var exerciseId = CurrentExerciseId ?? Guid.Empty;
        var ordinal = CompletedSets.Count(s => s.ExerciseId == exerciseId) + 1;
        var set = new CompletedWorkoutSet(
            Guid.CreateVersion7(),
            WorkoutSessionId,
            exerciseId,
            CurrentExerciseName,
            primaryMuscle,
            ordinal,
            load.Kilograms,
            repetitions,
            isWarmUp,
            toFailure,
            repsInReserve,
            completedUtc);

        CompletedSets.Add(set);
        return set;
    }

    public void SetCurrentExercise(ActiveWorkoutExercise exercise)
    {
        EnsureNotCompleted();
        ArgumentNullException.ThrowIfNull(exercise);
        CurrentExerciseId = exercise.ExerciseId;
        CurrentExerciseName = exercise.Name;
        if (ExerciseQueue.All(item => item.ExerciseId != exercise.ExerciseId))
        {
            ExerciseQueue.Add(exercise);
        }
    }

    public void ReorderExercise(Guid exerciseId, int newIndex)
    {
        EnsureNotCompleted();
        var current = ExerciseQueue.SingleOrDefault(e => e.ExerciseId == exerciseId);
        if (current is null)
        {
            return;
        }

        ExerciseQueue.Remove(current);
        ExerciseQueue.Insert(Math.Clamp(newIndex, 0, ExerciseQueue.Count), current);
    }

    public void SkipCurrentExercise()
    {
        EnsureNotCompleted();
        var currentIndex = ExerciseQueue.FindIndex(e => e.ExerciseId == CurrentExerciseId);
        var next = ExerciseQueue.Skip(currentIndex + 1).FirstOrDefault() ?? ExerciseQueue.FirstOrDefault(e => e.ExerciseId != CurrentExerciseId);
        if (next is not null)
        {
            CurrentExerciseId = next.ExerciseId;
            CurrentExerciseName = next.Name;
        }
    }

    public void StartRest(RestTimer timer)
    {
        EnsureNotCompleted();
        ActiveRestTimer = timer ?? throw new ArgumentNullException(nameof(timer));
    }

    public void ClearRest() => ActiveRestTimer = null;

    public void Complete(DateTimeOffset completedUtc)
    {
        EnsureNotCompleted();
        CompletedUtc = completedUtc;
        ActiveRestTimer = null;
    }

    public static SetEntry ToSetEntry(CompletedWorkoutSet set) => new()
    {
        Id = set.SetEntryId,
        WorkoutSessionId = set.WorkoutSessionId,
        ExerciseId = set.ExerciseId,
        Ordinal = set.Ordinal,
        Load = Mass.FromKilograms(set.LoadKilograms),
        Repetitions = set.Repetitions,
        IsWarmUp = set.IsWarmUp,
        ToFailure = set.ToFailure,
        RepsInReserve = set.RepsInReserve,
        CompletedUtc = set.CompletedUtc
    };

    private void EnsureNotCompleted()
    {
        if (IsCompleted)
        {
            throw new InvalidOperationException("A completed workout cannot be changed.");
        }
    }
}

public sealed record ActiveWorkoutExercise(Guid ExerciseId, string Name, string? PrimaryMuscle, decimal TargetLoadKilograms, int TargetRepetitions);

public sealed record CompletedWorkoutSet(
    Guid SetEntryId,
    Guid WorkoutSessionId,
    Guid ExerciseId,
    string ExerciseName,
    string? PrimaryMuscle,
    int Ordinal,
    decimal LoadKilograms,
    int Repetitions,
    bool IsWarmUp,
    bool ToFailure,
    int? RepsInReserve,
    DateTimeOffset CompletedUtc);
