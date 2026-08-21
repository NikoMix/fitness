using System.Globalization;

namespace Forge.Domain.Training;

/// <summary>Which part of an exercise a search query matched.</summary>
public enum ExerciseSearchField
{
    /// <summary>The exercise name.</summary>
    Name = 0,

    /// <summary>The primary muscle worked.</summary>
    PrimaryMuscle = 1,

    /// <summary>One of the secondary muscles worked.</summary>
    SecondaryMuscle = 2,

    /// <summary>The equipment required.</summary>
    Equipment = 3,

    /// <summary>The movement pattern.</summary>
    Pattern = 4,

    /// <summary>The difficulty band.</summary>
    Difficulty = 5
}

/// <summary>One ranked search hit.</summary>
/// <param name="Exercise">The matching exercise.</param>
/// <param name="Score">Relevance score. Higher is a better match.</param>
/// <param name="BestField">The strongest field the query matched.</param>
/// <param name="MatchExplanation">
/// A short note on why the result appeared, so a hit that does not contain the typed text in
/// its name does not look like a bug.
/// </param>
public sealed record ExerciseSearchResult(
    Exercise Exercise,
    int Score,
    ExerciseSearchField BestField,
    string MatchExplanation);

/// <summary>
/// A prepared, offline search index over the exercise library.
/// </summary>
/// <remarks>
/// <para>
/// Search runs on every keystroke, so the comparable text is folded to a single case once when
/// the index is built rather than repeatedly while the user types. Everything happens in memory
/// against the already-loaded catalogue: the library has to work with no network at all, and a
/// gym is exactly where a phone has no signal.
/// </para>
/// <para>
/// Matching is AND across the words typed and OR across the fields searched, which is what makes
/// "dumbbell press" behave the way people expect. Scoring is tiered rather than fuzzy: an exact
/// name beats a name prefix, which beats a word prefix, which beats a muscle or equipment hit.
/// Fuzzy scoring reorders results in ways nobody can predict, and an exercise library is
/// something people learn the shape of and expect to stay put.
/// </para>
/// </remarks>
public sealed class ExerciseSearchIndex
{
    private const int NameExact = 100;
    private const int NamePrefix = 80;
    private const int NameWordExact = 76;
    private const int NameWordPrefix = 70;
    private const int NameContains = 55;
    private const int PrimaryMuscleExact = 50;
    private const int PrimaryMusclePrefix = 44;
    private const int PrimaryMuscleContains = 38;
    private const int SecondaryMusclePrefix = 32;
    private const int SecondaryMuscleContains = 28;
    private const int EquipmentPrefix = 24;
    private const int EquipmentContains = 20;
    private const int PatternPrefix = 22;
    private const int DifficultyPrefix = 16;

    private const int WholeQueryNameExactBonus = 400;
    private const int WholeQueryNamePrefixBonus = 200;
    private const int WholeQueryNameWordPrefixBonus = 120;

    private static readonly char[] WordSeparators = [' ', '-', '/', '(', ')', ',', '.', '\'', '\t'];

    private readonly List<Entry> entries;

    /// <summary>Builds an index over a catalogue snapshot.</summary>
    /// <param name="catalogue">The exercises to index.</param>
    public ExerciseSearchIndex(IEnumerable<Exercise> catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        entries = catalogue.Select(Entry.Create).ToList();

        Muscles = entries
            .SelectMany(entry => entry.DisplayMuscles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Equipment = entries
            .Select(entry => entry.DisplayEquipment)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Patterns = entries
            .Select(entry => entry.Exercise.Pattern)
            .Where(pattern => pattern is not MovementPattern.Unspecified)
            .Distinct()
            .Order()
            .ToList();
    }

    /// <summary>How many exercises are indexed.</summary>
    public int Count => entries.Count;

    /// <summary>Every distinct muscle in the indexed catalogue, sorted for display.</summary>
    public IReadOnlyList<string> Muscles { get; }

    /// <summary>Every distinct equipment name in the indexed catalogue, sorted for display.</summary>
    public IReadOnlyList<string> Equipment { get; }

    /// <summary>Every movement pattern present in the indexed catalogue, in enum order.</summary>
    public IReadOnlyList<MovementPattern> Patterns { get; }

    /// <summary>
    /// Runs a filtered, ranked search.
    /// </summary>
    /// <param name="query">
    /// The text typed by the user. Blank returns the filtered library in browse order rather
    /// than nothing, so one code path serves both browsing and searching.
    /// </param>
    /// <param name="filter">Criteria to apply before ranking, or <see langword="null"/> for none.</param>
    /// <param name="limit">Maximum results to return.</param>
    /// <returns>Ranked results, best first.</returns>
    public IReadOnlyList<ExerciseSearchResult> Search(
        string? query,
        ExerciseFilter? filter = null,
        int limit = int.MaxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);

        var effectiveFilter = filter ?? ExerciseFilter.None;
        var candidates = effectiveFilter.IsEmpty
            ? entries
            : entries.Where(entry => effectiveFilter.Matches(entry.Exercise));

        var tokens = Tokenise(query);
        return tokens.Length == 0
            ? Browse(candidates, limit)
            : Rank(candidates, tokens, limit);
    }

    private static List<ExerciseSearchResult> Browse(IEnumerable<Entry> candidates, int limit)
        => candidates
            .OrderByDescending(entry => entry.Exercise.IsFavourite)
            .ThenByDescending(entry => entry.Exercise.LastUsedUtc)
            .ThenBy(entry => entry.Exercise.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(entry => new ExerciseSearchResult(entry.Exercise, 0, ExerciseSearchField.Name, string.Empty))
            .ToList();

    private static List<ExerciseSearchResult> Rank(IEnumerable<Entry> candidates, string[] tokens, int limit)
    {
        var whole = string.Join(' ', tokens);
        var results = new List<ExerciseSearchResult>();

        foreach (var entry in candidates)
        {
            var total = 0;
            var bestField = ExerciseSearchField.Name;
            var bestFieldScore = 0;
            var matchedEveryToken = true;

            foreach (var token in tokens)
            {
                var (score, field) = entry.ScoreToken(token);
                if (score == 0)
                {
                    matchedEveryToken = false;
                    break;
                }

                total += score;
                if (score > bestFieldScore)
                {
                    bestFieldScore = score;
                    bestField = field;
                }
            }

            if (!matchedEveryToken)
            {
                continue;
            }

            total += entry.WholeQueryBonus(whole);
            results.Add(new ExerciseSearchResult(entry.Exercise, total, bestField, entry.Explain(bestField)));
        }

        // Favourites and recency break ties only. Letting them add to the score would let a
        // pinned exercise outrank the thing the user actually typed.
        return results
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.Exercise.IsFavourite)
            .ThenByDescending(result => result.Exercise.LastUsedUtc)
            .ThenBy(result => result.Exercise.Name.Length)
            .ThenBy(result => result.Exercise.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static string[] Tokenise(string? query)
        => string.IsNullOrWhiteSpace(query)
            ? []
            : Fold(query).Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Upper-case folding rather than lower-case: upper is the round-trip-safe direction for
    // case-insensitive comparison keys, and it keeps the fixed strings culture-independent.
    private static string Fold(string value) => value.ToUpperInvariant();

    private sealed record Entry(
        Exercise Exercise,
        string Name,
        string[] NameWords,
        string PrimaryMuscle,
        string[] SecondaryMuscles,
        string Equipment,
        string Pattern,
        string Difficulty,
        IReadOnlyList<string> DisplayMuscles,
        string DisplayEquipment)
    {
        public static Entry Create(Exercise exercise)
        {
            ArgumentNullException.ThrowIfNull(exercise);

            var displayEquipment = EquipmentAvailability.Normalise(exercise.Equipment);
            var displayMuscles = new List<string>();
            if (!string.IsNullOrWhiteSpace(exercise.PrimaryMuscle))
            {
                displayMuscles.Add(exercise.PrimaryMuscle.Trim());
            }

            displayMuscles.AddRange(exercise.SecondaryMuscles
                .Where(muscle => !string.IsNullOrWhiteSpace(muscle))
                .Select(muscle => muscle.Trim()));

            var name = Fold(exercise.Name);

            return new Entry(
                exercise,
                name,
                name.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Fold(exercise.PrimaryMuscle ?? string.Empty),
                [.. exercise.SecondaryMuscles.Where(muscle => !string.IsNullOrWhiteSpace(muscle)).Select(Fold)],
                Fold(displayEquipment),
                Fold(exercise.Pattern.ToDisplayName()),
                Fold(exercise.Difficulty.ToString()),
                displayMuscles,
                displayEquipment);
        }

        public (int Score, ExerciseSearchField Field) ScoreToken(string token)
        {
            if (Name.Equals(token, StringComparison.Ordinal))
            {
                return (NameExact, ExerciseSearchField.Name);
            }

            if (Name.StartsWith(token, StringComparison.Ordinal))
            {
                return (NamePrefix, ExerciseSearchField.Name);
            }

            if (NameWords.Any(word => word.Equals(token, StringComparison.Ordinal)))
            {
                return (NameWordExact, ExerciseSearchField.Name);
            }

            if (NameWords.Any(word => word.StartsWith(token, StringComparison.Ordinal)))
            {
                return (NameWordPrefix, ExerciseSearchField.Name);
            }

            if (Name.Contains(token, StringComparison.Ordinal))
            {
                return (NameContains, ExerciseSearchField.Name);
            }

            if (PrimaryMuscle.Length > 0)
            {
                if (PrimaryMuscle.Equals(token, StringComparison.Ordinal))
                {
                    return (PrimaryMuscleExact, ExerciseSearchField.PrimaryMuscle);
                }

                if (PrimaryMuscle.StartsWith(token, StringComparison.Ordinal))
                {
                    return (PrimaryMusclePrefix, ExerciseSearchField.PrimaryMuscle);
                }

                if (PrimaryMuscle.Contains(token, StringComparison.Ordinal))
                {
                    return (PrimaryMuscleContains, ExerciseSearchField.PrimaryMuscle);
                }
            }

            if (SecondaryMuscles.Any(muscle => muscle.StartsWith(token, StringComparison.Ordinal)))
            {
                return (SecondaryMusclePrefix, ExerciseSearchField.SecondaryMuscle);
            }

            if (SecondaryMuscles.Any(muscle => muscle.Contains(token, StringComparison.Ordinal)))
            {
                return (SecondaryMuscleContains, ExerciseSearchField.SecondaryMuscle);
            }

            if (Equipment.StartsWith(token, StringComparison.Ordinal))
            {
                return (EquipmentPrefix, ExerciseSearchField.Equipment);
            }

            if (Equipment.Contains(token, StringComparison.Ordinal))
            {
                return (EquipmentContains, ExerciseSearchField.Equipment);
            }

            if (Pattern.StartsWith(token, StringComparison.Ordinal))
            {
                return (PatternPrefix, ExerciseSearchField.Pattern);
            }

            return Difficulty.StartsWith(token, StringComparison.Ordinal)
                ? (DifficultyPrefix, ExerciseSearchField.Difficulty)
                : (0, ExerciseSearchField.Name);
        }

        public int WholeQueryBonus(string whole)
        {
            if (Name.Equals(whole, StringComparison.Ordinal))
            {
                return WholeQueryNameExactBonus;
            }

            if (Name.StartsWith(whole, StringComparison.Ordinal))
            {
                return WholeQueryNamePrefixBonus;
            }

            return NameWords.Any(word => word.StartsWith(whole, StringComparison.Ordinal))
                ? WholeQueryNameWordPrefixBonus
                : 0;
        }

        public string Explain(ExerciseSearchField field) => field switch
        {
            ExerciseSearchField.PrimaryMuscle => string.Format(
                CultureInfo.CurrentCulture,
                "Trains the {0}",
                Exercise.PrimaryMuscle),
            ExerciseSearchField.SecondaryMuscle => "Also works this muscle",
            ExerciseSearchField.Equipment => string.Format(
                CultureInfo.CurrentCulture,
                "Uses {0}",
                DisplayEquipment),
            ExerciseSearchField.Pattern => string.Format(
                CultureInfo.CurrentCulture,
                "{0} pattern",
                Exercise.Pattern.ToDisplayName()),
            ExerciseSearchField.Difficulty => string.Format(
                CultureInfo.CurrentCulture,
                "{0} difficulty",
                Exercise.Difficulty),
            _ => string.Empty
        };
    }
}
