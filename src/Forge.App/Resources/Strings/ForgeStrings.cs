using System.Globalization;
using System.Resources;

namespace Forge.App.Localization;

/// <summary>The resource manager for Forge's display strings.</summary>
/// <remarks>
/// <para>
/// The namespace is <c>Forge.App.Localization</c> even though the file sits under
/// <c>Resources/Strings</c>. Declaring a namespace called <c>Forge.App.Resources</c> would
/// shadow the <c>Resources</c> member every MAUI <c>VisualElement</c> exposes, so
/// <c>Resources["Card"]</c> in a sibling namespace would stop compiling with a confusing error
/// about a namespace being used like a type. A <c>global using</c> alias does not fix it. The
/// project has hit this class of bug three times; see the contributor instructions.
/// </para>
/// <para>
/// The namespace is also what names the compiled resource. With
/// <c>EmbeddedResourceUseDependentUponConvention</c> - on by default in SDK projects - MSBuild
/// pairs <c>ForgeStrings.resx</c> with the <c>ForgeStrings.cs</c> beside it and builds the
/// manifest name from <em>this file's namespace</em> rather than from the folder path. So the
/// embedded resource is <c>Forge.App.Localization.ForgeStrings.resources</c>, which is exactly
/// <c>typeof(ForgeStrings).FullName</c>. Constructing the manager from the type therefore keeps
/// the two in step automatically, and avoids a hard-coded string that silently stops matching
/// the moment the file moves.
/// </para>
/// </remarks>
public static class ForgeStrings
{
    private static readonly ResourceManager Manager = new(typeof(ForgeStrings));

    /// <summary>The manifest resource base name the strings are compiled under.</summary>
    public static string BaseName => typeof(ForgeStrings).FullName!;

    /// <summary>The resource manager over <c>ForgeStrings.resx</c> and its satellites.</summary>
    public static ResourceManager ResourceManager => Manager;

    /// <summary>Looks a key up in exactly one culture, never in its parents.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="culture">The exact culture to consult.</param>
    /// <returns>The declared string, or null when this culture declares none.</returns>
    /// <remarks>
    /// English lives in the neutral (invariant) resource set rather than an <c>en</c> satellite,
    /// which is how <c>ForgeStrings.resx</c> compiles. The fallback chain in
    /// <c>LocalizationService</c> always ends at the invariant culture, so English is always
    /// reachable.
    /// </remarks>
    public static string? Find(string key, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(culture);

        var set = Manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        return set?.GetString(key);
    }

    /// <summary>Throws if the compiled resources are missing or misnamed.</summary>
    /// <exception cref="InvalidOperationException">The neutral resource set cannot be loaded.</exception>
    public static void EnsureAvailable()
    {
        try
        {
            var neutral = Manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
            if (neutral is not null)
            {
                return;
            }
        }
        catch (MissingManifestResourceException exception)
        {
            throw new InvalidOperationException(NotEmbeddedMessage, exception);
        }

        throw new InvalidOperationException(NotEmbeddedMessage);
    }

    private static string NotEmbeddedMessage =>
        $"Forge display strings are not embedded under '{BaseName}'. MSBuild derives that name " +
        "from this file's namespace, so check that ForgeStrings.cs and ForgeStrings.resx are " +
        "still named alike and still sit in the same folder.";
}
