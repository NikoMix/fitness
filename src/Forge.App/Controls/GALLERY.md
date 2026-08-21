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
