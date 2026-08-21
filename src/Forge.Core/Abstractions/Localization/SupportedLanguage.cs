using System.Globalization;

namespace Forge.Core.Abstractions.Localization;

/// <summary>A display language Forge ships translated strings for.</summary>
/// <remarks>
/// A language is deliberately not the same thing as a culture. Forge translates per language
/// (<c>de</c>), but formats per culture (<c>de-AT</c>), so that an Austrian device keeps its own
/// date and number conventions while reading the German translation.
/// </remarks>
public sealed record SupportedLanguage
{
    /// <summary>Creates a supported language from a neutral language code such as <c>en</c>.</summary>
    /// <param name="code">A neutral, two-letter ISO language code.</param>
    public SupportedLanguage(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Code = code;
        Culture = CultureInfo.GetCultureInfo(code);

        // NativeName is lower case in several languages ("français"). A language picker reads as
        // a proper-noun list, so it is title-cased using that language's own casing rules rather
        // than the current culture's.
        NativeName = Culture.TextInfo.ToTitleCase(Culture.NativeName);
        EnglishName = Culture.EnglishName;
        IsRightToLeft = Culture.TextInfo.IsRightToLeft;
    }

    /// <summary>The neutral language code, for example <c>de</c>.</summary>
    public string Code { get; }

    /// <summary>The neutral culture for this language.</summary>
    public CultureInfo Culture { get; }

    /// <summary>The language's name in its own language, for example <c>Deutsch</c>.</summary>
    public string NativeName { get; }

    /// <summary>The language's name in English, for example <c>German</c>.</summary>
    public string EnglishName { get; }

    /// <summary>Whether this language is written right to left.</summary>
    /// <remarks>
    /// Forge ships no right-to-left language yet. The flag exists so that the layout work
    /// described in <c>docs/localization/rtl-readiness.md</c> has one source of truth to drive
    /// <c>FlowDirection</c> from when it happens.
    /// </remarks>
    public bool IsRightToLeft { get; }
}

/// <summary>The languages Forge ships translations for.</summary>
public static class ForgeLanguages
{
    /// <summary>English, the source language every string is authored in.</summary>
    public const string English = "en";

    /// <summary>German.</summary>
    public const string German = "de";

    /// <summary>The language used when nothing else resolves.</summary>
    public static SupportedLanguage Default { get; } = new(English);

    /// <summary>Every shipped language, source language first.</summary>
    public static IReadOnlyList<SupportedLanguage> All { get; } = [Default, new(German)];

    /// <summary>Finds the shipped language matching a language or culture code, or null.</summary>
    /// <param name="code">A language code (<c>de</c>) or culture code (<c>de-AT</c>).</param>
    public static SupportedLanguage? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var separator = code.IndexOfAny(['-', '_']);
        var language = separator < 0 ? code : code[..separator];

        return All.FirstOrDefault(candidate => string.Equals(candidate.Code, language, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether Forge ships translations for the supplied language or culture code.</summary>
    /// <param name="code">A language code (<c>de</c>) or culture code (<c>de-AT</c>).</param>
    public static bool IsSupported(string? code) => Find(code) is not null;
}
