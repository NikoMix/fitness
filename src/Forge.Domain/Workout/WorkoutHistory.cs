using Forge.Domain.Measurement;
using Forge.Domain.Training;

namespace Forge.Domain.Workout;

/// <summary>One past session as it appears in the history list.</summary>
/// <param name="WorkoutSessionId">The session identifier, used to open its summary.</param>
/// <param name="Title">Session title.</param>
/// <param name="StartedUtc">When the session started.</param>
/// <param name="CompletedUtc">When it finished, or <see langword="null"/> if it never did.</param>
/// <param name="Duration">How long it ran.</param>
/// <param name="WorkingSetCount">Number of non-warm-up sets.</param>
/// <param name="TotalVolume">Working volume, load multiplied by repetitions.</param>
/// <param name="ExerciseNames">Exercises performed, ordered by how much work they took.</param>
/// <param name="IsInProgress">Whether the session was never completed.</param>
public sealed record WorkoutHistoryEntry(
    Guid WorkoutSessionId,
    string Title,
    DateTimeOffset StartedUtc,
    DateTimeOffset? CompletedUtc,
    TimeSpan Duration,
    int WorkingSetCount,
    Mass TotalVolume,
    IReadOnlyList<string> ExerciseNames,
    bool IsInProgress);

/// <summary>
/// Projects stored sessions into the history list.
/// </summary>
/// <remarks>
/// History is ordered newest first because the overwhelmingly common question is "what did I do
/// last time?", and it is ordered by when a session finished rather than when it started so that
/// a session begun late at night and finished after midnight does not jump behind the one that
/// followed it.
/// </remarks>
public static class WorkoutHistoryBuilder
{
    /// <summary>Builds the history list, newest first.</summary>
    /// <param name="sessions">Sessions to project.</param>
    /// <param name="exercises">Exercise catalogue used to resolve names.</param>
    /// <param name="nowUtc">Current time, used to measure sessions that never completed.</param>
    /// <returns>History entries, newest first.</returns>
    public static IReadOnlyList<WorkoutHistoryEntry> Build(
        IEnumerable<WorkoutSession> sessions,
        IReadOnlyDictionary<Guid, string> exercises,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(exercises);

        return
        [
            .. sessions
                .Select(session => Project(session, exercises, nowUtc))
                .OrderByDescending(entry => entry.CompletedUtc ?? entry.StartedUtc)
                .ThenByDescending(entry => entry.StartedUtc)
        ];
    }

    private static WorkoutHistoryEntry Project(
        WorkoutSession session,
        IReadOnlyDictionary<Guid, string> exercises,
        DateTimeOffset nowUtc)
    {
        var workingSets = session.Sets.Where(set => !set.IsWarmUp).ToArray();
        var volume = workingSets.Aggregate(Mass.Zero, (sum, set) => sum + set.Volume);

        var names = workingSets
            .GroupBy(set => set.ExerciseId)
            .OrderByDescending(group => group.Sum(set => set.Volume.Kilograms))
            .ThenBy(group => group.Min(set => set.CompletedUtc))
            .Select(group => exercises.TryGetValue(group.Key, out var name) ? name : "Exercise")
            .ToArray();

        return new WorkoutHistoryEntry(
            session.Id,
            string.IsNullOrWhiteSpace(session.Title) ? "Workout" : session.Title,
            session.StartedUtc,
            session.CompletedUtc,
            session.Duration(session.CompletedUtc ?? nowUtc),
            workingSets.Length,
            volume,
            names,
            session.IsInProgress);
    }
}
