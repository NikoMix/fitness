using System.Text;

namespace Forge.Domain.Training;

/// <summary>
/// What Forge was and was not able to make of a free-text movement limitation.
/// </summary>
/// <remarks>
/// <para>
/// Onboarding asks "anything Forge should work around?" and stores the answer verbatim, because a
/// fixed list of injuries would be wrong for most people who have one. <see cref="ExerciseFilter"/>
/// works the other way round: it knows a small, deliberately coarse set of body areas and the
/// movement patterns each one makes a poor idea. This type is the bridge, and it exists as its own
/// type because the interesting half of that bridge is the failure case.
/// </para>
/// <para>
/// Anything Forge cannot place is kept in <see cref="UninterpretedPhrases"/> exactly as it was
/// typed, and every caller is expected to show it. Dropping it silently would leave someone who
/// declared a limitation looking at a list that claims to account for it, which is a worse outcome
/// than never having offered the filter: they told the app, the app said nothing, and they have no
/// way to know it understood none of it.
/// </para>
/// <para>
/// Recognition is deliberately literal. A synonym is only accepted when the phrase names the same
/// joint or region as one of <see cref="ExerciseFilter.RecognisedInjuryAreas"/> - "lumbar" is the
/// lower back, "rotator cuff" is the shoulder. Symptoms and individual muscles are left
/// uninterpreted on purpose: inferring a region from them would be Forge guessing at a diagnosis,
/// and saying "I could not read this" is the honest answer.
/// </para>
/// </remarks>
public sealed class MovementLimitationDeclaration
{
    private static readonly char[] PhraseSeparators = [',', ';', '\n', '\r', '|', '/', '+', '&'];
    private static readonly string[] ConjunctionSeparators = [" and ", " plus ", " also "];

    /// <summary>
    /// Free-text spellings that name the same region as a recognised area.
    /// </summary>
    /// <remarks>
    /// Every entry here has to be defensible as "this word means that joint", not as "people with
    /// this usually cannot do that". The second kind of entry is how a browsing filter turns into
    /// an unqualified medical opinion.
    /// </remarks>
    private static readonly Dictionary<string, string> AreaSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lumbar"] = "lower back",
        ["low back"] = "lower back",
        ["thoracic"] = "back",
        ["upper back"] = "back",
        ["patella"] = "knee",
        ["patellar"] = "knee",
        ["meniscus"] = "knee",
        ["acl"] = "knee",
        ["mcl"] = "knee",
        ["rotator cuff"] = "shoulder",
        ["ac joint"] = "shoulder",
        ["deltoid"] = "shoulder",
        ["tennis elbow"] = "elbow",
        ["golfers elbow"] = "elbow",
        ["carpal tunnel"] = "wrist",
        ["carpal"] = "wrist",
        ["forearm"] = "wrist",
        ["achilles"] = "ankle",
        ["cervical"] = "neck",
        ["hip flexor"] = "hip",
        ["glute"] = "hip"
    };

    private MovementLimitationDeclaration(
        IReadOnlyList<string> recognisedAreas,
        IReadOnlyList<string> uninterpretedPhrases,
        IReadOnlySet<MovementPattern> excludedMovements)
    {
        RecognisedAreas = recognisedAreas;
        UninterpretedPhrases = uninterpretedPhrases;
        ExcludedMovements = excludedMovements;
    }

    /// <summary>A declaration holding nothing, for a profile that named no limitation.</summary>
    public static MovementLimitationDeclaration Empty { get; } = new(
        [],
        [],
        new HashSet<MovementPattern>());

    /// <summary>Body areas Forge recognised, in the canonical spelling the filter uses.</summary>
    public IReadOnlyList<string> RecognisedAreas { get; }

    /// <summary>Phrases Forge could not place, kept exactly as the user typed them.</summary>
    public IReadOnlyList<string> UninterpretedPhrases { get; }

    /// <summary>Movement patterns the recognised areas exclude.</summary>
    public IReadOnlySet<MovementPattern> ExcludedMovements { get; }

    /// <summary>Whether Forge recognised at least one area and can therefore narrow a list.</summary>
    public bool HasRecognisedAreas => RecognisedAreas.Count > 0;

    /// <summary>Whether anything the user wrote was left unread.</summary>
    public bool HasUninterpretedPhrases => UninterpretedPhrases.Count > 0;

    /// <summary>Whether the user wrote anything at all.</summary>
    public bool IsEmpty => !HasRecognisedAreas && !HasUninterpretedPhrases;

    /// <summary>Reads a free-text limitation exactly as onboarding stored it.</summary>
    /// <param name="declaration">
    /// The raw text from <c>UserProfile.MovementLimitations</c>, which may be blank.
    /// </param>
    /// <returns>What Forge could and could not read from it.</returns>
    public static MovementLimitationDeclaration FromDeclaration(string? declaration)
    {
        if (string.IsNullOrWhiteSpace(declaration))
        {
            return Empty;
        }

        // Longest first, so "lower back" is claimed before the bare "back" can take the word.
        var areas = ExerciseFilter.RecognisedInjuryAreas
            .Concat(AreaSynonyms.Keys)
            .OrderByDescending(area => area.Count(static character => character == ' '))
            .ThenByDescending(area => area.Length)
            .ToArray();

        var recognised = new List<string>();
        var seenAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var uninterpreted = new List<string>();
        var seenPhrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var phrase in SplitPhrases(declaration))
        {
            var words = Normalise(phrase);
            if (words.Length == 0)
            {
                continue;
            }

            var matched = false;
            foreach (var area in areas)
            {
                // Matched words are consumed so that "lower back pain" is read once, as the lower
                // back, rather than a second time as the bare "back" sitting inside it.
                if (!TryConsume(words, area.Split(' ')))
                {
                    continue;
                }

                matched = true;
                var canonical = AreaSynonyms.TryGetValue(area, out var mapped) ? mapped : area;
                if (seenAreas.Add(canonical))
                {
                    recognised.Add(canonical);
                }
            }

            if (!matched && seenPhrases.Add(phrase))
            {
                uninterpreted.Add(phrase);
            }
        }

        return new MovementLimitationDeclaration(
            recognised,
            uninterpreted,
            ExerciseFilter.FromDeclaredInjuries(recognised).ExcludedMovements);
    }

    private static IEnumerable<string> SplitPhrases(string declaration)
    {
        var separated = declaration.Split(
            PhraseSeparators,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in separated)
        {
            foreach (var phrase in part.Split(
                ConjunctionSeparators,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(phrase))
                {
                    yield return phrase;
                }
            }
        }
    }

    /// <summary>Reduces a phrase to comparable words, so "Left knees." reads as "left knee".</summary>
    private static string?[] Normalise(string phrase)
    {
        var builder = new StringBuilder(phrase.Length);
        foreach (var character in phrase)
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return builder
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => (string?)Singularise(word))
            .ToArray();
    }

    private static string Singularise(string word) =>
        word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal)
            ? word[..^1]
            : word;

    private static bool TryConsume(string?[] words, string[] term)
    {
        for (var start = 0; start + term.Length <= words.Length; start++)
        {
            var isMatch = true;
            for (var offset = 0; offset < term.Length; offset++)
            {
                if (!string.Equals(words[start + offset], Singularise(term[offset]), StringComparison.Ordinal))
                {
                    isMatch = false;
                    break;
                }
            }

            if (!isMatch)
            {
                continue;
            }

            for (var offset = 0; offset < term.Length; offset++)
            {
                words[start + offset] = null;
            }

            return true;
        }

        return false;
    }
}
