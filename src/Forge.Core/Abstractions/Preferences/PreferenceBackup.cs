using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Forge.Core.Abstractions.Media;

namespace Forge.Core.Abstractions.Preferences;

/// <summary>Portable representation of Forge preferences for inclusion in local backups.</summary>
/// <param name="SchemaVersion">Preference backup schema version.</param>
/// <param name="Values">Preference values keyed by <see cref="ForgePreferenceKeys"/>.</param>
/// <param name="ContentHash">SHA-256 hash of the canonical values payload.</param>
public sealed record PreferenceBackupDocument(
    int SchemaVersion,
    IReadOnlyDictionary<string, string> Values,
    string ContentHash);

/// <summary>Exports and imports preference backup documents with integrity checks.</summary>
public static class PreferenceBackup
{
    private const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>Creates a preference backup document from the current preferences.</summary>
    public static PreferenceBackupDocument Export(IForgePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [ForgePreferenceKeys.UnitSystem] = preferences.UnitSystem.ToString(),
            [ForgePreferenceKeys.ThemeMode] = preferences.ThemeMode.ToString(),
            [ForgePreferenceKeys.PreferredVideoQuality] = preferences.PreferredVideoQuality.ToString(),
            [ForgePreferenceKeys.DownloadMediaOverUnmeteredNetworksOnly] = preferences.DownloadMediaOverUnmeteredNetworksOnly.ToString(),
            [ForgePreferenceKeys.RestTimerDefaultSeconds] = ((int)preferences.RestTimerDefaultDuration.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
            [ForgePreferenceKeys.FirstDayOfWeek] = preferences.FirstDayOfWeek.ToString(),
            [ForgePreferenceKeys.HapticFeedbackEnabled] = preferences.HapticFeedbackEnabled.ToString(),
        };

        return new PreferenceBackupDocument(CurrentSchemaVersion, values, ComputeHash(values));
    }

    /// <summary>Validates and imports a preference backup document without partially applying invalid data.</summary>
    public static void Import(PreferenceBackupDocument document, IForgePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(preferences);

        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException("Preference backup schema is not supported.");
        }

        if (!string.Equals(ComputeHash(document.Values), document.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Preference backup integrity check failed.");
        }

        var unitSystem = ParseRequired<MeasurementSystemPreference>(document.Values, ForgePreferenceKeys.UnitSystem);
        var themeMode = ParseRequired<ThemeModePreference>(document.Values, ForgePreferenceKeys.ThemeMode);
        var videoQuality = ParseRequired<MediaQuality>(document.Values, ForgePreferenceKeys.PreferredVideoQuality);
        var unmeteredOnly = ParseRequiredBoolean(document.Values, ForgePreferenceKeys.DownloadMediaOverUnmeteredNetworksOnly);
        var restSeconds = ParseRequiredInt32(document.Values, ForgePreferenceKeys.RestTimerDefaultSeconds);
        var firstDay = ParseRequired<DayOfWeek>(document.Values, ForgePreferenceKeys.FirstDayOfWeek);
        var haptics = ParseRequiredBoolean(document.Values, ForgePreferenceKeys.HapticFeedbackEnabled);

        preferences.UnitSystem = unitSystem;
        preferences.ThemeMode = themeMode;
        preferences.PreferredVideoQuality = videoQuality;
        preferences.DownloadMediaOverUnmeteredNetworksOnly = unmeteredOnly;
        preferences.RestTimerDefaultDuration = TimeSpan.FromSeconds(restSeconds);
        preferences.FirstDayOfWeek = firstDay;
        preferences.HapticFeedbackEnabled = haptics;
    }

    /// <summary>Serializes a preference backup document.</summary>
    public static string Serialize(PreferenceBackupDocument document) => JsonSerializer.Serialize(document, JsonOptions);

    /// <summary>Deserializes a preference backup document.</summary>
    public static PreferenceBackupDocument Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<PreferenceBackupDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Preference backup document is empty.");
    }

    private static string ComputeHash(IReadOnlyDictionary<string, string> values)
    {
        var canonical = JsonSerializer.Serialize(
            values.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private static T ParseRequired<T>(IReadOnlyDictionary<string, string> values, string key)
        where T : struct, Enum
    {
        return values.TryGetValue(key, out var value) && Enum.TryParse<T>(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Preference backup contains an invalid value for {key}.");
    }

    private static bool ParseRequiredBoolean(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Preference backup contains an invalid value for {key}.");
    }

    private static int ParseRequiredInt32(IReadOnlyDictionary<string, string> values, string key)
    {
        return values.TryGetValue(key, out var value)
            && int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Preference backup contains an invalid value for {key}.");
    }
}
