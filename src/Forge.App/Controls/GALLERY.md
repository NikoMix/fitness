# Forge controls gallery

Use these controls from feature XAML with:

```xml
xmlns:controls="clr-namespace:Forge.App.Controls"
xmlns:dx="http://schemas.devexpress.com/maui"
```

All examples assume the shared dictionaries in `Resources/Styles/ForgeTokens.xaml` and `Resources/Styles/ForgeStyles.xaml` are loaded by the app.

## ForgeCard

Standard Forge surface container for grouped content.

Bindable properties:

- `Title` (`string`) — optional card heading.
- `Subtitle` (`string`) — optional secondary heading.
- `Content` — normal nested XAML content.

```xml
<controls:ForgeCard Title="Today's plan"
                    Subtitle="Three focused lifts">
    <VerticalStackLayout Spacing="{StaticResource SpaceS}">
        <Label Text="Squat · Bench · Row"
               Style="{StaticResource BodyText}" />
    </VerticalStackLayout>
</controls:ForgeCard>
```

## MetricTile

Compact readout for changing stats such as weight, calories, streaks, sets, or readiness. The value and unit columns reserve width so digit changes do not cause layout reflow.

Bindable properties:

- `Value` (`string`) — large centred number or short value.
- `Unit` (`string`) — short unit such as `kg`, `kcal`, or `days`.
- `Caption` (`string`) — descriptive label.
- `Accent` (`Color`) — optional accent for the value and unit; omit to use the default surface text colour.

```xml
<controls:MetricTile Value="82.5"
                     Unit="kg"
                     Caption="Working weight"
                     Accent="{dx:ThemeColor Primary}" />
```

## EmptyState

Purposeful first-run and no-data state. Use encouraging copy that explains the next useful action rather than apologizing for missing data.

Bindable properties:

- `Glyph` (`string`) — optional icon glyph; defaults to `✦`.
- `Headline` (`string`) — concise headline.
- `Message` (`string`) — supportive body copy.
- `ActionText` (`string`) — optional primary action label.
- `ActionCommand` (`ICommand`) — command invoked by the primary action. The button is shown only when both `ActionText` and `ActionCommand` are set.

```xml
<controls:EmptyState Glyph="＋"
                     Headline="Build your first workout"
                     Message="Start with one simple session. Forge will keep the structure ready for next time."
                     ActionText="Create workout"
                     ActionCommand="{Binding CreateWorkoutCommand}" />
```

## SkeletonPlaceholder

Loading block for local database reads or short calculations. It pulses while busy, but reads the platform Reduce Motion setting and becomes a static muted block when motion is reduced.

Bindable properties:

- `IsBusy` (`bool`) — shows and animates the placeholder when `true`; hides it when `false`.

```xml
<controls:SkeletonPlaceholder IsBusy="{Binding IsLoading}"
                              HeightRequest="88" />
```

## SectionHeader

Section title with an optional trailing text action. The action keeps the 48 dp minimum touch target.

Bindable properties:

- `Title` (`string`) — section heading.
- `ActionText` (`string`) — optional trailing action label.
- `ActionCommand` (`ICommand`) — command invoked by the trailing action. The action is shown only when both `ActionText` and `ActionCommand` are set.

```xml
<controls:SectionHeader Title="Recent workouts"
                        ActionText="See all"
                        ActionCommand="{Binding SeeAllWorkoutsCommand}" />
```

## StatRow

Two-column row for settings, summaries, and key/value details.

Bindable properties:

- `Label` (`string`) — left-side label.
- `Value` (`string`) — right-side value.

```xml
<controls:StatRow Label="Weekly target"
                  Value="4 workouts" />
```

## PageHeader

The single in-page title block. Forge shows **exactly one title per page**:

- **Tab roots** (`Today`, `Profile`, …) and **flows that own their own chrome** (onboarding) set `Shell.NavBarIsVisible="False"` and use `PageHeader`. The tab bar already names the destination, so a Shell title above an identical in-page heading is pure duplication.
- **Pushed detail pages** that rely on the Shell back button keep the Shell `Title` and do **not** add an in-page heading that repeats it.

Unlike a Shell title, `PageHeader` can carry an eyebrow and subtitle, scroll away, and grow with the OS font scale.

Bindable properties:

- `Eyebrow` (`string`) — optional small line above the title, such as a date or step counter.
- `Title` (`string`) — the page title.
- `Subtitle` (`string`) — optional supporting sentence.
- `ActionText` / `ActionCommand` — optional trailing action; shown only when both are set.
- `BackText` / `BackCommand` — optional leading back affordance, for flows where "back" is not "pop the page". Shown only when both are set.

```xml
<controls:PageHeader Eyebrow="{Binding DateLine}"
                     Title="{Binding Greeting}"
                     Subtitle="Read from what you have logged on this device."
                     ActionText="Refresh"
                     ActionCommand="{Binding RefreshCommand}" />
```

## ActivityRing

A progress ring with a percentage in the middle, a label and a detail line. The ring is square and **sizes itself to the width it is given**, so callers control layout and never guess a pixel size that happens to fit one handset. Value animation is switched off when Reduce Motion is enabled.

Bindable properties:

- `Progress` (`double`) — completion between 0 and 1.
- `Label` (`string`) — what the ring measures.
- `Detail` (`string`) — the real numbers behind it, for example `2 of 5 working sets`.

Lay rings out with a wrapping `FlexLayout` and a percentage basis rather than a horizontal scroller. Percentage basis fits the viewport at any screen width, and an extra ring wraps onto a second row instead of being clipped off-screen.

```xml
<FlexLayout BindableLayout.ItemsSource="{Binding Rings}"
            Direction="Row"
            Wrap="Wrap"
            JustifyContent="SpaceBetween"
            AlignItems="Stretch">
    <BindableLayout.ItemTemplate>
        <DataTemplate>
            <controls:ActivityRing Progress="{Binding Progress}"
                                   Label="{Binding Label}"
                                   Detail="{Binding Detail}"
                                   FlexLayout.Basis="31%" />
        </DataTemplate>
    </BindableLayout.ItemTemplate>
</FlexLayout>
```

## StepProgressIndicator

Segmented "Step 2 of 6" progress for multi-step flows. Segments are countable at a glance; the caption is what a screen reader is given.

Bindable properties:

- `StepCount` (`int`) — total number of steps.
- `CurrentStep` (`int`) — one-based position of the step showing.
- `StepTitle` (`string`) — appended to the spoken description.

```xml
<controls:StepProgressIndicator StepCount="{Binding StepCount}"
                                CurrentStep="{Binding StepNumber}"
                                StepTitle="{Binding StepTitle}" />
```

## AdvisoryPanel

Safety refusals, warnings and step guidance. Blocking advisories use the distinct `AdvisoryCard` surface; non-blocking guidance uses an ordinary card so routine notes do not shout. The whole panel is announced as a single accessible item.

Bindable properties:

- `Headline` (`string`) — short heading describing the outcome.
- `Message` (`string`) — the full reasoning, one paragraph per reason.
- `Signpost` (`string`) — where to get real help when the guardrail is a health matter.
- `Reassurance` (`string`) — what happened to the user's input. Never leave this empty on a refusal.
- `IsBlocking` (`bool`) — whether the advisory prevents continuing.

```xml
<controls:AdvisoryPanel IsVisible="{Binding HasSafetyAdvisory}"
                        Headline="{Binding SafetyHeadline}"
                        Message="{Binding SafetyMessage}"
                        Signpost="{Binding SafetySignpost}"
                        Reassurance="{Binding SafetyReassurance}"
                        IsBlocking="{Binding IsSafetyBlocking}" />
```
