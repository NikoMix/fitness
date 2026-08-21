using System.Globalization;
using Forge.Core.Abstractions.Localization;
using Forge.Core.Abstractions.Preferences;
using Shouldly;

namespace Forge.Core.Tests.Localization;

/// <summary>
/// Covers the rules a localized app gets wrong quietly: what a missing string looks like, which
/// culture a lookup falls back to, and what "follow the device" means once the user has chosen.
/// </summary>
public sealed class LocalizationServiceTests
{
    private const string Greeting = "test.greeting";
    private const string Untranslated = "test.only-in-english";

    [Fact]
    public void A_missing_string_is_visible_rather_than_blank()
    {
        var service = CreateService();

        var value = service.GetString("test.key.that.does.not.exist");

        // A blank label reads as a layout bug and gets triaged as one. The key has to be on
        // screen, because that is what makes it fixable from a screenshot.
        value.ShouldNotBeNullOrWhiteSpace();
        value.ShouldBe("!test.key.that.does.not.exist!");
    }

    [Fact]
    public void A_blank_translation_counts_as_missing()
    {
        // A translator clearing a cell is enough to produce this, and it is indistinguishable
        // from a missing string once it renders.
        var source = new InMemoryLocalizedStringSource()
            .With(string.Empty, new Dictionary<string, string> { [Greeting] = string.Empty });

        var service = CreateService(source);

        service.GetString(Greeting).ShouldBe($"!{Greeting}!");
    }

    [Fact]
    public void The_strict_policy_throws_so_tooling_can_fail_a_build()
    {
        var service = CreateService(options: new LocalizationOptions
        {
            MissingStringBehavior = MissingLocalizedStringBehavior.Throw,
        });

        var exception = Should.Throw<MissingLocalizedStringException>(() => service.GetString("test.absent"));

        exception.Key.ShouldBe("test.absent");
    }

    [Fact]
    public void A_regional_culture_falls_back_to_its_neutral_language()
    {
        var service = CreateService(device: "de-AT");

        service.UseLanguage(ForgeLanguages.German);

        // de-AT declares nothing, so the German translation applies rather than English.
        service.CurrentUICulture.Name.ShouldBe("de-AT");
        service.GetString(Greeting).ShouldBe("Hallo");
    }

    [Fact]
    public void A_regional_translation_wins_over_its_neutral_language()
    {
        var source = DefaultSource()
            .With("de-AT", new Dictionary<string, string> { [Greeting] = "Servus" });

        var service = CreateService(source, device: "de-AT");
        service.UseLanguage(ForgeLanguages.German);

        service.GetString(Greeting).ShouldBe("Servus");
    }

    [Fact]
    public void A_language_with_no_translation_falls_back_to_english()
    {
        var service = CreateService(device: "de-DE");
        service.UseLanguage(ForgeLanguages.German);

        service.GetString(Untranslated).ShouldBe("Only in English");
    }

    [Fact]
    public void An_unsupported_device_language_reads_english_but_keeps_the_devices_formatting()
    {
        var service = CreateService(device: "sv-SE");

        // Forge ships no Swedish, so the words are English.
        service.CurrentLanguage.Code.ShouldBe(ForgeLanguages.English);
        service.GetString(Greeting).ShouldBe("Hello");

        // Imposing American dates and decimal points on a Swede because Forge has no Swedish
        // translation would be a second, unrelated insult. Formatting still follows the device.
        service.CurrentCulture.Name.ShouldBe("sv-SE");
    }

    [Fact]
    public void Forge_follows_the_device_language_until_the_user_chooses()
    {
        var service = CreateService(device: "de-DE");

        service.FollowsSystemLanguage.ShouldBeTrue();
        service.SelectedLanguageCode.ShouldBeNull();
        service.CurrentLanguage.Code.ShouldBe(ForgeLanguages.German);
    }

    [Fact]
    public void A_chosen_language_survives_a_restart()
    {
        var store = new InMemoryPreferenceStore();

        CreateService(store: store, device: "en-GB").UseLanguage(ForgeLanguages.German);

        // A second service over the same store is what a relaunch looks like.
        var relaunched = CreateService(store: store, device: "en-GB");

        relaunched.FollowsSystemLanguage.ShouldBeFalse();
        relaunched.SelectedLanguageCode.ShouldBe(ForgeLanguages.German);
        relaunched.GetString(Greeting).ShouldBe("Hallo");
    }

    [Fact]
    public void Returning_to_the_device_language_survives_a_restart()
    {
        var store = new InMemoryPreferenceStore();
        var service = CreateService(store: store, device: "en-GB");

        service.UseLanguage(ForgeLanguages.German);
        service.UseSystemLanguage();

        var relaunched = CreateService(store: store, device: "en-GB");

        relaunched.FollowsSystemLanguage.ShouldBeTrue();
        relaunched.CurrentLanguage.Code.ShouldBe(ForgeLanguages.English);
    }

    [Fact]
    public void Switching_language_announces_the_change_so_screens_can_repaint()
    {
        var service = CreateService(device: "en-GB");
        var changes = new List<string>();
        service.LanguageChanged += (_, args) => changes.Add(args.Language.Code);

        service.UseLanguage(ForgeLanguages.German);

        // The event is what lets bound labels re-read themselves. Without it the only way to see
        // a new language would be to restart the app.
        changes.ShouldBe([ForgeLanguages.German]);
        service.GetString(Greeting).ShouldBe("Hallo");
    }

    [Fact]
    public void Choosing_the_language_already_in_use_announces_nothing()
    {
        var service = CreateService(device: "de-DE");
        var changes = 0;
        service.LanguageChanged += (_, _) => changes++;

        service.UseLanguage(ForgeLanguages.German);

        changes.ShouldBe(0);
    }

    [Fact]
    public void A_language_Forge_does_not_ship_is_rejected_rather_than_stored()
    {
        var store = new InMemoryPreferenceStore();
        var service = CreateService(store: store);

        Should.Throw<ArgumentException>(() => service.UseLanguage("sv"));

        store.GetString(LocalizationPreferenceKeys.Language, "unset").ShouldBe("unset");
    }

    [Fact]
    public void Choosing_a_language_keeps_the_devices_regional_variant()
    {
        var service = CreateService(device: "de-AT");

        service.UseLanguage(ForgeLanguages.German);

        // Austrian German is still German. Forcing plain "de" would quietly change month names
        // and date order for every Austrian user who touched the language picker.
        service.CurrentCulture.Name.ShouldBe("de-AT");
    }

    [Fact]
    public void Choosing_a_language_the_device_does_not_speak_uses_that_languages_own_culture()
    {
        var service = CreateService(device: "en-US");

        service.UseLanguage(ForgeLanguages.German);

        service.CurrentCulture.Name.ShouldBe("de");
        service.CurrentUICulture.Name.ShouldBe("de");
    }

    [Fact]
    public void Format_arguments_are_written_in_the_display_culture()
    {
        var source = DefaultSource()
            .With(string.Empty, new Dictionary<string, string>
            {
                [Greeting] = "Hello",
                [Untranslated] = "Only in English",
                ["test.total"] = "Total: {0}",
            })
            .With("de", new Dictionary<string, string>
            {
                [Greeting] = "Hallo",
                ["test.total"] = "Summe: {0}",
            });

        var german = CreateService(source, device: "de-DE");
        var british = CreateService(source, device: "en-GB");

        german.GetString("test.total", 1234.5).ShouldBe("Summe: 1234,5");
        british.GetString("test.total", 1234.5).ShouldBe("Total: 1234.5");
    }

    [Fact]
    public void The_language_choice_is_stored_beside_the_other_preferences()
    {
        var store = new InMemoryPreferenceStore();
        var service = CreateService(store: store);

        service.UseLanguage(ForgeLanguages.German);

        // One store, not two. A separate settings file for language would drift out of step with
        // preference backup and with data erasure.
        store.GetString(LocalizationPreferenceKeys.Language, "unset").ShouldBe(ForgeLanguages.German);
    }

    [Fact]
    public void Right_to_left_is_reported_from_the_language_rather_than_assumed()
    {
        var service = CreateService(device: "de-DE");

        service.IsRightToLeft.ShouldBeFalse();
    }

    private static InMemoryLocalizedStringSource DefaultSource() =>
        new InMemoryLocalizedStringSource()
            // English lives in the invariant set, which is exactly how ForgeStrings.resx
            // compiles: the neutral resource file has no culture suffix.
            .With(string.Empty, new Dictionary<string, string>
            {
                [Greeting] = "Hello",
                [Untranslated] = "Only in English",
            })
            .With("de", new Dictionary<string, string>
            {
                [Greeting] = "Hallo",
            });

    private static LocalizationService CreateService(
        ILocalizedStringSource? source = null,
        IPreferenceStore? store = null,
        string device = "en-GB",
        LocalizationOptions? options = null) =>
        new(
            source ?? DefaultSource(),
            store ?? new InMemoryPreferenceStore(),
            new SystemCultureProvider(CultureInfo.GetCultureInfo(device)),
            options);
}
