using System.Security.Cryptography;
using System.Text;

namespace Forge.Infrastructure.Media;

/// <summary>Stable names for exercise media, both on disk and inside a published pack.</summary>
public static class MediaAssetKeys
{
    /// <summary>The file extension every published demonstration uses.</summary>
    public const string VideoExtension = ".mp4";

    /// <summary>An opaque, filesystem-safe key for one exercise's locally stored media.</summary>
    /// <param name="exerciseName">The exercise display name.</param>
    /// <returns>A stable hexadecimal key.</returns>
    public static string ForExercise(string exerciseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exerciseName);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(exerciseName.Trim().ToUpperInvariant()));
        return Convert.ToHexString(bytes)[..24].ToLowerInvariant();
    }

    /// <summary>
    /// The file name a published video pack is expected to carry for one exercise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a contract with whoever encodes the packs, not an implementation detail: Play Asset
    /// Delivery and On-Demand Resources both address assets by name, so the app can only find a
    /// demonstration if it derives the same name the publisher used. Both runbooks under
    /// <c>docs/media</c> state the rule, and every quality tier publishes the same names so a
    /// device can play whichever tier it happens to hold.
    /// </para>
    /// <para>
    /// A readable slug is used rather than <see cref="ForExercise"/> so the packs can be built,
    /// inspected and diffed by a person. A hash would make a missing file impossible to spot.
    /// </para>
    /// </remarks>
    /// <param name="exerciseName">The exercise display name, exactly as the catalogue spells it.</param>
    /// <returns>A lowercase hyphenated file name, for example <c>bodyweight-squat.mp4</c>.</returns>
    public static string FileNameForExercise(string exerciseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exerciseName);
        return Slug(exerciseName) + VideoExtension;
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                continue;
            }

            // Apostrophes vanish rather than becoming separators, so "World's" reads as "worlds"
            // rather than splitting into two words the publisher would never have guessed.
            if (character is '\'' or '\u2019')
            {
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
