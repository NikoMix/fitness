# Adding or changing a string

Four files, in this order. Skipping any of them fails the build rather than shipping a broken
label, which is the point.

## 1. Declare the key

`src/Forge.Core/Abstractions/Localization/ForgeStringKeys.cs`

```csharp
/// <summary>Heading above the rest-timer controls.</summary>
public const string WorkoutRestTimerHeading = "workout.rest-timer.heading";
```

Key values are dotted, lower case, and prefixed by screen. They are read in XAML markup
extensions, so a stray capital or space is a runtime lookup failure rather than a compile error;
`ForgeStringResourcesTests` rejects anything that does not match `^[a-z0-9]+([.-][a-z0-9]+)*$`.

Group the constant with the others for its screen, and keep the key prefix and the constant name
telling the same story.

## 2. Add the English string

`src/Forge.App/Resources/Strings/ForgeStrings.resx`

```xml
<data name="workout.rest-timer.heading" xml:space="preserve">
  <value>Rest timer</value>
</data>
```

Entries are sorted by key. Add a `<comment>` whenever the string contains a placeholder or the
wording depends on context a translator cannot see:

```xml
<data name="workout.rest-timer.remaining" xml:space="preserve">
  <value>{0} remaining</value>
  <comment>{0} is an elapsed duration such as "1:35".</comment>
</data>
```

## 3. Translate it

`src/Forge.App/Resources/Strings/ForgeStrings.de.resx`, same key, same placeholders.

If you are not confident in a term, keep the English word and add a `<comment>` saying so, then
list it in [adding-a-locale.md](adding-a-locale.md#terms-flagged-for-review). A flagged English
term is honest and fixable. A confident wrong guess is neither, and it survives review because
nobody in the review can tell.

## 4. Use it

In XAML, after declaring the namespace once per file:

```xml
xmlns:loc="clr-namespace:Forge.App.Services.Localization"
...
<Label Text="{loc:Translate Key=workout.rest-timer.heading}" />
```

`{loc:Translate}` produces a binding, not a string. That is what lets a language change repaint
a page already on screen. Resolving to a string during inflation would freeze the text for the
life of that page.

In a view model, for composite strings or anything needing a format argument:

```csharp
public string Remaining => localization.GetString(ForgeStringKeys.WorkoutRestTimerRemaining, formatter.Duration(remaining));
```

Arguments are formatted with `ILocalizationService.CurrentCulture`, so numbers and dates inside a
composite string follow the display culture automatically.

A view model that shows composite strings should refresh on `ILocalizationService.LanguageChanged`
and detach again when the page disappears - see `LanguageSettingsPageViewModel.Attach` and
`Detach`. The service is a singleton and view models are transient, so subscribing without
detaching keeps every page the user ever opened alive.

## Formatting values

Never call `ToString()` on a number or date destined for the screen, and never interpolate one
into a string. Both use the ambient culture, which is right today only by accident, and neither
knows about the user's unit system.

Use `ILocalizedValueFormatter`:

| Value | Call |
| --- | --- |
| Date | `formatter.ShortDate(date)` / `formatter.LongDate(date)` |
| Time of day | `formatter.ShortTime(time)` |
| Number | `formatter.Number(value, decimals)` / `formatter.WholeNumber(count)` |
| Percentage | `formatter.Percent(fraction, decimals)` |
| Elapsed time | `formatter.Duration(span)` |
| Body mass, load | `formatter.Mass(kilograms)` |
| Height, distance | `formatter.Length(centimeters)` |
| Fluid | `formatter.Volume(milliliters)` |
| Energy | `formatter.Energy(kilocalories)` |

The last four take canonical metric values - Forge stores metric regardless of what it displays -
and convert according to `IForgePreferences.UnitSystem`. The unit system decides *what* is shown;
the culture decides *how* it is written. Do not infer one from the other.

## Verifying

```
dotnet test tests\Forge.Core.Tests\Forge.Core.Tests.csproj
```

`ForgeStringResourcesTests` fails if the key and the string files disagree in either direction,
if any value is blank, or if the German translation does not use the same `{0}` placeholders as
the English source.
