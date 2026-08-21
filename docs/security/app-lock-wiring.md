# App lock: what the integrator has to do

Everything below lives in files this feature does not own. The feature compiles and its tests
pass without them; the list is what turns it on and makes it work on a device.

## 1. Register the feature — required

`src/Forge.App/Features/FeatureRegistration.cs`, keeping the list alphabetical (between
`AddProgressFeature()` and `AddSettingsFeature()`):

```csharp
.AddSecurityFeature()
```

and the matching using:

```csharp
using Forge.App.Features.Security;
```

That is the only shared code change the feature needs. Lock triggers, app-switcher privacy and
the platform prompts are all wired from inside `Features/Security` — see section 5.

## 2. Android manifest — required for the prompt to appear

`src/Forge.App/Platforms/Android/AndroidManifest.xml`, alongside the existing permissions:

```xml
<uses-permission android:name="android.permission.USE_BIOMETRIC" />
```

Without it `BiometricPrompt.Authenticate` throws a `SecurityException` at runtime. It is a
normal permission, so there is no runtime consent dialog and no Play Console declaration.

## 3. iOS Info.plist — required for Face ID, and for App Review

`src/Forge.App/Platforms/iOS/Info.plist`, inside the top-level `<dict>`:

```xml
<key>NSFaceIDUsageDescription</key>
<string>Forge asks for Face ID before showing your training history and body measurements, so they are not visible to anyone who picks up your phone.</string>
```

Without it, iOS refuses the Face ID evaluation at runtime and App Review rejects the build.
Touch ID and the passcode fallback do not need a usage string, so the failure looks like "works
on my older test device, fails on the reviewer's".

## 4. Reach the settings screen — recommended

The lock screen itself is reachable automatically. The settings screen is registered at
`SecurityRoutes.AppLockSettings` (`"settings-app-lock"`) but nothing links to it yet, so today it
can only be reached by an explicit `GoToAsync`. Two follow-ups, both outside this feature:

- **Settings entry point.** Add a row to `SettingsPage` that navigates to
  `Forge.App.Features.Security.SecurityRoutes.AppLockSettings`.
- **Route constant.** Fold the constant into `ForgeRoutes` next to `AppLock` and delete
  `SecurityRoutes`. It lives in the feature folder only because `ForgeRoutes` is a shared file
  that parallel branches are asked not to touch.

## 5. Tell the lock when a workout is running — required for the workout allowance

Without this the lock still behaves correctly and is safe; it simply cannot know a session is in
progress, so the 15-minute workout allowance never applies and `IsActivityInProgress` stays
`false` forever. A user who put the phone down between sets will then meet a lock screen on the
way back — the single worst failure mode this feature has, and the reason the app lock settings
screen promises an allowance that will not happen until this is wired.

In the Workout feature, take a dependency on `IAppLockActivityContext` and hold a scope for the
lifetime of the session:

```csharp
// Injected: IAppLockActivityContext activityContext
private IDisposable? activityScope;

private void OnWorkoutStarted() => activityScope = activityContext.BeginActivity();

private void OnWorkoutFinished()
{
    activityScope?.Dispose();
    activityScope = null;
}
```

Scopes nest and are counted, so a rest timer that opens its own scope inside a workout does not
end the workout when it closes. Disposing twice is safe.

## 6. Nothing to add under `Platforms/` for the lock triggers or app-switcher privacy

Worth stating explicitly, because it is the part that usually needs platform files.

`AppLockLifecycleEvents` registers a `Microsoft.Maui.LifecycleEvents.LifecycleEventRegistration`
in the container. MAUI resolves every registration of that type when it first builds its
lifecycle service:

```csharp
// Microsoft.Maui, Hosting/LifecycleEvents/AppHostBuilderExtensions.cs
builder.Services.TryAddSingleton<ILifecycleEventService>(
    sp => new LifecycleEventService(sp.GetServices<LifecycleEventRegistration>()));
```

so a feature can subscribe to `OnStop`, `OnResume`, `DidEnterBackground`, `OnActivated` and
`OnResignActivation` from its own folder, without `ConfigureLifecycleEvents` in `MauiProgram.cs`
and without touching `MainActivity` or `AppDelegate`.

The same route carries the app-switcher privacy work:

- Android `FLAG_SECURE` is applied to `Platform.CurrentActivity.Window` by
  `PlatformPrivacyScreenController`, and reapplied on every foreground so an activity recreated
  under memory pressure comes back covered.
- The iOS blur cover is added to the key window on `OnResignActivation` and removed on
  `OnActivated` by the same type.

If you would rather have this in `Platforms/` anyway — for example because you want the flag set
before the first frame rather than on first resume — the equivalent platform code is:

```csharp
// Platforms/Android/MainActivity.cs
protected override void OnCreate(Bundle? savedInstanceState)
{
    base.OnCreate(savedInstanceState);

    // Only when the user has app lock on; unconditionally setting this also blocks
    // screenshots for users who never asked for a lock.
    if (IPlatformApplication.Current?.Services.GetService<IAppLockSettings>() is { IsEnabled: true, HideInAppSwitcher: true })
    {
        Window?.AddFlags(Android.Views.WindowManagerFlags.Secure);
    }
}
```

```csharp
// Platforms/iOS/AppDelegate.cs
private UIVisualEffectView? privacyCover;

[Export("applicationWillResignActive:")]
public void OnResignActivation(UIApplication application)
{
    if (IPlatformApplication.Current?.Services.GetService<IAppLockSettings>() is not { IsEnabled: true, HideInAppSwitcher: true })
    {
        return;
    }

    var window = application.Windows.FirstOrDefault(w => w.IsKeyWindow);
    if (window is null || privacyCover is not null)
    {
        return;
    }

    privacyCover = new UIVisualEffectView(UIBlurEffect.FromStyle(UIBlurEffectStyle.SystemMaterial))
    {
        Frame = window.Bounds,
        AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight,
    };

    window.AddSubview(privacyCover);
    window.BringSubviewToFront(privacyCover);
}

[Export("applicationDidBecomeActive:")]
public void OnActivated(UIApplication application)
{
    privacyCover?.RemoveFromSuperview();
    privacyCover?.Dispose();
    privacyCover = null;
}
```

If you take that route, delete `PlatformPrivacyScreenController` and register
`UnavailablePrivacyScreenController` instead, so the settings screen does not claim a capability
it is no longer providing. Keeping both would leave two owners of the same window flag.

## 7. What could not be verified here

No device with an enrolled biometric was available, so the following are written and compiled
but not exercised on hardware:

- the Android `BiometricPrompt` callback path, including `Lockout` and `LockoutPermanent`;
- the iOS `LAContext` evaluation and its error mapping;
- that the iOS blur lands before the system snapshot on a real device;
- that `FLAG_SECURE` blanks the recents thumbnail on a current Android build;
- that the lock screen renders correctly, which the repository's own guidance calls out as
  something to check on a device rather than trust from a clean build.

The decision logic behind all of it is pure, platform-free and covered by
`tests/Forge.Core.Tests/Security`.
