# Tablet layout

Forge ships one binary for phones and tablets. The iOS `Info.plist` declares `UIDeviceFamily` 1
**and** 2, so the App Store listing supports iPad: App Store Connect requires a 13-inch iPad
screenshot set, and a reviewer will run Forge on an iPad. Every layout in the app was designed
phone-first. This document records how that was fixed, so the next screen is built the same way as
the last one.

## The decision: adapt on measured width

Three options were on the table.

| Approach | Verdict |
| --- | --- |
| `OnIdiom` | Rejected for layout. A device idiom is fixed for the life of the process, and an iPad is not one width. |
| `VisualStateManager` + `AdaptiveTrigger` | Rejected. `AdaptiveTrigger` keys off the window, which is right, but a `VisualState` setter cannot reach a sibling element without `TargetName`, so a two-pane page needs states scattered across half a dozen elements that must be kept consistent by hand. |
| **Container-driven sizing** | **Chosen.** One container measures itself and publishes the decisions; the page binds to them. |

The deciding argument is iPad multitasking. The same hardware reports **1366, 981, 678 and 320
points wide** depending on how the user has arranged Split View and Slide Over, and they change it
by dragging while the app is running. Anything keyed on "is this a tablet" is wrong in three of
those four states, and an iPad in a 320-point Slide Over must be laid out as a phone. Rotation has
the same property. So: **layout follows the measured width of the window, never the device.**

The one genuine exception is the **type scale**. A device's physical size really is fixed, and a
tablet in Slide Over should still use tablet type. That is the only thing keyed on `OnIdiom`, and
it is applied centrally in `ForgeStyles.xaml` so no page opts in.

### Where the pieces live

| File | Role |
| --- | --- |
| `Forge.Core/Adaptive/AdaptiveLayoutMetrics.cs` | The arithmetic. Pure, framework-free, unit tested against real device sizes. |
| `Forge.App/Adaptive/AdaptiveHost.cs` | A `ContentView` that measures itself and publishes bindable layout decisions. It never moves its child. |
| `Resources/Styles/ForgeAdaptive.xaml` | The implicit style that feeds `AdaptiveHost` from the tokens, plus the `DetailPane` and `MeasuredColumn` styles. |
| `Resources/Styles/ForgeTokens.xaml` | Every number: breakpoints, measures, pane widths, tablet type scale. |

`AdaptiveHost` deliberately does **not** resize or reposition its child. A page's layout is always
readable from that page's own XAML, rather than hidden inside a container's arrange pass.

## Breakpoints

Two orthogonal rules, because they answer different questions.

**Size class** — how generous should spacing and column counts be? Material 3 window size classes;
Apple's regular/compact boundaries fall close enough that one scale serves both stores.

| Class | Width | Examples |
| --- | --- | --- |
| Compact | < 600 | every phone in portrait; iPad Slide Over (320) |
| Medium | 600–839 | iPad mini portrait (744); half-width Split View |
| Expanded | ≥ 840 | iPad 11" landscape (1194); iPad 13" portrait (1024) and landscape (1366) |

**Split capability** — is there room for two panes? `width ≥ 740 && height ≥ 520`.

Height is in the test on purpose. A large phone in landscape is **932 points wide** — wider than an
iPad in portrait Split View — and 430 points tall. Gating on width alone would split a phone the
moment somebody turned it sideways, leaving two columns four rows deep behind a keyboard. 740 sits
just under the 744-point width of an iPad mini in portrait, so the smallest tablet Forge will meet
still gets a real split.

`AdaptiveLayoutMetricsTests` pins these against the actual point sizes of every device in the
table above, in both orientations.

## The two problems, and the two answers

### 1. Line length

An uncapped paragraph on a 13-inch iPad runs to roughly 180 characters a line. Past about ninety
the eye loses the start of the next line on the return sweep, so long-form text is *physically
harder* to read on the larger screen. Two caps:

- `ReadingMeasure` (680) — running prose: legal documents, exercise guidance, the mid-workout screen.
- `ContentMeasure` (1040) — a column of cards and controls, which tolerate more width than a
  sentence does.

`ResolveContentWidth` returns **-1 when the cap would not bite**, which maps onto MAUI's "no
request". On a phone the width request is therefore never applied at all: the layout is not
merely equivalent to the old one, it is the same one.

### 2. A narrow column floating in whitespace

Capping alone is the lazy fix — a phone layout centred in a box still looks like a phone layout in
a box. So the width is spent on something real wherever the content justifies it:

**True two-pane (list and detail side by side)**

| Page | Panes |
| --- | --- |
| `Exercises/ExerciseLibraryPage` | Exercise list · full technique guidance |
| `Plans/PlanTemplatesPage` | Template list · the week that template produces, and Adopt |
| `Nutrition/Recipes/RecipesPage` | Recipe list · scaled ingredients, macros and steps |

**Multi-column card grids** (`DXCollectionView.ItemSpanCount` bound to `CardColumns`)

Achievements, Streaks, Shop products, Plan list, Plan schedule, Workout history and summary,
Nutrition meal summaries, Hydration presets and history, Readiness components, Food log shortcuts,
Exercise alternatives.

**Deliberately capped and single-column**: the active workout and rest timer use the *tight*
measure, not the wide one. Everything there is touched between sets, one-handed and out of breath;
spreading the set controls across a 13-inch display would put the weight field and the tick a
forearm apart. Wider is worse on that screen.

## The recipe

### Every page

```xml
<ContentPage xmlns:adaptive="clr-namespace:Forge.App.Adaptive" ...>
    <adaptive:AdaptiveHost x:Name="Adaptive">
        <Grid Padding="{StaticResource PagePadding}"
              HorizontalOptions="Center"
              WidthRequest="{Binding Source={x:Reference Adaptive}, Path=ContentWidth}">
            ...
        </Grid>
    </adaptive:AdaptiveHost>
</ContentPage>
```

Use `Path=ReadingWidth` instead for prose-heavy pages and for anything used mid-set.

### A list-and-detail page

```xml
<adaptive:AdaptiveHost x:Name="Adaptive">
    <Grid ColumnDefinitions="{Binding Source={x:Reference Adaptive}, Path=SplitColumns}"
          ColumnSpacing="{Binding Source={x:Reference Adaptive}, Path=PaneSpacing}"
          Padding="{StaticResource PagePadding}">

        <Grid Grid.Column="0"> <!-- list --> </Grid>

        <dx:DXBorder Grid.Column="1"
                     Style="{StaticResource DetailPane}"
                     IsVisible="{Binding Source={x:Reference Adaptive}, Path=IsSplit}">
            <!-- detail -->
        </dx:DXBorder>
    </Grid>
</adaptive:AdaptiveHost>
```

`SplitColumns` always has two columns, so a child in column 1 is never out of range. When stacked
the second collapses to zero width and `PaneSpacing` drops to zero — a grid applies column spacing
between every pair of columns including an empty one, and a fixed gutter would leave dead space
down the right edge of every phone screen.

The view model has to know, because navigation changes: on a phone, opening an item pushes a page;
in a split it fills the pane. Add an `IsSplitLayout` property and push it from the page:

```csharp
Adaptive.PropertyChanged += (_, e) =>
{
    if (e.PropertyName == nameof(AdaptiveHost.IsSplit))
    {
        viewModel.IsSplitLayout = Adaptive.IsSplit;
    }
};
```

Set it in `OnAppearing` too, for the first layout.

**Share the detail markup, do not copy it.** `ExerciseGuidanceView` is a `ContentView` used by both
`ExerciseDetailPage` and the library's detail pane, so the phone and the tablet show the same
screen rather than two implementations that agree today.

**Nest a second host inside a wide pane.** The reading measure has to be taken from the width the
prose actually gets; capping against the window would force a 680-point column into a 420-point
pane on a small tablet.

### A card grid

```xml
<dx:DXCollectionView ItemsSource="{Binding Items}"
                     ItemSpanCount="{Binding Source={x:Reference Adaptive}, Path=CardColumns}" />
```

Only on a collection that gets the full page width. A collection inside a 360-point list pane would
be handed the *page's* column count and shred itself. Override `MinimumCardWidth` and
`MaximumCardColumns` on the host where the content has a natural count — the plan schedule sets
seven, because a training week is seven days and a calendar that wraps at three reads as a list of
dates.

## What `AdaptiveHost` publishes

| Property | Meaning |
| --- | --- |
| `SizeClass`, `IsCompact`, `IsMediumOrWider`, `IsExpanded` | Window size class |
| `IsSplit`, `IsStacked` | Whether two panes fit |
| `IsLandscape` | Wider than tall |
| `ReadingWidth`, `ContentWidth` | Width to request, or -1 for "leave it alone" |
| `SplitColumns`, `PaneSpacing`, `ListPaneWidth` | Two-pane geometry |
| `DetailPaneColumn`, `DetailPaneColumnSpan` | Where the detail goes when it cannot sit beside the list |
| `CardColumns` | Columns for a wrapping card grid |

## Typography and touch targets

Applied through `OnIdiom` in `ForgeStyles.xaml`, so it reaches every page including ones nobody
edited. The increase is deliberately **sub-linear**: a 13-inch iPad has roughly three times the
area of a phone but is held at most twice the distance, and body text that grows in proportion to
the display looks like an accessibility setting left switched on.

| Token | Phone | Tablet |
| --- | --- | --- |
| Display | 34 | 42 |
| Headline L / M | 28 / 24 | 34 / 29 |
| Title L / M | 20 / 17 | 23 / 19 |
| Body L / M | 16 / 14 | 17 / 15 |
| Label / Caption | 13 / 12 | 14 / 13 |
| Metric / Metric large | 40 / 56 | 52 / 76 |

Headings move furthest, because a larger canvas needs a stronger hierarchy to stay navigable. Body
and caption barely move, because their size is set by reading distance, not by the display.

Touch targets: `TouchTargetMin` stays at **48** on every device. It is already an absolute minimum
rather than a comfortable size, and enlarging it on a tablet would space every list row out for no
benefit. Only `TouchTargetPrimary` grows, 64 → 76, for the controls used mid-set: a tablet propped
on a gym floor is further from both the eye and the hand than a phone held at chest height.

`PagePadding` is an `OnIdiom<Thickness>` (20,16,20,24 → 28,20,28,32). It is applied directly by
about forty pages rather than through a style, so widening the token is what gives every screen —
including the ones this wave could not touch — room to breathe. The tablet value is kept modest
because it also applies in a 320-point Slide Over window.

> **Reading `PagePadding` from C#**: it is an `OnIdiom<Thickness>`, not a `Thickness`. XAML applies
> the implicit conversion; a cast from `object` cannot and will throw. See
> `LegalDocumentPage.PagePadding()`.

## Pages this wave could not touch

`Features/Today`, `Onboarding`, `Profile`, `Insights`, `Progress`, `Settings` and `Controls` were
owned by other worktrees. They already benefit from the shared type scale and the wider
`PagePadding`, but none of them is width-capped, so on a 13-inch iPad they still render a single
full-width column. Apply the following once those branches land.

### Mechanical: add the measure cap

For each page, add the namespace, wrap the body, and cap the element that carries
`Padding="{StaticResource PagePadding}"`:

1. `xmlns:adaptive="clr-namespace:Forge.App.Adaptive"` after the `xmlns:dx` line.
2. `<adaptive:AdaptiveHost x:Name="Adaptive">` immediately inside `<ContentPage>`, closed before
   `</ContentPage>`.
3. On the element with `Padding="{StaticResource PagePadding}"`, add:
   ```xml
   HorizontalOptions="Center"
   WidthRequest="{Binding Source={x:Reference Adaptive}, Path=ContentWidth}"
   ```

| Page | Measure | Notes |
| --- | --- | --- |
| `Today/TodayPage` | `ContentWidth` | Also bind `ItemSpanCount` on the ring/metric collections. |
| `Onboarding/WelcomePage` | `ReadingWidth` | Almost entirely prose; the worst offender today. |
| `Onboarding/GoalWizardPage` | `ReadingWidth` | A form. One question per step reads badly at 1366 points wide. |
| `Profile/ProfilePage` | `ContentWidth` | |
| `Progress/ProgressPage` | `ContentWidth` | `Destinations` is a navigation grid: bind `ItemSpanCount` to `CardColumns`. |
| `Insights/InsightsPage` | `ContentWidth` | `Highlights`: bind `ItemSpanCount`. |
| `Insights/PersonalRecordsPage` | `ContentWidth` | `Records`: bind `ItemSpanCount`. |
| `Insights/BodyMetricsPage` | `ContentWidth` | Charts should keep the **wide** measure; a chart earns its width. |
| `Insights/ExerciseProgressPage` | `ContentWidth` | As above. |
| `Settings/SettingsPage` | `ContentWidth` | Strong two-pane candidate — see below. |
| `Settings/UnitsSettingsPage` | `ReadingWidth` | |
| `Settings/NotificationSettingsPage` | `ReadingWidth` | |
| `Settings/DataManagementPage` | `ReadingWidth` | |
| `Settings/DeleteMyDataPage` | `ReadingWidth` | Consequential prose; keep it tight. |

### Worth more than the mechanical pass

- **`Settings/SettingsPage`** is the strongest remaining two-pane candidate and the one iPadOS
  users will expect: the settings list on the left, the chosen sub-page on the right. It needs the
  same treatment the exercise library got — a shared `ContentView` per settings sub-page so the
  phone pushes it and the tablet embeds it, plus `IsSplitLayout` on `SettingsPageViewModel`.
- **`Insights`** charts should *not* be capped at the reading measure. A time series is one of the
  few things that genuinely reads better at 1366 points, so give those pages `ContentWidth` and let
  the chart fill it.
- **`Controls/MetricTile`** and **`Controls/ActivityRing`** size themselves from
  `ForgeResources.Double`. That helper pattern-matches on `double` and falls back safely, so the
  `OnIdiom` tokens do not break it — but if either control is ever given a `Thickness` or
  `OnIdiom` token, it will silently take the fallback.

## Verification status

Verified on an **Android tablet emulator** (Pixel Tablet, 2560×1600 at 320 dpi — 1280×800 points),
in both orientations, which exercises exactly the same measured-width logic that an iPad will.
Also re-checked on a phone emulator to confirm the phone layout is unchanged.

**Not yet verified on real iPad hardware.** An Android tablet is a proxy, not a substitute. The
following still need a pass on a Mac with the iOS simulator or a device:

- **Split View and Slide Over.** Android has no equivalent gesture, so the 320-point Slide Over
  case and the drag-to-resize transition are untested. This is the highest-value remaining check,
  because it is the one case the width-driven design exists for.
- **13-inch iPad at 1366 × 1024**, which is wider than any Android tablet AVD used here.
- **iPad type rendering.** The tablet type scale was chosen against Android font metrics.
- **The App Store screenshot set**, which must be captured on a 13-inch iPad regardless.
