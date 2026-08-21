using Forge.Domain.Workout;

namespace Forge.App.Features.Workout;

internal sealed class WorkoutClock : IWorkoutClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
