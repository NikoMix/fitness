# Converting the remaining screens

The localization mechanism is finished and one screen uses it. Converting the other forty-nine
XAML files is a separate piece of work with one hard scheduling constraint.

## The ordering constraint

**Run the conversion when no other stream is editing XAML.**

Converting a screen rewrites nearly every line of its markup: each literal becomes a
`{loc:Translate}`, and the file gains an `xmlns:loc` declaration. Any branch that touched the
same file in parallel produces a conflict on almost every line, and resolving it by hand means
re-deciding every change on both sides. That is not a merge, it is a rewrite, and it is where
translated strings get silently dropped.

So the conversion is sequential work, not parallel work:

1. Land or park every branch that touches `src/Forge.App/**/*.xaml`.
2. Announce a freeze on XAML edits.
3. Convert, in one branch, screen by screen.
4. Merge, then lift the freeze.

The freeze does not need to be long - see the estimate below - but it does need to be real. A
single feature branch editing one page during the conversion costs more to reconcile than the
whole screen took to convert.

Two things reduce the exposure if a freeze is genuinely impossible:

- Convert in feature-sized batches and merge each one the same day, so any collision is one
  feature wide rather than the whole app.
- Do the `xmlns:loc` declaration and the string extraction in a single commit per screen, so a
  bisect or a revert is per screen.

## Before starting

Add the wiring line. Nothing in this document works without it:

```csharp
// src/Forge.App/Features/FeatureRegistration.cs, keeping the list alphabetical
.AddLegalFeature()
.AddLocalizationFeature()   // <- add this
.AddMediaFeature()
```

Also add `using Forge.App.Features.Settings.Localization;` to that file's using block.

Until this line exists, `LocalizedStrings.Current` throws when any localized XAML is inflated,
which is deliberate: a page that silently rendered markers would be worse.

## Converting one screen

1. **Inventory the literals.** Every `Text=`, `Title=`, `Placeholder=`, `Label=` and
   `Description=` with a literal value, plus every capitalised string literal in the matching
   view model.
2. **Name the keys.** `<screen>.<element>`, dotted and lower case, following
   [adding-a-string.md](adding-a-string.md). Reuse `common.*` rather than adding a fourth
   "Cancel".
3. **Add constants, English strings and German translations** in that order.
4. **Rewrite the XAML.** Add
   `xmlns:loc="clr-namespace:Forge.App.Services.Localization"` to the root element, then replace
   each literal with `{loc:Translate Key=...}`.
5. **Rewrite the view model.** Replace literals with `localization.GetString(...)`, replace any
   `ToString()` or string interpolation of a number, date or measurement with the matching
   `ILocalizedValueFormatter` call, and add `Attach`/`Detach` if the page shows composite
   strings.
6. **Check for sentences built by concatenation.** `"You lifted " + volume + " today"` cannot be
   translated - German puts the verb elsewhere. It must become one resource entry with `{0}`.
7. **Run the gates.**

## Watch for

- **Strings that are not user-facing.** Route names, preference keys, log messages, analytics
  identifiers and `AutomationId` values must stay literal. Translating a route name breaks
  navigation; translating a log message makes support harder.
- **Accessibility text.** `SemanticProperties.Description` and `SemanticProperties.Hint` are read
  aloud and must be translated. They are easy to miss because they are not visible.
- **Enum-derived display text.** Several view models call `.ToString()` on an enum and show the
  result. Those need a key per member, not a translated enum name.
- **`ComboBoxEdit` option lists built from literal strings.** `UnitsSettingsPageViewModel` binds
  option lists that are also used as the *value* being parsed back. Translating the display
  string without separating it from the value silently breaks the setter.
- **Text length.** German runs roughly 30% longer than English. Fixed-width buttons and
  single-line labels that fit in English will truncate. Check the longest strings on the
  narrowest supported width.

## DevExpress localization

`MauiProgram.cs` currently calls:

```csharp
.UseDevExpress(useLocalization: false)
```

with a comment saying the flag stays false until localized resources arrive. They have now
arrived, but the flag governs DevExpress's *own* strings - date-picker month names, editor
validation messages, collection-view "no data" text, filter panel labels - not Forge's.

**Recommendation: leave it `false` until the conversion actually begins, then set it to `true`
in the same branch.**

The reasoning is that the flag is only ever wrong in one direction at a time:

- Leaving it `false` after the app is translated produces a screen where Forge's own labels are
  German and the date picker inside them is English. That is visibly broken, but only on screens
  that use a localizable DevExpress control, and today only one screen is translated at all.
- Turning it `true` now buys nothing, because there is nothing to be consistent with, and it
  costs start-up time on every launch for every user.

On cost: enabling it loads the DevExpress localization resources during `UseDevExpress`, against
a 2.0 s cold-start budget. It has not been measured on a device here, and it should be before it
ships - the honest expectation is single-digit to low tens of milliseconds, not a tenth of a
second, because it is resource loading rather than assembly loading. That is affordable, but
"affordable" should be a measurement rather than an assumption, so:

1. Measure cold start on a mid-range Android device with the flag `false`.
2. Flip it, measure again.
3. If the delta is above about 50 ms, look at deferring the load rather than accepting it - the
   English strings are correct for most users on first launch anyway.

If the measurement is bad, the fallback is to enable it only when the resolved language is not
English, which is a one-line condition:

```csharp
.UseDevExpress(useLocalization: storedLanguage != "en")
```

That is not possible from a feature registration, because `UseDevExpress` runs on the builder
before any service exists. It would need the stored preference read directly in `MauiProgram`,
which is a small, contained edit to that file.

## Estimate

Measured against the current tree: 50 XAML files outside `Resources/Styles`, roughly 354 literal
user-facing attribute values, and 41 view models containing roughly 392 capitalised string
literals - of which a substantial share are not user-facing and must stay put.

| Phase | Estimate |
| --- | --- |
| Extract, key and translate ~450-500 user-facing strings | 3-4 days |
| Rewrite 49 XAML files and 41 view models | 3-4 days |
| Enum and option-list cases, concatenated sentences, accessibility text | 1-2 days |
| German review by a native speaker | 1 day, plus turnaround |
| Layout pass for German text length on the narrowest width | 1 day |
| **Total** | **9-12 working days for one person** |

The mechanical parts are highly parallelisable across people but not across branches, which is
the whole point of the freeze. Two people converting different features in the same branch works
well; two people converting in different branches does not.

A useful first slice is the Settings and Profile trees: they are string-dense, layout-simple, and
they contain the option-list and enum cases that need a decided pattern before the rest of the
app copies one.
