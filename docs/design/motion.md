# Forge motion language

Forge motion should feel clean, quick, and helpful. It must never make workout logging wait for decoration.

## Tokens

Use the C# `MotionTokens` mirror or the XAML tokens from `ForgeTokens.xaml`:

- `MotionFast` / `MotionTokens.Fast` (150 ms): tiny local changes, fades, press feedback.
- `MotionMedium` / `MotionTokens.Medium` (250 ms): slide-ins, cross-fades, number changes.
- `MotionSlow` / `MotionTokens.Slow` (400 ms): attention pulses where the user is not mid-input.
- `MotionCelebration` / `MotionTokens.Celebration` (900 ms): set-completion or PR celebration.

Short durations are for small local changes. Longer durations are only for transitions that move many pixels or intentionally mark a milestone.

## Reduce Motion contract

Every motion entry point in `Forge.App.Motion` consults `MotionPreferences.Current` centrally. Feature authors must use these helpers rather than calling MAUI animation APIs directly.

Platform detection:

- iOS reads `UIAccessibility.IsReduceMotionEnabled`.
- Android reads `Settings.Global.TRANSITION_ANIMATION_SCALE` and `Settings.Global.ANIMATOR_DURATION_SCALE`; either value at zero skips animation.

When Reduce Motion is enabled, helpers set the final visual state immediately. No pulse, number tween, press scale, stagger, or celebration runs.

## Never block input

Animations are visual feedback only. Do not `await` them before saving data, navigating, completing a set, or enabling the next action. Start persistence and navigation immediately, then fire animation work independently with a cancellation token tied to the page or view lifecycle.

```csharp
await CompleteSetAsync(cancellationToken);
_ = ForgeAnimations.CelebrateAsync(RootLayout, cancellationToken: cancellationToken);
```

## Performance guidance

Forge targets 60 fps with no frame over 16.6 ms. Prefer transform and opacity animations (`TranslationX`, `TranslationY`, `Scale`, `Opacity`) because they are cheap. Do not animate layout-affecting properties such as `WidthRequest`, `HeightRequest`, `Margin`, `Padding`, row height, or column width; they force a layout pass every frame and are the most common source of janky MAUI animation.

## Usage examples

### Fade and slide a panel in

```csharp
private CancellationTokenSource? motion;

protected override void OnAppearing()
{
    base.OnAppearing();
    motion = new CancellationTokenSource();
    _ = ForgeAnimations.SlideInAsync(SummaryCard, fromY: 16, cancellationToken: motion.Token);
}

protected override void OnDisappearing()
{
    motion?.Cancel();
    motion?.Dispose();
    motion = null;
    base.OnDisappearing();
}
```

### Count changing metrics

```xml
<Label Text="0">
    <Label.Behaviors>
        <motion:AnimatedNumberBehavior Value="{Binding CaloriesToday}" Format="0" />
    </Label.Behaviors>
</Label>
```

### Press feedback

```xml
<dx:DXBorder Style="{StaticResource MotionPressable}">
    <dx:DXBorder.Behaviors>
        <motion:PressFeedbackBehavior />
    </dx:DXBorder.Behaviors>
</dx:DXBorder>
```

`PressFeedbackBehavior` triggers subtle scale and haptic feedback. Android checks the system haptic setting; iOS haptic APIs are used only when supported and the OS suppresses output when haptics are disabled.

### Stagger list rows without slowing long lists

```xml
<dx:DXCollectionView ItemsSource="{Binding Items}">
    <dx:DXCollectionView.ItemTemplate>
        <DataTemplate>
            <Grid>
                <Grid.Behaviors>
                    <motion:StaggeredAppearBehavior Index="{Binding Index}" MaxTotalDelay="210" />
                </Grid.Behaviors>
            </Grid>
        </DataTemplate>
    </dx:DXCollectionView.ItemTemplate>
</dx:DXCollectionView>
```

The delay is capped so a long virtualized list does not trickle in slowly.

### Celebration

```csharp
_ = ForgeAnimations.CelebrateAsync(RootLayout, glyph: "★", cancellationToken: cancellationToken);
```

This epic uses a lightweight pure-MAUI glyph burst rather than Lottie. The Lottie package is referenced, but the API and asset contract were not verified in this task; guessing would risk broken builds and oversized assets. The fallback has no asset cost and still honours Reduce Motion.

## Integration note

`Resources/Styles/ForgeMotion.xaml` is intentionally not merged in `App.xaml` by this epic because shared app resources are owned by another integration task. Merge it during integration before using the XAML styles.
