using System.Globalization;

namespace Forge.Core.Abstractions.Localization;

/// <summary>Supplies translated strings for one exact culture.</summary>
/// <remarks>
/// <para>
/// Implementations must not walk the culture fallback chain themselves. Returning only what the
/// requested culture actually declares keeps the fallback rule in one place -
/// <see cref="LocalizationService"/> - where it is unit-testable without a resource file, an
/// emulator or a satellite assembly. A source that silently falls back also makes it impossible
/// to tell a real translation from an inherited one, which is exactly the distinction a
/// translation-coverage check needs.
/// </para>
/// <para>
/// The MAUI-facing implementation lives in <c>Forge.App/Services/Localization</c> and reads the
/// <c>.resx</c> resource sets. <see cref="InMemoryLocalizedStringSource"/> covers tests and
/// design-time previews.
/// </para>
/// </remarks>
public interface ILocalizedStringSource
{
    /// <summary>Returns the string declared for the exact culture, or null when absent.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="culture">The exact culture to look in. Parents must not be consulted.</param>
    string? Find(string key, CultureInfo culture);
}

/// <summary>An in-memory string source, for tests and design-time previews.</summary>
public sealed class InMemoryLocalizedStringSource : ILocalizedStringSource
{
    private readonly Dictionary<string, Dictionary<string, string>> byCulture =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Adds or replaces the strings declared for one culture.</summary>
    /// <param name="cultureName">The exact culture name, for example <c>de</c> or <c>de-AT</c>.</param>
    /// <param name="strings">The strings that culture declares.</param>
    /// <returns>The same source, for chaining.</returns>
    public InMemoryLocalizedStringSource With(string cultureName, IReadOnlyDictionary<string, string> strings)
    {
        ArgumentNullException.ThrowIfNull(cultureName);
        ArgumentNullException.ThrowIfNull(strings);

        byCulture[cultureName] = new Dictionary<string, string>(strings, StringComparer.Ordinal);
        return this;
    }

    /// <inheritdoc />
    public string? Find(string key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(culture);

        return byCulture.TryGetValue(culture.Name, out var strings) && strings.TryGetValue(key, out var value)
            ? value
            : null;
    }
}
