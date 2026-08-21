# Adding a language

Adding French, as a worked example. Three files, one of which is optional.

## 1. Register the language

`src/Forge.Core/Abstractions/Localization/SupportedLanguage.cs`

```csharp
public const string French = "fr";

public static IReadOnlyList<SupportedLanguage> All { get; } = [Default, new(German), new(French)];
```

Display names, English names and the right-to-left flag are all read from `CultureInfo`, so
nothing else needs declaring. The picker shows each language in its own language - "Français",
not "French" - because a user looking for their language does not read the current one.

Use the **neutral** code (`fr`), not a regional one (`fr-CA`). Forge translates per language and
formats per culture: a Canadian device keeps `fr-CA` date and number conventions while reading
the `fr` translation. Ship a regional resource file only when the wording genuinely differs, and
then only for the entries that differ - the fallback chain fills in the rest.

## 2. Create the resource file

Copy `src/Forge.App/Resources/Strings/ForgeStrings.resx` to `ForgeStrings.fr.resx` and translate
every `<value>`. Keep:

- the same keys, in the same order;
- the same `{0}`, `{1}` placeholders, in whatever position the target language needs;
- the `<comment>` elements, updated where they help the next translator.

The `.resx` glob in the SDK picks the file up automatically and MSBuild compiles it into a
`fr/Forge.App.resources.dll` satellite assembly. No project file change is needed.

## 3. Extend the resource test (optional but recommended)

`tests/Forge.Core.Tests/Localization/ForgeStringResourcesTests.cs` currently names
`ForgeStrings.de.resx` explicitly. Add the new file to `No_shipped_string_is_blank` and give it
its own completeness and placeholder tests, or generalise those tests over every
`ForgeStrings.*.resx` on disk. Without it, a half-finished translation falls back to English
silently, which reads as a half-translated app rather than as the bug it is.

## Verifying

```
dotnet test tests\Forge.Core.Tests\Forge.Core.Tests.csproj
dotnet build src\Forge.App\Forge.App.csproj -f net10.0-android
```

Then run the app, open **Settings → Language**, and check that:

- the new language appears in the picker, named in its own language;
- switching to it repaints the current screen without a restart;
- the formatting preview shows that language's date order and decimal separator;
- the measurement system on that card has **not** changed.

## Size cost

Each language adds one satellite assembly of a few kilobytes. That is negligible against the
Android bundle ceiling, but `SatelliteResourceLanguages` is the lever if it ever stops being.

## Terms flagged for review

Kept in English or translated with low confidence. Each is a real decision, not an oversight, and
each should be confirmed by a native speaker before the language is advertised in the store.

| Key | Language | Shipped value | Why it is flagged |
| --- | --- | --- | --- |
| `common.imperial` | German | `Imperial` | German fitness apps generally use the English term rather than "angloamerikanisch", but this is a product-voice judgement rather than a translation fact. |

Add a row here whenever you keep an English term or guess at one. A flagged term gets fixed. An
unflagged guess ships.
