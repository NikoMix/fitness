namespace Forge.Domain.Training;

/// <summary>Which slice of the library a filter applies to.</summary>
public enum ExerciseScope
{
    /// <summary>Everything in the library.</summary>
    All = 0,

    /// <summary>Only exercises the user pinned as favourites.</summary>
    Favourites = 1,

    /// <summary>Only exercises the user has opened or trained recently.</summary>
    RecentlyUsed = 2,

    /// <summary>Only exercises the user created themselves.</summary>
    UserCreated = 3
}

/// <summary>
/// Filtering criteria for the exercise catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Every axis takes a set rather than a single value, because "show me dumbbell or bodyweight
/// work" is the question people actually ask in a gym. Within one axis the values are combined
/// with OR; across axes they are combined with AND. A single-value filter is just a set of one,
/// so nothing needs a second code path.
/// </para>
/// <para>
/// This is a class rather than a record on purpose. Records advertise value equality, and the
/// compiler-generated equality would compare the sets by reference, so two filters holding the
/// same criteria would report themselves as different. Better to not make the promise.
/// </para>
/// </remarks>
public sealed class ExerciseFilter
{
    private static readonly IReadOnlySet<string> NoStrings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<MovementPattern> NoPatterns = new HashSet<MovementPattern>();
    private static readonly IReadOnlySet<ExerciseDifficulty> NoDifficulties = new HashSet<ExerciseDifficulty>();

    /// <summary>
    /// Movement patterns that a declared injury makes a poor idea.
    /// </summary>
    /// <remarks>
    /// This is coarse by design. Forge cannot assess an injury, so it errs toward removing a
    /// whole pattern rather than guessing which individual movements are tolerable. The user
    /// keeps the final say: nothing here is a diagnosis, and the exclusions only ever narrow a
    /// browsing list rather than block anything.
    /// </remarks>
    private static readonly Dictionary<string, MovementPattern[]> InjuryMovementExclusions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["knee"] = [MovementPattern.Squat, MovementPattern.Lunge],
            ["hip"] = [MovementPattern.Hinge, MovementPattern.Squat, MovementPattern.Lunge],
            ["lower back"] = [MovementPattern.Hinge, MovementPattern.Carry],
            ["back"] = [MovementPattern.Hinge, MovementPattern.Carry],
            ["shoulder"] = [MovementPattern.Push, MovementPattern.Pull],
            ["elbow"] = [MovementPattern.Push, MovementPattern.Pull],
            ["wrist"] = [MovementPattern.Push, MovementPattern.Carry],
            ["ankle"] = [MovementPattern.Lunge, MovementPattern.Squat, MovementPattern.Cardio],
            ["neck"] = [MovementPattern.Carry, MovementPattern.Core]
        };

    /// <summary>A filter that accepts every exercise.</summary>
    public static ExerciseFilter None { get; } = new();

    /// <summary>
    /// The body areas Forge can map to movement patterns, in their canonical spelling.
    /// </summary>
    /// <remarks>
    /// Published so that whatever turns a user's free text into injuries reads its vocabulary from
    /// the same place the exclusions are defined. A second, hand-copied list would drift, and the
    /// way it would fail is by quietly recognising nothing.
    /// </remarks>
    public static IReadOnlyCollection<string> RecognisedInjuryAreas { get; } = InjuryMovementExclusions.Keys.ToArray();

    /// <summary>Muscles to include, matched against primary and secondary muscles.</summary>
    public IReadOnlySet<string> Muscles { get; init; } = NoStrings;

    /// <summary>Equipment to include. Bodyweight movements match the name "Bodyweight".</summary>
    public IReadOnlySet<string> Equipment { get; init; } = NoStrings;

    /// <summary>Movement patterns to include.</summary>
    public IReadOnlySet<MovementPattern> Patterns { get; init; } = NoPatterns;

    /// <summary>Difficulties to include.</summary>
    public IReadOnlySet<ExerciseDifficulty> Difficulties { get; init; } = NoDifficulties;

    /// <summary>Movement patterns excluded because of a declared injury.</summary>
    public IReadOnlySet<MovementPattern> ExcludedMovements { get; init; } = NoPatterns;

    /// <summary>Which slice of the library to draw from.</summary>
    public ExerciseScope Scope { get; init; } = ExerciseScope.All;

    /// <summary>Whether the filter has no criteria and therefore accepts everything.</summary>
    public bool IsEmpty =>
        Muscles.Count == 0
        && Equipment.Count == 0
        && Patterns.Count == 0
        && Difficulties.Count == 0
        && ExcludedMovements.Count == 0
        && Scope == ExerciseScope.All;

    /// <summary>How many criteria the user has actively chosen, for showing a "clear" affordance.</summary>
    public int ActiveCriteriaCount =>
        Muscles.Count
        + Equipment.Count
        + Patterns.Count
        + Difficulties.Count
        + (Scope == ExerciseScope.All ? 0 : 1);

    /// <summary>Builds a filter from loose criteria, ignoring blanks.</summary>
    /// <param name="muscles">Muscles to include.</param>
    /// <param name="equipment">Equipment to include.</param>
    /// <param name="patterns">Movement patterns to include.</param>
    /// <param name="difficulties">Difficulties to include.</param>
    /// <param name="scope">Which slice of the library to draw from.</param>
    /// <param name="injuries">Declared injuries whose movement patterns should be excluded.</param>
    /// <returns>A filter combining the supplied criteria.</returns>
    public static ExerciseFilter For(
        IEnumerable<string>? muscles = null,
        IEnumerable<string>? equipment = null,
        IEnumerable<MovementPattern>? patterns = null,
        IEnumerable<ExerciseDifficulty>? difficulties = null,
        ExerciseScope scope = ExerciseScope.All,
        IEnumerable<string>? injuries = null) => new()
        {
            Muscles = TextSet(muscles),
            Equipment = EquipmentSet(equipment),
            Patterns = patterns?.ToHashSet() ?? NoPatterns,
            Difficulties = difficulties?.ToHashSet() ?? NoDifficulties,
            ExcludedMovements = ExcludedPatterns(injuries),
            Scope = scope
        };

    /// <summary>Creates a filter whose excluded movement patterns are derived from injuries.</summary>
    /// <param name="injuries">Declared injuries, for example "knee" or "lower back".</param>
    /// <returns>A filter that excludes the contraindicated movement patterns.</returns>
    public static ExerciseFilter FromDeclaredInjuries(IEnumerable<string> injuries)
    {
        ArgumentNullException.ThrowIfNull(injuries);
        return new ExerciseFilter { ExcludedMovements = ExcludedPatterns(injuries) };
    }

    /// <summary>Returns whether an exercise satisfies every axis of the filter.</summary>
    /// <param name="exercise">The exercise to test.</param>
    /// <returns><see langword="true"/> when the exercise passes all criteria.</returns>
    public bool Matches(Exercise exercise)
    {
        ArgumentNullException.ThrowIfNull(exercise);

        if (ExcludedMovements.Count > 0 && ExcludedMovements.Contains(exercise.Pattern))
        {
            return false;
        }

        var scopeAllows = Scope switch
        {
            ExerciseScope.Favourites => exercise.IsFavourite,
            ExerciseScope.RecentlyUsed => exercise.LastUsedUtc is not null,
            ExerciseScope.UserCreated => exercise.IsUserCreated,
            _ => true
        };

        if (!scopeAllows)
        {
            return false;
        }

        if (Patterns.Count > 0 && !Patterns.Contains(exercise.Pattern))
        {
            return false;
        }

        if (Difficulties.Count > 0 && !Difficulties.Contains(exercise.Difficulty))
        {
            return false;
        }

        if (Equipment.Count > 0 && !Equipment.Contains(EquipmentAvailability.Normalise(exercise.Equipment)))
        {
            return false;
        }

        return Muscles.Count == 0 || Muscles.Overlaps(MusclesOf(exercise));
    }

    private static IEnumerable<string> MusclesOf(Exercise exercise)
    {
        if (!string.IsNullOrWhiteSpace(exercise.PrimaryMuscle))
        {
            yield return exercise.PrimaryMuscle.Trim();
        }

        foreach (var muscle in exercise.SecondaryMuscles)
        {
            if (!string.IsNullOrWhiteSpace(muscle))
            {
                yield return muscle.Trim();
            }
        }
    }

    private static IReadOnlySet<MovementPattern> ExcludedPatterns(IEnumerable<string>? injuries)
    {
        if (injuries is null)
        {
            return NoPatterns;
        }

        var excluded = injuries
            .Where(injury => !string.IsNullOrWhiteSpace(injury))
            .SelectMany(injury => InjuryMovementExclusions.TryGetValue(injury.Trim(), out var patterns)
                ? patterns
                : [])
            .ToHashSet();

        return excluded.Count == 0 ? NoPatterns : excluded;
    }

    private static IReadOnlySet<string> TextSet(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return NoStrings;
        }

        var set = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return set.Count == 0 ? NoStrings : set;
    }

    private static IReadOnlySet<string> EquipmentSet(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return NoStrings;
        }

        var set = values
            .Select(EquipmentAvailability.Normalise)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return set.Count == 0 ? NoStrings : set;
    }
}
