using System.Text;
using System.Text.RegularExpressions;

namespace Forge.Core.Abstractions.Diagnostics;

/// <summary>
/// Removes health data from text on its way into the diagnostic log.
/// </summary>
/// <remarks>
/// <para>
/// Forge encrypts its database with SQLCipher so that body weight, injuries, food logs, workout
/// history and profile names are not readable at rest. A plaintext log file that captures any of
/// those has quietly created a second copy of the most sensitive thing the app stores, sitting
/// next to the encrypted one, and most of it is GDPR Article 9 special-category data.
/// </para>
/// <para>
/// The leak this is designed against is <strong>not</strong> deliberate logging. Nobody writes
/// <c>logger.LogInformation("weight {Weight}", weight)</c> on purpose. It is
/// <strong>exception messages and file paths</strong>, which arrive already carrying values
/// nobody chose to include:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>ArgumentException: 'Left knee - ACL reconstruction 2019' is not a valid note.</c>
/// </description></item>
/// <item><description>
/// <c>FormatException: The input string '82.4' was not in a correct format.</c>
/// </description></item>
/// <item><description>
/// <c>IOException: Could not open /data/user/0/com.nikomix.forge/files/Alex-export.json</c>
/// </description></item>
/// </list>
/// <para>
/// So the design is inverted from the usual one. Rather than looking for known-bad content it
/// treats every variable region of a line as suspect and keeps only what is recognisably
/// structural: type names, property names, counts, durations. That costs readability - the trade
/// is stated in <c>docs/diagnostics/logging.md</c> and taken deliberately, because over-redaction
/// produces a log that is harder to read and under-redaction produces a breach.
/// </para>
/// <para>
/// The rules run over the <em>whole</em> rendered line rather than only over the exception, so a
/// message argument, a scope value and an exception message are all treated with equal suspicion.
/// </para>
/// </remarks>
public static partial class DiagnosticLogRedactor
{
    /// <summary>What replaces text that was removed for containing, or possibly containing, health data.</summary>
    public const string RedactedMarker = "<redacted>";

    /// <summary>
    /// What replaces a whole line the rules could not finish inspecting.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="RedactedMarker"/> on purpose. Both mean "this text was not
    /// written", but only one of them means the redactor gave up - and a file that says
    /// <c>&lt;redacted&gt;</c> for a line with nothing sensitive in it looks like an over-eager
    /// rule rather than the timeout it actually was. That exact confusion cost a debugging session
    /// on a device: a MAUI layout warning came out blank, and the reason was that the first match
    /// on a cold Release build ran past a 250 ms budget on a loaded emulator.
    /// </remarks>
    public const string TimedOutMarker = "<redacted: the redaction rules did not finish>";

    /// <summary>What replaces a whole line when a rule failed outright.</summary>
    public const string FailedMarker = "<redacted: the redaction rules failed>";

    /// <summary>What replaces a number that sat close enough to a health term to be one.</summary>
    public const string NumberMarker = "<number>";

    /// <summary>What replaces a quantity written with a unit Forge measures people in.</summary>
    public const string MeasurementMarker = "<measurement>";

    /// <summary>What replaces a filesystem path with no usable extension.</summary>
    public const string PathMarker = "<path>";

    /// <summary>What replaces a date, because when somebody trained is itself health data.</summary>
    public const string DateMarker = "<date>";

    /// <summary>What replaces an email address.</summary>
    public const string EmailMarker = "<email>";

    /// <summary>Appended where text was cut short by a length cap.</summary>
    public const string TruncationMarker = "…";

    /// <summary>
    /// How far from a health term a bare number is still assumed to belong to it.
    /// </summary>
    /// <remarks>
    /// A window rather than the whole line, because the whole line is too blunt: "Imported 60
    /// exercises" and "Retried 3 times" are exactly the diagnostics this feature exists to
    /// preserve, and a line-wide rule deletes them the moment any health word appears anywhere on
    /// the line. 48 characters comfortably spans "Body weight of 82.4 was rejected" without
    /// reaching across a sentence.
    /// </remarks>
    private const int HealthTermProximityWindow = 48;

    /// <summary>
    /// Separators that mean "the value comes next".
    /// </summary>
    /// <remarks>
    /// <c>of</c> is deliberately not here. It is the most collision-prone preposition in .NET
    /// diagnostics - "out of range", "index of", "profile of type" - and admitting it removed far
    /// more type names than it would ever have removed health data.
    /// </remarks>
    private const string ValueSeparatorPattern = @":|=|\bis\b|\bwas\b|\bnamed\b|\bcalled\b";

    /// <summary>
    /// Units Forge measures people in.
    /// </summary>
    /// <remarks>
    /// Time units (<c>ms</c>, <c>s</c>, <c>h</c>) and byte units (<c>B</c>, <c>KB</c>, <c>MB</c>)
    /// are deliberately absent: they are how a log reports its own behaviour, they say nothing
    /// about a person, and redacting them would delete every duration and every size in the file.
    /// The trailing word-boundary check is what keeps "3 instances" and "2 lines" intact while
    /// still catching "3 in" and "2 l".
    /// </remarks>
    private const string HealthUnitPattern = @"kgs?|lbs?|mg|g|oz|st|cm|mm|ft|in|kcal|cal|kj|ml|l|bpm|bmi|%";

    /// <summary>
    /// The words that make a nearby number, or a following value, suspect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Matched as whole words, with the variants spelled out, rather than as prefixes. Prefix
    /// matching was tried first and is quietly wrong: <c>name</c> matches <c>namespace</c>,
    /// <c>fat</c> matches <c>fatal</c>, <c>age</c> matches <c>agent</c>. Each of those turns a
    /// common word in an ordinary exception into a trigger that strips the numbers out of the
    /// line, which makes the log worse for no privacy gain at all.
    /// </para>
    /// <para>
    /// Deliberately absent: <c>exercise</c>, <c>set</c>, <c>rep</c>, <c>workout</c>,
    /// <c>target</c>. Those are structural nouns in this app and in this build, present in a
    /// large share of diagnostic lines, and admitting them would strip the counts out of the
    /// messages most worth keeping while protecting nothing. An exercise name is a catalogue
    /// entry, not a fact about a person.
    /// </para>
    /// </remarks>
    private static readonly string[] HealthTerms =
    [
        "weight", "weights", "bodyweight", "mass", "bmi", "bodyfat", "fat", "height", "waist",
        "hip", "hips", "chest", "thigh", "thighs", "bicep", "biceps", "neck", "girth",
        "measurement", "measurements", "circumference",
        "injury", "injuries", "injured", "pain", "sore", "soreness", "condition", "conditions",
        "medication", "medications", "symptom", "symptoms", "diagnosis", "allergy", "allergies",
        "pregnancy", "pregnant", "menstrual", "limitation", "limitations",
        "kcal", "calorie", "calories", "macro", "macros", "protein", "carb", "carbs",
        "carbohydrate", "fibre", "fiber", "sugar", "sodium", "food", "foods", "meal", "meals",
        "recipe", "recipes", "nutrition", "portion", "serving", "hydration", "water", "drink",
        "sleep", "energy", "mood", "stress", "readiness", "rpe", "rir", "heart", "bpm",
        "note", "notes", "name", "names", "firstname", "lastname", "surname", "email",
        "birth", "birthday", "birthdate", "dob", "age", "gender", "sex", "profile", "goal",
    ];

    private static readonly Regex HealthTermRegex = new(
        $@"\b(?:{string.Join('|', HealthTerms)})\b",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(2000));

    // A term, a separator that introduces a value, then the value up to the next structural
    // boundary. This is what catches free text with no number and no quotes in it at all -
    // "injury: Left knee ACL reconstruction" - which neither the quoting rule nor the proximity
    // rule can see.
    //
    // The boundary set is ';' and '|' and end of line, and NOT ',' or '.'. That is the fix for a
    // leak found on a device: with a comma ending the value,
    // "injury note: Left knee - ACL reconstruction 2019, avoid deep flexion" came out as
    // "injury note: <redacted>, avoid deep flexion". Free text has commas and full stops in it,
    // so treating them as boundaries redacts the first clause of an injury description and prints
    // the rest. Once a value is known to follow a health label, all of it goes.
    private static readonly Regex HealthTermValueRegex = new(
        $@"(?<term>\b(?:{string.Join('|', HealthTerms)})\b)(?<sep>\s*(?:{ValueSeparatorPattern})\s*)(?<value>[^;|\r\n]{{1,200}})",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(2000));

    private static readonly Regex MeasurementRegex = new(
        $@"(?<![\w.])\d+(?:[.,]\d+)?\s*(?:{HealthUnitPattern})(?![\w])",
        RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(2000));

    /// <summary>
    /// Removes health data from a line of log text.
    /// </summary>
    /// <param name="text">The rendered message, exception text, or both.</param>
    /// <param name="maxLength">Characters kept before truncation.</param>
    /// <returns>Text safe to write to an unencrypted file.</returns>
    /// <remarks>
    /// Never throws. A redactor that can fail is a redactor that leaks the moment it does, so a
    /// fault inside any rule collapses the whole input to <see cref="RedactedMarker"/> rather
    /// than falling back to the original text.
    /// </remarks>
    public static string Redact(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        try
        {
            return Truncate(ApplyRules(text), maxLength);
        }
        catch (RegexMatchTimeoutException)
        {
            // Fail closed. The alternative - returning the input - would write out exactly the
            // text the rules could not finish inspecting.
            return TimedOutMarker;
        }
        catch (ArgumentException)
        {
            return FailedMarker;
        }
    }

    /// <summary>
    /// Renders an exception for the log, with every message in the chain redacted.
    /// </summary>
    /// <param name="exception">The exception to describe.</param>
    /// <param name="options">The caps that bound how much is written.</param>
    /// <returns>Type names, redacted messages and stack frames.</returns>
    /// <remarks>
    /// <para>
    /// The exception <em>type</em> and the stack are the useful part and are kept up to the length
    /// cap; the <em>message</em> is the dangerous part and is both redacted and capped hard. That
    /// split is the whole point: a type plus a stack frame locates the fault without containing
    /// anything a user typed.
    /// </para>
    /// <para>
    /// Stack frames carry the build machine's source paths, which the path rule reduces to
    /// <c>&lt;path.cs&gt;</c>. The file name is not what makes a frame useful - the method is.
    /// </para>
    /// </remarks>
    public static string Describe(Exception? exception, DiagnosticLogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (exception is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var current = exception;
        var depth = 0;

        while (current is not null && depth < 8)
        {
            if (depth > 0)
            {
                builder.Append(" ---> ");
            }

            builder.Append(current.GetType().FullName);

            var message = Redact(current.Message, options.MaxExceptionMessageLength);
            if (message.Length > 0)
            {
                builder.Append(": ").Append(message);
            }

            if (current.StackTrace is { Length: > 0 } stack)
            {
                builder.Append('\n').Append(Redact(stack, options.MaxExceptionLength));
            }

            current = current.InnerException;
            depth++;
        }

        return Truncate(builder.ToString(), options.MaxExceptionLength);
    }

    private static string ApplyRules(string text)
    {
        // Order is load-bearing.
        //
        // Emails first, because their local part can contain the dots and dashes that the path
        // rules would otherwise chew through. Paths next, because a path contains dates, numbers
        // and dotted identifiers that every later rule would rewrite into an unreadable mess.
        // Dates before the numeric rules, so "2026-02-11" is not read as arithmetic. Quoted
        // values before the numeric rules too, so a quoted measurement is removed entirely rather
        // than left as "<measurement>" inside quotes that hint at what it was.
        var result = EmailPattern().Replace(text, EmailMarker);
        result = FileUriPattern().Replace(result, ReplacePath);
        result = WindowsPathPattern().Replace(result, ReplacePath);
        result = UnixPathPattern().Replace(result, ReplacePath);
        result = DatePattern().Replace(result, DateMarker);
        result = DoubleQuotedPattern().Replace(result, ReplaceQuoted);
        result = SingleQuotedPattern().Replace(result, ReplaceQuoted);
        result = HealthTermValueRegex.Replace(result, ReplaceTermValue);
        result = MeasurementRegex.Replace(result, MeasurementMarker);
        result = LongDigitRunPattern().Replace(result, NumberMarker);
        return RedactNumbersNearHealthTerms(result);
    }

    /// <summary>
    /// Replaces a path with its extension, so "which kind of file" survives and "whose file" does not.
    /// </summary>
    /// <remarks>
    /// Both halves of a path leak. The directory carries the Android user id and, on a shared
    /// tablet, tells one profile apart from another. The file name is worse: Forge's own export
    /// names are built from the profile's name, so <c>Alex-export.json</c> is a person's name in a
    /// file nobody thought of as containing one.
    /// </remarks>
    private static string ReplacePath(Match match)
    {
        var value = match.Value;
        var lastDot = value.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == value.Length - 1)
        {
            return PathMarker;
        }

        var extension = value[(lastDot + 1)..];
        return extension.Length <= 8 && extension.All(char.IsLetterOrDigit)
            ? $"<path.{extension}>"
            : PathMarker;
    }

    /// <summary>
    /// Removes a quoted value unless the whole of it is a dotted identifier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Quoted runs are where a framework puts <em>values</em>: <c>'82.4'</c> from a
    /// <c>FormatException</c>, <c>'Left knee - ACL reconstruction'</c> from a validation failure.
    /// They are also where it puts <em>names</em>: <c>'Exercise.Id'</c>, <c>'DateTimeOffset'</c>,
    /// <c>'UserProfileId'</c>, which are exactly what makes an EF or SQLite fault diagnosable.
    /// </para>
    /// <para>
    /// The test that separates them is strict on purpose: the entire quoted run must be an
    /// identifier or a dotted chain of them. Anything with a space, a leading digit, an operator
    /// or punctuation is removed. That deliberately loses the text of a LINQ expression, which was
    /// genuinely useful once - it is how the <c>DateTimeOffset</c> ordering fault was read off a
    /// screen - but a LINQ expression can quote a constant a user typed, and the same fault is
    /// still locatable from the exception type and the stack.
    /// </para>
    /// </remarks>
    private static string ReplaceQuoted(Match match)
    {
        var inner = match.Groups["value"].Value;
        if (inner.Length == 0)
        {
            return match.Value;
        }

        return StructuralIdentifierPattern().IsMatch(inner)
            ? match.Value
            : string.Concat(match.Groups["open"].Value, RedactedMarker, match.Groups["close"].Value);
    }

    private static string ReplaceTermValue(Match match) =>
        string.Concat(match.Groups["term"].Value, match.Groups["sep"].Value, RedactedMarker);

    /// <summary>
    /// Blanks bare numbers that sit within <see cref="HealthTermProximityWindow"/> of a health term.
    /// </summary>
    /// <remarks>
    /// The catch-all behind the unit rule. "Body weight 82.4 was rejected" carries no unit at all,
    /// and that is the shape a validation exception takes most often, because the unit lives in
    /// the property type rather than in the text.
    /// </remarks>
    private static string RedactNumbersNearHealthTerms(string text)
    {
        var terms = HealthTermRegex.Matches(text);
        if (terms.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var written = 0;

        foreach (var number in BareNumberPattern().Matches(text).Cast<Match>())
        {
            if (!IsNearAny(terms, number.Index, number.Length))
            {
                continue;
            }

            builder.Append(text, written, number.Index - written).Append(NumberMarker);
            written = number.Index + number.Length;
        }

        if (written == 0)
        {
            return text;
        }

        builder.Append(text, written, text.Length - written);
        return builder.ToString();
    }

    private static bool IsNearAny(MatchCollection terms, int index, int length)
    {
        foreach (var term in terms.Cast<Match>())
        {
            var gap = index >= term.Index + term.Length
                ? index - (term.Index + term.Length)
                : term.Index - (index + length);

            if (gap <= HealthTermProximityWindow)
            {
                return true;
            }
        }

        return false;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (maxLength <= 0)
        {
            return string.Empty;
        }

        return value.Length <= maxLength
            ? value
            : string.Concat(value.AsSpan(0, maxLength), TruncationMarker);
    }

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+", RegexOptions.ExplicitCapture, 2000)]
    private static partial Regex EmailPattern();

    // Handles the schemes that carry a device path across an API boundary. The generic path rules
    // below cannot see these, because their leading separator is preceded by another separator -
    // which is exactly the guard that keeps http and https URLs intact.
    [GeneratedRegex(@"(?:file|content)://[^\s""'<>|]*", RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase, 2000)]
    private static partial Regex FileUriPattern();

    // Accepts either separator after the drive letter, because .NET reports Windows paths with
    // forward slashes often enough that a backslash-only rule leaves half of them in the file.
    //
    // The lookbehind is not decoration. Without it "https://aka.ms/..." matches: the 's' of
    // "https" is a letter, followed by ':' and '/', which is precisely a drive root. Every URL in
    // the file was being turned into "http<path><path>", which removed the one class of text in
    // an exception message that is both useful and provably impersonal.
    [GeneratedRegex(@"(?<![A-Za-z])[A-Za-z]:[\\/](?:[^\\/:*?""<>|\r\n]+[\\/])*[^\\/:*?""<>|\r\n ]*", RegexOptions.ExplicitCapture, 2000)]
    private static partial Regex WindowsPathPattern();

    // At least two segments, so "and/or" and a lone "/" are left alone.
    //
    // No space in the segment class, and the lookbehind excludes '/' as well as word characters
    // and ':'. Both are deliberate. A class containing a space matched greedily past the end of
    // the path and swallowed the rest of the sentence - "IOException: could not open /files/x.db
    // because the disk is full" lost the reason along with the path. Excluding '/' and word
    // characters from the lookbehind is what leaves an http or https URL alone: every slash in
    // one is preceded by ':', '/' or a hostname character.
    [GeneratedRegex(@"(?<![\w:/])/(?:[\w.@%+~()-]+/)+[\w.@%+~()-]*", RegexOptions.ExplicitCapture, 2000)]
    private static partial Regex UnixPathPattern();

    [GeneratedRegex(
        @"\d{4}-\d{2}-\d{2}(?:[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})?)?|\d{1,2}/\d{1,2}/\d{4}",
        RegexOptions.ExplicitCapture,
        2000)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"(?<open>"")(?<value>[^""\r\n]{0,400})(?<close>"")", RegexOptions.ExplicitCapture, 2000)]
    private static partial Regex DoubleQuotedPattern();

    // The lookaround is what stops an English apostrophe pairing with the next one: in "Forge
    // couldn't open what wasn't there" both apostrophes are preceded by a letter, so neither
    // opens a quoted run and the sentence survives intact.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9])(?<open>')(?<value>[^'\r\n]{0,400})(?<close>')(?![A-Za-z0-9])",
        RegexOptions.ExplicitCapture,
        2000)]
    private static partial Regex SingleQuotedPattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*$", RegexOptions.ExplicitCapture, 2000)]
    private static partial Regex StructuralIdentifierPattern();

    [GeneratedRegex(@"(?<![\w.])\d{7,}(?![\w])", RegexOptions.ExplicitCapture, 2000)]
    private static partial Regex LongDigitRunPattern();

    [GeneratedRegex(@"(?<![\w.])\d+(?:[.,]\d+)?(?![\w])", RegexOptions.ExplicitCapture, 2000)]
    private static partial Regex BareNumberPattern();
}
