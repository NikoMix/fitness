# App lock: threat model and honest limits

Forge stores training history, bodyweight, body measurements and food logs, and it stores all
of it on one device with no server copy. Protecting it on that device is the only protection
that exists. This document says exactly what the app lock does, what it does not do, and why it
behaves the way it does — including the parts that are weaker than a user might assume.

## What already existed before this feature

The Forge database is encrypted at rest with SQLCipher. The key lives in the Android Keystore
or the iOS Keychain and is fetched once during startup by `IDatabaseKeyProvider`. That is a real
protection: a copy of `forge.db` lifted off the device is unreadable without the key, and the
key does not leave the platform's secure storage.

The app lock does **not** change this. It does not derive a key, hold a key, re-key anything, or
add a second encryption layer.

## What the app lock actually is

A presentation gate. When it is on, Forge shows a lock screen instead of your data until the
operating system confirms who you are — fingerprint, face, or device PIN, pattern or passcode.

That is worth having. The realistic risk to a fitness app on a phone is not a forensic
laboratory; it is the phone being handed to someone to look at a photo, left unlocked on a desk,
picked up by a partner, a flatmate or a colleague. A lock screen stops all of that.

It is also worth being precise about, because the gap between "asks for your fingerprint" and
"is secure" is where users make bad decisions.

## Threat model

### Defends against

| Threat | Why it works |
| --- | --- |
| Someone picking up your unlocked phone and opening Forge | They cannot pass the platform prompt |
| Someone reading your last screen from the app switcher | Content is hidden from the switcher while the lock is on |
| A shoulder-surfer seeing your weight or measurements after you switch apps | Same |
| An unattended phone in a gym, a changing room or an office | The lock re-arms after the grace period |

### Does not defend against

| Threat | Why not |
| --- | --- |
| Anyone who knows your device passcode | The lock's fallback *is* the device passcode. This is deliberate: without it, a broken sensor would lock you out of your own data permanently |
| Anyone whose biometric the device accepts | If a face or fingerprint is enrolled on the device, Forge cannot tell it apart from yours |
| A determined attacker with your unlocked device and time | Forge's process holds the database key while it runs. Anything with debugger, root or jailbreak access is past this |
| Device backups and platform sync | Whatever your OS backs up is outside Forge's control |
| Malware or a compromised OS | Out of scope for any app-level control |
| Someone who has already obtained the database file **and** the keystore secret | The encryption is what stands there, not this lock |

**Plainly: this is a curtain, not a safe.** It raises the effort of casual access from zero to
"needs your face or your passcode". It does not make Forge resistant to someone who has both
your phone and your passcode, and the settings screen says so in those words.

## Lock trigger policy, and why

Two triggers, both on the way *into* the foreground:

1. **On launch.** A cold start always locks when the feature is on.
2. **On return from the background,** if Forge was away for longer than the grace period.

There is deliberately **no timer**. The lock is only ever evaluated when the app comes to the
foreground, so a lock screen can never appear over a screen you are currently reading. That is a
structural property of `AppLockPolicy`: its only two triggers are `Launched` and `Foregrounded`.

### Grace period

Default **1 minute**. Options: immediately, 15 seconds, 1 minute, 5 minutes, 15 minutes.

Immediately is offered but is not the default. An app that demands a fingerprint every time you
glance at a notification is an app whose lock gets switched off, and a lock that is off protects
nobody.

### The workout case

This is the case that decides whether the feature is usable, so it was designed first.

During a workout, the phone is put down between sets, the screen turns off, a track gets
changed, a message gets answered, a photo of the whiteboard gets taken. Every one of those
backgrounds the app. If the lock fires on the way back:

- fingerprints fail on sweaty or chalked hands;
- face unlock fails at the angle you hold a phone from under a bar;
- the cost lands mid-set, with a rest timer running, when the user has the least attention
  available.

A lock that does that gets switched off within a week — again protecting nobody.

**The choice: during a workout, the grace period is stretched to a floor of 15 minutes.** It is
only ever lengthened, never shortened, so a user who picked 30 minutes keeps 30 minutes.

Fifteen minutes sits above the longest thing a lifter plausibly does with their phone mid-session
and below the point where a session has clearly been abandoned. A phone face down for a quarter
of an hour is a phone whose owner has left, and locking is the right answer again.

Two things keep this honest rather than a silent override:

- it is a **visible setting** ("Give me longer while I am training", on by default), and the
  settings screen states the 15-minute floor in the same words the code implements;
- it is driven by `IAppLockActivityContext`, an explicit signal the Workout feature raises —
  not a guess.

### Events chosen on each platform

Which lifecycle event is used is part of the security design, not an implementation detail.

- **Android** uses `OnStop` / `OnResume`, not `OnPause`. The system biometric dialog pauses the
  hosting activity without stopping it. Treating a pause as backgrounding would start the grace
  timer every time Forge asked the user to unlock — and a user who chose "immediately" would be
  re-locked the instant they succeeded, with no way out of the loop.
- **iOS** uses `DidEnterBackground` / `OnActivated` for the lock, and the earlier
  `OnResignActivation` for the app-switcher cover. Over-eager blurring costs nothing; blurring
  after the snapshot has been taken protects nothing.

A second guard covers the same failure from the other side: a foreground event with no recorded
backgrounding never locks. See `AppLockPolicy.Decide`.

And a third closes the mirror-image hole: a foreground event can never *clear* a lock that is
already up. Only a successful authentication does that. Without it, the resume that follows
tapping "Cancel" on the fingerprint dialog would dismiss the lock screen and hand over the data.
See `AppLockStateMachine.EnterForeground`.

## Never locking the user out

Forge holds the only copy of this data. A lock that can strand its owner is worse than no lock.
Four independent guarantees:

1. **It only turns on after a successful check on this device.** Enabling requires passing the
   prompt, which proves the mechanism the user is about to depend on actually works for them.
2. **A device that cannot authenticate disables the lock.** If there is no device passcode set,
   or the platform cannot present the prompt, `AppLockPolicy` returns
   `DisableBecauseUnavailable`: Forge unlocks *and persists the setting as off*, so it does not
   come back at the next launch. This covers the user who removes their screen lock after
   enabling Forge's.
3. **Failure never destroys anything.** There is no attempt counter, no escalating delay of
   Forge's own and no wipe-on-failure. The platform already rate-limits its own sensor; anything
   added on top could only punish the person who owns the phone. `AppLockStateMachine` has no
   transition from a failed attempt to anything except "still locked", and
   `Forge.Core.Abstractions.Security` has no reference to the database, the data session or the
   erasure service — asserted by a test, so it cannot be added later by accident.
4. **Biometric lockout falls through to the passcode.** Both platforms are asked for the
   combined policy (`DeviceCredential` on Android, `LAPolicy.DeviceOwnerAuthentication` on iOS),
   so a locked-out sensor is handled by the platform rather than by Forge guessing.

A transient probe failure is treated differently from a permanent one: `TemporarilyUnavailable`
keeps the lock on and retries. Silently disabling a security control because a sensor was busy
for a moment is a downgrade the user never agreed to.

## App-switcher privacy

Both platforms photograph a running app to draw the task switcher, and on both that image
outlives the app — Android keeps it in recents, iOS writes it into the app's own container. A
lock over a running app is theatre if the last screen of body measurements is still sitting in
the switcher behind it, so the two are switched on together.

- **Android:** `WindowManagerFlags.Secure` (`FLAG_SECURE`) on the activity window. The system
  then draws a blank placeholder in recents. It **also blocks screenshots and screen recording**
  of Forge — that is a side effect of the only mechanism Android offers, and the settings screen
  says so rather than letting the user discover it when a screenshot silently fails.
- **iOS:** there is no equivalent flag. A `UIVisualEffectView` blur is added over the key window
  on `OnResignActivation` and removed on `OnActivated`.

Hiding is applied only while the lock is enabled. Blanking recents for someone who never asked
for a lock would look like a bug, and on Android would silently break their screenshots.

The cover is held until the lock screen is actually on screen, not merely until the lock
*decision* is made, so returning to a locked Forge on iOS does not flash the previous screen
while Shell navigates. If presenting the lock screen fails, the cover comes off anyway — a user
staring at a permanent blur with no way out is worse than the leak it would prevent.

### Known limitation on Android

Android has no cover view here, only `FLAG_SECURE`. That covers the persistent artefact — the
recents thumbnail — but the live window is still the previous screen for the moment between the
activity resuming and Shell navigating to the lock page. In practice the system draws the
(blanked) snapshot or the starting window during that window, so there is usually nothing to
see, but it is not a guarantee the way the iOS cover is. Adding an Android decor-view overlay
would close it and is the obvious follow-up; it was not done here because it manipulates MAUI's
view tree and could not be verified on a device.

## Platform requirements

- **Android 10 (API 29) or newer.** Below that the framework `BiometricPrompt` cannot offer a
  device-credential fallback, so a user with no enrolled fingerprint would have no way through —
  exactly the lockout this feature must never create. Older devices report `Unavailable` and are
  never offered the lock. Forge's own floor is Android 8.0, so those users keep the app and lose
  only this optional feature.
- **iOS 15 or newer**, matching Forge's floor. `LAPolicy.DeviceOwnerAuthentication` is used so
  iOS itself handles the passcode fallback.

## Manifest and Info.plist requirements

These live under `Platforms/`, which this feature does not own. Both are required for the
feature to work on a device:

- `Platforms/Android/AndroidManifest.xml` needs
  `<uses-permission android:name="android.permission.USE_BIOMETRIC" />`.
- `Platforms/iOS/Info.plist` needs `NSFaceIDUsageDescription`. Without it iOS refuses the
  evaluation at runtime and App Review rejects the build.

## What has not been verified on a device

Written and compiled for both platforms, but not exercised on hardware with an enrolled
biometric:

- the Android `BiometricPrompt` callback path, including the lockout and lockout-permanent
  branches;
- the iOS `LAContext` evaluation and its error mapping;
- that the iOS blur cover lands before the system snapshot on a real device;
- that `FLAG_SECURE` blanks the recents thumbnail as expected on a current Android build.

The decision logic behind all of them — grace periods, the workout allowance, the state
transitions and the lockout guarantees — is pure, has no platform dependency and is covered by
`tests/Forge.Core.Tests/Security`.
