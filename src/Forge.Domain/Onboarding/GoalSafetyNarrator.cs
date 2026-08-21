using Forge.Domain.Profile;

namespace Forge.Domain.Onboarding;

/// <summary>
/// Turns a <see cref="GoalSafetyResult"/> into something a person can act on.
/// </summary>
/// <remarks>
/// <para>
/// The evaluator already produces plain-language advisories, but a screen that shows only the
/// first one hides the fact that two or three separate guardrails objected, and someone who fixes
/// the one message they can see will be refused again for a reason they were never told. Every
/// advisory is surfaced.
/// </para>
/// <para>
/// A refusal is never allowed to look like the app quietly threw the answers away. The narration
/// always carries an explicit statement that the entered values are still there, because the
/// alternative - a form that resets itself and says "invalid" - reads as the app overruling the
/// user rather than explaining itself.
/// </para>
/// </remarks>
public static class GoalSafetyNarrator
{
    /// <summary>The reassurance shown when a goal is refused.</summary>
    public const string RefusedReassurance =
        "Your answers are exactly as you entered them and nothing has been discarded. Adjust the target, the timeframe or the daily energy figure and Forge re-checks immediately.";

    /// <summary>The reassurance shown when a goal is accepted with something worth reading.</summary>
    public const string AcceptedReassurance =
        "Forge will save this as it stands. You can revisit any of it later from Profile.";

    /// <summary>Narrates a safety result.</summary>
    /// <param name="result">The result to narrate.</param>
    /// <returns>Narration suitable for direct display.</returns>
    public static GoalSafetyNarration Narrate(GoalSafetyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var severity = result.Severity;
        var blocks = !result.IsAccepted;

        // Refusals are listed first, and on their own. Mixing "this is fine" information into a
        // refusal makes the blocking reason harder to find at exactly the moment it matters.
        var relevant = blocks
            ? result.Advisories.Where(advisory => advisory.Severity == SafetySeverity.Refused).ToList()
            : result.Advisories.Where(advisory => advisory.Severity != SafetySeverity.None).ToList();

        return new GoalSafetyNarration(
            severity,
            HeadlineFor(severity, blocks),
            relevant.Select(advisory => advisory.Message).ToList(),
            relevant
                .Select(advisory => advisory.SupportSignpost)
                .Where(signpost => !string.IsNullOrWhiteSpace(signpost))
                .Select(signpost => signpost!)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            blocks ? RefusedReassurance : AcceptedReassurance,
            blocks);
    }

    private static string HeadlineFor(SafetySeverity severity, bool blocks) => blocks
        ? "Forge cannot plan this goal as it stands"
        : severity switch
        {
            SafetySeverity.Warning => "Worth reading before you continue",
            SafetySeverity.Information => "This goal is inside Forge's guardrails",
            _ => "This goal is inside Forge's guardrails",
        };
}

/// <summary>
/// A safety result rendered for display.
/// </summary>
/// <param name="Severity">The highest severity present in the underlying result.</param>
/// <param name="Headline">A short heading describing the outcome.</param>
/// <param name="Reasons">Every reason the guardrails gave, in evaluation order.</param>
/// <param name="Signposts">Distinct support signposts, such as speaking to a clinician.</param>
/// <param name="Reassurance">An explicit statement about what happened to the user's input.</param>
/// <param name="BlocksSaving">Whether the goal cannot be saved as configured.</param>
public sealed record GoalSafetyNarration(
    SafetySeverity Severity,
    string Headline,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Signposts,
    string Reassurance,
    bool BlocksSaving)
{
    /// <summary>Whether there is anything worth showing.</summary>
    public bool HasContent => Reasons.Count > 0;

    /// <summary>Whether the result is purely informational.</summary>
    public bool IsInformationOnly => !BlocksSaving && Severity <= SafetySeverity.Information;

    /// <summary>Every reason as one block of text, one reason per paragraph.</summary>
    public string ReasonText => string.Join(Environment.NewLine + Environment.NewLine, Reasons);

    /// <summary>Every signpost as one block of text.</summary>
    public string SignpostText => string.Join(" ", Signposts);
}
