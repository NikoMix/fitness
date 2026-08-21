namespace Forge.Domain.Workout;

/// <summary>
/// Where the user is inside a superset or circuit, and where they go next.
/// </summary>
/// <param name="Next">The exercise to perform next.</param>
/// <param name="Position">Zero-based index of <paramref name="Next"/> within the group.</param>
/// <param name="RoundCompleted">Whether moving to <paramref name="Next"/> closed a full round.</param>
/// <param name="CompletedRounds">Rounds in which every member of the group has been performed.</param>
public sealed record SupersetStep(ActiveWorkoutExercise Next, int Position, bool RoundCompleted, int CompletedRounds);

/// <summary>
/// Ordering and round accounting for supersets and circuits.
/// </summary>
/// <remarks>
/// <para>
/// A superset is just an ordered ring of exercises that the user walks around: A, B, A, B, or
/// A, B, C, A, B, C for a circuit. The only interesting state is where in the ring you are and
/// how many complete laps you have done, and both are derived here rather than stored.
/// </para>
/// <para>
/// Deriving the round count from the logged sets rather than from a counter is deliberate.
/// A stored counter drifts the moment the user edits or deletes a set, skips a station, or the
/// app is killed between two stations, and the drift is invisible until the summary is wrong.
/// The completed sets are the record of what actually happened, so the round count is defined
/// as the number of laps every member has genuinely finished.
/// </para>
/// </remarks>
public static class SupersetCycle
{
    /// <summary>Returns the members of one group, in queue order.</summary>
    /// <param name="queue">The full exercise queue.</param>
    /// <param name="groupId">The superset group identifier.</param>
    /// <returns>The group members, or an empty list when the group does not exist.</returns>
    public static IReadOnlyList<ActiveWorkoutExercise> Members(IEnumerable<ActiveWorkoutExercise> queue, Guid groupId)
    {
        ArgumentNullException.ThrowIfNull(queue);
        return [.. queue.Where(exercise => exercise.SupersetGroupId == groupId)];
    }

    /// <summary>
    /// Counts the rounds in which every member of the group has been performed.
    /// </summary>
    /// <param name="members">The group members.</param>
    /// <param name="completedSets">All sets logged in the session.</param>
    /// <returns>The number of complete laps around the group.</returns>
    public static int CompletedRounds(
        IReadOnlyList<ActiveWorkoutExercise> members,
        IEnumerable<CompletedWorkoutSet> completedSets)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(completedSets);

        if (members.Count == 0)
        {
            return 0;
        }

        // Warm-up sets are excluded: ramping into the first station is not a lap of the circuit,
        // and counting it would report a round the user never actually completed.
        var working = completedSets.Where(set => !set.IsWarmUp).ToArray();
        var minimum = int.MaxValue;
        foreach (var member in members)
        {
            var performed = working.Count(set => set.ExerciseId == member.ExerciseId);
            if (performed < minimum)
            {
                minimum = performed;
            }
        }

        return minimum == int.MaxValue ? 0 : minimum;
    }

    /// <summary>
    /// Whether every station in the group has now been performed the same number of times.
    /// </summary>
    /// <remarks>
    /// This is the condition for shared rest. Starting rest after the first station would defeat
    /// the point of a superset, and deriving it from position alone would be wrong the moment the
    /// user skips a station or repeats one, so it is derived from what was actually logged.
    /// </remarks>
    /// <param name="members">The group members.</param>
    /// <param name="completedSets">All sets logged in the session.</param>
    /// <returns><see langword="true"/> when a full round has just been finished.</returns>
    public static bool IsRoundComplete(
        IReadOnlyList<ActiveWorkoutExercise> members,
        IEnumerable<CompletedWorkoutSet> completedSets)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(completedSets);

        if (members.Count < 2)
        {
            return false;
        }

        var working = completedSets.Where(set => !set.IsWarmUp).ToArray();
        var counts = members.Select(member => working.Count(set => set.ExerciseId == member.ExerciseId)).ToArray();
        return counts.Min() > 0 && counts.Min() == counts.Max();
    }

    /// <summary>
    /// Determines the next station in the group.
    /// </summary>
    /// <param name="members">The group members, in queue order.</param>
    /// <param name="currentExerciseId">The station just finished.</param>
    /// <param name="completedSets">All sets logged in the session.</param>
    /// <returns>The next step, or <see langword="null"/> when the group has fewer than two members.</returns>
    public static SupersetStep? Next(
        IReadOnlyList<ActiveWorkoutExercise> members,
        Guid currentExerciseId,
        IEnumerable<CompletedWorkoutSet> completedSets)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(completedSets);

        if (members.Count < 2)
        {
            return null;
        }

        var currentIndex = IndexOf(members, currentExerciseId);

        // An unknown current exercise means the user jumped into the group from outside it, so
        // the honest answer is to start at the top rather than guess a midpoint.
        var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % members.Count;
        var roundCompleted = currentIndex >= 0 && nextIndex == 0;

        return new SupersetStep(
            members[nextIndex],
            nextIndex,
            roundCompleted,
            CompletedRounds(members, completedSets));
    }

    /// <summary>Finds the zero-based position of an exercise within a group.</summary>
    /// <param name="members">The group members.</param>
    /// <param name="exerciseId">The exercise to locate.</param>
    /// <returns>The index, or <c>-1</c> when the exercise is not a member.</returns>
    public static int IndexOf(IReadOnlyList<ActiveWorkoutExercise> members, Guid exerciseId)
    {
        ArgumentNullException.ThrowIfNull(members);
        for (var index = 0; index < members.Count; index++)
        {
            if (members[index].ExerciseId == exerciseId)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Builds the human-readable station label, for example "B of A-B-C".</summary>
    /// <param name="position">Zero-based position within the group.</param>
    /// <param name="memberCount">Number of members in the group.</param>
    /// <returns>A short label, or an empty string when the group is not a superset.</returns>
    public static string StationLabel(int position, int memberCount)
    {
        if (memberCount < 2 || position < 0 || position >= memberCount)
        {
            return string.Empty;
        }

        var letters = Enumerable.Range(0, memberCount).Select(index => (char)('A' + index));
        return $"{(char)('A' + position)} of {string.Join('-', letters)}";
    }
}
