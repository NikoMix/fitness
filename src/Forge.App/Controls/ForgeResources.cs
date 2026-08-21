namespace Forge.App.Controls;

/// <summary>
/// Reads Forge design tokens from the merged application resource dictionaries.
/// </summary>
/// <remarks>
/// Controls that size themselves at runtime still have to obey <c>ForgeTokens.xaml</c>. Copying a
/// token's current value into a C# constant is how a design system drifts: the XAML changes, the
/// constant does not, and nothing fails. Looking the token up keeps one source of truth. The
/// fallback only matters in a design-time or unit-test context where no application resources are
/// loaded.
/// </remarks>
internal static class ForgeResources
{
    /// <summary>Reads a <see cref="double"/> token, or returns the supplied fallback.</summary>
    /// <param name="key">The resource key, for example <c>TouchTargetPrimary</c>.</param>
    /// <param name="fallback">The value to use when no application resources are available.</param>
    /// <returns>The token value.</returns>
    public static double Double(string key, double fallback)
    {
        var resources = Microsoft.Maui.Controls.Application.Current?.Resources;
        if (resources is not null && resources.TryGetValue(key, out var value) && value is double token)
        {
            return token;
        }

        return fallback;
    }
}
