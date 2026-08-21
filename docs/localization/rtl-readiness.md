# Right-to-left readiness

Forge ships no right-to-left language, and no layout has been converted. This document records
what converting would actually involve, so the decision to ship Arabic or Hebrew can be costed
rather than guessed at.

## What already exists

| Piece | State |
| --- | --- |
| `SupportedLanguage.IsRightToLeft` | Present, read from `CultureInfo.TextInfo.IsRightToLeft`. |
| `ILocalizationService.IsRightToLeft` | Present, reports the current language. |
| `android:supportsRtl="true"` | Already set in `Platforms/Android/AndroidManifest.xml`. |
| Per-page `FlowDirection` | Wired on `LanguageSettingsPage` as the pattern to copy. |

So the plumbing is done. What remains is layout work, plus two platform declarations.

## What converting would take

### 1. Set `FlowDirection` at the root, not per page

`LanguageSettingsPage.OnAppearing` sets its own `FlowDirection` as a demonstration. Doing that on
every page is thirty copies of one line. Set it once on the `Window` in `App.CreateWindow`, and
again from `LocalizationRuntime` when the language changes, and let every child inherit through
the default `FlowDirection.MatchParent`. That is one edit to `App.xaml.cs` and one to
`LocalizationRuntime`, and it removes the per-page line entirely.

### 2. Replace directional layout values with flow-relative ones

Sweep the XAML for values that mean "left" rather than "leading":

| Replace | With |
| --- | --- |
| `HorizontalOptions="Start"` / `"End"` | Already correct - these are flow-relative. |
| `HorizontalTextAlignment="Left"` / `"Right"` | `"Start"` / `"End"` |
| Asymmetric `Margin` and `Padding` such as `"16,0,0,0"` | Verify each one under a forced-RTL run; asymmetric insets are the most common thing to get wrong. |
| `Grid` column order | Mirrored automatically by the layout pass; check any layout that positions by absolute coordinates. |

### 3. Mirror the icons that mean direction, and only those

Chevrons, back arrows, progress indicators and "next set" affordances mirror. Play buttons,
cameras, dumbbells and the brand mark do not. There is no automatic rule; each asset needs a
decision, and getting it wrong is more jarring than not mirroring at all.

### 4. Check the charting and gauge surfaces

DevExpress charts, `RadialProgressBar` rings and the workout timer are drawn rather than laid
out, so they do not inherit `FlowDirection`. Each needs its own look, and a ring that fills
anticlockwise for an Arabic user is a deliberate decision either way.

### 5. Declare the language to the platforms

- **Android**: nothing beyond `supportsRtl`, which is already set.
- **iOS**: add `CFBundleLocalizations` to `Platforms/iOS/Info.plist` listing every shipped
  language. Without it iOS keeps system chrome - the share sheet, permission prompts, the
  keyboard - in the development region regardless of what the app itself renders. This is
  worth adding for German too, independently of any RTL work.

### 6. Handle bidirectional text in composite strings

Numbers and Latin unit abbreviations stay left-to-right inside right-to-left text. The platform
handles that, but only if the string is one string. Building sentences by concatenation in C#
breaks it. Every composite string must stay a single resource entry with `{0}` placeholders, so
the translator controls the order - which is already the rule in
[adding-a-string.md](adding-a-string.md).

## Finding the problems before committing to a translation

Android developer options include **Force RTL layout direction**. It flips every layout without
needing a single Arabic string, so the entire layout sweep above can be done, reviewed and fixed
before anyone is paid to translate anything. Do that first: it costs an afternoon and it is the
difference between a costed decision and an open-ended one.

## Estimate

The layout sweep is roughly a day for the current thirty-odd screens, most of it looking rather
than editing. Icon decisions and the drawn surfaces are a second day. The translation itself is
the larger cost and is unrelated to any of the above.
