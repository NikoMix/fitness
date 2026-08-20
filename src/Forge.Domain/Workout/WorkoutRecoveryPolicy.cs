using Forge.Domain.Training;

namespace Forge.Domain.Workout;

/// <summary>Classifies interrupted workouts for recovery.</summary>
public static class WorkoutRecoveryPolicy
{
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromHours(12);

    public static WorkoutRecoveryKind Classify(WorkoutSession? session, DateTimeOffset nowUtc, TimeSpan? staleAfter = null)
    {
        if (session is null || !session.IsInProgress)
        {
            return WorkoutRecoveryKind.None;
        }

        return nowUtc - session.StartedUtc > (staleAfter ?? DefaultStaleAfter)
            ? WorkoutRecoveryKind.Stale
            : WorkoutRecoveryKind.Resume;
    }
}

public enum WorkoutRecoveryKind
{
    None = 0,
    Resume = 1,
    Stale = 2
}
