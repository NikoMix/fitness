# Verification: E02, E03, E23, E24, E27 - design system, motion, accessibility, localisation, adaptive layout

Read-only reconciliation of the Forge backlog against the code on `nikomix/feature/verify-e02-e27-design-accessibility`
(branched from `main`). Every story, feature and epic in the range has a verdict. No application code was changed.

Method: read each story's `requirements` and `acceptanceCriteria` in `backlog/epics/*.yml`, then find the code and
decide whether the criteria are met. `implementation.notes` was treated as a hint only - it is frequently wrong about
file paths, and several features are implemented correctly under different names. Where a file exists but nothing
reaches it, the verdict follows the behaviour, not the file.

Nothing was built or executed except three cheap read-only checks: `tools/ci/Test-XamlAttributes.ps1`, a repository
scan for hex literals and inline font sizes in XAML, and a count of `AutomationId`, `SemanticProperties` and
`{loc:Translate}` occurrences.

## Summary

| Epic | Title | Stories | DONE | PARTIAL | NOT-DONE | DEFERRED | UNCLEAR | Epic verdict |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| **E02** | Design System and Theming | 18 | 0 | 11 | 7 | 0 | 0 | **PARTIAL** |
| **E03** | Motion, Animation and Micro-interactions | 18 | 0 | 5 | 13 | 0 | 0 | **PARTIAL** |
| **E23** | Accessibility and Inclusive Design | 15 | 0 | 11 | 4 | 0 | 0 | **PARTIAL** |
| **E24** | Localisation and Internationalisation | 15 | 0 | 6 | 9 | 0 | 0 | **PARTIAL** |
| **E27** | Adaptive Layout and Responsive Experience | 18 | 0 | 6 | 12 | 0 | 0 | **PARTIAL** |
| | **Total** | **84** | **0** | **39** | **45** | **0** | **0** | |

Features: 20 PARTIAL, 8 NOT-DONE, 0 DONE. Epics: 5 PARTIAL, 0 DONE.
The 8 NOT-DONE features are `F02.05`, `F03.02`, `F03.03`, `F03.05`, `F03.06`, `F24.03`, `F27.03`, `F27.05`.

### Why zero DONE

This is not padding, and it is worth understanding before reading further. Every story in these five epics carries,
in addition to its own criteria, a set of **epic-wide boilerplate acceptance criteria** that resolve against shared
harnesses which were never built:

- **E02** - every story's AC3 and AC4 are measured "in the component gallery" by "the automated contrast verifier".
  Neither exists.
- **E24** - every story's AC3 fails through "the locale validation report". There is no such report.
- **E27** - every story's boilerplate criteria require a `VisualStateManager` state selection and
  `dx:SafeKeyboardAreaView` keeping focused fields visible. Neither construct appears anywhere in `src/`.
- **E03** and **E23** are lighter, but still route timing and traversal criteria through a motion gallery and a
  screen-reader test matrix that do not exist.

So a handful of stories are **substantively complete and fail only on the shared harness**. If you want the
shortest list of things that are genuinely built and working, it is these:

| Story | What is actually working |
| --- | --- |
| `S02.01.01` | Seed, bootstrap order and semantic-role consumption are all correct. |
| `S02.01.02` | Zero hex literals in page XAML; only the lint gate is missing. |
| `S02.03.03` | 48dp/64dp/76dp targets applied through shared styles everywhere. |
| `S23.01.01` | Android screen-reader labelling, verified on a device. |
| `S23.01.03` | Semantic announcements, correctly de-duplicated rather than per-tick. |
| `S23.02.03` | 48dp minimum enforced through styles, including a real device-found DevExpress fix. |
| `S24.02.02` | Culture-aware formatter, correct and tested - just not called. |
| `S24.05.03` | The `useLocalization: false` decision is documented exactly as required. |
| `S27.01.01` | Breakpoints, size classes and 15 tests against real device widths. |
| `S27.01.02` | Phone portrait is protected by construction, not by convention. |
| `S27.02.01` | Two-pane container, correct and tokenised (named `AdaptiveHost`, not `AdaptivePaneView`). |
| `S27.06.02` | `docs/design/tablet-layout.md` is genuinely good documentation. |

### The four findings that matter most

**1. `PressFeedbackBehavior` and `AnimatedNumberBehavior` are attached to nothing.** Both are fully implemented,
both are documented in `docs/design/motion.md:63-83`, and a grep across all of `src/` returns their own definitions
and the documentation and **nothing else**. No button in Forge has press feedback and no metric anywhere counts up.
A MAUI `Style` cannot set `Behaviors`, so the styles in `ForgeStyles.xaml` and `ForgeMotion.xaml` cannot rescue
this; each usage has to be written into the page. `ForgeMotion.xaml:17-19` defines a `MotionPressable` style that
only sets `MinimumHeightRequest`, which reads like the intended hook and is not one.
(`S03.03.01`, `S03.04.02`)

**2. Haptic feedback never fires, and Settings promises that it does.** The only call to a platform haptic API in
the repository is `ForgeAnimations.cs:217`, inside `internal static void TryHapticClick`. That helper is called from
exactly two places - `PressFeedbackBehavior.cs:101` and `:122` - and that behaviour is attached to nothing (finding
1). Meanwhile `UnitsSettingsPage.xaml:66-69` ships a **Haptic feedback** toggle whose hint reads *"Vibrates when a
set or a rest period ends"*, backed by a real persisted preference and a real `SettingsMotionPreferences` gate. A
user can turn on a feature that has no implementation. This is the closest thing in my range to a shipped lie, and
it also breaks `S23.03.03`, where haptics are the accessible alternative to an audio cue.
(`S03.03.02`, `S23.03.03`)

**3. Deleting a custom exercise has no confirmation, and Delete sits 2dp from Edit.** `ExerciseLibraryPage.xaml:301`
stacks Favourite, View, Edit and Delete in one `VerticalStackLayout` with `SpaceXxs` spacing - **2dp**, against a
required 24dp - and `ExerciseLibraryViewModel.cs:397-406` calls `DeleteCustomAsync` immediately with no
confirmation surface. The rest of the app is careful here (`ProfileSwitcherViewModel.cs:219`,
`ImportDataViewModel.cs:65`, `DeleteMyDataPageViewModel.cs:42-46` all confirm), which makes this an inconsistency
rather than a policy. It fails `S02.05.02` AC2, `S23.02.03` requirement 2 and `S27.05.02` AC1 simultaneously, and it
destroys user-authored data on a single mis-tap on a 2dp-separated target.
(`S02.05.02`, `S23.02.03`, `S27.05.02`)

**4. Kilojoules cannot be selected, and the conversion code that handles them is unreachable.**
`UnitFormatter.cs:54-57` implements the kcal-to-kJ conversion correctly, with the right 4.184 factor and the right
suffix. It can never run: `UnitPreferences.cs:267-277` hard-codes the `EnergyUnit` getter to `Kilocalories` and its
setter **throws `NotSupportedException`** for anything else. `S24.02.01` AC3 is impossible to satisfy. The same file
contains a related trap: `MassUnit`, `LengthUnit` and `VolumeUnit` (lines 246-264) look like four independent
per-category preferences and are not - each getter derives from the single `UnitSystem` and each setter flips it, so
choosing pounds silently converts height to feet-and-inches and volume to fluid ounces.
(`S24.02.01`, `S24.02.03`)

### Where the backlog itself is wrong

- **`AdaptivePaneView` vs `AdaptiveHost`.** `S27.02.01` specifies `AdaptivePaneView` and
  `Resources/Styles/AdaptiveLayout.xaml`. The app implements the same capability as `Adaptive/AdaptiveHost.cs` and
  `Resources/Styles/ForgeAdaptive.xaml`, and the decision is reasoned out in `docs/design/tablet-layout.md:11-27`.
  The backlog is out of date, not the code. The same document explicitly **rejects** the `VisualStateManager` +
  `AdaptiveTrigger` approach that E27's boilerplate acceptance criteria mandate, with a defensible argument, so
  those criteria can never pass as written and should be rewritten rather than chased.
- **`IStringLocalizer` vs `ILocalizationService`.** `S24.01.01` names `IStringLocalizer`. Forge deliberately uses
  its own `ILocalizationService` so the fallback chain is explicit and testable rather than buried in
  `ResourceManager`; the reasoning is in `docs/localization/README.md:56-66`. Treat the deviation as correct.
- **`RadialGauge` in `S03.04.01`.** The contributor instructions require `dx:RadialProgressBar` and forbid
  `RadialGauge`. `ActivityRing` uses the right one; the story text is wrong.
- **`S02.05.01` assumes SVG or font-glyph icon assets.** Forge currently uses inline Unicode glyphs and no icon
  assets at all. That may be a deliberate simplification worth ratifying, but nothing records it as a decision, and
  the consequence - a tab bar with no icons whose selected state is colour-only - is a real accessibility problem.

### Stories I would like a second opinion on

- **`S03.05.03`** (govern animation assets). Forge bundles zero animation assets by an explicit decision
  (`docs/design/motion.md:109`), so the 80KB and 500KB budgets hold vacuously. I called it NOT-DONE because no
  metadata file or budget check exists, but a case can be made for DEFERRED. The epic's `nonGoals` rule out Lottie
  and say nothing about governance, so I did not use DEFERRED.
- **`S27.01.01`** requirement 2, "effective width after safe-area insets". `AdaptiveHost` measures its own width,
  which on MAUI is normally already inside the page's insets. I called it a gap because nothing in `src/` reasons
  about insets at all. Someone who has run this on a notched device in landscape would settle it in a minute.
- **`S02.02.03` / `S23.02.02`** hinge on my reading that the tab bar's selected state is colour-only. `AppShell.xaml`
  sets no `Icon` and configures only `TabBarTitleColor` against `TabBarUnselectedColor`; whether the platform adds
  its own indicator on Android and iOS needs a device to confirm.

### Things I found outside my range while verifying

- **`src/Forge.App/Features/Settings/ViewModels/DeleteMyDataPageViewModel.cs:59`** interpolates `ex.Message` into a
  `DisplayAlertAsync` shown to the user. That is the exact pattern the contributor instructions forbid, and it is on
  the erase-all-my-data path. Belongs to E25/E26, but it should be fixed.
- **Two documents are stale in a way that would mislead a reader.** `docs/accessibility/README.md:137-144` still
  says `ItemSpanCount` is dead on nine pages - it has since been fixed and is now guarded. `docs/design/motion.md:113`
  says `ForgeMotion.xaml` is not merged in `App.xaml` - it is, at line 24. `docs/localization/README.md:94-95` says
  `.AddLocalizationFeature()` is not wired - it is, at `FeatureRegistration.cs:60`. All three understate the app.

---

## E02 - Design System and Theming

**Epic verdict: PARTIAL** (0 DONE, 11 PARTIAL, 7 NOT-DONE)

The **theme layer is real and correct**; the **verification layer does not exist**. Colour comes from one seed through DevExpress MD3 roles, the bootstrap order is right, and a scan of every XAML file outside `Resources/Styles` finds zero hex literals and zero inline numeric font sizes. What is missing is everything F02.06 was supposed to build: there is no in-app component gallery, no contrast verifier and no snapshot baseline, and eleven of the eighteen stories put their acceptance criteria inside that gallery. Iconography (F02.05) is the one substantive hole - `Resources/Images` does not exist and the five primary tabs have no icons at all.

### F02.01 - Establish brand colour and semantic theme roles

**Feature verdict: PARTIAL** - `S02.01.01` PARTIAL, `S02.01.02` PARTIAL, `S02.01.03` PARTIAL.

#### `S02.01.01` Define the Forge brand seed and ThemeManager bootstrap - **PARTIAL**

*Evidence.* src/Forge.App/Branding/ForgeBrand.cs:21 holds the single seed; src/Forge.App/MauiProgram.cs:35-36 sets ThemeManager.UseAndroidSystemColor=false and ThemeManager.Theme before MauiApp.CreateBuilder() at line 40, with the reason recorded at lines 32-34; ForgeStyles.xaml:21,38,48,91,169,181 consume dx:ThemeColor roles and a scan of every XAML file outside Resources/Styles finds zero hex literals.

*Gap.* AC2, AC3 and AC4 all resolve in 'the component gallery', which does not exist (no Features/DesignGallery, no gallery route in Navigation/ForgeRoutes.cs). There is no automated contrast verifier anywhere in tests/ and no touch-target measurement, so the 4.5:1 / 3:1 / 7:1 and 48dp / 44pt claims are asserted by tokens only, never measured.

#### `S02.01.02` Replace literal colours with semantic ThemeColor roles - **PARTIAL**

*Evidence.* ForgeStyles.xaml defines semantic text, surface, outline, error and metric styles entirely from dx:ThemeColor roles (lines 21,27,33,38,48,53,65,71,91,97,105,117,169,181); ForgeStyles.xaml:41-49 documents choosing OnSurfaceVariant over reduced opacity specifically to stay above 4.5:1; a repository scan confirms zero '#rrggbb' literals in any XAML outside Resources/Styles.

*Gap.* AC1 requires a lint command that fails and reports file and line on a literal hex. No such check exists: .github/workflows/ci.yml runs six guards (Test-RouteRegistrations, Test-DataAccessPatterns, Test-LegalContentSync, Test-LocalizationManifests, Test-RouteReachability, Test-XamlAttributes) and none is a colour lint, so today's clean state is convention, not enforcement. AC2's greyscale gallery check has no gallery. Colors.xaml and Styles.xaml, the stock MAUI templates full of literal hex, are still merged at App.xaml:19-20.

#### `S02.01.03` Add dark mode with an OLED-black workout option - **PARTIAL**

*Evidence.* Forge follows the system scheme and can override it: Features/Settings/Services/MauiThemePreferenceApplier.cs:39-44 maps the preference onto Application.Current.UserAppTheme and re-applies on change (lines 22-28); the picker is on UnitsSettingsPage.xaml:34-38.

*Gap.* There is no OLED black option at all. ThemeModePreference (Forge.Core/Abstractions/Preferences/UnitPreferences.cs:16-27) has exactly System, Light and Dark; a case-insensitive grep for 'OLED' across src/ and docs/ returns nothing. No workout override ResourceDictionary exists, so AC2 (black workout surfaces at >=7:1) is unimplemented, as is the 7:1 requirement it depends on.

### F02.02 - Verify accessibility and gym legibility

**Feature verdict: PARTIAL** - `S02.02.01` NOT-DONE, `S02.02.02` NOT-DONE, `S02.02.03` PARTIAL.

#### `S02.02.01` Automate WCAG contrast verification for theme roles - **NOT-DONE**

*Evidence.* tests/ contains exactly three projects - Forge.Core.Tests, Forge.Domain.Tests, Forge.Infrastructure.Tests - and a grep for 'contrast', 'wcag' or 'luminance' across all of tests/ matches only unrelated files (ReminderSchedulingPolicyTests, BackupServiceTests, DatabaseSchemaParityTests). tests/Forge.App.Design.Tests does not exist.

*Gap.* Nothing measures contrast. No relative-luminance implementation, no token-pair matrix, no allowlist with expiry, no build gate. Both acceptance criteria are unimplemented, and the 4.5:1 / 3:1 / 7:1 claims repeated across all 18 E02 stories rest on this story.

#### `S02.02.02` Verify sunlight legibility for workout-critical text - **NOT-DONE**

*Evidence.* ForgeTokens.xaml:121-128 does give workout metrics a deliberately large scale (FontMetric 40, FontMetricLarge 56, tablet 52/76), comfortably over the 20dp minimum, and ForgeStyles.xaml:61-66 applies it as MetricText.

*Gap.* There is no CriticalText role in the token or colour matrices, no 7:1 measurement (see S02.02.01), and no sunlight QA checklist anywhere in docs/. AC1 needs a contrast calculation that does not exist and AC2 needs a checklist that does not exist.

#### `S02.02.03` Ensure state is never communicated by colour alone - **PARTIAL**

*Evidence.* Advisories carry headline, message, signpost and reassurance text on a distinct surface rather than a colour change (Controls/AdvisoryPanel.xaml; ForgeStyles.xaml:104-106 AdvisoryCard); EmptyState pairs a glyph with headline and message (Controls/EmptyState.xaml:11-29); docs/design/accessibility-audit.md:17 records a manual pass over Controls/ finding no colour-only state.

*Gap.* Requirement 3 is violated on the app's most-used control: AppShell.xaml:32-43 gives the five tab items no Icon at all, and the only configured difference between selected and unselected is colour (AppShell.xaml:23-25 sets TabBarTitleColor Primary against TabBarUnselectedColor OnSurfaceVariant). AC2's state-audit test does not exist, there is no StateCue metadata file, and there are no greyscale snapshots.

### F02.03 - Build typography, spacing and touch-density tokens

**Feature verdict: PARTIAL** - `S02.03.01` PARTIAL, `S02.03.02` PARTIAL, `S02.03.03` PARTIAL.

#### `S02.03.01` Define the Forge type scale and numeric styles - **PARTIAL**

*Evidence.* ForgeTokens.xaml:90-128 covers display, headline, title, body, label, caption and two metric roles plus a sub-linear tablet scale; ForgeStyles.xaml:18-86 maps each to a named style through OnIdiom; smallest body style is FontBodyM 14 (ForgeTokens.xaml:96), meeting the 14pt floor; a scan finds no inline numeric FontSize in XAML outside Resources/Styles.

*Gap.* AC2 (tabular figures, digit column shifting no more than 1px) is not implemented: MetricText (ForgeStyles.xaml:61-66) achieves stability by centring alone, no font feature is requested, and the fonts registered at MauiProgram.cs:89-90 are plain OpenSans faces. There is no test measuring digit-column shift and no gallery to render the scale in.

#### `S02.03.02` Support Dynamic Type and 200 percent font scaling - **PARTIAL**

*Evidence.* Sizes are unscaled base values with the OS factor applied on top, stated at ForgeTokens.xaml:87-88; shared controls wrap rather than truncate (EmptyState.xaml:22,28; docs/design/accessibility-audit.md:7-9 records removing fixed-width columns and caption truncation from MetricTile and StatRow); 56 of 69 XAML files carry SemanticProperties for icon-only and glyph buttons.

*Gap.* No automated layout scan at 200 percent exists, so both acceptance criteria are unverified. Worse, the team's own docs/accessibility/README.md:145-148 lists fixed DXCollectionView HeightRequest as an unresolved clipping risk at large font scales, and it is still present on FoodLogPage.xaml:53,108, HydrationPage.xaml:39,67, NutritionPage.xaml:106, WorkoutSummaryPage.xaml:24,34 and ReadinessPage.xaml:34.

#### `S02.03.03` Standardise spacing, radius and gym-friendly touch targets - **PARTIAL**

*Evidence.* ForgeTokens.xaml:23-30 names eight spacing steps, 59-63 five radii, 73-84 touch targets; TouchTargetMin 48 is applied by PrimaryButton, SecondaryButton, TextButton, ListRow, ForgeTextEdit and ForgeNumericEdit (ForgeStyles.xaml:126,141,162,176,189,195) and TouchTargetPrimary 64 (76 on tablet) by WorkoutActionButton (ForgeStyles.xaml:134-137), exceeding the 56dp relaxed density the story asks for.

*Gap.* AC1 requires a token parse test asserting every spacing value is a multiple of 4dp. There is no such test, and it would fail today: SpaceXxs is 2 (ForgeTokens.xaml:23). The named range also stops at 48 rather than 64, radius has no 'none' or 'sheet' value, and AC2's per-control measurement does not exist.

### F02.04 - Create reusable surfaces, lists and loading components

**Feature verdict: PARTIAL** - `S02.04.01` PARTIAL, `S02.04.02` PARTIAL, `S02.04.03` PARTIAL.

#### `S02.04.01` Define elevation and surface hierarchy tokens - **PARTIAL**

*Evidence.* Surface levels come from the DevExpress MD3 roles and are used consistently: Surface for pages (ForgeStyles.xaml:169), SurfaceContainerLow for cards (91), SurfaceContainerHigh for elevated cards (97), SurfaceContainerLowest for detail panes (ForgeAdaptive.xaml:74), ErrorContainer for advisories (105).

*Gap.* There are no elevation tokens. ForgeTokens.xaml defines no shadow, border or tonal treatment for levels 0-4 and no overlay scrim - the only opacity tokens are OpacityDisabled and OpacityMuted (lines 211-213). AC1 (levels 0-4 each distinct with a 3:1 boundary cue) and AC2 (scrim token applied over a DXPopup, which the app never uses) are both unimplemented.

#### `S02.04.02` Build reusable card, metric tile and list row templates - **PARTIAL**

*Evidence.* Controls/ForgeCard.xaml, MetricTile.xaml, StatRow.xaml, SectionHeader.xaml and PageHeader.xaml exist and are used across the app; ForgeCard.xaml:35 correctly hosts caller content in a plain ContentView, with the ContentPresenter binding-inheritance trap documented at lines 20-34; ForgeStyles.xaml:173-177 supplies a ListRow style with the 48dp minimum.

*Gap.* ForgeCard supports only Title, Subtitle and Content (src/Forge.App/Controls/GALLERY.md:16-20) - no leading icon, no action slot, no selected state. MetricTile has no trend indicator (GALLERY.md:36-41). There is no Resources/Styles/ListTemplates.xaml and no one-line / two-line / metric / swipe-action row variants: a grep finds zero SwipeItem usages under Features/. AC2's 500-row 60fps scroll test does not exist.

#### `S02.04.03` Provide empty-state and skeleton loading components - **PARTIAL**

*Evidence.* Controls/EmptyState.xaml provides glyph, headline, guidance and a primary action with the glyph removed from the accessibility tree (line 16); Controls/SkeletonPlaceholder.xaml.cs:60-63 skips the pulse entirely under Reduce Motion and renders a static block, and stops on unload or IsBusy=false (lines 43-46, 69-88).

*Gap.* EmptyState has no secondary action slot, so requirement 1 is unmet. Skeletons do not reserve the final layout: callers pass an arbitrary height (AchievementsPage.xaml:27 SkeletonHeight 240), and there is no measurement of the <=4dp shift AC2 requires.

### F02.05 - Establish iconography and visual state language

**Feature verdict: NOT-DONE** - `S02.05.01` NOT-DONE, `S02.05.02` NOT-DONE, `S02.05.03` NOT-DONE.

#### `S02.05.01` Create the Forge icon catalogue and naming convention - **NOT-DONE**

*Evidence.* src/Forge.App/Resources contains only AppIcon, Fonts, Raw, Splash, Strings and Styles - there is no Images directory and therefore no Resources/Images/Icons. There is no src/Forge.App/Theme folder and no IconCatalog.json. Icons in the product are inline text glyphs (AchievementsPage.xaml:55 '🏆', ExerciseLibraryPage.xaml:346 '&#8592;', GALLERY.md:56 default '✦').

*Gap.* No catalogue, no metadata, no snake_case naming convention. AC2 fails outright: AppShell.xaml:32-43 gives the five primary tabs no icons in either state, so there are no selected and unselected variants to compare and the selected state changes colour only.

#### `S02.05.02` Define icon button and destructive action states - **NOT-DONE**

*Evidence.* No IconActionButton type exists anywhere in src/. ForgeStyles.xaml defines PrimaryButton, WorkoutActionButton, SecondaryButton and TextButton but declares no pressed, focused, selected or destructive visual state and no focus indicator.

*Gap.* AC2 fails on a shipping screen: the Delete action in the exercise library (ExerciseLibraryPage.xaml:318-323) routes to ExerciseLibraryViewModel.cs:397-406, which calls DeleteCustomAsync immediately with no confirmation surface. AC1's 3:1 focus and selected indicators do not exist to be measured.

#### `S02.05.03` Add asset pipeline checks for icon size and tinting - **NOT-DONE**

*Evidence.* tools/ contains backlog-sync, ci, legal, perf, release and smoke; there is no tools/design-assets and no asset inspection script. No SVG metadata parsing, no size budget and no hard-coded-fill detection exists.

*Gap.* Both acceptance criteria are unimplemented. The story is also currently vacuous because there are no action icon assets to check (see S02.05.01).

### F02.06 - Publish the component gallery and regression workflow

**Feature verdict: PARTIAL** - `S02.06.01` NOT-DONE, `S02.06.02` NOT-DONE, `S02.06.03` PARTIAL.

#### `S02.06.01` Build the component gallery navigation page - **NOT-DONE**

*Evidence.* There is no Features/DesignGallery module. Navigation/ForgeRoutes.cs registers no gallery route, and a search for 'gallery' in src/ matches only src/Forge.App/Controls/GALLERY.md, which is a markdown document.

*Gap.* No in-app gallery exists in any configuration, so AC1 (absent in release) is vacuously true and AC2 (every section renders from a DXCollectionView menu) is unimplemented. This is the single largest blocker in E02: eleven other stories put their verification inside this gallery.

#### `S02.06.02` Add visual regression snapshots for light and dark themes - **NOT-DONE**

*Evidence.* No snapshot infrastructure exists: no baselines directory anywhere in the repository, no image-diff tooling in tools/, and no tests/Forge.App.Design.Tests project (tests/ holds three projects, all non-UI).

*Gap.* Neither acceptance criterion is implemented. There is nothing to capture, because the gallery of S02.06.01 does not exist.

#### `S02.06.03` Document design-system usage rules for feature teams - **PARTIAL**

*Evidence.* src/Forge.App/Controls/GALLERY.md sits beside the controls as the story asks and documents ForgeCard, MetricTile, EmptyState, SkeletonPlaceholder, SectionHeader, StatRow, PageHeader, ActivityRing, StepProgressIndicator and AdvisoryPanel, each with bindable properties and a working XAML example; GALLERY.md:115-120 and 148 explain the one-title-per-page and wrapping-FlexLayout rules.

*Gap.* AC1 requires an anti-example for each of card, icon, empty state and skeleton; GALLERY.md has none, and IconActionButton is undocumented because it does not exist. The prohibited-pattern list (literal colours, inline sizes, small targets, custom shadows) lives in .github/copilot-instructions.md, not beside the controls, and no rule links to a gallery example or automated test because neither exists. AC2's link checker is not implemented.

---

## E03 - Motion, Animation and Micro-interactions

**Epic verdict: PARTIAL** (0 DONE, 5 PARTIAL, 13 NOT-DONE)

This is the epic where **code exists and behaviour does not**. `MotionTokens`, `ForgeAnimations`, `PressFeedbackBehavior`, `AnimatedNumberBehavior` and `StaggeredAppearBehavior` are all written, all reduce-motion aware, and all documented in `docs/design/motion.md` - and two of the three behaviours are attached to **nothing**, while `CelebrateAsync`, `PulseAsync` and `CrossFadeAsync` have **zero call sites**. Reduce Motion support is genuine, because the helpers that are used consult it. Everything downstream of "attach the behaviour to a control" was never done.

### F03.01 - Establish motion governance and reduced-motion support

**Feature verdict: PARTIAL** - `S03.01.01` PARTIAL, `S03.01.02` PARTIAL, `S03.01.03` PARTIAL.

#### `S03.01.01` Define motion duration and easing tokens - **PARTIAL**

*Evidence.* src/Forge.App/Motion/MotionTokens.cs:9-13 defines Instant, Fast 150, Medium 250, Slow 400 and Celebration 900, and lines 15-20 define Emphasized, Standard, Entrance, Exit, Press and Count easings; the same durations are mirrored in ForgeTokens.xaml:137-141 with the rationale at 130-135, and docs/design/motion.md:5-14 documents when to use each.

*Gap.* AC1 requires a lint test that fails on a literal duration outside MotionTokens. No such test exists, and it would fail today: Controls/SkeletonPlaceholder.xaml.cs:73,79 hard-code 650ms fades. The token set is also missing the story's decelerate, accelerate and linear easings. AC2's motion gallery does not exist.

#### `S03.01.02` Honour OS Reduce Motion for every animation helper - **PARTIAL**

*Evidence.* IMotionPreferences is declared at Motion/MotionPreferences.cs:10-15; iOS reads UIAccessibility.IsReduceMotionEnabled (line 29) and Android treats a zero animator or transition scale as reduced (lines 25-27); MotionPreferences.Current is replaced with the settings-aware implementation at Features/Settings/SettingsFeatureRegistration.cs:34; every helper consults it - ForgeAnimations.cs:262, StaggeredAppearBehavior.cs:93, AnimatedNumberBehavior.cs:78, PressFeedbackBehavior.cs:96,107, ActivityRing.xaml.cs:69.

*Gap.* IMotionPreferences exposes no change notification, contrary to requirement 1; it is only polled when an animation starts. Requirement 3's local app override does not exist - SettingsMotionPreferences.cs:10 forwards the platform value unchanged and no Settings control offers a Forge-level motion setting. AchievementsPage.xaml:81-87 uses a raw dx:RadialProgressBar with no AllowAnimation guard, so that ring still sweeps under Reduce Motion. AC1's motion gallery does not exist.

#### `S03.01.03` Ensure animations never block input - **PARTIAL**

*Evidence.* ForgeAnimations.AnimateAsync registers a cancellation callback that aborts the MAUI animation (Motion/ForgeAnimations.cs:236-241) and every helper takes a CancellationToken; call sites fire and forget rather than await - PressFeedbackBehavior.cs:125,137, AnimatedNumberBehavior.cs:88, GoalWizardPage.xaml.cs:83; StaggeredAppearBehavior and PressFeedbackBehavior implement IDisposable; docs/design/motion.md:27-34 states the never-await rule.

*Gap.* There is no MotionRunner tracking animations by owner page, so requirement 3 (navigating away cancels the old page's animations) is only honoured where a behaviour happens to detach. AC1 cannot be satisfied because there is no set-completion micro-animation to interrupt (see S03.05.01), and AC2's page transitions do not exist (see S03.02.01).

### F03.02 - Animate navigation, shared elements and lists

**Feature verdict: NOT-DONE** - `S03.02.01` NOT-DONE, `S03.02.02` NOT-DONE, `S03.02.03` NOT-DONE.

#### `S03.02.01` Add clean page transitions for tab and modal flows - **NOT-DONE**

*Evidence.* Navigation/ShellNavigationService.cs is 46 lines and delegates directly to Shell.GoToAsync with no animation hook; a grep for ForgeAnimations across src/ finds call sites only in Motion/ itself and at Features/Onboarding/GoalWizardPage.xaml.cs:83, which fades a step host inside one page.

*Gap.* There is no tab-change transition, no modal transition and no reduced-motion transition path. Both acceptance criteria are unimplemented.

#### `S03.02.02` Add shared-element exercise list to detail transition - **NOT-DONE**

*Evidence.* No SharedElementTransition type or overlay-capture code exists anywhere in src/. ExerciseLibraryPage.xaml.cs and ExerciseLibraryViewModel.cs:454-457 navigate to the detail route with no visual continuity.

*Gap.* Nothing is implemented. Neither acceptance criterion can be evaluated.

#### `S03.02.03` Add bounded list item stagger for newly loaded content - **NOT-DONE**

*Evidence.* Motion/StaggeredAppearBehavior.cs exists with Index and MaxTotalDelay properties and skips under Reduce Motion (line 93), and docs/design/motion.md:85-101 documents applying it inside a DXCollectionView.ItemTemplate.

*Gap.* It is attached to exactly five elements on two pages - TodayPage.xaml:38,65,100 and ProfilePage.xaml:36,56 - and every one is a static card or FlexLayout, never a DXCollectionView item template. No list in Forge staggers. Both acceptance criteria concern a collection view: AC1's first-20 cap and AC2's recycled-row rule are therefore unexercised, and the behaviour has no notion of an ItemsSource generation to reset against.

### F03.03 - Add tactile input and refresh micro-interactions

**Feature verdict: NOT-DONE** - `S03.03.01` NOT-DONE, `S03.03.02` NOT-DONE, `S03.03.03` NOT-DONE.

#### `S03.03.01` Add button press visual feedback tokens - **NOT-DONE**

*Evidence.* Motion/PressFeedbackBehavior.cs implements the behaviour correctly, including the reduced-motion path that avoids Scale (lines 96,107) and a 0.97 pressed scale (line 125), and docs/design/motion.md:73-83 documents it.

*Gap.* It is attached to nothing. A grep for PressFeedbackBehavior across all of src/ returns only its own definition and the documentation - zero usages in any XAML. No style attaches it either, and a MAUI Style cannot set Behaviors; ForgeMotion.xaml:17-19 defines MotionPressable but it only sets MinimumHeightRequest. No button in Forge has press feedback, so both acceptance criteria fail on every real screen.

#### `S03.03.02` Add platform haptic feedback for important actions - **NOT-DONE**

*Evidence.* The only call to a platform haptic API in the entire repository is ForgeAnimations.cs:217, inside the internal TryHapticClick helper (lines 205-226), which correctly checks the Forge preference through SettingsMotionPreferences.cs:13.

*Gap.* TryHapticClick is called from exactly two places, PressFeedbackBehavior.cs:101 and 122, and that behaviour is attached to no element (see S03.03.01), so no haptic ever fires. There is no haptic on set completed, timer finished, invalid action or PR. Meanwhile UnitsSettingsPage.xaml:66-69 ships a 'Haptic feedback' toggle whose hint reads 'Vibrates when a set or a rest period ends' - a user-visible promise with no implementation behind it. AC1 and AC2 both fail.

#### `S03.03.03` Add pull-to-refresh feedback for refreshable lists - **NOT-DONE**

*Evidence.* A grep for PullToRefresh, IsPullToRefreshEnabled and RefreshView across src/ matches only the stock MAUI template style at Resources/Styles/Styles.xaml:249; no feature page enables pull-to-refresh on any DXCollectionView.

*Gap.* Nothing is implemented: no shared pull pattern, no duplicate-pull suppression, no completion message. Both acceptance criteria are unimplemented.

### F03.04 - Animate progress, numbers and loading states

**Feature verdict: PARTIAL** - `S03.04.01` PARTIAL, `S03.04.02` NOT-DONE, `S03.04.03` PARTIAL.

#### `S03.04.01` Animate progress ring fill for goals and readiness - **PARTIAL**

*Evidence.* Controls/ActivityRing.xaml.cs:69 sets Ring.AllowAnimation from MotionPreferences.Current.IsReduceMotionEnabled on a dx:RadialProgressBar, so the sweep animates normally and is switched off under Reduce Motion, and the numeric percentage is a sibling label so the final value is legible before, during and after (GALLERY.md:138-140).

*Gap.* The <=600ms bound is whatever DevExpress defaults to; nothing sets it from MotionTokens, so AC1's timing is unasserted. AchievementsPage.xaml:81-87 renders a raw dx:RadialProgressBar with no AllowAnimation guard, so that ring keeps sweeping when Reduce Motion is on, failing AC2 on a reachable screen.

#### `S03.04.02` Add count-up number transitions for metrics - **NOT-DONE**

*Evidence.* Motion/AnimatedNumberBehavior.cs is implemented, with a Value/Format/Duration surface, cancellation on a second update (line 88) and a reduced-motion path (line 78); ForgeAnimations.CountUpAsync (lines 115-132) finishes at the exact formatted target; docs/design/motion.md:63-71 documents the usage.

*Gap.* It is attached to nothing. A grep for AnimatedNumberBehavior across src/ returns only its own definition and the documentation - zero usages in any XAML - so no metric anywhere in Forge counts up. AC1 fails on every screen. AC2's 800ms static highlight for the reduced-motion path is also not implemented; the reduced path simply sets the text.

#### `S03.04.03` Add reduced-motion skeleton shimmer behaviour - **PARTIAL**

*Evidence.* Controls/SkeletonPlaceholder.xaml.cs loops an opacity pulse only while IsBusy and loaded (lines 69-81), stops on IsBusy=false and on unload (43-46, 83-88) and renders a static block under Reduce Motion (60-63); it uses opacity rather than a layout-affecting property, which is the cheap path docs/design/motion.md:38 mandates.

*Gap.* It probes the platform Reduce Motion setting itself (lines 90-102) instead of consulting MotionPreferences.Current, so it duplicates the logic and would ignore any Forge-level override. Durations are hard-coded 650ms (lines 73,79) rather than MotionTokens, breaking S03.01.01's rule. AC1's <4ms per-frame measurement and its gallery scenario do not exist; there is no MotionRunner owning the loop as the story specifies.

### F03.05 - Deliver celebration moments with safe fallbacks

**Feature verdict: NOT-DONE** - `S03.05.01` NOT-DONE, `S03.05.02` NOT-DONE, `S03.05.03` NOT-DONE.

#### `S03.05.01` Add set-completed micro-celebration - **NOT-DONE**

*Evidence.* ForgeAnimations.CelebrateAsync (Motion/ForgeAnimations.cs:159-203) and PulseAsync (134-157) are implemented and honour Reduce Motion, but a grep across src/ finds zero call sites for either.

*Gap.* The workout screen announces a completed set to a screen reader (ActiveWorkoutPageViewModel.cs:317) but plays no visual confirmation motion at all, so AC1 has nothing to observe. There is no row visual state for confirmation and no failure path distinguishing celebration from an error state (AC2).

#### `S03.05.02` Add personal record and streak celebration surfaces - **NOT-DONE**

*Evidence.* The only celebration surface in the app is a static text banner inside a card on AchievementsPage.xaml:48-52, driven by AchievementsPageViewModel.cs:149-150.

*Gap.* There is no DXPopup or BottomSheet celebration - the single BottomSheet in the app is the plate calculator (ActiveWorkoutPage.xaml:341). There is no dismissal affordance, no 3-second prominence reduction, and no streak celebration surface at all. AC1 and AC2 are unimplemented.

#### `S03.05.03` Govern animation assets for size and fallback - **NOT-DONE**

*Evidence.* docs/design/motion.md:109 records the deliberate decision to use a pure-MAUI glyph burst instead of Lottie, so Forge currently bundles zero animation assets and the 80KB and 500KB budgets are vacuously satisfied.

*Gap.* No asset metadata file exists (source, licence, size, fallback, owning feature), and no validation or budget test runs at build or package time, so both acceptance criteria are unimplemented. The epic's nonGoals rule out Lottie but say nothing about governance, so this is not deferred - it is simply not built.

### F03.06 - Limit motion frequency and preserve focus

**Feature verdict: NOT-DONE** - `S03.06.01` NOT-DONE, `S03.06.02` NOT-DONE, `S03.06.03` NOT-DONE.

#### `S03.06.01` Limit celebration frequency and user control - **NOT-DONE**

*Evidence.* No CelebrationPolicy type exists in src/. ForgePreferenceKeys (Forge.Core/Abstractions/Preferences/UnitPreferences.cs:95-125) contains no celebration intensity key and UnitsSettingsPage.xaml offers no such control.

*Gap.* Neither the per-workout and per-day caps nor the Full/Subtle/Off setting exist. The story is currently moot because there are no celebration surfaces to limit (see S03.05.01, S03.05.02).

#### `S03.06.02` Preserve screen reader focus after animated updates - **NOT-DONE**

*Evidence.* SemanticScreenReader.Announce is wired on two screens - ActiveWorkoutPage.xaml.cs:76 and RestTimerPage.xaml.cs:63 - fed by view-model events (ActiveWorkoutPageViewModel.cs:997, RestTimerPageViewModel.cs:34).

*Gap.* Announcement is not focus placement. No MotionFocusBehavior exists, nothing moves screen-reader focus to a destination heading after navigation, and there is no rule preventing list insertions stealing focus. Both acceptance criteria are unimplemented.

#### `S03.06.03` Add gesture cancel and overscroll micro-feedback - **NOT-DONE**

*Evidence.* No DXCollectionView in Features/ configures swipe actions - a grep for SwipeItem and SwipeActions matches only the stock template style at Resources/Styles/Styles.xaml:325 - and no overscroll feedback code exists.

*Gap.* There is no swipe commit threshold, no cancel return animation and no overscroll treatment, so both acceptance criteria are unimplemented.

---

## E23 - Accessibility and Inclusive Design

**Epic verdict: PARTIAL** (0 DONE, 11 PARTIAL, 4 NOT-DONE)

The most substantively advanced epic in this range, and the only one with **on-device evidence**. `ForgeAccessibility.cs` fixes two DevExpress defects that cannot be fixed from XAML - the missing button role and the anonymous inner views of composite editors - and `docs/accessibility/sweep-evidence.md` records before-and-after accessibility node dumps from a real emulator with TalkBack bound. Three things hold it back: it is **Android only** by explicit decision, there is **no automated check in CI**, and the **settings hub, keyboard focus indicator and accessibility statement do not exist**.

### F23.01 - Make screen reader navigation reliable in dynamic flows

**Feature verdict: PARTIAL** - `S23.01.01` PARTIAL, `S23.01.02` PARTIAL, `S23.01.03` PARTIAL.

#### `S23.01.01` Label all primary controls and data visualisations - **PARTIAL**

*Evidence.* 56 of 69 XAML files carry SemanticProperties and 18 mark drawn content out of the tree with AutomationProperties.IsInAccessibleTree=False alongside a text summary (AchievementsPage.xaml:87 is representative); src/Forge.App/Accessibility/ForgeAccessibility.cs:130-149 gives every DXButton the android.widget.Button role, a clickable node and ACTION_CLICK, and 154-178 names the inner EditText and ImageButton of composite editors; the before-and-after node dumps are in docs/accessibility/sweep-evidence.md:82-129, measured on a device.

*Gap.* Android only. ForgeAccessibility.cs:265-272 is an explicit no-op off Android, so AC2 (VoiceOver announcing a chart's title, range and value summary) is unimplemented and the two DevExpress defects almost certainly persist on iOS - docs/accessibility/README.md:133-136 says so. 13 XAML files carry no SemanticProperties at all, and README.md:149-152 records that most charts expose only a one-line summary rather than the key values.

#### `S23.01.02` Define logical traversal order for workout execution - **PARTIAL**

*Evidence.* ActiveWorkoutPage.xaml is laid out in task order rather than relying on traversal overrides, headings carry SemanticProperties.HeadingLevel (51 of 69 files), and decorative rings and charts are removed from the tree (docs/accessibility/README.md:18-24).

*Gap.* AC2 fails: the plate-calculator BottomSheet is opened from code-behind at ActiveWorkoutPage.xaml.cs:81 and nothing returns focus to the opening button on dismissal. There is no IAccessibilityFocusService anywhere in src/, and no focus management code of any kind. The traversal order itself has never been verified with a screen reader - docs/design/accessibility-audit.md:22 states focus grouping could not be checked.

#### `S23.01.03` Throttle live announcements for timers and changing values - **PARTIAL**

*Evidence.* Announcements are semantic events rather than per-second ticks, exactly as the story asks: ActiveWorkoutPageViewModel.cs:290-293 announces rest completion once, guarded by a completedRestAnnouncement field so a repeated tick cannot re-announce, plus set logged (317), rest skipped (393) and station changes (601); RestTimerPageViewModel.cs:105,113 announces adjustments and skips; both reach the platform through SemanticScreenReader.Announce (ActiveWorkoutPage.xaml.cs:76, RestTimerPage.xaml.cs:63).

*Gap.* There is no IAccessibilityAnnouncementService and no throttling policy anywhere, so the 10-second rule in requirement 2 is unenforced - it is satisfied incidentally because nothing announces per tick. AC1 requires announcements at start and at 10 seconds remaining as well as completion; neither exists. Requirement 3 (a user setting to disable live announcements) is not implemented.

### F23.02 - Support scalable text, contrast and touch targets

**Feature verdict: PARTIAL** - `S23.02.01` PARTIAL, `S23.02.02` PARTIAL, `S23.02.03` PARTIAL.

#### `S23.02.01` Scale text to 200 percent without truncating actions - **PARTIAL**

*Evidence.* Token sizes are unscaled base values with the OS factor applied on top (ForgeTokens.xaml:87-88); shared controls wrap rather than truncate and fixed-width columns were removed from MetricTile and StatRow for exactly this reason (docs/design/accessibility-audit.md:7-8); pages are inside ScrollViews so growth is absorbed vertically.

*Gap.* Requirement 1 asks for testing at 100, 150 and 200 percent - there is no UI test project at all, so nothing is tested at any scale. Requirement 3 (999.5 kg and 9999 kcal shown in full) is unverified. The fixed DXCollectionView HeightRequest pattern the team documented as a large-text clipping risk (docs/accessibility/README.md:145-148) is still present on seven collections, so AC1 and AC2 are at genuine risk, not merely unproven.

#### `S23.02.02` Verify contrast and avoid colour-only meaning - **PARTIAL**

*Evidence.* All colour comes from DevExpress MD3 semantic roles - zero hex literals in any XAML outside Resources/Styles - and ForgeStyles.xaml:41-49 records choosing OnSurfaceVariant over reduced opacity specifically to stay above the 4.5:1 floor; states are carried by text: AdvisoryPanel headline/message/reassurance, EmptyState headline/message.

*Gap.* AC1 requires the token palette to be scanned and measured. No contrast test exists anywhere in tests/ (see S02.02.01), so the 4.5:1 and 3:1 claims are unmeasured. Colour-only meaning survives on the tab bar: AppShell.xaml:23-25,32-43 gives the five tabs no icons and distinguishes the selected tab by colour alone. Chart series legends and labels have not been audited for greyscale survival.

#### `S23.02.03` Enforce mobile touch target minimums - **PARTIAL**

*Evidence.* TouchTargetMin 48 is applied through shared styles to every button, list row and editor (ForgeStyles.xaml:126,141,162,176,189,195), and ForgeStyles.xaml:145-164 records a real device-found fix: DevExpress Text buttons ship with no padding and collapsed to zero visible height while still occupying 48dp and announcing themselves, which affected all 57 uses of the style. There are no swipe actions in the app, so requirement 3 is vacuous rather than violated.

*Gap.* Requirement 2 is violated on a shipping screen: ExerciseLibraryPage.xaml:301-323 stacks Favourite, View, Edit and Delete in one column with SpaceXxs (2dp) spacing, so a destructive action sits 2dp from non-destructive ones, and ExerciseLibraryViewModel.cs:397-406 deletes with no confirmation step. AC1's measurement has no harness.

### F23.03 - Provide motion, media and sensory alternatives

**Feature verdict: PARTIAL** - `S23.03.01` PARTIAL, `S23.03.02` PARTIAL, `S23.03.03` PARTIAL.

#### `S23.03.01` Honour Reduce Motion across animations and transitions - **PARTIAL**

*Evidence.* The setting is read on both platforms - Motion/MotionPreferences.cs:29 for iOS UIAccessibility, 25-27 for Android animator and transition scales - and consulted by every Forge helper (ForgeAnimations.cs:262, StaggeredAppearBehavior.cs:93, AnimatedNumberBehavior.cs:78, ActivityRing.xaml.cs:69, SkeletonPlaceholder.xaml.cs:60).

*Gap.* Requirement 3 (a user override of motion intensity inside Forge settings) does not exist: SettingsMotionPreferences.cs:10 forwards the platform value unchanged and no Settings control exposes motion. Requirement 1's 'on resume' re-read is not implemented - the value is polled per animation with no lifecycle hook. AC1 has no celebration to suppress (see S03.05.01) and AC2 has no page transitions to suppress (see S03.02.01). AchievementsPage.xaml:81-87 still animates its ring under Reduce Motion.

#### `S23.03.02` Add captions and transcripts for exercise videos - **PARTIAL**

*Evidence.* A transcript equivalent genuinely exists and is offline: ExerciseVideoPage.xaml:106-107 renders coaching cues beside the player from ExerciseVideoViewModel.cs:49,62, and ExerciseGuidanceView.xaml:110-118 shows the same cues plus steps on the detail page, so AC2 is met. VideoLibraryPage.xaml:25 states plainly that steps, cues and mistakes are available from text guidance and video is enrichment.

*Gap.* There are no captions of any kind - no caption files, no caption metadata beside seed content, no synchronised cue timing, and no approved no-speech marker. MauiProgram.cs:79-82 notes that clips are silent demonstrations, which would justify a no-speech marker, but that justification is a code comment rather than recorded caption metadata. AC1 is unimplemented, as is requirement 3 (caption text scaling over the video).

#### `S23.03.03` Provide visual and haptic alternatives for audio cues - **PARTIAL**

*Evidence.* The visual half is real and persistent: RestTimerPageViewModel.cs:61,90-92 keeps an explicit IsComplete state and a 'Rest complete' line rather than a transient cue, and safety advisories stay on screen until the input changes (Controls/AdvisoryPanel.xaml, bound through HasSafetyAdvisory). Haptics are independently switchable from sound in principle (ForgePreferenceKeys.HapticFeedbackEnabled, UnitPreferences.cs:118).

*Gap.* AC1 fails: no haptic fires when a rest timer completes, because the only haptic call site in the repository (ForgeAnimations.cs:217) is reached only through PressFeedbackBehavior, which is attached to nothing (see S03.03.02). Requirement 1 is also vacuous in the other direction - Forge plays no audio cues at all, so there are no cues to provide alternatives for.

### F23.04 - Support motor access, keyboard operation and timing control

**Feature verdict: PARTIAL** - `S23.04.01` NOT-DONE, `S23.04.02` PARTIAL, `S23.04.03` NOT-DONE.

#### `S23.04.01` Enable switch control and external keyboard navigation - **NOT-DONE**

*Evidence.* The Android accessibility delegate does advertise ACTION_CLICK on DevExpress buttons (Accessibility/ForgeAccessibility.cs:221) and performs a real activation (238-263), which helps switch access on Android.

*Gap.* There is no visible focus indicator anywhere: ForgeStyles.xaml defines no focus state or focus ring, and no VisualState covers focus. No keyboard handling exists, no Enter/Space activation is wired, and iOS has nothing at all. Both acceptance criteria - Tab traversal on Android and Switch Control on iOS - are unimplemented.

#### `S23.04.02` Let users extend or disable rest timer deadlines - **PARTIAL**

*Evidence.* Extend and Skip both exist and are announced: RestTimerPageViewModel.cs:95-106 adds a signed number of seconds through session.AdjustRestAsync and announces the new remaining value, and 108-115 skips and announces; RestTimerPage.xaml exposes +15/+30/+60 and -15/Skip. No destructive action anywhere times out on its own.

*Gap.* There is no Pause - a grep for Pause across Features/Workout returns nothing - so requirement 1 is two thirds met. Requirement 3 and AC2 depend on a 'reduced timing pressure' preference that does not exist in ForgePreferenceKeys or any settings page, so timer completion behaviour is not user-configurable.

#### `S23.04.03` Provide an accessibility settings hub - **NOT-DONE**

*Evidence.* The only accessibility-adjacent toggle in the app is 'Haptic feedback' on UnitsSettingsPage.xaml:66-69, and it controls a code path nothing reaches (see S03.03.02). SettingsPageViewModel.cs lists no accessibility destination.

*Gap.* There is no accessibility settings page and no AccessibilityPreferences service. None of reduced motion, live announcements, captions default or reduced timing pressure is exposed, and nothing seeds Forge defaults from the platform accessibility settings on first launch, so AC1 and AC2 are both unimplemented.

### F23.05 - Validate accessibility continuously and publish the commitment

**Feature verdict: PARTIAL** - `S23.05.01` NOT-DONE, `S23.05.02` PARTIAL, `S23.05.03` NOT-DONE.

#### `S23.05.01` Add automated accessibility checks to CI - **NOT-DONE**

*Evidence.* CI does run one relevant job: .github/workflows/ci.yml:153 executes tools/smoke/Test-ForgeSmokeChecks.ps1, and tools/smoke/lib/ForgeUiAnalysis.ps1 implements real unlabelled-control and blank-container detection with seeded fixtures under tools/smoke/fixtures/.

*Gap.* That job is the harness's own self-test against fixtures, not a scan of the app - the device run (tools/smoke/Invoke-ForgeSmoke.ps1) is manual. There is no tools/accessibility directory, no XAML scan for missing accessible names on interactive controls, and no token contrast test, so AC1 and AC2 are both unimplemented. ci.yml's six guards are route registration, data access, legal sync, localization manifests, route reachability and XAML attributes.

#### `S23.05.02` Run manual assistive-technology release testing - **PARTIAL**

*Evidence.* Genuine device evidence exists and is unusually honest: docs/accessibility/sweep-evidence.md records a baseline run with counts (lines 12-29), a diagnosis, a per-screen after table (92-99), a TalkBack session with the service actually bound (131-145), and caveats including the fact that TTS audio could not be captured (147-150) and that only tap-reachable screens were checked (160-161).

*Gap.* There is no docs/quality/accessibility-test-matrix.md and no matrix at all. Only TalkBack was exercised: VoiceOver, Switch Control, external keyboard, large text, Reduce Motion and captions have no dated pass, fail or not-applicable result, and no physical iOS device was used. The matrix is not part of the release checklist, so AC1 and AC2 are unimplemented.

#### `S23.05.03` Publish an accessibility statement and feedback route - **NOT-DONE**

*Evidence.* docs/legal/ contains privacy-policy, terms-of-service, medical-disclaimer, licences, support, data-safety, data-export, delete-my-data and store-compliance-checklist - there is no accessibility-statement.md, and tools/legal/Build-LegalSite.ps1 therefore publishes none.

*Gap.* No statement exists, so it names no target standard, no known limitations, no review date and no contact route, and Settings has no entry pointing at one. Both acceptance criteria are unimplemented. docs/accessibility/README.md:131-152 already contains an honest known-gaps list that would seed the statement.

---

## E24 - Localisation and Internationalisation

**Epic verdict: PARTIAL** (0 DONE, 6 PARTIAL, 9 NOT-DONE)

**Confirmed not done, as the brief said.** The infrastructure is well built and well tested - keys in `Forge.Core`, an explicit fallback chain, a runtime culture applier that runs before the first frame, and a CI guard that stops `ForgeLanguages.All`, the `.resx` set and `CFBundleLocalizations` drifting apart. But `{loc:Translate}` appears in **exactly one XAML file**, the resource pair holds **24 strings and all of them belong to the language picker**, and `useLocalization` is still `false`. Two facts worth flagging beyond that: the per-category unit preferences are a facade over one switch, and kilojoules are unreachable by construction.

### F24.01 - Establish resource-backed string architecture

**Feature verdict: PARTIAL** - `S24.01.01` PARTIAL, `S24.01.02` NOT-DONE, `S24.01.03` NOT-DONE.

#### `S24.01.01` Configure RESX resources and IStringLocalizer - **PARTIAL**

*Evidence.* The mechanism is complete and running: keys are compile-checked constants in Forge.Core/Abstractions/Localization/ForgeStringKeys.cs, resolution has an explicit fallback chain in LocalizationService with a documented no-fallback string source (docs/localization/README.md:56-66), English and German .resx live under Resources/Strings, and the feature is registered at Features/FeatureRegistration.cs:60 and started before the first window via IMauiInitializeService (LocalizationFeatureRegistration.cs:50,62-75). Tests exist at tests/Forge.Core.Tests/Localization/.

*Gap.* AC1 fails outright. {loc:Translate} appears in exactly one XAML file in the repository, Features/Settings/Localization/LanguageSettingsPage.xaml; the other 68 carry literal text, so Today, Workout, Nutrition and Settings labels are all string literals. The resx pair holds 24 entries and every one of them is for the language picker itself. Forge.Core validation messages are not routed through resource keys. The abstraction is Forge's own ILocalizationService rather than IStringLocalizer - a deliberate, documented deviation (docs/localization/README.md:44-49), not a defect.

#### `S24.01.02` Block hard-coded user-facing strings in CI - **NOT-DONE**

*Evidence.* There is no tools/localization directory; tools/ holds backlog-sync, ci, legal, perf, release and smoke. .github/workflows/ci.yml runs no string scan - the only localization gate is Test-LocalizationManifests.ps1 (ci.yml:128), which compares ForgeLanguages.All against Info.plist and the .resx set.

*Gap.* No Roslyn or XAML scanner, no suppression file with expiries, no reporting of path, line and suggested key. Both AC1 and AC2 are unimplemented. This is the gate that would have made the state described in S24.01.01 visible.

#### `S24.01.03` Localise legal, safety and notification copy consistently - **NOT-DONE**

*Evidence.* docs/legal/ contains a single English copy of each document (privacy-policy.md, terms-of-service.md, medical-disclaimer.md and the rest) with no locale variants and no per-locale directory; tools/legal/Build-LegalSite.ps1 and Test-LegalContentSync.ps1 have no locale dimension.

*Gap.* No German legal text exists, so AC1 cannot pass. Notification copy is composed from literals in Services/Notifications/LocalNotificationScheduler.cs rather than resource keys. No release validation blocks a locale on missing legal translation, so AC2 is unimplemented.

### F24.02 - Format dates, numbers and fitness units by locale and preference

**Feature verdict: PARTIAL** - `S24.02.01` PARTIAL, `S24.02.02` PARTIAL, `S24.02.03` PARTIAL.

#### `S24.02.01` Store canonical values and display preferred units - **PARTIAL**

*Evidence.* Storage is canonical metric and conversion is a display concern: Forge.Domain/Measurement/Mass.cs plus Length, Volume and Percentage value objects with tests (tests/Forge.Domain.Tests/Measurement/MassTests.cs, Profile/LengthTests.cs, Nutrition/VolumeTests.cs); Forge.Core/Abstractions/Preferences/UnitFormatter.cs converts on the way out and ILocalizedValueFormatter.cs:113-122 exposes Mass, Length, Volume and Energy through the display culture.

*Gap.* AC3 is impossible to satisfy. UnitPreferences.cs:267-277 hard-wires EnergyUnit to Kilocalories and its setter throws NotSupportedException for anything else, so the kJ conversion at UnitFormatter.cs:54-57 is unreachable code and no user can ever toggle kcal to kJ. Requirement 3 is also unmet: MassUnit, LengthUnit and VolumeUnit (UnitPreferences.cs:246-264) are not independent preferences at all - each getter derives from the single UnitSystem and each setter flips it, so choosing pounds silently changes height and volume too.

#### `S24.02.02` Format dates, times and numbers with culture-aware services - **PARTIAL**

*Evidence.* LocalizedValueFormatter (Forge.Core/Abstractions/Localization/ILocalizedValueFormatter.cs:71-110) formats dates, times, timestamps, day names, numbers, whole numbers, percentages and durations through localization.CurrentCulture, with a documented reason for treating elapsed duration as digits (97-102); CurrentUICulture and CurrentCulture are kept separate on purpose (docs/localization/README.md:68-72); a first-day-of-week preference exists (UnitPreferences.cs:280-284, UnitsSettingsPage.xaml:76-81) and LocalizedValueFormatterTests covers it.

*Gap.* The formatter reaches one screen. A grep for ILocalizedValueFormatter across src/ finds injection only in Features/Settings/Localization/LanguageSettingsPageViewModel.cs:23,33. Everything else formats inline - ActiveWorkoutPageViewModel.cs:317 interpolates '{LoadKilograms:0.##} kilograms', RestTimerPageViewModel.cs:132 builds m:ss by hand - so AC1's de-DE display is not achieved on any real screen. Input parsing is not culture-aware anywhere: requirement 2's parse half is unimplemented.

#### `S24.02.03` Add unit and locale preferences to settings and onboarding - **PARTIAL**

*Evidence.* Settings exposes both: Features/Settings/UnitsSettingsPage.xaml:19-27 offers a unit system with live previews driven by UnitsSettingsPageViewModel, and Features/Settings/Localization/LanguageSettingsPage.xaml offers language with an immediate-apply note; SettingsPageViewModel.cs:18 registers the Language destination so it is reachable from the settings list.

*Gap.* There is no Units step in onboarding - no Features/Onboarding/UnitsPage.xaml exists - so AC1 is unimplemented and no region-derived default is ever suggested. Requirement 2 asks for independent weight, height, volume and energy preferences; there is one Metric/Imperial switch and energy is locked to kcal (UnitPreferences.cs:246-277). AC2's one-second history refresh is unverifiable because workout history does not format through the unit formatter.

### F24.03 - Support right-to-left layout and real plural rules

**Feature verdict: NOT-DONE** - `S24.03.01` NOT-DONE, `S24.03.02` NOT-DONE, `S24.03.03` NOT-DONE.

#### `S24.03.01` Mirror layouts, icons and gestures for RTL cultures - **NOT-DONE**

*Evidence.* docs/localization/rtl-readiness.md:1-5 states it directly: 'Forge ships no right-to-left language, and no layout has been converted.' The plumbing exists - SupportedLanguage.IsRightToLeft (SupportedLanguage.cs:48), ILocalizationService.IsRightToLeft, android:supportsRtl in the manifest - and FlowDirection is demonstrated on one page only (rtl-readiness.md:14,22-26).

*Gap.* FlowDirection is not set at the application root, so AC1 fails. No directional icon set exists to mirror (there are no icon assets at all - see S02.05.01), and gesture copy is not resource-backed. rtl-readiness.md:74-78 costs the remaining work at roughly two days plus translation, which is the honest state.

#### `S24.03.02` Implement CLDR-based pluralisation for counts and durations - **NOT-DONE**

*Evidence.* No PluralMessageFormatter exists anywhere in src/, and no plural category table is embedded or referenced. Counts are concatenated directly in view models, for example AchievementsPageViewModel.cs:150 switching on NewlyEarned.Count.

*Gap.* No zero/one/two/few/many/other handling, no ban on English-only helpers, and no English, Polish or Arabic tests. Both acceptance criteria are unimplemented.

#### `S24.03.03` Validate bidirectional text in user content and catalogues - **NOT-DONE**

*Evidence.* Search is explicitly ordinal, not locale-aware: Forge.Domain/Training/ExerciseSearchIndex.cs uses StringComparer.OrdinalIgnoreCase throughout (lines 94,95,100,101,156,206), which performs no diacritic folding and no culture-aware case folding.

*Gap.* AC2 fails by construction - an accented query variant will not match an unaccented entry. There is no bidi handling for mixed Latin and RTL names, no number-and-unit association rule, and no bidi examples in the seed-content tests, so AC1 is unimplemented too.

### F24.04 - Plan launch languages and translator workflow

**Feature verdict: PARTIAL** - `S24.04.01` PARTIAL, `S24.04.02` NOT-DONE, `S24.04.03` NOT-DONE.

#### `S24.04.01` Choose a realistic launch language set - **PARTIAL**

*Evidence.* A language set is genuinely chosen and shipped: ForgeLanguages.All is English plus German (SupportedLanguage.cs:55-64), the choice is enforced end to end by tools/ci/Test-LocalizationManifests.ps1 against Info.plist and the .resx files, and docs/localization/adding-a-locale.md documents the process including terms flagged for native-speaker review.

*Gap.* There is no docs/localization/language-plan.md. Requirement 1 asks for two to four candidates with rationale and requirement 2 for rationale covering fitness market, food-data coverage, legal translation cost and support capacity - none of that is recorded anywhere, so AC1 is unimplemented. AC2's release validation gate on legal translation capacity does not exist, and no wave-6 candidate list is documented.

#### `S24.04.02` Provide translator handoff files with context - **NOT-DONE**

*Evidence.* No tools/localization directory exists and no export or import tooling is present anywhere in the repository; docs/localization/ has README, adding-a-string, adding-a-locale, full-conversion-runbook and rtl-readiness, but no glossary.md.

*Gap.* There is no XLIFF or CSV export, no key/source/note/screenshot columns, no glossary of training, nutrition, health-consent and safety terminology, and no import validation. Both acceptance criteria are unimplemented. docs/localization/adding-a-string.md does define the placeholder discipline translators would need, which is the nearest thing that exists.

#### `S24.04.03` Run pseudo-localisation and expansion tests - **NOT-DONE**

*Evidence.* No pseudo culture is registered - a search for qps-ploc or qps-plocm across the repository returns nothing - and no pseudo-localisation generator or test exists.

*Gap.* There is no 30 percent expansion, no marker wrapping, no forced-RTL pseudo mode and no CI screenshot capture, so both acceptance criteria are unimplemented.

### F24.05 - Localise store assets, food content and DevExpress resources

**Feature verdict: PARTIAL** - `S24.05.01` NOT-DONE, `S24.05.02` NOT-DONE, `S24.05.03` PARTIAL.

#### `S24.05.01` Prepare localised store listings and screenshots - **NOT-DONE**

*Evidence.* docs/release/store-listing.md is a single English document covering identity, categories, keywords, age rating, data safety, screenshots and review notes with no locale dimension; there is no docs/release/store-listings/<locale> directory and no per-locale screenshot metadata.

*Gap.* German is a shipped app language (ForgeLanguages.All) but has no store text, so the listing understates the product. There is no asset validation that blocks a locale and no screenshot unit-consistency check, so AC1 and AC2 are unimplemented.

#### `S24.05.02` Select region-appropriate food database coverage - **NOT-DONE**

*Evidence.* No food data manifest records source, region coverage, licence or update date; Forge.Infrastructure content importers (SeedContentImporter, SeedCatalogueImport) carry no region or licence metadata, and nutrition search applies no regional scoring.

*Gap.* AC1 (German regional results ranked ahead of global) is unimplemented because there is no region dimension at all. The food search empty state does not disclose coverage limits, so AC3 is unmet, and the language plan that AC2 depends on does not exist (see S24.04.01).

#### `S24.05.03` Enable DevExpress localisation resources with startup budget - **PARTIAL**

*Evidence.* Requirement 2 is met precisely and visibly: MauiProgram.cs:62-65 documents the chosen setting and the reason - useLocalization stays false until localized resources arrive with E24, because loading the DevExpress localization assemblies costs startup time against a 2.0 second cold-start budget - and tools/perf/Measure-ColdStart.ps1 plus the StartupTimeline marks (MauiProgram.cs:29-107) exist to measure the change when it is made.

*Gap.* It is still false (MauiProgram.cs:65), so DevExpress built-in editor, picker and collection-view text stays English whatever language the user picks: AC2 fails. No measured before-and-after comparison has been recorded, so AC1 has not been executed. docs/localization/README.md:92-93 lists this as a known gap.

---

## E27 - Adaptive Layout and Responsive Experience

**Epic verdict: PARTIAL** (0 DONE, 6 PARTIAL, 12 NOT-DONE)

The half of this epic that was built is **unusually well built**: layout adapts on measured window width rather than device idiom, the arithmetic lives in framework-free code with 15 tests pinned to real device point sizes, and `docs/design/tablet-layout.md` is honest about what was not verified. The `ItemSpanCount` defect named in the brief is **fixed** - all 13 bindings now sit inside their tags and `Test-XamlAttributes.ps1` passes with 69 files scanned and 0 violations. The other half is missing entirely: **no safe-area handling anywhere in `src/`**, no keyboard avoidance, no foldable support, no landscape mode, no rotation state, and 20 of 50 feature pages with no adaptive treatment at all.

### F27.01 - Establish breakpoint and orientation foundations

**Feature verdict: PARTIAL** - `S27.01.01` PARTIAL, `S27.01.02` PARTIAL, `S27.01.03` NOT-DONE.

#### `S27.01.01` Define Forge breakpoint and size-class tokens - **PARTIAL**

*Evidence.* The arithmetic is real, framework-free and tested: Forge.Core/Adaptive/AdaptiveLayoutMetrics.cs:33-51 resolves Compact/Medium/Expanded, thresholds come from ForgeTokens.xaml:155-156 (600 and 840) via ForgeAdaptive.xaml:30-31, LayoutSizeClass is in Forge.Core, and tests/Forge.Core.Tests/Adaptive/AdaptiveLayoutMetricsTests.cs pins 15 cases against real device point sizes in both orientations. AdaptiveHost.OnSizeAllocated recalculates and raises change notifications (AdaptiveHost.cs:269-273, 328-340).

*Gap.* Requirement 2 is unmet: the size class is computed from the host's measured width and no safe-area inset is subtracted anywhere - a search for SafeArea across src/ returns zero matches. There is no IAdaptiveLayoutService as an injectable service, and the 100ms notification bound in requirement 3 and AC2 is neither guaranteed nor measured.

#### `S27.01.02` Protect phone portrait as the primary layout target - **PARTIAL**

*Evidence.* The phone layout is protected by construction rather than by convention: AdaptiveLayoutMetrics.ResolveContentWidth returns -1 when the cap would not bite (lines 85-93), so on a phone the width request is never applied and the layout is byte-for-byte the old one (docs/design/tablet-layout.md:77-79); SupportsTwoPanes requires height >= 520 as well as width (AdaptiveLayoutMetrics.cs:67-71) specifically so a large phone in landscape never splits, with the reasoning at ForgeTokens.xaml:158-171.

*Gap.* AC1 requires compact portrait snapshots at 360x800 for exercise library, exercise detail, nutrition search and profile, with primary actions in the lower third; there are no snapshots, no lower-third rule and no justified-exception record. AC2's 200 percent scan does not exist. ProfilePage is one of 20 of the 50 feature pages that carry no AdaptiveHost at all.

#### `S27.01.03` Preserve adaptive state across rotation and resume - **NOT-DONE**

*Evidence.* Android avoids activity recreation on rotation through Platforms/Android/MainActivity.cs:7 (ConfigChanges.ScreenSize | Orientation | UiMode | ScreenLayout | SmallestScreenSize | Density), so in-memory state incidentally survives a device rotation there.

*Gap.* That is a side effect, not the story. There is no state snapshot of route, selected item key, scroll offset or focused input anywhere in src/ - a grep for ScrollOffset, RestoreState and snapshot in Forge.App returns nothing - no restore path after first layout, and nothing at all on iOS. Neither acceptance criterion is implemented and the 250ms bound is unmeasured.

### F27.02 - Support tablet and foldable master-detail experiences

**Feature verdict: PARTIAL** - `S27.02.01` PARTIAL, `S27.02.02` PARTIAL, `S27.02.03` NOT-DONE.

#### `S27.02.01` Add reusable one-pane and two-pane adaptive containers - **PARTIAL**

*Evidence.* The container exists under a different name and does the job: Adaptive/AdaptiveHost.cs publishes SplitColumns, IsSplit, IsStacked, PaneSpacing, ListPaneWidth, DetailPaneColumn and DetailPaneColumnSpan (lines 184-266) from AdaptiveLayoutMetrics.ResolveListPaneWidth (131-149), collapses the second column to zero when stacked (286-287) and drops the split when the minimum detail width cannot be honoured (299-302). Pane widths are tokenized (ForgeAdaptive.xaml:36-38) and DetailPaneMinimumWidth is 420, comfortably above the 320 floor (ForgeTokens.xaml:193). Selection lives in the view model and is pushed from measured width, so it survives the mode change (ExerciseLibraryPage.xaml.cs:25,31-34; ExerciseLibraryViewModel.cs:179,190).

*Gap.* AC1's second half is not honoured: when the viewport narrows, IsDetailPaneVisible (ExerciseLibraryViewModel.cs:190) goes false and the list is shown - the selection is retained but the detail does not become the active single pane, so the user must tap View again. The story's boilerplate criterion about dx:SafeKeyboardAreaView keeping focused fields visible is unimplemented app-wide (see S27.03.02). The backlog names this AdaptivePaneView; the implementation is AdaptiveHost.

#### `S27.02.02` Add two-pane exercise library and nutrition browsing - **PARTIAL**

*Evidence.* The exercise library is a genuine two-pane: ExerciseLibraryPage.xaml:38 binds Grid.ColumnDefinitions to SplitColumns, 341-360 is the detail pane with a real empty-selection placeholder, and 354-358 nests a second AdaptiveHost so the reading measure is taken from the pane rather than the window; compact width keeps push navigation (ExerciseLibraryViewModel.cs:445-457); the detail markup is the shared ExerciseGuidanceView used by both the pane and ExerciseDetailPage, so phone and tablet cannot drift. Recipes (RecipesPage.xaml:57) and Plan templates (PlanTemplatesPage.xaml:18,79) split the same way.

*Gap.* Nutrition browsing is not two-pane at any width: NutritionPage, FoodLogPage and food search remain single-column, so half of requirement 1 and half of AC2 are unimplemented. The empty-selection placeholder carries a message but no action button, so requirement 3's 'one recommended next action' is unmet (ExerciseLibraryPage.xaml:345-348).

#### `S27.02.03` Support Android foldable hinge and posture changes - **NOT-DONE**

*Evidence.* A grep for Foldable and TwoPaneView across all of src/ returns zero matches; Microsoft.Maui.Controls.Foldable is not referenced by Forge.App and UseFoldable is not called in MauiProgram.cs.

*Gap.* There is no FoldablePaneHost, no posture handling and no hinge-bounds avoidance, so both acceptance criteria are unimplemented.

### F27.03 - Handle safe areas, keyboard and landscape modes

**Feature verdict: NOT-DONE** - `S27.03.01` NOT-DONE, `S27.03.02` NOT-DONE, `S27.03.03` NOT-DONE.

#### `S27.03.01` Apply safe areas for notches, Dynamic Island and gesture insets - **NOT-DONE**

*Evidence.* A case-insensitive search for SafeArea, SafeAreaEdges and inset across all of src/ returns zero matches. There is no AdaptivePageScaffold. Pages set Padding from the PagePadding token (ForgeTokens.xaml:41-44), which is a fixed 20/16/20/24 (28/20/28/32 on tablet) and has no inset component.

*Gap.* Nothing in Forge reasons about safe areas. Top content relies entirely on Shell's default chrome behaviour and bottom actions have no gesture-inset clearance, so AC1 (nothing intersecting the Dynamic Island bounds) and AC2 (8dp above the Android gesture inset) are unimplemented and unverified. This is a real iPhone and Android gesture-navigation risk, not just a missing file.

#### `S27.03.02` Add keyboard avoidance for forms with SafeKeyboardAreaView - **NOT-DONE**

*Evidence.* A grep for SafeKeyboardAreaView, SafeAreaEdges and SoftInput across src/ returns zero matches. Data-entry pages - FoodLogPage, PlanEditorPage, GoalWizardPage and the exercise editor inside ExerciseLibraryPage - wrap their content in plain ScrollViews with no keyboard-aware container.

*Gap.* Nothing guarantees the focused editor and the primary action stay above the keyboard, and nothing bounds the layout shift. Both acceptance criteria are unimplemented. This criterion is also repeated as boilerplate on every other E27 story, so it fails across the epic.

#### `S27.03.03` Optimise landscape workout mode and video playback - **NOT-DONE**

*Evidence.* AdaptiveHost.IsLandscape exists and is computed (Adaptive/AdaptiveHost.cs:194-198, 309), but no XAML binds it - a search for IsLandscape across all 69 XAML files returns zero matches. ExerciseVideoPage tracks position only to drive the scrubber (ExerciseVideoPage.xaml.cs:50,53-60).

*Gap.* There is no landscape layout, no LandscapeWorkoutScaffold, no thumb-zone placement rule and no playback-position preservation across an orientation change. Both acceptance criteria are unimplemented.

### F27.04 - Guarantee large text and accessibility scaling

**Feature verdict: PARTIAL** - `S27.04.01` NOT-DONE, `S27.04.02` NOT-DONE, `S27.04.03` PARTIAL.

#### `S27.04.01` Add 200 percent text-scaling layout tests for core flows - **NOT-DONE**

*Evidence.* There is no UI or layout test project: tests/ holds Forge.Core.Tests, Forge.Domain.Tests and Forge.Infrastructure.Tests, none of which instantiates a page. The story's own mechanism is absent too - AutomationId occurs zero times across all 69 XAML files, and no CriticalContent attached property exists.

*Gap.* No core flow is tested at any text scale, so AC1 and AC2 are unimplemented and the horizontal-scrolling prohibition is unenforced.

#### `S27.04.02` Reflow validation and helper text in forms - **NOT-DONE**

*Evidence.* No ValidationMessageView or FormFieldLayout wrapper exists in src/. Errors are surfaced as page-level banners (for example ExerciseLibraryViewModel.ShowError) rather than per-field messages with wrapping and severity.

*Gap.* There is no three-line wrap guarantee, no overlap rule, and nothing scrolls the first invalid field into view or announces it within 500ms. Both acceptance criteria are unimplemented.

#### `S27.04.03` Preserve logical reading order in adaptive panes - **PARTIAL**

*Evidence.* XAML order in the split pages is list then detail (ExerciseLibraryPage.xaml:38-360, RecipesPage.xaml:57-95, PlanTemplatesPage.xaml:18-88), which gives a sane default traversal, and headings carry SemanticProperties.HeadingLevel across 51 of 69 files so a screen reader can jump heading to heading (docs/accessibility/README.md:25-27).

*Gap.* No traversal behaviours or SemanticProperties ordering is applied to the panes, and nothing restores focus to the equivalent logical item when the size class changes, so AC2 is unimplemented. AC1 has never been checked - docs/design/accessibility-audit.md:22 records that TalkBack focus grouping could not be verified from this environment, and no tablet two-pane screen-reader run appears in docs/accessibility/sweep-evidence.md.

### F27.05 - Optimise one-handed reachability and thumb-safe actions

**Feature verdict: NOT-DONE** - `S27.05.01` NOT-DONE, `S27.05.02` NOT-DONE, `S27.05.03` NOT-DONE.

#### `S27.05.01` Create lower-third primary action slots - **NOT-DONE**

*Evidence.* There is no AdaptivePageScaffold in src/. Pages place primary actions inline inside scrolling stacks - EmptyState.xaml:30-35 centres its action in the card, ActiveWorkoutPage.xaml keeps actions in the page flow - and no page reserves a bottom action slot.

*Gap.* There is no bottom slot, no safe-area padding for one (see S27.03.01) and no measurement of action position, so AC1 and AC2 are unimplemented.

#### `S27.05.02` Separate destructive actions from primary thumb zones - **NOT-DONE**

*Evidence.* Requirement 3 is met - destructive labels name the action and the object, for example SemanticProperties.Description 'Delete this custom exercise' (ExerciseLibraryPage.xaml:319) - and several flows do confirm properly: ProfileSwitcherViewModel.cs:219, ImportDataViewModel.cs:65, DeleteMyDataPageViewModel.cs:42-46.

*Gap.* AC1 fails on a shipping screen. ExerciseLibraryPage.xaml:301-323 stacks Favourite, View, Edit and Delete in one column with SpaceXxs (2dp) spacing, far short of the required 24dp, and ExerciseLibraryViewModel.cs:397-406 executes the delete immediately with no confirmation surface. There is no ActionPlacementRules type and no test, so the rule is unenforced anywhere.

#### `S27.05.03` Add adaptive floating and sticky action behaviour - **NOT-DONE**

*Evidence.* No AdaptiveActionHost exists in src/, and no page uses a floating or sticky action - the only overlay in the app is the plate-calculator BottomSheet (ActiveWorkoutPage.xaml:341), which is a modal sheet rather than a persistent action bar.

*Gap.* There is no floating-to-sticky collapse, no last-item clearance rule and no reduced-motion path for the transition, so both acceptance criteria are unimplemented.

### F27.06 - Document adaptive layout rules and equivalence

**Feature verdict: PARTIAL** - `S27.06.01` NOT-DONE, `S27.06.02` PARTIAL, `S27.06.03` NOT-DONE.

#### `S27.06.01` Keep tablet and foldable layouts content-equivalent to phone - **NOT-DONE**

*Evidence.* One equivalence is genuinely protected: the exercise library's detail pane and ExerciseDetailPage render the same ExerciseGuidanceView, so phone and tablet cannot show different content (docs/design/tablet-layout.md:162-164).

*Gap.* The story's mechanism does not exist: AutomationId occurs zero times in the app, so no action inventory can be collected and no comparison test can be written; there is no exception file with owner and expiry. Equivalence is also unverified for the 20 of 50 feature pages that have no adaptive treatment at all - Today, Onboarding, Profile, Insights, Progress, Settings, Health, Security and Scanning - which docs/design/tablet-layout.md:228-233 acknowledges as unfinished. Both acceptance criteria are unimplemented.

#### `S27.06.02` Document adaptive layout rules and exceptions - **PARTIAL**

*Evidence.* docs/design/tablet-layout.md is thorough and honest: the decision and the two rejected alternatives (lines 11-27), where each piece lives (29-39), both breakpoint rules with the reason height is in the split test (41-63), the two problems and their answers (65-103), a copy-and-paste recipe for a page, a split page and a card grid (105-181), what AdaptiveHost publishes (183-193), typography and touch targets (195-226), an explicit list of pages this wave could not touch (228-278) and a verification status that names what was tested on which emulator and what still needs an iPad (280-294).

*Gap.* AC1 names four search terms and the document answers only two. 'Keyboard' and 'foldable' appear nowhere in it, and there is no lower-third or primary-action-placement rule; large text is covered only implicitly through the type scale. Each rule names AdaptiveLayoutMetricsTests as enforcement but nothing enforces the page-level recipe. AC2's exception file and documentation test do not exist.

#### `S27.06.03` Add adaptive regression snapshots for representative viewports - **NOT-DONE**

*Evidence.* No snapshot infrastructure exists anywhere in the repository: no baselines, no image-diff tooling in tools/, and no UI test project in tests/.

*Gap.* None of the four viewports is captured, no diff threshold is enforced and no text-scale variants exist, so both acceptance criteria are unimplemented. It shares the missing harness with S02.06.02.

---

## Scope and limits of this review

- **No build and no test run.** CI is green on `main` and seven sessions share this machine. Every verdict above is
  derived from reading source, not from executing it. Where a verdict turns on runtime behaviour I have said so.
- **No device.** Six shipped defects in this project were visible only on a device, and three of my PARTIAL verdicts
  (`S23.01.02`, `S27.04.03`, `S02.02.03`) would be settled quickly by one emulator session with TalkBack.
- **The counts are strict by design.** A story is DONE only when every acceptance criterion is met by reachable
  code. See "Why zero DONE" for the systemic reason none reached that bar, and the table beneath it for the twelve
  that are substantively complete.
