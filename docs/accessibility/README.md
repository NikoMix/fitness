# Accessibility in Forge

How Forge is made usable with a screen reader, what is genuinely fixed, and what is still broken.

Forge is a gym app. It is used one-handed, out of breath, mid-set, sometimes with the screen
unreadable because of sweat or glare. Everything that makes it usable with TalkBack makes it more
usable in that state too, so this is not a compliance exercise bolted on at the end.

## The rules

1. **Every interactive control carries `SemanticProperties.Description`.** DevExpress buttons are
   otherwise exposed to Android as non-focusable text with no description, and a screen-reader user
   simply cannot reach them.
2. **A description must stay meaningful when its data is absent.** A description bound to the same
   property as the button's visible content is not a description; it is the label a second time, and
   it becomes nothing when the binding is empty. Bound descriptions are paired with a static
   `SemanticProperties.Hint` that names the action, so the control always announces something.
3. **Drawn content is taken out of the accessibility tree and its meaning put into text.** Charts and
   progress rings reach Android as unnamed rectangles. They are marked
   `AutomationProperties.IsInAccessibleTree="False"` and a sibling label carries the same figures.
   This is why the smoke harness's blank-content check does not fire on the progress screen.
4. **A container with a description hides its children from the tree.** `StatRow`, `MetricTile` and
   `ActivityRing` mark their own labels `IsInAccessibleTree="False"` and set one summarising
   description on the chrome in code-behind, so a tile announces once rather than four times.
5. **Headings carry `SemanticProperties.HeadingLevel`.** Page titles are `Level1`, section headings
   are `Level2`. TalkBack can then jump heading to heading instead of swiping through every control.
   A value that merely happens to be large is not a heading.
6. **A list that can empty must not keep its height.** See "The blank container" below.
7. **48dp minimum touch targets**, from `TouchTargetMin` in `ForgeTokens.xaml`. Never a literal.
   Colour comes from DevExpress semantic roles, never a hex literal, so contrast survives dark mode.

## What DevExpress does not give you, and what Forge does about it

Two problems cannot be fixed from XAML at all. Both are handled centrally in
[`src/Forge.App/Accessibility/ForgeAccessibility.cs`](../../src/Forge.App/Accessibility/ForgeAccessibility.cs),
installed from `App`'s constructor by appending to the shared MAUI view mapper - which every
control's handler chains from, so the fix reaches controls that file never names.

### Buttons announced without a role and with no click action

A `DXButton` reaches Android as a bare `android.view.ViewGroup`. With a description set it becomes
focusable and TalkBack reads the label, but the node still reports `clickable=false` and its class
stays `ViewGroup`:

```
desc='Log'  class=android.view.ViewGroup  clickable=false  focusable=true
```

So it is announced as anonymous content rather than as a button, and advertises no way to activate
it. The baseline run reported ten controls in this state.

Forge attaches an accessibility delegate that reports `android.widget.Button`, marks the node
clickable and handles `ACTION_CLICK`. It changes only what the accessibility node reports - the
view's own `Clickable` flag is deliberately left alone, because setting it would insert Forge into
DevExpress's touch handling and risk either swallowing taps or firing a command twice. Activation
is a synthetic tap rather than an invocation of `Command`, because that is the one path that behaves
identically whether a button is driven by `Command` or by a `Clicked` handler.

```
desc='Log'  class=android.widget.Button  clickable=true  focusable=true
```

### Composite editors that label only their outer container

A `ComboBoxEdit` renders as a container holding an `EditText` and an `ImageButton`. Setting
`SemanticProperties.Description` puts a content description on the container **only**. Both inner
views are independently focusable and completely anonymous:

```
ViewGroup    desc='Primary goal'
  EditText     clickable=true  focusable=true   text=''  desc=''
  ImageButton  clickable=true  focusable=false  text=''  desc=''
```

A screen reader stops on "edit box", then on "button", with no indication of what either does.
Forge walks the editor's native view tree once it has been laid out and names both parts, mirroring
what MAUI's own `Entry` handler does for `SemanticProperties.Description`:

```
EditText     desc='Primary goal'
ImageButton  desc='Show options for Primary goal'
```

The affordance wording varies by editor type, so a date field says "Choose a date for ..." rather
than "Show options for ...".

## The blank container

The smoke harness reported a 975x420 container on the food log holding two views, none of which had
text, a content description or an image. That shape has two completely different causes and they
need opposite fixes, so it was diagnosed before it was touched.

**It was not a dead binding.** The generated XAML source confirms all four `ItemsSource` bindings on
that page are applied. The container sat directly beneath the "Recent" heading and its whole subtree
was `ViewGroup > ScrollView > HorizontalScrollView` and nothing else - a `DXCollectionView` with a
`HeightRequest` of 160 and no rows in it. The page's own empty state was showing at the same time,
confirming there genuinely was no data.

So it was a layout bug: **a list that keeps its reserved height after it empties**. Sections now bind
their visibility through
[`CollectionHasItemsConverter`](../../src/Forge.App/Controls/CollectionHasItemsConverter.cs), so an
empty list takes its heading with it rather than leaving a title over a void.

This is worth recognising on sight, because it is the same silhouette as the `ContentPresenter`
regression that emptied 98 bindings across 16 pages. "Present, correctly sized, and completely
empty" can mean a broken binding **or** an honestly empty list, and telling them apart is the whole
job. Check the generated source for the binding before assuming either.

## Verifying

`dotnet build` and `dotnet test` cannot see any of this. Six shipped defects in this project were
visible only on a device. The before-and-after measurements for this sweep are in
[`sweep-evidence.md`](sweep-evidence.md).

```powershell
pwsh tools/smoke/Test-ForgeSmokeChecks.ps1                                  # no device, seconds
pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -Install       # on device, minutes
```

To hear it rather than read a report:

```powershell
adb -s emulator-5554 shell settings put secure enabled_accessibility_services `
  com.google.android.marvin.talkback/com.google.android.marvin.talkback.TalkBackService
adb -s emulator-5554 shell settings put secure accessibility_enabled 1
```

Swipe right to move forward, swipe left to move back, double-tap to activate. Turn it off by setting
`accessibility_enabled` to `0` and clearing `enabled_accessibility_services`.

## Known gaps

- **iOS has had no accessibility work.** The helper above is Android-only. iOS is in product scope,
  but there is no way to verify a fix for it from the current environment, and an unverified
  accessibility claim is worse than an honest gap. The same two DevExpress problems very probably
  exist there and will need `AccessibilityLabel` set on the equivalent inner `UIView`s.
- **`ItemSpanCount` is silently dead on nine pages.** `ReadinessPage`, `HydrationPage` (twice),
  `NutritionPage`, `ExerciseAlternativesPage`, `PlanListPage`, `PlanSchedulePage`,
  `WorkoutSummaryPage` (twice) and both Engagement pages open the `DXCollectionView` tag before the
  attribute, so the attribute is parsed as element text and discarded. The generated code contains no
  `ItemSpanCount` call at all and the build reports nothing. It is a tablet layout bug rather than an
  accessibility one, and changing it alters column counts on screens nobody has re-checked, so it was
  left for a separate change. The two on the food log were fixed because that page was being
  restructured anyway.
- **Fixed list heights remain elsewhere.** Several pages give a `DXCollectionView` a `HeightRequest`,
  which both nests a scroll region inside the page scroll and can clip rows at large font scales. The
  food log's sections are now hidden when empty, but the underlying pattern is unchanged across the
  app.
- **Charts convey nothing beyond their sibling summary.** Marking a chart out of the tree keeps a
  screen reader from stopping on an unnamed rectangle, but per-point data is only available where a
  page also renders the values as text. The progress screen and the nutrition macro split do; other
  charts give a one-line summary only.
