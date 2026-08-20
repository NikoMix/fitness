namespace Forge.Domain.Workout;

/// <summary>Supplies wall-clock time to workout state machines.</summary>
public interface IWorkoutClock
{
    /// <summary>The current UTC wall-clock time.</summary>
    DateTimeOffset UtcNow { get; }
}
