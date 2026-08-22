# Accessibility sweep - on-device evidence

Everything here was measured with `tools/smoke`, on an emulator, against a build produced from the
tree at the time. Numbers from a build are not included, because no build has ever detected any of
these problems.

## Baseline

`pwsh tools/smoke/Invoke-ForgeSmoke.ps1 -Serial emulator-5554 -Install -OnboardingMode Skip`
Phone, 1080x2400. 13 routes visited, 87 actions, 1817s. Result **FAIL**.

| Check | Count |
| --- | ---: |
| Blank containers | 2 |
| Unlabelled interactive elements | 3 |
| Actionable but not exposed to accessibility | 10 |
| Process deaths | 0 |
| Fatal exceptions | 0 |

```
FAIL [BlankContainer]        food-log   975x420 at [53,1310][1028,1730], 2 descendants, no text, no content-desc, no image
FAIL [BlankContainer]        settings   975x147 at [53,490][1028,637], 1 descendant, same
FAIL [UnlabelledInteractive] settings   EditText at [85,532][996,595]
FAIL [UnlabelledInteractive] goal-wizard EditText at [127,1078][849,1141]
FAIL [UnlabelledInteractive] goal-wizard ImageButton at [891,1078][954,1141]
WARN [ActionableNotExposed]  x10        'Log food' 'Log' 'Hydration' 'Add' 'Settings' 'Finish setup'
                                        'View workout history' 'Open the plate calculator'
                                        'Browse exercises' 'Start a workout'
```

## Diagnosis

### The food log container was empty, not dead

The two candidate causes need opposite fixes, so this was settled before anything was changed.

The generated XAML source shows all four `ItemsSource` bindings on the page being applied:

```
dxCollectionView.SetBinding(DXCollectionView.ItemsSourceProperty, bindingBase4);
dxCollectionView1.SetBinding(DXCollectionView.ItemsSourceProperty, bindingBase9);
dxCollectionView2.SetBinding(DXCollectionView.ItemsSourceProperty, bindingBase15);
dxCollectionView3.SetBinding(DXCollectionView.ItemsSourceProperty, bindingBase20);
```

The container sat immediately below the "Recent" heading, and its entire subtree was:

```
[53,1210][1028,1268]  TextView               text='Recent'
[53,1310][1028,1730]  ViewGroup              text='' desc=''
[53,1310][1028,1730]  ScrollView             text='' desc=''
[53,1310][1028,1730]  HorizontalScrollView   text='' desc=''
```

420px at density 2.625 is exactly the `HeightRequest="160"` on that `DXCollectionView`, and 975px is
exactly the content width. The page's own empty state ("No food logged today") was on screen at the
same time, so there genuinely was no data. **A live binding over an empty collection in a list that
keeps its reserved height.**

An unrelated defect was found while confirming this: the `ItemSpanCount` attribute is stranded as
element text on nine other pages and silently discarded. `ItemSpanCount` appears zero times in the
generated code. It is a tablet layout bug, not an accessibility one, and is listed in
[`README.md`](README.md#known-gaps) rather than fixed here.

### The goal wizard controls were the inside of a ComboBoxEdit

```
[95,955][986,1005]    TextView     text='Primary goal'
[95,1036][986,1183]   ViewGroup    desc='Primary goal'            <- the description landed here
[127,1078][849,1141]  EditText     clickable=true  text='' desc=''
[891,1078][954,1141]  ImageButton  clickable=true  text='' desc=''
```

`SemanticProperties.Description` reaches the outer container only. Not fixable from XAML.

### All ten unexposed controls were the DevExpress button limitation

Every one already had a description - that is how the harness knew their names. The problem was the
node, not the markup:

```
desc='Log'  class=android.view.ViewGroup  clickable=false  focusable=true
```

**Zero of the ten were missing `SemanticProperties`.** All ten were the role-mapping limitation.

## After

Verified on emulator-5556, tablet, 2560x1600, using the harness's own
`Find-ForgeBlankContainers` and `Find-ForgeAccessibilityIssues` against live hierarchy dumps.

| Screen | Blank containers | Unlabelled interactive | Labelled but unreachable |
| --- | ---: | ---: | ---: |
| Today and the tab bar | 0 | 0 | 0 |
| Goal wizard, step 1 | 0 | 0 | 0 |
| Food log | 0 | 0 | 0 |
| Settings | 0 | 0 | 0 |
| Train | 0 | 0 | 0 |
| Profile | 0 | 0 | 0 |

The food log now hides a section whose list is empty, so the "Recent" heading and its wordless box
are both gone:

```
Food log | Search and log food | Search foods | ＋ | No food logged today
Search the local catalogue or use repeat shortcuts once you have history...
Logged today | Copy yesterday
```

The goal wizard's editor parts are named, and its buttons carry the button role:

```
Back to welcome, button
Step 1 of 6, What are you working towards?
Display name / What should Forge call you?
Primary goal, edit box
Show options for Primary goal, button
Continue, button
```

The settings search field, previously both findings at once:

```
EditText text='Search settings' desc='Search settings'
```

Controls that were `ActionableNotExposed` are now `android.widget.Button` with `clickable=true`:
`Start a workout`, `Log hydration`, `Refresh`, `Browse exercises`, `View workout history`,
`Open the plate calculator`, `Settings`, `Finish setup`, `Log training`, `Continue`.

## TalkBack

TalkBack was enabled on emulator-5556 and driven with swipe gestures:

```
enabled_accessibility_services = com.google.android.marvin.talkback/...TalkBackService
accessibility_enabled = 1
touchExplorationEnabled = true
Enabled services:{{com.google.android.marvin.talkback/...TalkBackService}}
```

It bound, took touch exploration, and requested audio focus once per swipe, so it was traversing.
The app stayed up throughout and no `Forge accessibility` failure was logged, which matters because
an accessibility service exercises code paths ordinary use never touches. A dump taken with TalkBack
still running reported 0 blank containers and 0 unlabelled controls.

**The spoken audio itself could not be captured.** The emulator has no TTS voice data, so TalkBack
logged `TTS is not ready` instead of the utterance. The announcements quoted above are read from the
accessibility nodes, which are TalkBack's input rather than its output. On a device with a voice
installed they should be confirmed by ear.

## Caveats

- Both emulators are shared, and other processes force-stopped and reinstalled the package during
  these runs (`Force stopping com.nikomix.forge ... from pid NNNN`, and a `lastUpdateTime` that moved
  mid-run). One earlier run also hit a wiped FastDev override directory, which surfaced as
  `TypeInitializationException` on `SqliteConnection` and looked exactly like an app defect. The
  per-screen results above were therefore taken from targeted runs against a package whose install
  timestamp was checked first.
- Only screens reachable by tapping were checked. The pages listed as unvisited in the harness report
  are not verified.
- The blank-content check reads the accessibility tree, not pixels. Content drawn with no accessible
  representation is indistinguishable from an empty card, which is exactly why charts are marked out
  of the tree and summarised in text instead.
