using System.Globalization;
using Forge.App.Localization;
using Forge.Core.Abstractions.Localization;

namespace Forge.App.Services.Localization;

/// <summary>Reads Forge's display strings from the compiled <c>.resx</c> resource sets.</summary>
/// <remarks>
/// Lookups are exact-culture only. <see cref="System.Resources.ResourceManager.GetString(string, CultureInfo)"/>
/// would walk parent cultures itself, which sounds convenient and is not: it hides whether a
/// string was genuinely translated or merely inherited, and it puts the fallback rule inside a
/// framework type that no unit test can reach. The rule belongs to
/// <see cref="LocalizationService"/>, which is why this class only ever answers for the culture
/// it was asked about.
/// </remarks>
public sealed class ResxLocalizedStringSource : ILocalizedStringSource
{
    /// <summary>Creates the source and verifies the resources are actually embedded.</summary>
    /// <exception cref="InvalidOperationException">
    /// The resource files are missing or no longer match their manifest name. Failing here beats
    /// shipping an app whose every label reads <c>!some.key!</c>.
    /// </exception>
    public ResxLocalizedStringSource() => ForgeStrings.EnsureAvailable();

    /// <inheritdoc />
    public string? Find(string key, CultureInfo culture) => ForgeStrings.Find(key, culture);
}
