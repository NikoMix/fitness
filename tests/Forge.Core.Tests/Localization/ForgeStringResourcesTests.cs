using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Forge.Core.Abstractions.Localization;
using Shouldly;

namespace Forge.Core.Tests.Localization;

/// <summary>
/// Keeps <see cref="ForgeStringKeys"/> and the resource files in exact agreement.
/// </summary>
/// <remarks>
/// <para>
/// The resource files live in the app head, which targets Android and iOS and therefore cannot
/// be referenced by any test project. Reading the <c>.resx</c> as XML from disk is what makes
/// them testable at all, and the drift it catches is the whole failure mode of a localized app:
/// a key added without a string ships as <c>!some.key!</c>, a translator dropping a <c>{0}</c>
/// ships as a crash or a truncated sentence, and both survive code review easily.
/// </para>
/// <para>
/// This is the substitute for a generated designer class. It is a better one: a designer file
/// only proves the English file parses, while this proves every language is complete and
/// consistent.
/// </para>
/// </remarks>
public sealed class ForgeStringResourcesTests
{
    private static readonly Regex Placeholder = new(@"\{(\d+)[^}]*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Every_key_constant_has_an_english_string()
    {
        var english = await ReadStringsAsync("ForgeStrings.resx").ConfigureAwait(true);

        var missing = KeyConstants().Where(key => !english.ContainsKey(key)).ToList();

        missing.ShouldBeEmpty(
            "These keys are declared in ForgeStringKeys but have no entry in ForgeStrings.resx, " +
            "so every screen using them would render a !marker!.");
    }

    [Fact]
    public async Task Every_english_string_has_a_key_constant()
    {
        var english = await ReadStringsAsync("ForgeStrings.resx").ConfigureAwait(true);
        var constants = KeyConstants().ToHashSet(StringComparer.Ordinal);

        var orphans = english.Keys.Where(key => !constants.Contains(key)).ToList();

        orphans.ShouldBeEmpty(
            "These strings exist in ForgeStrings.resx but no constant references them. Either " +
            "add the constant or delete the string - an unreferenced string is dead weight that " +
            "translators still pay for.");
    }

    [Fact]
    public async Task German_translates_every_english_string()
    {
        var english = await ReadStringsAsync("ForgeStrings.resx").ConfigureAwait(true);
        var german = await ReadStringsAsync("ForgeStrings.de.resx").ConfigureAwait(true);

        var untranslated = english.Keys.Where(key => !german.ContainsKey(key)).ToList();

        untranslated.ShouldBeEmpty(
            "These keys have no German entry. They would silently fall back to English, which " +
            "reads as a half-translated app rather than as the bug it is.");
    }

    [Fact]
    public async Task German_declares_no_string_english_does_not()
    {
        var english = await ReadStringsAsync("ForgeStrings.resx").ConfigureAwait(true);
        var german = await ReadStringsAsync("ForgeStrings.de.resx").ConfigureAwait(true);

        var stale = german.Keys.Where(key => !english.ContainsKey(key)).ToList();

        stale.ShouldBeEmpty("These German entries survived the removal of their English source and are unreachable.");
    }

    [Fact]
    public async Task No_shipped_string_is_blank()
    {
        foreach (var file in new[] { "ForgeStrings.resx", "ForgeStrings.de.resx" })
        {
            var strings = await ReadStringsAsync(file).ConfigureAwait(true);

            var blanks = strings.Where(entry => string.IsNullOrWhiteSpace(entry.Value)).Select(entry => entry.Key).ToList();

            blanks.ShouldBeEmpty($"{file} contains blank values, which render as an empty label rather than a marker.");
        }
    }

    [Fact]
    public async Task Translations_keep_the_same_format_placeholders()
    {
        var english = await ReadStringsAsync("ForgeStrings.resx").ConfigureAwait(true);
        var german = await ReadStringsAsync("ForgeStrings.de.resx").ConfigureAwait(true);

        foreach (var (key, source) in english)
        {
            if (!german.TryGetValue(key, out var translation))
            {
                continue;
            }

            // A dropped {0} loses the value the sentence exists to show; an invented {1} throws
            // a FormatException on a device and nowhere else.
            PlaceholdersOf(translation).ShouldBe(
                PlaceholdersOf(source),
                $"The German translation of '{key}' does not use the same placeholders as the English source.");
        }
    }

    [Fact]
    public async Task Keys_follow_the_dotted_lower_case_convention()
    {
        var english = await ReadStringsAsync("ForgeStrings.resx").ConfigureAwait(true);

        foreach (var key in english.Keys)
        {
            // Keys are read in XAML markup extensions, so a stray capital or space is a runtime
            // lookup failure rather than a compile error.
            key.ShouldMatch("^[a-z0-9]+([.-][a-z0-9]+)*$", $"'{key}' is not a dotted lower-case key.");
        }
    }

    private static IReadOnlyList<int> PlaceholdersOf(string value) =>
        [.. Placeholder.Matches(value)
            .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .Order()];

    private static IEnumerable<string> KeyConstants() =>
        typeof(ForgeStringKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    private static async Task<IReadOnlyDictionary<string, string>> ReadStringsAsync(string fileName)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "Forge.App", "Resources", "Strings", fileName);
        File.Exists(path).ShouldBeTrue($"Expected Forge display strings at {path}.");

        var xml = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken).ConfigureAwait(true);
        var document = XDocument.Parse(xml);

        return document.Root!
            .Elements("data")
            .ToDictionary(
                element => element.Attribute("name")!.Value,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Forge.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find Forge.slnx above '{AppContext.BaseDirectory}'. The resource files are " +
                "read from the working tree because the app head cannot be referenced from a test project.");
    }
}
