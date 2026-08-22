# E16-E20 verification: insights, coaching, recovery, notifications, gamification

Read-only reconciliation of the Forge backlog against the code, covering 5 epics, 25 features and
75 stories. Verdicts were reached by reading source, XAML and call graphs; no build or test run was
performed. Criteria that can only be settled by measurement on a device - frame timing, millisecond
budgets, text scaling, screen-reader announcement order - are named and excluded rather than
credited or failed.

## Counts

| Epic | Stories | DONE | PARTIAL | NOT-DONE | DEFERRED | Features | Epic |
| --- | --- | --- | --- | --- | --- | --- | --- |
| E16 Progress analytics | 15 | 0 | 9 | 6 | 0 | 5 PARTIAL | PARTIAL |
| E17 Adaptive coaching | 15 | 0 | 5 | 10 | 0 | 4 PARTIAL, 1 NOT-DONE | PARTIAL |
| E18 Recovery and readiness | 15 | 0 | 5 | 10 | 0 | 3 PARTIAL, 2 NOT-DONE | PARTIAL |
| E19 Notifications and habits | 15 | 0 | 9 | 6 | 0 | 4 PARTIAL, 1 NOT-DONE | PARTIAL |
| E20 Gamification | 15 | 0 | 9 | 5 | 1 | 5 PARTIAL | PARTIAL |
| **Total (105 items)** | **75** | **0** | **37** | **37** | **1** | 20 PARTIAL, 5 NOT-DONE | 5 PARTIAL |

Feature and epic verdicts roll up from their children: NOT-DONE only when every child is NOT-DONE,
otherwise PARTIAL. Five features are NOT-DONE in their entirety - F17.03 plateau detection, F18.03
soreness, F18.04 mobility and wind-down, F19.05 the notification centre, and F20.03 personal
challenges - and F17.03 and F18.03 are NOT-DONE despite containing finished, tested code.

No story in this range reached DONE. That is not a scoring convention: in every case where the
implementation was good, at least one written criterion was still unmet - usually a missing surface
(a filter, a preview, a detail view) rather than wrong arithmetic.

## What most deserves attention

### 1. Claimed-done but broken: soreness has no writer

`SorenessEntry` is a fully realised feature everywhere except the one place that matters. The entity
exists with `MuscleGroup`, `Level` and `RecordedOn`; `SorenessTracker.LatestForMuscle` and
`IsSeverelySore` are implemented and unit-tested; `RecoveryConfigurations.cs:26-30` maps the table;
the initial migration creates it; `ProfileStore` counts it for the data-areas screen and soft-deletes
it on erasure; and `CoachingDataService` reads it twice, at lines 44 and 73, feeding it into both
`ReadinessScore` and `NextSessionRecommender`.

Nothing in the application ever creates one. A search across `src/` excluding migrations for
`Repository<SorenessEntry>` or `new SorenessEntry` returns no results. There is no soreness screen,
and `MorningCheckInPage.xaml` captures only a single whole-body soreness integer on a different
entity.

The consequences run past its own story. `NextSessionRecommender.cs:35-48` refuses to load a muscle
marked severely sore - a genuine safety guardrail that can never fire. `ReadinessScore.cs:100-107`
takes the maximum of the check-in value and the per-muscle entries, so it always silently falls back.
Three stories fail on this single missing writer: **S18.03.01**, **S18.03.02** and, in part,
**S17.02.03**.

### 2. Claimed-done but broken: `Contraindications: []`

`CoachingDataService.cs:57` passes a hard-coded empty list into a recommender that fully understands
contraindications. `NextSessionRecommender.FindContraindication` (lines 126-131) matches an active
injury against the target exercise's primary and secondary muscles and returns `BlockedBySafety` with
copy naming the restriction. It is unreachable.

The data exists. `GoalWizardViewModel` collects `UserProfile.MovementLimitations` and
`ProfileStore.cs:430` persists it. `Forge.Domain.Training.ExerciseFilter.FromDeclaredInjuries` exists
to consume exactly this, and its only non-test caller is the exercise library, not coaching. So Forge
asks a user about their injuries during onboarding, stores the answer, and then generates load
recommendations that provably cannot consider it. **S17.02.03** and the AC3 that repeats across all
of E17 depend on this.

**The obvious fix does not work, and fails in the most misleading possible way.** Passing the
declared injuries straight into `Contraindications` compiles and blocks almost nothing, because the
two halves model injury differently:

- `ExerciseFilter.InjuryMovementExclusions` (`src/Forge.Domain/Training/ExerciseFilter.cs:50-61`) is
  keyed on **joints and regions** — knee, hip, lower back, back, shoulder, elbow, wrist, ankle, neck
  — and maps each to `MovementPattern` values.
- `NextSessionRecommender.FindContraindication` (lines 126-131) does a case-insensitive **exact
  match** of `MuscleGroup` against the exercise's `PrimaryMuscle` and `SecondaryMuscles`.

Against the 27 distinct muscle names in the 60-exercise catalogue
(`src/Forge.Infrastructure/Content/exercise-catalogue.json`), exactly **one of those nine keys
matches**: `lower back`. Three lose on the plural — `shoulder` vs `Shoulders`, `ankle` vs `Ankles`,
`hip` vs `Hips` — and `knee`, `elbow`, `wrist`, `neck` and bare `back` name no muscle at all.

Eight of nine silently doing nothing while the ninth quietly works is *worse* than uniformly dead: a
tester with a back injury sees a block and concludes the guardrail works. The join should be on
movement pattern, which is what `ExerciseFilter` already models.

### 3. Implemented, tested, and unreachable

Four substantial pieces of domain logic in this range have no caller at all. Two are worse than
uncalled: they are registered in DI, which makes them look wired.

| Type | State | Story |
| --- | --- | --- |
| `PlateauDetector` | `AddTransient` at `CoachingFeatureRegistration.cs:18`, injected nowhere | S17.03.01, S17.03.02 |
| `DeloadRecommender` | `AddTransient` at `CoachingFeatureRegistration.cs:19`, injected nowhere | S17.02.02 |
| `OvertrainingDetector` | complete, tested, zero references outside its own file and tests | S18.04.03 |
| `VolumeAggregator` | zero references outside its own file and tests | - |

`OvertrainingDetector` is the sharpest case: it already implements the two-signal threshold, the
cautious wording and the seek-professional-advice line that S18.04.03 asks for. The entire gap is the
absence of a caller.

### 4. Claimed-done but broken: an inert settings toggle

`NotificationSettingsPage.xaml` offers three category checkboxes, and it is genuinely reachable -
`SettingsPageViewModel.cs:17` lists it under Preferences. One of those checkboxes, **Meal
reminders**, writes a preference key (`NotificationSettingsPageViewModel.cs:39`) that no code reads -
`ReminderRefreshService.ReadPreferences` (lines 114-126) does not look at it, `ReminderKind` has no
meal member, and no meal candidate is ever built. Meanwhile the two categories the planner *does*
honour, `DailyCheckInEnabled` and `StreakProtectionEnabled`, have no UI at all.

The same page's own subtitle states the position plainly: *"These preferences only control local
reminder intent. Notification delivery is wired by the notification epic."* It is worth trusting that
sentence over the presence of the file.

### 4b. A coaching screen that opens on constants

`CoachingPage.xaml.cs` has no `OnAppearing`. The screen therefore renders `CoachingViewModel`'s field
initialisers until the user notices and presses "Load recommendation": *"Repeat current load"* and
*"Log a session to unlock progression"*. Those read as a recommendation. This is the same failure
mode as the hard-coded 60 kg x 8 target found elsewhere in this project - a plausible number on a
screen whose job is to give the user a number - and it costs one line to fix.

### 5. The whole notification engine has one entry point

`ReminderSchedulingPolicy` is good work - pure, priority-ordered, quiet-hours aware including
overnight wrap, DST-gap safe, with a named suppression reason per rejected candidate. It is reached
from exactly one place in the application: `StreaksPageViewModel.EnableRespectfulRemindersAsync`, a
button on the Consistency screen. Nothing refreshes reminders on launch, on plan change, or on
workout completion, and only the current local day is ever planned.

So S19.02.01's AC2 - moving a planned workout updates the reminder within five seconds - cannot
happen, and a workout planned for tomorrow is never scheduled today. Separately, notification taps
and actions do not exist: `NotificationTapped` and `NotificationActionTapped` have zero references,
which fails **S19.04.01** outright and takes the snooze half of **S19.02.05** with it. The
`ReturningData` payload is serialised onto every notification (`LocalNotificationScheduler.cs:186`)
and deserialised by nothing.

### 6. Two quieter defects worth a device check

- **Duplicate morning check-ins.** `CoachingDataService.SaveMorningCheckInAsync` always calls
  `AddAsync` (line 92), so saving twice in a day writes two rows for the same date, and
  `GetReadinessAsync` orders only by `Date` (line 71) and picks an arbitrary one. S18.02.01's
  "edit the current day until midnight" is not merely missing; attempting it corrupts the day.
- **Share card may be shared unflushed.** `AchievementsPage.xaml.cs:43-52` creates the PNG with
  `await using var file = File.Create(...)`, copies into it, and then calls `Share.RequestAsync`
  *before* that `using` scope ends. The stream is still open when the share sheet reads the file.
  This needs an emulator to confirm, but it is visible from the code.

### 7. Two independent notification caps, and the lower always wins

`ReminderSchedulingPolicy.Plan` suppresses at `input.Preferences.DailyNotificationCap` (line 141);
`LocalNotificationScheduler.CanScheduleMore` compares against the hard constant
`MaxNonCriticalNotificationsPerLocalDay = 4` (line 239) and never reads the preference. The effective
cap is therefore `min(preference, 4)` — lowering the preference binds at the planning stage, raising
it above 4 is silently ignored at the scheduling stage.

In the shipped app the asymmetry is latent rather than observable, because **nothing writes the
preference**: `DailyCap` has exactly one reference in `src/`, the read at
`ReminderRefreshService.cs:121`. There is no cap control on the settings page, so the value is always
the default. That is the meal-toggle defect inverted — a preference with a reader and no writer,
sitting beside a preference with a writer and no reader.

The fix this implies is not "make the scheduler read the setting"; it is to decide which layer owns
the cap, since two caps in different layers will drift again.

## Where the backlog is wrong, not the code

Five criteria in this range should be corrected rather than implemented.

1. **S20.01.01 / S20.01.02 / S20.02.01 - daily streaks, freezes, volume and PR badges.**
   `docs/design/engagement-ethics.md` removes all four deliberately and argues the case at length: a
   counter that falls when you recover teaches that recovering is a loss; a limited supply of
   forgiveness runs out on the person who needed it most; a cumulative-kilogram badge rewards junk
   volume; a PR badge rewards attempting a maximal single. The removal is enforced rather than
   documented - `Streak` has no `CurrentDays`, `EngagementMetrics` omits total volume and PR counts
   entirely so such a rule cannot be written by accident, and tests assert both by reflection.
   S20.01.02 is graded **DEFERRED** on that basis. S20.01.01 and S20.02.01 are graded PARTIAL because
   parts of them are satisfied in the weekly form, but their day-level and category criteria should
   be rewritten.

2. **S16.01.02 AC2 - the 12-rep cutoff.** The backlog specifies 12; `OneRepMaxEstimator.cs:23-30`
   uses 10 and explains why (the Epley and Brzycki fits coincide at ten and diverge past it, so
   beyond ten the divergence exceeds any useful precision). The code is better reasoned. Correct the
   criterion.

3. **S18.02.02 contradicts itself.** R1 specifies 35 percent sleep / 25 load / 25 wellbeing /
   15 heart rate; R6, in the same requirement list, specifies 35 sleep / 25 energy / 20 inverse
   soreness / 10 inverse stress. Both cannot be satisfied. This needs reconciling before anyone
   changes `ReadinessScore`, whose current weights (30/25/15/15/10/5) match neither.

4. **S16.03.01 AC2 - muscle mapping weights.** The criterion assumes exercises carry per-muscle
   volume weights that reconcile to the session total within 0.1 percent. Forge's model attributes
   full volume to the primary muscle and to each secondary (`TrainingTrendAggregator.cs:197-202`) and
   says so on screen: *"Working volume attributed to every muscle an exercise trains."* That is a
   defensible model, but it cannot reconcile, and no weight field exists on `Exercise` to make it.
   Decide which model is intended before failing the story.

5. **S16.05.03 - analytics caching.** `InsightsDataService.cs:26-30` argues explicitly against it:
   *"Nothing is cached. Someone who finishes a workout and opens Progress expects to see that
   workout, and a cache short-lived enough to guarantee that would be too short-lived to save any
   work."* Graded NOT-DONE rather than DEFERRED because that reasoning lives in a code comment, not
   in a doc or an ADR. If the decision is real it should be recorded in `docs/` and the story retired;
   if it is not, the read-every-row-and-aggregate approach is the scaling risk this story existed to
   prevent.

## What could not be decided by reading

These criteria were excluded from the verdicts that mention them rather than being credited or failed:

- **Frame and render budgets.** S16.01.01 AC1, S16.02.02 AC1, S16.03.01 AC3, S16.03.02, S16.04.01,
  S16.04.03, S18.01.03 AC2, S19.05.01 AC3, S20.04.01 AC3 - all specify 300 ms renders, 60 fps pans or
  16.6 ms frame ceilings on a Pixel 6a. Two of these were still decidable on structure alone:
  S16.02.02 and S20.04.01 both require `dx:DXCollectionView` virtualisation and both use a
  `BindableLayout` inside a `ScrollView`, so the mechanism the criterion depends on is absent
  regardless of what a profiler would say.
- **Millisecond interaction budgets.** The recurring "empty state appears within 400 ms" and
  "recalculates within 300 ms" clauses across E16 and E18.
- **Screen-reader announcement order.** S16.05.02 AC2, S18.02.01 AC2 and S20.02.04 AC3. Where the
  structure made the answer clear it is stated - the achievement card exposes one composed
  `AccessibleDescription` rather than an ordered sequence of stops - but the announcement itself needs
  TalkBack or VoiceOver.
- **Badge evaluation timing.** S20.02.01 AC3 (500 events in under 500 ms).
- **The share-card stream defect** in S20.04.02 is a code-visible risk, but whether the PNG actually
  arrives truncated depends on platform buffering and needs an emulator.

## Cross-cutting observations

- **There is no `Forge.App.Tests` project.** `tests/` contains `Forge.Core.Tests`,
  `Forge.Domain.Tests` and `Forge.Infrastructure.Tests` only. Every gap in this report that takes the
  form "the domain logic is right but nothing calls it" sits precisely in the untested layer. The
  green domain suite is not evidence about reachability, and in this range it repeatedly was not.
- **The honest-uncertainty discipline is real and mostly holds.** `SparseDataPolicy` refuses to draw
  a chart it would not describe in words; `SmoothedTrend` refuses a trend claim below the sample
  threshold; `TrainingLoadCalculator` carries a caveat about contested evidence;
  `SleepPerformancePairing` refuses to pair rest days and explains that doing so would manufacture the
  finding. The one place it slips is
  `SleepPerformanceAssociationAnalyzer.MinimumSampleSize = 8` against a backlog requirement of 20-21
  paired samples - Forge will state a sleep-performance association from eight paired days
  (**S18.05.01**).
- **Five features have no code whatsoever**: progress photos, nutrition adherence trends, chart
  export, volume landmarks, and personal challenges. F20.03 is empty in its entirety - `Challenge`
  has zero matches across `src/`, `tests/` and `docs/`.

## Per-story reasoning

### E16 - Progress Analytics, Charts and Personal Records

**Epic verdict: PARTIAL.** Progress and Insights are real, reachable screens built from logged sets: src/Forge.App/Features/Progress/ProgressPage.xaml renders weekly volume, weekly mean load and consistency from src/Forge.Domain/Analytics/TrainingTrendAggregator.cs and ConsistencyAnalyzer.cs; src/Forge.App/Features/Insights/InsightsPage.xaml adds muscle-group and movement-pattern breakdowns plus a sleep association card; ExerciseProgressPage, PersonalRecordsPage and BodyMetricsPage exist and are routed from InsightsFeatureRegistration.cs:40-43 and reachable from ProgressViewModel.cs:26-31. SparseDataPolicy.cs gates every chart behind a 4-point minimum and explains itself in words.

_Gaps:_ Five of fifteen stories have no implementation at all: period comparison (S16.01.03), the insights feed (S16.02.03), progress photos (S16.04.02), nutrition adherence trends (S16.04.03) and chart export (S16.05.01). No dx:DateEdit range selection exists anywhere in analytics, so every 'selectable date range' criterion in F16.01 is unmet. No dx:PieChartView is used outside Nutrition. TrainingLoadCalculator computes an acute:chronic ratio but no training-load screen exists, so the ACWR formula and its caveat never reach a user as F16.03 requires. Personal records are capped at 30 rows (InsightsDataService.cs:207) and rendered in a non-virtualised BindableLayout.

#### F16.01 - Show strength progression and estimated max trends

**PARTIAL.** src/Forge.App/Features/Insights/ExerciseProgressPage.xaml:41-53 charts estimated one-rep max as a dx:LineSeries, with a formula note, an exclusion note and a sparse-data explanation supplied by InsightsDataService.BuildExerciseProgress (src/Forge.App/Features/Insights/Services/InsightsDataService.cs:425-483). OneRepMaxEstimator.cs implements both Epley and Brzycki with a documented 10-rep ceiling.

_Gaps:_ There is no exercise picker: BuildExerciseProgress silently charts whichever exercise has the most estimable sets (InsightsDataService.cs:436-441). There is no date-range selection, no dx:DateEdit and no range presets. Only one series is plotted (estimated 1RM); best weight and total volume are absent. Period comparison does not exist at all.

##### S16.01.01 - Plot exercise strength history with selectable date ranges

**PARTIAL.**

*Evidence.* ExerciseProgressPage.xaml:41-53 draws a dx:ChartView LineSeries bound to ExerciseProgressViewModel.EstimatePoints; ExerciseProgressViewModel.cs:83-88 sets HasData/IsEmpty/ShowChart from SparseDataPolicy.Evaluate, and the page shows controls:EmptyState with a concrete 'Go to training' action when empty (ExerciseProgressPage.xaml:30-35). Below the chart threshold the points are listed as text instead (lines 63-74), which is a genuinely better answer than a two-point line.

*Gaps.* R1 unmet: no range presets for 4 weeks, 12 weeks, 6 months, 1 year or custom dates exist anywhere on the page, and no exercise can be selected - InsightsDataService.cs:436-441 hard-picks the most-logged exercise. R1 also unmet for series: only estimated 1RM is charted; best weight and total volume are not. R2 and AC3 unmet: no dx:DateEdit is used in any analytics screen (the only DateEdit in src/ is Onboarding/GoalWizardPage.xaml:132), so there is no end-before-start validation and no Apply button. AC2 partially unmet: the empty state fires at zero points and does not state a two-session minimum; the sparse copy names four points, not two sessions. AC1 (300 ms render, 60 fps pan on a Pixel 6a) cannot be judged by reading and is left out of this verdict.

##### S16.01.02 - Explain estimated one-rep max with named formulas

**PARTIAL.**

*Evidence.* src/Forge.Domain/Training/OneRepMaxEstimator.cs:56-66 implements Epley as w*(1+r/30) and Brzycki as w*36/(37-r) and returns null outside 1..10 reps, so out-of-range sets are excluded rather than estimated badly. ExerciseProgressViewModel.cs:90-96 renders a formula note naming the formula and stating that error grows with repetitions, plus an exclusion note counting the sets left out and explaining why. Every record row on PersonalRecordsPage carries an 'Estimate' badge and a caveat (PersonalRecordsViewModel.cs:60-62).

*Gaps.* AC3 unmet: the formula is fixed to InsightsDataService.cs:208 DefaultFormula = Epley and there is no user-facing switch, so Brzycki is unreachable despite being implemented. AC4 partially unmet: the screen names the formula but never shows the equation text itself, and there is no formula-explainer surface. R2 unmet: there is no per-exercise opt-in for high-rep estimates - the cut-off is unconditional. Backlog defect: R2/AC2 specify a 12-rep cutoff, but OneRepMaxEstimator.cs:23-30 sets 10 and documents why (the Epley/Brzycki fits coincide at ten and diverge past it); the code's choice is the better one and the criterion should be corrected rather than the code.

##### S16.01.03 - Compare current progress against previous periods

**NOT-DONE.**

*Evidence.* Searched the whole tree for a period-comparison surface: no PeriodComparisonService, no ComparisonRangeSelector, and no comparison card in ProgressPage.xaml, InsightsPage.xaml or ExerciseProgressPage.xaml. No screen offers a selectable range at all, so there is nothing for a preceding range of equal length to be derived from. ProgressViewModel exposes only cumulative weekly series (src/Forge.App/Features/Progress/ViewModels/ProgressViewModel.cs:41).

*Gaps.* All of R1-R3 and AC1-AC2 are unimplemented: no overlay of two periods, no absolute/percentage change cards for estimated max, weekly volume or session count, and no 'no prior data' message because there is no baseline concept in the code.


#### F16.02 - Detect personal records and surface notable insights

**PARTIAL.** src/Forge.Domain/Analytics/PersonalRecordDetector.cs detects four record types and is wired into a reachable screen through InsightsDataService.BuildPersonalRecords (InsightsDataService.cs:485-507) and PersonalRecordsPage.xaml. Each row names the set and date that produced it, and estimated records are visibly marked.

_Gaps:_ Timed-duration records are not detected. There is no PR history filtering, no drill-down detail, and no previous-best or improvement figure. The insights feed does not exist in any form.

##### S16.02.01 - Detect personal records across multiple PR types

**PARTIAL.**

*Evidence.* PersonalRecordDetector.DetectAll (src/Forge.Domain/Analytics/PersonalRecordDetector.cs:29-46) detects heaviest load, estimated one-rep max, most reps at a load and greatest session volume, all excluding warm-ups via IsWorkingSet (line 130). Records are recomputed from the live set table on every screen load (InsightsDataService.cs:273-280), so a corrected or deleted set is reflected on the next open - AC2 is satisfied, by recomputation rather than by a stored projection.

*Gaps.* R1 unmet: timed-duration records are not detected; only four of the five listed types exist. R2 unmet: detection does not run after a workout is saved - there is no PersonalRecordProjection and no write-time hook; it runs lazily when the Personal Records screen opens. AC3 unmet: a first-ever performance is rendered with the same wording as any other record (InsightsDataService.cs:527-534); nothing labels it a first record.

##### S16.02.02 - Show personal record history and drill-down details

**PARTIAL.**

*Evidence.* src/Forge.App/Features/Insights/PersonalRecordsPage.xaml lists each record with its type, exercise, headline figure, the source set, the date, and a per-row SemanticProperties.Description built in PersonalRecordsViewModel.cs:93-94. Default ordering is newest first (InsightsDataService.cs:496).

*Gaps.* R1 unmet: there is no filtering by exercise, PR type or date range. R2 unmet: no detail view exists, and previous best, absolute improvement and percentage improvement are never computed or shown - PersonalRecordDisplay (InsightsDataService.cs:167-173) has no baseline field. R3 and AC1 structurally unmet: the list is a BindableLayout inside a ScrollView, not the dx:DXCollectionView the criterion names, so nothing is virtualised; and InsightsDataService.cs:207 caps output at MaximumRecordsShown = 30, so a user with 1,000 PR events can only ever see 30. AC2 unmet with AC1: no percentage improvement is shown for any record, so the zero-baseline case cannot be distinguished.

##### S16.02.03 - Build an insights feed for notable progress changes

**NOT-DONE.**

*Evidence.* There is no InsightGenerator and no feed anywhere in src/. src/Forge.App/Features/Insights/InsightsPage.xaml is a breakdown hub - consistency, muscle-group volume, movement-pattern volume, a sleep association card and three navigation buttons - with no per-insight rows, no source/comparison metadata and no dismiss affordance. InsightsViewModel.cs contains no dismissal state and nothing persists a dismissal.

*Gaps.* All of R1-R3 unmet: no PR, streak, 15-percent volume change or body-trend insights are generated as feed items; no dismiss action exists, so AC3 (dismissal surviving restart) has nothing to test; and the empty state on InsightsPage.xaml:34-39 shows a single generic message rather than three examples of what will unlock.


#### F16.03 - Analyse volume, consistency and training load

**PARTIAL.** TrainingTrendAggregator.PerWeekByMuscleGroup and PerWeekByMovementPattern feed dx:BarSeries charts plus parallel text lists on InsightsPage.xaml:54-93 and 141-177. ConsistencyAnalyzer.cs is a substantial weekly adherence implementation wired through InsightsDataService.BuildConsistency (line 351-362) into both Progress and Insights.

_Gaps:_ No training-load screen exists, so the acute:chronic ratio computed by TrainingLoadCalculator.cs never reaches a user with its formula or caveat. dx:PieChartView is not used for muscle distribution. Muscle volume is attributed in full to every muscle an exercise trains rather than split by mapping weights.

##### S16.03.01 - Visualise weekly training volume by muscle group

**PARTIAL.**

*Evidence.* InsightsPage.xaml:54-93 renders a dx:BarSeries of volume by muscle group backed by TrainingTrendAggregator.PerWeekByMuscleGroup (InsightsDataService.cs:259), with the same values repeated as an accessible text list beneath it and the chart marked AutomationProperties.IsInAccessibleTree=False. Muscle coverage is data-driven from the exercise catalogue via PrimaryMuscle plus SecondaryMuscles (TrainingTrendAggregator.cs:197-202), and the chart is only drawn once SparseDataPolicy allows it.

*Gaps.* R3 unmet: no dx:PieChartView is used for proportional distribution anywhere in Insights (the only PieChartView in src/ is Features/Nutrition/NutritionPage.xaml). AC2 unmet: TrainingTrendAggregator.cs:197-202 attributes the whole of a set's volume to the primary muscle and to every secondary muscle, so no mapping weight is applied and the group totals deliberately sum to more than the session total - they cannot reconcile within 0.1 percent. AC1 percentages therefore describe attributed volume, not a share of counted sets. AC3's 60 fps requirement cannot be judged by reading and is left out of this verdict.

##### S16.03.02 - Track consistency and programme adherence

**PARTIAL.**

*Evidence.* src/Forge.Domain/Analytics/ConsistencyAnalyzer.cs computes completed training days per calendar week against the active plan's weekly target, excludes the running week from adherence, credits each week at most its target, and returns HasAdherenceClaim = false when no plan defines a target (line 77). InsightsDataService.WeeklySessionTarget (line 380-388) returns zero rather than inventing a default target, and documents why. ProgressPage.xaml:50-83 renders the weekly session bars, a plain-language headline and detail, and a values list when the chart is too sparse to draw.

*Gaps.* R2 partially unmet: a week with no planned sessions is handled by suppressing the adherence claim entirely (HasAdherenceClaim, ConsistencyAnalyzer.cs:77) rather than by labelling that week 'unplanned' in the weekly list, so AC2's per-week labelling is not visible. R3 unmet: the window is not a 12-week rolling one - ConsistencyAnalyzer measures from the first logged session onward (ConsistencyAnalyzer.cs:147) and ProgressViewModel binds every week it returns, with no rolling bound. AC1 unmet in two specific ways, both visible at ProgressViewModel.cs:208-213: the adherence percentage is rounded to a whole number with the `{percent:0}` format, so 66.7 percent renders as '67%', and the raw counts shown beside it are weeks-on-target ('2 of 3 full weeks on target'), not the session counts the criterion asks for.

##### S16.03.03 - Show training load with honest limitations

**PARTIAL.**

*Evidence.* src/Forge.Domain/Analytics/TrainingLoadCalculator.cs:23-49 implements a 7-day acute over 28-day chronic ratio, returns null when chronic load is zero, and carries EvidenceCaveat stating that ACWR has weak, contested evidence for individual injury prediction. Its one caller is CoachingDataService.LoadTrainingLoadAsync (src/Forge.App/Features/Coaching/Services/CoachingDataService.cs:96-101), which feeds it into ReadinessScore; the ratio and its caveat therefore surface on the Readiness screen as one component line (ReadinessScore.cs:97, ReadinessViewModel.cs:37-41).

*Gaps.* R1 unmet: there is no load screen and no weekly tonnage chart - the only place the ratio appears is as a readiness component, and the ACWR formula text is never shown. R2 unmet: TrainingLoadCalculator gates on chronic volume being non-zero, not on 28 days of history existing, so a user with a single week of data still gets a ratio; AC2's '28 days are required' message does not exist. AC1 unmet: the ratio is displayed to 2 dp inside a readiness component sentence with no visible formula. The caveat text itself does satisfy R3 wherever the component is shown.


#### F16.04 - Present body metrics, photos and nutrition adherence trends

**PARTIAL.** src/Forge.App/Features/Insights/BodyMetricsPage.xaml and BodyMetricsViewModel.cs render a smoothed body-weight trend built by SmoothedTrend.Build (InsightsDataService.cs:404-423), with the window size stated, a partial-window note and a refusal to claim a trend below the sample threshold.

_Gaps:_ Progress photos do not exist in any form and nutrition adherence trending does not exist in any form; two of this feature's three stories have no code.

##### S16.04.01 - Plot body metrics with moving averages

**PARTIAL.**

*Evidence.* InsightsDataService.BuildBodyMetrics (lines 404-423) averages same-day entries and passes them to SmoothedTrend.Build, which defaults to MovingAverage.DefaultWindowSize = 7 (src/Forge.Domain/Analytics/MovingAverage.cs:12), so AC1's seven-day arithmetic holds and R1's bodyweight default is met. BodyMetricsViewModel.cs:85-94 shows 'Smoothed view - N-day moving average', a partial-window note naming how many leading points are averaged over fewer entries, and either a trend sentence or an explicit refusal to claim one (TrendDirection.NoClaim). Below SparseDataPolicy's threshold the entries are listed as values instead of drawn, with the reason stated (lines 79-86).

*Gaps.* R2, R6 and AC3 unmet: there is no 'Show daily values' toggle and raw daily weight is not hidden by default - BodyMetricPointViewModel.From (lines 133-141) always composes 'X kg recorded - Y kg averaged' into the visible Detail string, so the raw value is on screen from the first load and the anxiety this requirement exists to prevent is not prevented. R3 unmet: only body weight is charted; circumference measurements and body-fat percentage are neither read nor plotted, and R1's weekly-point default for measurements has nothing to apply to. AC2 unmet: the screen never states that seven entries are needed for a moving average - the sparse copy names SparseDataPolicy's four-point chart threshold instead, so the number the user is told does not match the number that governs the average.

##### S16.04.02 - Compare progress photos privately on device

**NOT-DONE.**

*Evidence.* There is no progress-photo feature. MediaPicker is referenced nowhere in src/, there is no ProgressPhoto entity, repository, EF configuration or migration column, and no page or route for photo capture or comparison. src/Forge.App/Services/Media contains media-pack code for exercise assets only.

*Gaps.* All of R1-R5 and AC1-AC3 unimplemented: no camera or gallery capture, no side-by-side comparison with dates, no share-confirmation step, and no permission-denial path.

##### S16.04.03 - Trend nutrition adherence beside body changes

**NOT-DONE.**

*Evidence.* No nutrition adherence trend exists. Searched for NutritionAdherence, AdherenceService and any nutrition series on an analytics screen: nothing. ProgressPage.xaml and InsightsPage.xaml contain no nutrition card, and InsightsDataService reads no nutrition entities for either screen (LoadProgressAsync line 221, LoadInsightsAsync line 239).

*Gaps.* All of R1-R3 and AC1-AC3 unimplemented: no 7/28/90 day adherence percentages against calorie or protein bands, no body-metric overlay with a 14-day gate, and no association-not-causation statement on a nutrition surface.


#### F16.05 - Export progress visuals and handle low-data states

**PARTIAL.** Empty and sparse handling is a genuine strength of this epic: src/Forge.Domain/Analytics/SparseDataPolicy.cs decides per series whether a chart may be drawn and returns the sentence to show, and every analytics screen consumes it (ProgressViewModel, InsightsViewModel, ExerciseProgressViewModel, BodyMetricsViewModel), falling back to listing values rather than drawing a two-point line.

_Gaps:_ Chart export does not exist at all, and no analytics aggregate is cached or projected - InsightsDataService.cs:26-30 states that nothing is cached, by choice.

##### S16.05.01 - Export any chart as a shareable image

**NOT-DONE.**

*Evidence.* No chart export exists. There is no ChartExportService, no IChartExportRequest, no IScreenshot use, and no Export action on ProgressPage.xaml, InsightsPage.xaml, ExerciseProgressPage.xaml or BodyMetricsPage.xaml. The only CaptureAsync in src/ is the achievement share card at src/Forge.App/Features/Engagement/AchievementsPage.xaml.cs:35, which is not a chart.

*Gaps.* All of R1-R3 and AC1-AC3 unimplemented: no PNG generation from a chart, no export preview, no cancel-and-delete path, and no guarantee that a formula caveat travels into a shared image.

##### S16.05.02 - Design purposeful empty and sparse analytics states

**PARTIAL.**

*Evidence.* src/Forge.Domain/Analytics/SparseDataPolicy.cs defines Empty, TooSparse and Ready explicitly with a required point count, and each view model maps them onto IsEmpty/ShowValues/ShowChart (for example ExerciseProgressViewModel.cs:83-88, BodyMetricsViewModel.cs:77-86). Every analytics screen renders a controls:EmptyState with a headline, an explanatory message and a single primary action, and the sparse case shows the values as text with a sentence naming how many more entries are needed (SparseDataPolicy.cs:89-99). No screen leaves a blank chart or a permanent spinner: SkeletonPlaceholder is bound to IsLoading and IsLoading is cleared in a finally block.

*Gaps.* R4 partially unmet: the sparse sentence names the threshold, but the empty-state copy usually does not. ExerciseProgressPage.xaml:33 describes a repetition-range constraint rather than a data threshold, ProgressPage.xaml:37 and PersonalRecordsPage.xaml:28 name no minimum, and InsightsPage.xaml:37 names none either - so a user at zero data is told what will happen but not how much data unlocks it. AC2 (screen-reader announcement order) and AC3 (400 ms) cannot be judged by reading and are left out of this verdict.

##### S16.05.03 - Cache analytics aggregates for responsive dashboards

**NOT-DONE.**

*Evidence.* There is no analytics projection or cache. No AnalyticsProjectionDbSet, no AnalyticsProjectionRebuilder and no projection table in the EF configurations or migrations. src/Forge.App/Features/Insights/Services/InsightsDataService.cs:26-30 states the opposite policy explicitly: 'Nothing is cached. Someone who finishes a workout and opens Progress expects to see that workout, and a cache short-lived enough to guarantee that would be too short-lived to save any work.' Every screen reads the whole owned table and aggregates in memory on a background thread (ReadAsync, line 304-319).

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented: no rebuildable weekly projections, no post-write rebuild, no rebuild timing guarantee. Candidate backlog defect: the code documents a deliberate rejection of caching with a reasoned argument, but that reasoning lives in a code comment rather than in an ADR, so this cannot be graded DEFERRED as it stands. If the decision is genuine it should be recorded in docs/ and this story retired; if not, the read-everything-and-aggregate approach is the scaling risk this story existed to prevent.


### E17 - Adaptive Coaching and On-Device Intelligence

**Epic verdict: PARTIAL.** A reachable coaching surface exists: src/Forge.App/Features/Coaching/CoachingPage.xaml is routed at CoachingFeatureRegistration.cs:28 and opened from TrainViewModel.cs:40, and it renders a bounded, explainable next-set recommendation produced by src/Forge.Domain/Coaching/NextSessionRecommender.cs via CoachingDataService.GetNextSessionRecommendationAsync. The recommender caps session-to-session load increase at 5 percent with a stated rationale (NextSessionRecommender.cs:10-11, 113-124), carries a medical disclaimer on every result, and implements both an injury contraindication block and a severe-soreness block.

_Gaps:_ This epic is the clearest concentration of implemented-but-unreachable logic in the range. PlateauDetector and DeloadRecommender are registered in DI at CoachingFeatureRegistration.cs:18-19 and injected nowhere - plateau detection, plateau interventions and deload recommendations have no UI at all. The injury guardrail cannot fire because CoachingDataService.cs:57 passes Contraindications: [] and the only injury data Forge collects, UserProfile.MovementLimitations, is written by onboarding and read by no coaching code. The soreness guardrail cannot fire because nothing in the app ever writes a SorenessEntry. Morning check-in data never reaches the recommender, so bad-day autoregulation does not exist. Overrides store nothing (CoachingViewModel.cs:33). Volume landmarks, form-check reminders, weekly reviews, habit nudges and goal forecasting have no code.

#### F17.01 - Recommend next workout loads and reps transparently

**PARTIAL.** NextSessionRecommender.Recommend (src/Forge.Domain/Coaching/NextSessionRecommender.cs:15-111) selects RPE-autoregulated or double progression depending on whether reps-in-reserve was recorded, caps the increase at 5 percent, and returns an explanation naming the latest set and the rule's own reason. CoachingPage.xaml:20-30 shows the load, the set-and-rep prescription, the explanation and the disclaimer.

_Gaps:_ Rep targets are derived from the last logged set rather than from the programmed range, back-off sets do not exist, and the override action stores nothing. The equipment-specific 2.5 kg / 5 kg caps of R2 are not implemented - only a flat 5 percent.

##### S17.01.01 - Suggest next-workout weight from recent sets and RPE

**PARTIAL.**

*Evidence.* CoachingDataService.GetNextSessionRecommendationAsync (src/Forge.App/Features/Coaching/Services/CoachingDataService.cs:19-61) reads the most recent 12 working sets for the active profile, materialising before ordering because SQLite cannot order a DateTimeOffset, and hands the matching exercise's history to NextSessionRecommender. CapIncrease (NextSessionRecommender.cs:113-124) enforces the 5 percent bound and appends the reason to the trace, satisfying AC4. The explanation is a full sentence naming the load, the reps and the reps-in-reserve behind it (lines 96-98), and the card shows the medical disclaimer, satisfying R6's wording.

*Gaps.* AC3 is structurally impossible: CoachingDataService.cs:57 passes Contraindications: [] into a recommender that does implement contraindication blocking (NextSessionRecommender.cs:126-131), and no code path ever populates it from UserProfile.MovementLimitations, so a declared knee injury can never affect a recommendation. AC5 unmet: CoachingViewModel.OverrideCommand (CoachingViewModel.cs:33) only sets a status string - no override value, reason or timestamp is written anywhere. AC2 diverges: with no history the recommender returns InsufficientData asking the user to repeat the current load, whereas the criterion requires suppressing the recommendation and naming a two-session minimum. R2 unmet: the cap is a flat 5 percent with no upper-body / lower-body distinction and no absolute kilogram bound. R7's 15 percent decrease bound is not enforced anywhere. Separately worth fixing: CoachingPage.xaml.cs has no OnAppearing, so the screen opens showing CoachingViewModel's initialiser constants - 'Repeat current load' and 'Log a session to unlock progression' - until the user finds and presses 'Load recommendation'. Those strings are indistinguishable from a real recommendation for a user who does not press the button.

##### S17.01.02 - Recommend rep targets and back-off sets

**PARTIAL.**

*Evidence.* ProgressionModel supplies TargetRepsMin, TargetRepsMax and SetCount through NextSessionRecommender (NextSessionRecommender.cs:100-110), and CoachingViewModel.cs:39 renders them as 'N sets x a-b reps'. The rule's own reason string is included in the explanation, so the user is told why the prescription moved.

*Gaps.* R1 unmet: the rep range is not the programmed range. CoachingDataService.cs:51-52 constructs it from the last logged set as (latest.Repetitions - 2) to latest.Repetitions, so the target follows what the user happened to do rather than what the plan prescribes, and no AMRAP or test-set opt-in exists. R2 and AC2 unmet: there is no back-off set concept anywhere in Forge.Domain.Coaching - no 5-15 percent reduction from a top set and no top-set ceiling. R3 unmet: nothing labels the suggestion as progression, repeat, back-off or regression; CoachingViewModel.cs:44 sets Status to only 'Recommendation loaded' or 'Safety blocked'. AC3 and AC5 unmet for the same reasons as S17.01.01 (empty contraindications, no stored override).

##### S17.01.03 - Allow user overrides with stored reasoning

**NOT-DONE.**

*Evidence.* The only override affordance is CoachingPage.xaml:26-27, bound to CoachingViewModel.OverrideCommand, which is a RelayCommand whose entire body sets a status string: 'Override chosen - log what you actually do so future coaching reflects reality.' (src/Forge.App/Features/Coaching/ViewModels/CoachingViewModel.cs:33). Nothing is persisted, no entity exists to persist it to, and the recommendation never changes a workout in the first place - the Coaching screen is read-only and does not write to the active session.

*Gaps.* R1 unmet: there is no Accept, Adjust or Dismiss - one button that changes a label. R2 unmet: there is no adjusted value, so no bounds re-validation exists and AC2's blocked-change path cannot occur. R3 and R5 unmet: no override reason is offered or stored. AC1 unmet: the workout never receives an adjusted value from this screen.


#### F17.02 - Autoregulate sessions for bad days and fatigue

**PARTIAL.** NextSessionRecommender.cs:19-48 implements two safety blocks ahead of any progression arithmetic - an injury contraindication block naming the muscle group and reason, and a severe-soreness block - both returning BlockedBySafety with IsOverridable = true and a medical disclaimer, and CoachingViewModel.cs:44 surfaces 'Safety blocked' on the card.

_Gaps:_ Both blocks are unreachable in the shipped app: contraindications are hard-coded empty and no code writes a SorenessEntry. Bad-day autoregulation from the morning check-in does not exist, and the deload recommender has no caller.

##### S17.02.01 - Adjust recommendations for bad-day check-ins

**NOT-DONE.**

*Evidence.* The four bad-day inputs are captured - MorningCheckIn.cs holds Energy, Soreness, Motivation and Stress on 1-5 scales and MorningCheckInViewModel saves them - but they never reach coaching. CoachingDataService.GetNextSessionRecommendationAsync (CoachingDataService.cs:19-61) reads only SetEntry, Exercise and SorenessEntry; it never queries MorningCheckIn, and NextSessionRecommendationRequest (src/Forge.Domain/Coaching/CoachingModels.cs:18-30) has no field for a check-in or a readiness score. The check-in feeds ReadinessScore only (CoachingDataService.cs:63-78), which is a separate screen that produces a number and no prescription change.

*Gaps.* R2 and AC1 unmet: there is no combined 1-20 score, no 8-or-lower threshold, and no 2.5-10 percent load reduction or 1-3 set volume reduction rule anywhere in the domain. R3 and AC2 unmet: there is no adjustment to ignore, so no ignored state is stored. R5 unmet: no override reasoning is stored. The consequence is that a user who reports energy 2, soreness 2, motivation 2 and stress 2 in the morning is given exactly the same load recommendation as one who reports 5s.

##### S17.02.02 - Recommend deloads from fatigue and performance decay

**NOT-DONE.**

*Evidence.* src/Forge.Domain/Coaching/DeloadRecommender.cs is a complete, tested implementation - it triggers on an acute:chronic ratio at or above 1.5 or performance decay at or above 8 percent, delegates the load arithmetic to ProgressionModel.Deload, and returns reasons plus the medical disclaimer. It has exactly one reference outside its own file and its unit tests: services.AddTransient<DeloadRecommender>() at src/Forge.App/Features/Coaching/CoachingFeatureRegistration.cs:19. Nothing resolves it. ICoachingDataService (src/Forge.App/Features/Coaching/Services/ICoachingDataService.cs) exposes only GetNextSessionRecommendationAsync, GetReadinessAsync and SaveMorningCheckInAsync, and no XAML in the repository binds a deload card.

*Gaps.* No user can reach a deload recommendation. R1 is also unmet in the logic itself: the trigger requires only one signal, not two of the listed set, and there is no three-week minimum-data gate, so AC1's two-signal requirement and AC2's '3 weeks are required' message do not exist. R2's 30-50 percent weekly volume reduction is not implemented - only a 10 percent load reduction.

##### S17.02.03 - Apply injury-aware safety guardrails to all coaching

**PARTIAL.**

*Evidence.* The guardrail logic is written and correct: NextSessionRecommender.FindContraindication (lines 126-131) matches an active injury against the target exercise's primary and secondary muscles and returns BlockedBySafety with copy naming the muscle group and the reason without repeating free-text notes; SorenessTracker.IsSeverelySore blocks direct loading at level 5 (NextSessionRecommender.cs:35-48). Every recommendation carries ReadinessScoreResult.DefaultMedicalDisclaimer and an override note, and CoachingPage.xaml:25 and 28 render both, satisfying the persistent not-medical-advice part of R3.

*Gaps.* R1 and R2 unmet in practice, and this is the most consequential finding in E17. CoachingDataService.cs:57 passes Contraindications: [] - a hard-coded empty list - so the injury block can never fire. The app does collect injury data: GoalWizardViewModel writes UserProfile.MovementLimitations and ProfileStore.cs:430 persists it, but no coaching code reads it, and Forge.Domain.Training.ExerciseFilter.FromDeclaredInjuries has no caller outside tests. The soreness block is equally unreachable: SorenessEntry is read at CoachingDataService.cs:44 and 73, configured in RecoveryConfigurations.cs and counted for profile deletion, but a search of all of src/ for Repository<SorenessEntry>().AddAsync or new SorenessEntry returns nothing - no screen can create one. AC1 and AC2 therefore cannot occur on a device. R5's stored override responsibility is not implemented. Important for whoever fixes this: passing the declared injuries straight into Contraindications compiles and blocks almost nothing, because the two halves model injury differently. ExerciseFilter.InjuryMovementExclusions (src/Forge.Domain/Training/ExerciseFilter.cs:50-61) is keyed on joints and regions - knee, hip, lower back, back, shoulder, elbow, wrist, ankle, neck - and maps them to MovementPattern values, whereas FindContraindication (NextSessionRecommender.cs:126-131) does a case-insensitive exact match of MuscleGroup against the exercise's PrimaryMuscle and SecondaryMuscles. Against the 27 distinct muscle names in the 60-exercise catalogue (src/Forge.Infrastructure/Content/exercise-catalogue.json), exactly one of those nine keys matches: 'lower back'. Three lose on the plural (shoulder vs Shoulders, ankle vs Ankles, hip vs Hips) and five name no muscle at all. Eight of nine silently doing nothing while the ninth works is worse than uniformly dead, because a tester with a back injury sees a block and concludes the guardrail works. The join should be on movement pattern, which is what ExerciseFilter already models.


#### F17.03 - Detect plateaus and suggest concrete interventions

**NOT-DONE.** src/Forge.Domain/Coaching/PlateauDetector.cs exists and is unit-tested, requiring four working sessions and flagging a plateau when load range is within 1.25 kg and rep range within 1, with three intervention strings attached. Its only reference outside its own file and tests is services.AddTransient<PlateauDetector>() at CoachingFeatureRegistration.cs:18. No view model injects it, no page renders a plateau card, and no forecasting code exists anywhere in the repository.

_Gaps:_ All three stories in this feature are unreachable or absent: plateau detection has no UI, its interventions are never surfaced with trade-offs, and goal forecasting with confidence ranges has no implementation at all.

##### S17.03.01 - Detect exercise plateaus from repeated stalled performance

**NOT-DONE.**

*Evidence.* PlateauDetector.Detect (src/Forge.Domain/Coaching/PlateauDetector.cs:9-34) is implemented and tested: it returns a PlateauResult carrying IsPlateaued, the sample count, an explanation, and interventions, and refuses to call a plateau below four sessions with the wording 'At least 4 working sessions are needed before Forge calls a plateau.' It has no caller. The type appears exactly twice in src/ outside its own definition: the DI registration at CoachingFeatureRegistration.cs:18 and nothing else. No XAML binds a plateau card and ICoachingDataService exposes no plateau method.

*Gaps.* No user path reaches this code, so AC1 and AC2 cannot occur. R2 also diverges from the implementation: the detector compares raw load and rep ranges, not a 1 percent change in best estimated max plus missed targets in 2 of the last 4 sessions, and it has no notion of a target to miss. R3's 'likely plateau' labelling exists nowhere in the UI because there is no UI.

##### S17.03.02 - Suggest plateau interventions with explicit trade-offs

**NOT-DONE.**

*Evidence.* The only intervention text in the codebase is the three-string array inside PlateauDetector.cs:21-28 ('Repeat the load but add one rep...', 'Reduce load by 5-10% for one session...', 'Swap to a close variation for two weeks...'). PlateauResult.Interventions (CoachingModels.cs:54) is never read by any view model or page, because PlateauDetector itself is never called.

*Gaps.* R2 and R3 unmet even in the domain: interventions are a fixed list with no ranking against available signals and no expected benefit or trade-off attached to each. R1's fuller intervention set (add a rest day, reduce weekly sets by 20 percent) is not present. AC1 and AC2 cannot occur because nothing renders.

##### S17.03.03 - Forecast goal progress with confidence ranges

**NOT-DONE.**

*Evidence.* No forecasting code exists. src/Forge.Domain/Analytics/TrendAnalyzer.cs produces a direction, a magnitude per day and a sample count, and refuses a claim below four points, but it projects nothing forward and has no confidence interval. Searched for a goal forecast surface: no forecast card in ProgressPage.xaml, InsightsPage.xaml or CoachingPage.xaml, and no earliest/midpoint/latest date anywhere in src/.

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented: no linear projection to a goal, no confidence range expressed as three dates, and no explanation for a suppressed forecast on a flat or negative slope.


#### F17.04 - Guide training volume, substitutions and form reminders

**PARTIAL.** Exercise substitution is genuinely built and reachable: src/Forge.Domain/Training/ExerciseSubstitution.cs is called from src/Forge.App/Features/Exercises/ExerciseAlternativesViewModel.cs:153, which ranks alternatives against user-toggled equipment chips and renders an explanation plus an 'equipment that would unlock more' hint.

_Gaps:_ Volume landmarks (MEV/MAV/MRV) do not exist anywhere in the repository, and form-check reminders do not exist. Substitution ignores declared injuries entirely.

##### S17.04.01 - Explain volume landmarks in plain language

**NOT-DONE.**

*Evidence.* Searched the whole repository for MEV, MAV, MRV, MinimumEffectiveVolume and any volume-landmark surface: no matches in src/, tests/ or docs/. src/Forge.Domain/Planning/VolumeBalanceAnalyzer.cs is a different concern - it checks push/pull balance for plan authoring and is used at src/Forge.App/Features/Plans/PlansFeatureViewModels.cs:303. The weekly muscle volume from E16 (TrainingTrendAggregator.PerWeekByMuscleGroup) is rendered as raw kilograms with no range judgement of any kind.

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented: nothing explains MEV, MAV or MRV, nothing maps weekly muscle volume to below/in/above range, and no four-week minimum-data gate exists because there is no judgement to gate.

##### S17.04.02 - Suggest exercise substitutions from equipment and constraints

**PARTIAL.**

*Evidence.* ExerciseSubstitution.Suggest is wired into a reachable screen: ExerciseAlternativesViewModel.Rank (src/Forge.App/Features/Exercises/ExerciseAlternativesViewModel.cs:143-170) builds an EquipmentAvailability from the user's selected chips, calls Suggest, and binds both the ranked alternatives and the returned Explanation, plus an UnlockHint naming equipment that would open more options. Matching is on primary muscle, movement pattern and equipment, so R3's 'at least two factors' is satisfied by the explanation.

*Gaps.* R1 and R2 unmet, and AC2 cannot occur: ExerciseSubstitution.Suggest takes only the original exercise, the catalogue and equipment availability - it has no injury or avoided-exercise parameter, and ExerciseAlternativesViewModel never reads UserProfile.MovementLimitations. Forge.Domain.Training.ExerciseFilter.FromDeclaredInjuries exists for exactly this purpose and has callers only in tests and in ExerciseLibraryViewModel.cs:528, not in the substitution path. There is also no 'mark as avoided' concept for an exercise, so a user cannot express the exclusion the criterion assumes.

##### S17.04.03 - Schedule form-check reminders by exercise cadence

**NOT-DONE.**

*Evidence.* No form-check reminder exists. Searched for FormCheck and for any exposure-count or cadence tracking per exercise: nothing in src/. src/Forge.Domain/Training/ExerciseGuidance.cs supplies static cue text shown on the exercise detail and alternatives screens (ExerciseDetailViewModel.cs:111, 218-237), but it is not scheduled, not tied to exposure counts, and not shown before a working set. ForgeNotificationCategory (src/Forge.Core/Abstractions/Notifications/NotificationModels.cs:4-26) has no form-check category.

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented: no first-exposure trigger, no every-4th-exposure-or-28-days cadence, no suppression during rest countdowns, and no 30-day per-exercise silence.


#### F17.05 - Summarise weeks and nudge habits safely

**PARTIAL.** NextSessionRecommendation (src/Forge.Domain/Coaching/CoachingModels.cs:41-51) carries a Reasons list that accumulates the rule's own reason, the source set's figures and any applied safety bound, which is the beginning of the trace this feature asks for.

_Gaps:_ The weekly coaching review and habit nudges do not exist in any form, and the trace that does exist is never rendered - CoachingPage.xaml binds Explanation but never Reasons.

##### S17.05.01 - Generate an explainable weekly coaching review

**NOT-DONE.**

*Evidence.* No weekly review exists. Searched for WeeklyReview and for any week-scoped coaching summary: nothing in src/. src/Forge.Domain/Dashboard/TodayFocusPlanner.cs, used by TodayViewModel.cs:24, chooses between exactly four actions - FinishSetup, StartWorkout, ChoosePlan and ReviewToday - and ReviewToday simply navigates to the Insights tab (TodayViewModel.cs:214). Nothing assembles completed sessions, a PR signal, a consistency signal and a next-week focus into one surface.

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented: no weekly review screen, no per-statement link to a source metric or workout, and no empty state for a week with no completed workouts.

##### S17.05.02 - Nudge habits without shame or unsafe pressure

**NOT-DONE.**

*Evidence.* No habit nudge system exists. Searched for Nudge across src/, tests/ and docs/: no matches. TodayViewModel renders rings, a focus action and recent activity from InsightsDataService.BuildTodaySummary (InsightsDataService.cs:545-601); there is no nudge candidate list, no ranking, and no dismissal state with an expiry.

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented: no missed-session, protein-adherence, hydration-gap or late-workout nudges; no one-per-day cap; and no seven-day dismissal. The shame-term constraint in R3 is separately enforced for engagement copy by EngagementEthicsPolicy, but that policy is not applied to any nudge because no nudge exists.

##### S17.05.03 - Display recommendation confidence and trace details

**PARTIAL.**

*Evidence.* NextSessionRecommender builds a partial trace: Reasons accumulates the progression rule's reason, the source set's load, reps and reps-in-reserve, and the capping reason when the 5 percent bound is applied (NextSessionRecommender.cs:82-94), and the blocked paths add the specific safety reason (lines 30, 45). The blocked-path copy names the muscle group and reason without reproducing free-text injury notes, which is the spirit of R3.

*Gaps.* The trace is never shown. CoachingPage.xaml:20-30 binds Status, Load, Reps, Explanation, MedicalDisclaimer and OverrideNote - it does not bind Reasons, and CoachingViewModel exposes no property for it, so the list is computed on every recommendation and discarded. There is no 'Why this' affordance. R1 unmet in the model too: there is no rule id, no explicit input-metric set and no blocked-factor list as distinct fields. R2 and AC2 unmet: confidence labels do not exist anywhere in the codebase - nothing computes Low, Medium or High from data quantity or recency, and nothing detects data older than 28 days.


### E18 - Recovery, Sleep and Readiness

**Epic verdict: PARTIAL.** Readiness is the strongest thing in this epic and it is real: src/Forge.Domain/Recovery/ReadinessScore.cs computes a 0-100 composite from six named components with explicit weight constants, renormalises around unavailable inputs instead of silently penalising them, and returns every component with its weight, raw score, availability and explanation; src/Forge.App/Features/Coaching/ReadinessPage.xaml renders the ring plus a dx:DXCollectionView of those components including the weight, and ReadinessViewModel.cs:33 states which inputs were missing. Morning check-in capture and persistence work end to end (MorningCheckInViewModel -> CoachingDataService.SaveMorningCheckInAsync, CoachingDataService.cs:86-94), and both screens are routed and reachable from TrainViewModel.cs:32-36. The sleep-performance association card on InsightsPage.xaml:181-192 is careful work: SleepPerformancePairing refuses to pair rest days and states why, and renders two specific caveats rather than a generic hedge.

_Gaps:_ Per-muscle soreness has a table, an entity, a tracker and readers but no writer anywhere in the app, so F18.03 cannot function. OvertrainingDetector is complete, tested and has zero callers. Resting heart rate is never read for readiness. There is no sleep trend, no mobility routine, no wind-down routine, no rest-day card, no readiness-band analysis and no recovery export. The association threshold is 8 paired samples where the backlog requires 20.

#### F18.01 - Import and enter sleep data safely

**PARTIAL.** Sleep is read only through the E12 abstraction: CoachingDataService.TryReadSleepHoursAsync (src/Forge.App/Features/Coaching/Services/CoachingDataService.cs:103-123) resolves IHealthDataService, checks GetAvailabilityAsync, and returns null for NotSupportedOnPlatform or PermissionUnknown rather than failing. Manual entry remains available as MorningCheckIn.SleepHours, and ReadinessScore.SleepComponent (ReadinessScore.cs:71-81) prefers health sleep and falls back to the manual value.

_Gaps:_ Nothing displays a sleep summary, no manual entry captures bedtime, wake time or quality, and no sleep trend chart exists.

##### S18.01.01 - Read sleep summaries through the health data abstraction

**PARTIAL.**

*Evidence.* R1 is satisfied precisely: the Recovery path calls no platform API directly. CoachingDataService.cs:105-119 resolves IHealthDataService from the container, gates on HealthAvailability, and calls ReadAsync([HealthDataType.Sleep], start, end, ...); the Health Connect and HealthKit implementations live behind that abstraction in src/Forge.App/Platforms/. Manual entry is never blocked by a denied permission, and ReadinessScore.cs:76 states in the component explanation that sleep was unavailable and readiness used manual signals rather than silently lowering the score.

*Gaps.* R2 unmet: only a duration is extracted. CoachingDataService.cs:120 takes the maximum TotalHours from the returned SleepHealthSample list and discards sleep start, sleep end and any source label, so AC1's '8 h 0 min and the source' cannot be displayed. The window is also fixed at the last 24 hours (lines 117-118), so a late-morning read after a short nap can outrank the night. R3 and AC2 unmet: there is no Recovery tab and no permission-denied surface offering manual entry - the only signal a user gets is a 'Missing' row on the Readiness component list. AC3's 400 ms empty state has no screen to appear on.

##### S18.01.02 - Add manual sleep entry when platform data is unavailable

**PARTIAL.**

*Evidence.* Manual sleep entry exists as an optional field on the morning check-in: MorningCheckInPage.xaml:49-53 renders a dx:NumericEdit labelled 'Sleep hours (optional)' with a SemanticProperties.Description, and MorningCheckInViewModel.cs:39 rounds and stores it on MorningCheckIn.SleepHours, which ReadinessScore then prefers or falls back to. The value is durable in SQLite through IDataSessionFactory (CoachingDataService.cs:91-93).

*Gaps.* R1 largely unmet: only a duration is captured - no sleep date other than today, no bedtime, no wake time, no perceived quality and no wake count. R2 unmet: there is no midnight-crossing calculation because there are no clock times, and no validation rejects durations below 1 hour or above 16 - MorningCheckInViewModel.cs:39 rounds whatever the numeric editor holds. AC2 therefore cannot occur. R3 unmet: entries cannot be edited or deleted. Worse, CoachingDataService.SaveMorningCheckInAsync always calls AddAsync (line 92), so saving twice on the same day writes two rows for the same date; GetReadinessAsync orders only by Date (line 71) and so picks an arbitrary one of them. Editing today's check-in is not just missing, it silently duplicates.

##### S18.01.03 - Trend sleep quality and duration over time

**NOT-DONE.**

*Evidence.* No sleep trend exists. Searched every analytics and coaching page for a sleep series: ProgressPage.xaml, InsightsPage.xaml, BodyMetricsPage.xaml, ExerciseProgressPage.xaml and ReadinessPage.xaml contain no sleep chart. The only consumer of stored sleep hours is InsightsDataService.cs:251-254, which projects MorningCheckIn.SleepHours into SleepNight records purely as input to the association card. No nightly duration bars, no moving average over sleep and no quality series exist, and perceived quality is not captured at all.

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented. R2's separation of quality from duration is vacuous because quality is never recorded (MorningCheckIn.cs has no quality field).


#### F18.02 - Build transparent readiness scoring and morning check-ins

**PARTIAL.** ReadinessScore.cs exposes its weights as public constants with a stated rationale (lines 35-43), renormalises across available components only (lines 61-65), and returns MissingInputs so the screen can say what was absent; ReadinessPage.xaml:34-47 lists every component with its weight and explanation, which is exactly the 'visible input breakdown instead of an opaque score' the epic asks for.

_Gaps:_ The weight set does not match either weighting specified in the backlog, resting heart rate is not an input at all, and input freshness is never shown.

##### S18.02.01 - Capture morning energy, soreness, motivation and stress

**PARTIAL.**

*Evidence.* MorningCheckInPage.xaml captures energy, soreness, motivation and stress on 1-5 scales in one short form, each editor carrying its own SemanticProperties.Description and a 'Rate from 1 to 5' hint with the visible label deliberately removed from the accessibility tree (lines 25-48). MorningCheckInViewModel.ClampFivePoint bounds every value to 1-5 before saving, and CoachingDataService.SaveMorningCheckInAsync stamps the active profile and commits through a single IDataSessionFactory session (CoachingDataService.cs:86-94), so AC1's durability holds.

*Gaps.* R2 unmet in a way that corrupts data: there is no edit path, and SaveMorningCheckInAsync always inserts (AddAsync, line 92), so a second save on the same day creates a duplicate row rather than editing the current day. GetReadinessAsync orders by Date only (line 71), so which duplicate wins is arbitrary. Skipping a day is possible but nothing states that skipping is fine. AC2 unmet: the control is a free numeric editor whose hint says 'Rate from 1 to 5' but never announces which end is best, and 'the current value and that 5 is highest energy' is not what a NumericEdit announces. AC3's 400 ms empty state does not apply to a capture form and is left out.

##### S18.02.02 - Calculate readiness with visible weights and inputs

**PARTIAL.**

*Evidence.* src/Forge.Domain/Recovery/ReadinessScore.cs:46-69 builds six components - sleep, training load, energy, soreness, motivation and stress - each with a public weight constant, a raw 0-100 score, a contribution, an availability flag and a human explanation, then divides the summed contributions by the summed available weight so a missing input is redistributed proportionally rather than scored as zero (lines 61-65). ReadinessViewModel.cs:37-41 renders each component as 'Weight 30% - <explanation>' with the raw score or the word 'Missing', and MissingInputs is shown above it. That is a genuinely transparent score, and R2's redistribution behaviour is implemented correctly even though it is applied to a different input than the criterion names.

*Gaps.* R1 and AC1 unmet: the weights are 30 sleep / 25 training load / 15 energy / 15 soreness / 10 motivation / 5 stress (ReadinessScore.cs:35-40), not 35 sleep / 25 load / 25 wellbeing / 15 heart rate, and there is no heart-rate component at all, so AC1's 74.0 result cannot be produced. R3 partially unmet: inputs and weights are visible but input freshness is not - nothing shows how old the sleep reading or the check-in is, and GetReadinessAsync will happily use a check-in from weeks ago (CoachingDataService.cs:71-72) without saying so, which is a real honesty gap for a score a user acts on. Backlog defect: this story specifies two different weightings in the same requirement list - R1 says 35/25/25/15 while R6 says 35 sleep / 25 energy / 20 inverse soreness / 10 inverse stress - so the criterion cannot be satisfied as written and should be reconciled before the code is changed to match it.

##### S18.02.03 - Incorporate resting heart rate when available

**NOT-DONE.**

*Evidence.* Resting heart rate is never read for readiness. CoachingDataService requests only HealthDataType.Sleep (line 119), ReadinessInput (src/Forge.Domain/Recovery/ReadinessScore.cs:6-10) has no heart-rate field, and ReadinessScore.Calculate builds no heart-rate component. The health abstraction does expose heart-rate types through HealthDataTypeCatalog, so the capability exists at the platform layer and is simply not consumed by Recovery.

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented: no reading count over a 14-day window, no 7-reading availability gate, and no 'insufficient' marking. R3 is trivially true only because heart rate is absent entirely rather than because it was made optional.


#### F18.03 - Track soreness and adapt daily recovery guidance

**NOT-DONE.** The whole feature rests on per-muscle soreness, and per-muscle soreness cannot be recorded. SorenessEntry (src/Forge.Domain/Recovery/SorenessTracker.cs:7-20) is a persisted, profile-owned entity with an EF configuration (src/Forge.Infrastructure/Persistence/Configurations/Recovery/RecoveryConfigurations.cs:26-30) and a table in the initial migration, and it is read by CoachingDataService at lines 44 and 73 - but a search across all of src/ (excluding migrations) for Repository<SorenessEntry> or new SorenessEntry returns no results. Nothing creates one. ProfileStore only counts them for the data-areas screen and soft-deletes them on erasure (ProfileStore.cs:272, 333).

_Gaps:_ The soreness table is permanently empty in a shipped app, so the readiness soreness component always falls back to the whole-body check-in value (ReadinessScore.cs:100-107), NextSessionRecommender's severe-soreness block can never fire, and no soreness-adapted guidance or rest-day card exists to be driven by it.

##### S18.03.01 - Track muscle-group soreness and affected areas

**NOT-DONE.**

*Evidence.* This is claimed-done-but-broken. Everything except the writer exists: SorenessEntry carries MuscleGroup, Level and RecordedOn; SorenessTracker.LatestForMuscle and IsSeverelySore are implemented and unit-tested; RecoveryConfigurations.cs configures the table; CoachingDataService reads the rows twice. But no screen creates a soreness entry - there is no soreness page, no soreness section on MorningCheckInPage.xaml (which captures only a single whole-body soreness integer on MorningCheckIn), and no Repository<SorenessEntry>().AddAsync call anywhere in src/.

*Gaps.* R1 unmet: no capture surface, and the entity's scale is 1-5 rather than the 0-5 the criterion specifies. R2 unmet: no muscle-group list is offered because there is no list to offer. R3 unmet: nothing highlights entries above 3 - SorenessTracker only recognises level 5 as severe (SevereSorenessLevel = 5) and defines HighSorenessLevel = 4 with no consumer. AC1 and AC2 cannot occur.

##### S18.03.02 - Adapt recommended sessions from soreness and readiness

**NOT-DONE.**

*Evidence.* The only soreness-driven adaptation in the codebase is the severe-soreness block at NextSessionRecommender.cs:35-48, which returns BlockedBySafety when SorenessTracker.IsSeverelySore reports level 5 for the target muscle. That branch is unreachable because no SorenessEntry can ever exist (see S18.03.01). Readiness is not an input to coaching at all: NextSessionRecommendationRequest (CoachingModels.cs:18-30) has no readiness field and CoachingDataService.GetNextSessionRecommendationAsync never calls GetReadinessAsync.

*Gaps.* R1 unmet: there is no level-4 threshold behaviour and no three-way choice between reduce volume, substitute movement and rest - only an all-or-nothing block at level 5. R2 unmet: the explanation names the muscle and the soreness, but never the readiness score or the planned target, because neither is available to the recommender. R3 unmet: overrides record nothing (CoachingViewModel.cs:33). AC1 cannot occur on a device.

##### S18.03.03 - Recommend rest days without guilt copy

**NOT-DONE.**

*Evidence.* No rest-day recommendation exists. src/Forge.Domain/Dashboard/TodayFocusPlanner.cs offers exactly four actions - FinishSetup, StartWorkout, ChoosePlan and ReviewToday - none of which is rest, and TodayViewModel renders only rings, that focus action and recent activity. Nothing counts consecutive training days, nothing tests readiness against a 40 threshold for a Today card, and there is no dismissible recovery card anywhere in src/.

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented. The supportive copy the criterion asks for does exist in the engagement layer (EngagementEthicsPolicy.RestIsTrainingMessage), but it is shown on the Consistency screen rather than as a rest-day recommendation, and it is not triggered by readiness, soreness or a six-day run.


#### F18.04 - Provide mobility and wind-down routines

**NOT-DONE.** No mobility content, no wind-down routine and no overtraining surface exist. Searched for Mobility as a routine concept: the only hits are HealthWorkoutActivities.cs:41 (an activity-type constant for health export) and a string in InsightsDataService.cs:570. WindDown has zero matches across src/, tests/ and docs/. src/Forge.Domain/Recovery/OvertrainingDetector.cs is fully implemented and tested but has no caller anywhere in src/.

_Gaps:_ All three stories are unimplemented or unreachable; the one piece of finished logic in the feature, OvertrainingDetector, cannot be reached from any screen.

##### S18.04.01 - Offer mobility routines by sore muscle group

**NOT-DONE.**

*Evidence.* There is no mobility routine feature: no routine entity, no seeded routine content, no page and no route. The exercise catalogue seed and ExerciseGuidance provide per-exercise cues but nothing assembles a timed routine, and nothing maps a sore muscle group to a routine because no per-muscle soreness can be recorded in the first place (see S18.03.01).

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented: no 5/10/15 minute routines, no per-movement duration, setup cue or stop-if-pain warning, and no default general recovery routine.

##### S18.04.02 - Create wind-down routines tied to training schedule

**NOT-DONE.**

*Evidence.* No wind-down routine exists. WindDown has no matches anywhere in the repository. The reminder system (src/Forge.App/Services/Notifications/ReminderRefreshService.cs) plans only four reminder kinds - Workout, Hydration, DailyCheckIn and StreakProtection (ReminderSchedulingPolicy.cs:56-69) - none of which is an evening wind-down, and none of which is triggered by tomorrow being a planned training day.

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented: no evening-window card, no 3-5 step routine, and no per-card reminder disable.

##### S18.04.03 - Flag overtraining warning signals cautiously

**NOT-DONE.**

*Evidence.* src/Forge.Domain/Recovery/OvertrainingDetector.cs is a complete implementation of exactly this story: Evaluate (lines 25-60) collects up to four signals - readiness below 45, load ratio at or above 1.5, severe soreness and sleep below 6 hours - grades Low/Elevated/High at two and three signals, uses cautious wording, and appends 'speak with a qualified clinician if symptoms persist' plus the medical disclaimer. It has no caller: the type appears only in its own file and in Forge.Domain.Tests. No page renders a recovery warning card and ICoachingDataService exposes no method for it.

*Gaps.* No user can reach this. AC1 and AC2 cannot occur. The logic itself is closer to the criterion than most unreachable code in this range - it already requires two signals - which makes the absence of a caller the whole of the gap.


#### F18.05 - Surface sleep-performance correlations responsibly

**PARTIAL.** The sleep-performance association is implemented and reachable on InsightsPage.xaml:181-192, backed by src/Forge.Domain/Analytics/SleepPerformancePairing.cs and SleepPerformanceAssociationAnalyzer.cs, gated behind a minimum sample size and worded as association only.

_Gaps:_ The sample threshold is 8 rather than the 20/21 the epic requires, readiness-band analysis does not exist because readiness is never stored, and no recovery export exists.

##### S18.05.01 - Correlate sleep with training performance after minimum samples

**PARTIAL.**

*Evidence.* This is careful work. SleepPerformanceAssociationAnalyzer.Analyze (src/Forge.Domain/Recovery/SleepPerformanceAssociationAnalyzer.cs:15-35) refuses a claim below the minimum sample size and again when one side of the sleep threshold is empty, and the message it returns says 'associated with' and 'This is an association only, not a prescription or diagnosis'. SleepPerformancePairing.BuildSamples pairs only days containing training and documents why treating a rest day as zero performance would manufacture the finding (lines 45-50). InsightsPage.xaml:184-190 renders the message, the sample counts, the NonCausationCaveat naming specific confounds, and the PerformanceMeasureCaveat explaining what the performance figure actually is.

*Gaps.* R1 and AC2 unmet: SleepPerformanceAssociationAnalyzer.MinimumSampleSize is 8 (line 12), not the 20 paired nights R1 requires nor the 21 that R5 applies epic-wide, so Forge will state an association from eight paired days. Given this epic's own standing rule about honest uncertainty, that is a real gap rather than a formality. R3 unmet: there is no outlier handling at all - no 3-standard-deviation exclusion and no exclusion count reported. R2 partially unmet: the claim is directional ('higher' or 'lower') and does not quantify the difference as a percentage against a named exercise; the code discloses that performance is whole-day working volume, which is honest but is not the per-exercise metric the criterion describes.

##### S18.05.02 - Show readiness and performance trend associations

**NOT-DONE.**

*Evidence.* No readiness-performance association exists, and it could not exist as the code stands: readiness is computed on demand by CoachingDataService.GetReadinessAsync and never persisted - there is no readiness entity, no readiness table in any migration, and no historical score to join against a workout. Searched for readiness banding (below 50 / 50-74 / 75+): no matches in src/.

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented: no same-day readiness history, no bands, no per-band sample counts and no five-sample suppression rule. Storing the daily readiness result is a prerequisite that has not been built.

##### S18.05.03 - Export recovery insights with caveats included

**NOT-DONE.**

*Evidence.* No recovery export exists. There is no export action on InsightsPage.xaml, ReadinessPage.xaml or any other recovery surface, and no chart export exists anywhere in the app (see S16.05.01). The only image share in src/ is the achievement card at AchievementsPage.xaml.cs:30-53, which is unrelated to recovery and has no redaction or cleanup logic.

*Gaps.* All of R1-R3 and AC1-AC2 unimplemented: no exported image carrying title, date range, sample count and non-causation caveat; no exact-sleep-time redaction toggle; no temporary-file deletion policy.


### E19 - Notifications, Reminders and Habit Loops

**Epic verdict: PARTIAL.** The reminder engine is real and better than most of this range. src/Forge.Core/Abstractions/Notifications/ReminderSchedulingPolicy.cs is a pure planner that produces four reminder kinds in priority order, records a specific suppression reason for each rejected candidate (PermissionDenied, QuietHours, DailyCapReached, AlreadyCompleted, NotApplicable, TimePassed), handles overnight quiet-hours ranges, and resolves wall-clock intent through TimeZoneInfo while stepping over DST gaps (ResolveWallClock, lines 183-195). src/Forge.App/Services/Notifications/ReminderRefreshService.cs feeds it real profile-scoped data - active plan, workouts, hydration, check-ins and streak state - and cancels reminders whose action is already complete. LocalNotificationScheduler persists intent in Preferences independently of platform ids, schedules with AndroidScheduleMode.InexactAllowWhileIdle, and registers a BOOT_COMPLETED / TIMEZONE_CHANGED receiver. The Android manifest declares no exact-alarm permission.

_Gaps:_ The engine has almost no way in. The single caller of IReminderRefreshService and INotificationScheduler in the entire app is StreaksPageViewModel.EnableRespectfulRemindersAsync - a button on the Consistency screen. Nothing refreshes reminders on launch, on plan change or on workout completion. Notification actions and taps do not exist at all: NotificationTapped and NotificationActionTapped have zero references, so no Start workout, Log water or Snooze can ever be handled. There is no notification centre, no reminder history and no foreground coordination. NotificationSettingsPage exposes a Meal reminders toggle that no code reads, and hides the DailyCheckIn and StreakProtection toggles that the planner does read.

#### F19.01 - Earn notification permission at a value moment

**PARTIAL.** LocalNotificationScheduler.RequestPermissionForDemonstratedValueAsync (src/Forge.App/Services/Notifications/LocalNotificationScheduler.cs:54-74) refuses outright when the reason is AppLaunch, which is the substance of R1, and POST_NOTIFICATIONS is declared as an assembly-level UsesPermission at lines 16-17. Permission state is mapped to a platform-free tri-state (Unknown / Authorized / Denied) and persisted so a denial is not re-prompted.

_Gaps:_ There is no pre-permission explanation screen, and the notification settings page shows no permission status, no deep link to system settings and no resume refresh.

##### S19.01.01 - Request notification permission after the first useful reminder is configured

**PARTIAL.**

*Evidence.* R1 is enforced in code rather than by convention: RequestPermissionForDemonstratedValueAsync returns false without prompting when reason == NotificationPermissionPromptReason.AppLaunch (LocalNotificationScheduler.cs:60-63), and nothing in onboarding or startup calls it - the only caller in src/ is StreaksPageViewModel.cs:211, passing UserEnabledReminder after the user taps 'Enable respectful reminders'. R4 is satisfied: POST_NOTIFICATIONS is declared and the plugin's runtime request is used. R5 is satisfied by ReminderSchedulingPolicy.Plan (line 129-133), which marks every candidate PermissionDenied and schedules nothing while preserving the intent in Preferences.

*Gaps.* R2 unmet: the prompt is not tied to creating a reminder intent. The toggles on NotificationSettingsPage write preference keys and trigger no permission flow at all, so the one place a user configures a reminder is the one place that never asks. R3 and AC4 unmet: there is no pre-permission explanation screen naming the category, the scheduled time and the exact system permission, and therefore no 'Not now' path that suppresses the system prompt. AC3 unmet: reopening settings shows the same checkboxes with no saved-but-inactive state and no enable-in-settings button.

##### S19.01.02 - Reflect platform notification status in settings and recovery paths

**NOT-DONE.**

*Evidence.* The settings surface exists and is reachable - SettingsPageViewModel.cs:17 lists a 'Notifications' preference row routing to ForgeRoutes.NotificationSettings, registered at SettingsFeatureRegistration.cs:58 - which makes what it does and does not do decisive rather than academic. src/Forge.App/Features/Settings/NotificationSettingsPage.xaml renders three category checkboxes and two quiet-hours text fields and nothing else, and its own subtitle admits the state of the feature: 'These preferences only control local reminder intent. Notification delivery is wired by the notification epic.' (lines 13-14). NotificationSettingsPageViewModel.cs is 48 lines of Preferences get/set with no dependency on INotificationScheduler at all, so it cannot know or display the permission state.

*Gaps.* R1 unmet: no status of any kind is shown - not asked, enabled, denied, provisional or system-disabled - even though LocalNotificationScheduler.GetPermissionStateAsync exists to supply it. R2 unmet: there is no deep link to platform notification settings. R4 and AC1/AC3/AC4 unmet: the page has no OnAppearing or resume hook and its view model holds only Preferences values, so a permission change made in system settings is never reflected. R3 is vacuously true only because the toggles are always editable, never because they reflect a disabled platform state.


#### F19.02 - Schedule helpful reminders without exact-alarm dependency

**PARTIAL.** ReminderSchedulingPolicy builds workout, hydration, daily check-in and streak-protection candidates in a fixed priority order and suppresses each with a named reason; ReminderRefreshService drives it from the active plan via PlanScheduler.Schedule (line 147) and from today's actual hydration, workouts and check-ins. Quiet hours are enforced twice - once in the planner and once again in LocalNotificationScheduler.IsSuppressedByQuietHours - and rest timers are exempted from both quiet hours and the cap.

_Gaps:_ Only today is ever planned, no notification carries an action button, meal reminders do not exist, snooze does not exist, and the user-configurable daily cap is ignored by the scheduler that enforces it.

##### S19.02.01 - Schedule workout reminders from the training plan

**PARTIAL.**

*Evidence.* ReminderRefreshService.FindTodaySession (src/Forge.App/Services/Notifications/ReminderRefreshService.cs:139-148) derives the day's session from the active plan through PlanScheduler, and ReminderSchedulingPolicy.BuildWorkoutReminder (ReminderSchedulingPolicy.cs:212-238) names the planned day in the body, satisfying most of R5. R4 is implemented: when HasCompletedWorkoutToday is true the candidate is marked AlreadyCompleted and ReminderRefreshService.cs:105-108 actively cancels the pending notification. R3 and AC3 are satisfied and verifiable by reading: ShowAsync sets AndroidScheduleMode.InexactAllowWhileIdle (LocalNotificationScheduler.cs:194) and src/Forge.App/Platforms/Android/AndroidManifest.xml declares only ACCESS_NETWORK_STATE, INTERNET, CAMERA and USE_BIOMETRIC - neither SCHEDULE_EXACT_ALARM nor USE_EXACT_ALARM is present. AC4 is satisfied by the daily cap in both the policy and the scheduler.

*Gaps.* R1 partially and R2/AC2 fully unmet: the refresh only ever plans the current local day (ReminderUserSnapshot is built from localDate alone, ReminderRefreshService.cs:53, 89-96), so a workout planned for tomorrow is never scheduled ahead and AC1's 'planned Strength A for tomorrow at 07:30' cannot produce a notification today. Worse, the only trigger for a refresh in the whole app is the user tapping a button on the Consistency screen (StreaksPageViewModel.cs:221) - moving a planned workout from Tuesday to Wednesday updates nothing, so the five-second requirement in R2 cannot be met. R5 partially unmet: estimated duration is not included in the copy.

##### S19.02.02 - Add meal and hydration reminders with one-tap water logging

**PARTIAL.**

*Evidence.* Hydration reminders exist and are data-driven: ReminderSchedulingPolicy.BuildHydrationReminder (lines 240-264) suppresses the nudge as AlreadyCompleted once HydrationConsumedMillilitres reaches the configured target, and ReminderRefreshService.cs:73-75 sums the day's real HydrationEntry rows for the active profile, so R5 and AC3 hold. Quiet hours defer the reminder (AC4's suppression half).

*Gaps.* Meal reminders do not exist. ForgeNotificationCategory.MealReminder is declared (NotificationModels.cs:13) but ReminderKind has no meal member, the policy builds no meal candidate, and the MealRemindersEnabled preference written by NotificationSettingsPageViewModel.cs:39 is read by nothing - ReminderRefreshService.ReadPreferences (lines 114-126) does not look at that key. The settings toggle is inert. R1 unmet: exactly one hydration reminder per day is possible, not six, and there is no meal-reminder count. R2, R3 and R4 unmet: no notification carries any action button - ShowAsync (LocalNotificationScheduler.cs:178-206) sets no action set - so Log water, Open meal log and Snooze do not exist, there is no action handler to be idempotent, and AC1 and AC2 cannot occur.

##### S19.02.03 - Add rest-day recovery prompts and streak-protection nudges

**PARTIAL.**

*Evidence.* The streak-protection nudge is implemented with the right constraints. ReminderSchedulingPolicy.BuildStreakReminder (lines 292-316) fires at most once per local day via the stable id 'streak-protection:<date>', is suppressed when the day is not a training day or the workout is already done, and its copy is explicitly non-coercive: 'If training no longer fits today, a planned rest day is a valid choice.' AC2 is satisfied: ReminderRefreshService.cs:96 passes streak.AllowsSupportiveReminders(localDate), and Streak.AllowsSupportiveReminders (src/Forge.Domain/Engagement/Streak.cs:229) returns GamificationEnabled && !IsProtectedOn(today), so disabling gamification stops the nudge and so does an active protected period. AC3 holds because the stable id is per-day and the planner runs once per kind. AC4 holds through the daily cap. R5 is enforced by EngagementEthicsPolicy's prohibited-terms lists, which the engagement tests assert against every string those screens can produce.

*Gaps.* R1 and AC1 unmet: there is no rest-day or deload recovery prompt. ReminderKind (ReminderSchedulingPolicy.cs:56-69) has only Workout, Hydration, DailyCheckIn and StreakProtection, and nothing inspects the plan for a scheduled rest or deload day - BuildStreakReminder in fact requires IsTrainingDay, so no reminder of any kind is planned for a rest day. R3's 'only fire before quiet hours' is satisfied by suppression rather than by scheduling earlier, so a user whose quiet hours start before the 20:00 default simply never receives it, silently.

##### S19.02.04 - Enforce quiet hours, category controls and frequency caps

**PARTIAL.**

*Evidence.* Quiet hours are correctly implemented including overnight wrap: ReminderSchedulingPolicy.IsInQuietHours (lines 158-169) handles both start<=end and the wrapping case, and it is applied both in the planner and again defensively in LocalNotificationScheduler.IsSuppressedByQuietHours (line 208-217), with rest timers exempt. A per-local-day cap exists (LocalNotificationScheduler.CanScheduleMore, lines 230-246) and suppression reasons are modelled explicitly as ReminderSuppressionReason and returned from Plan, so AC1's suppression holds and AC2's suppression holds.

*Gaps.* R3 unmet on the number and on the mechanism. The default is 4, not 3. More precisely, there are two independent caps and the lower always wins: ReminderSchedulingPolicy.Plan suppresses at input.Preferences.DailyNotificationCap (line 141) while LocalNotificationScheduler.CanScheduleMore compares against the hard constant MaxNonCriticalNotificationsPerLocalDay = 4 (line 239) and never reads the preference, so the effective cap is min(preference, 4) - lowering the preference would bind, raising it above 4 is silently ignored at the scheduling stage. In the shipped app the asymmetry is latent rather than observable, because no UI writes the preference: 'DailyCap' has exactly one reference in src/, the read at ReminderRefreshService.cs:121, so it is always the default. That is the meal-toggle defect inverted - a preference with a reader and no writer. R2 unmet: only Workout, Meal and Hydration have toggles in NotificationSettingsPage, Meal is read by nothing, and the DailyCheckIn and StreakProtection categories the planner actually honours have no UI at all. R4 unmet: suppression reasons are computed and returned, then discarded - StreaksPageViewModel.cs:222 counts them and shows a single sentence; nothing is recorded to diagnostics. R5 and AC3 unmet: there is no Reduce frequency action and no Disable all action on the settings page, and no cap control of any kind.

##### S19.02.05 - Schedule rest timers and reminder snoozes without restricted exact alarms

**PARTIAL.**

*Evidence.* AC1 is satisfied and verifiable by reading: no exact-alarm permission appears in src/Forge.App/Platforms/Android/AndroidManifest.xml or in any assembly-level UsesPermission attribute, and all scheduling goes through AndroidScheduleMode.InexactAllowWhileIdle (LocalNotificationScheduler.cs:194). The scheduler already treats ForgeNotificationCategory.RestTimer as a special case exempt from quiet hours and the frequency cap (lines 210-213, 232-235) and maps it to NotificationCategoryType.Alarm.

*Gaps.* R1 unmet in practice: nothing ever schedules a rest-timer notification. ForgeNotificationCategory.RestTimer appears only inside LocalNotificationScheduler's own exemption branches and in ReminderRefreshService's pending-count filter (line 81); no code constructs a ForgeNotificationRequest with that category, so the rest timer is in-app only and a backgrounded user is never told the rest is over. R2, R3, R4 and AC2/AC3/AC4 unmet: snooze does not exist anywhere in the codebase - no snooze action, no snooze scheduling, no 15-minute default, and no late-delivery elapsed-time reconciliation.


#### F19.03 - Recover schedules after device changes

**PARTIAL.** Reminder intent is persisted independently of platform notification ids as a JSON store in Preferences (LocalNotificationScheduler.StoredNotification, lines 277-293), notification ids are derived deterministically from the stable id by SHA-256 (line 259-263) so a rebuild is idempotent, and an Android BroadcastReceiver handles BOOT_COMPLETED and TIMEZONE_CHANGED (lines 296-313).

_Gaps:_ iOS has no rebuild path at all, ACTION_TIME_CHANGED is not handled, and the rebuild bypasses the quiet-hours check.

##### S19.03.01 - Rebuild reminder schedules after device reboot

**PARTIAL.**

*Evidence.* R1 is satisfied: intent is stored as StableId plus the intended local delivery instant, with the platform NotificationId derived from the stable id rather than stored authoritatively (LocalNotificationScheduler.cs:259-263). R2 is satisfied: NotificationRescheduleReceiver (lines 296-313) is registered with an IntentFilter for android.intent.action.BOOT_COMPLETED and calls ReschedulePersistedAsync, and RECEIVE_BOOT_COMPLETED is declared at line 16. R4 is satisfied: ReschedulePersistedAsync rebuilds from the same stable ids and the same derived notification ids, so re-running it produces no duplicates, and past-dated entries are filtered out (line 159).

*Gaps.* R3 and AC2 unmet: nothing on iOS calls ReschedulePersistedAsync - its only callers are the Android receiver and its own internal helper - so an iOS reboot loses the schedule until the user happens to press the button on the Consistency screen. AC4 unmet: ReschedulePersistedAsync calls ShowAsync directly (line 165) and never consults IsSuppressedByQuietHours, so a reboot during quiet hours re-registers notifications that the normal path would have refused. AC3's two-second rebuild timing cannot be judged by reading and is left out of this verdict.

##### S19.03.02 - Recalculate reminders after timezone and daylight-saving changes

**PARTIAL.**

*Evidence.* R4 is implemented deliberately and documented: ReminderSchedulingPolicy.ResolveWallClock (lines 183-195) advances a wall time that falls in a DST spring-forward gap to the next valid minute and re-derives the offset, and the remarks explain that the result is still checked against quiet hours so a 02:30 reminder never becomes a surprise 03:00 one. R2 is half-implemented: the receiver's IntentFilter includes android.intent.action.TIMEZONE_CHANGED and rebuilds with NotificationRescheduleReason.TimeZoneChanged, and StoredNotification.WithLocalOffset re-projects the stored wall-clock DateTime onto the current offset (line 291-292). R5 is satisfied: past-dated entries are filtered out before rescheduling (line 159). AC4 holds because each rebuild keys on the same stable id.

*Gaps.* R1 partially unmet: the stored intent keeps a local wall-clock DateTimeOffset but not the timezone id it was projected from, so nothing can detect that the zone changed - the code relies on receiving the broadcast. R2 unmet in part: ACTION_TIME_CHANGED is not in the IntentFilter (only BOOT_COMPLETED and TIMEZONE_CHANGED, line 298), so a manual clock change does not rebuild. R3 and AC1 unmet: iOS has no resume-time timezone comparison and no rebuild trigger at all, so the Europe/Paris to America/New_York case is unhandled on that platform.


#### F19.04 - Make notifications actionable and informative

**PARTIAL.** Notification copy itself is safe by construction - every body is a fixed literal built in ReminderSchedulingPolicy, and no health value can reach one because ReminderUserSnapshot carries none - which is the substance of S19.04.02's R1 and R2. Beyond that, notifications are display-only: LocalNotificationScheduler.ShowAsync (lines 178-206) builds a NotificationRequest with a title, subtitle, description and a ReturningData payload but registers no action set, and nothing anywhere in src/ subscribes to notification tap or action events - NotificationTapped and NotificationActionTapped have zero references across the repository.

_Gaps:_ S19.04.01 and S19.04.03 fail on the same missing piece: there is no handler, so no action can be routed, no cold-start payload can be processed, and no foreground / system-shade coordination is possible. The ReturningData payload is serialised on every notification and read by nothing. There is also no discreet-notification mode and no in-app prompt surface.

##### S19.04.01 - Handle start workout, log water and snooze actions from notifications

**NOT-DONE.**

*Evidence.* No notification action exists. ShowAsync sets NotificationRequest.CategoryType and Android channel options but never an action list, and LocalNotificationCenter tap/action events are never subscribed - a repository-wide search for NotificationTapped and NotificationActionTapped returns nothing. LocalNotificationScheduler.cs:186 does serialise a NotificationReturnData(StableId, Category) payload into ReturningData, but nothing ever deserialises it: the private record at line 275 has no reader.

*Gaps.* All of R1-R5 and AC1-AC4 unimplemented: no Start workout or Log water action, no snooze at all, no cold-start routing, and no local action analytics. The payload plumbing exists in one direction only, which is the shape most likely to be mistaken for a finished feature.

##### S19.04.02 - Add rich notification content without exposing sensitive data

**PARTIAL.**

*Evidence.* R1 and R2 hold by construction. Every notification body in the app is a fixed literal built in ReminderSchedulingPolicy (lines 220-228, 248-254, 274-280, 300-306); the only interpolated value in any of them is the planned workout day name (line 227), and no bodyweight, calorie, macro, heart-rate or free-text note reaches a notification because ReminderUserSnapshot carries no such field. AC2 is therefore satisfied for the categories that exist.

*Gaps.* R3 unmet: there is no discreet-notification setting - NotificationSettingsPageViewModel exposes only three category toggles and quiet hours, and ShowAsync has no discreet branch, so AC1 and AC4 cannot occur. R4 unmet: the only notification test file in the repository is tests/Forge.Core.Tests/Notifications/ReminderSchedulingPolicyTests.cs, whose four tests cover quiet hours, the daily cap, already-completed suppression and DST resolution - none asserts that a template rejects a sensitive placeholder token, so AC3's failing-test guarantee does not exist. AC2's meal case is vacuous because meal reminders are not implemented.

##### S19.04.03 - Coordinate foreground notifications with in-app prompts

**NOT-DONE.**

*Evidence.* There is no foreground coordination. Nothing subscribes to notification delivery while the app is active, there is no in-app prompt component, and there is no notification history in which a 'Dismissed in app' state could be recorded - LocalNotificationScheduler's Preferences store holds only pending intent and drops entries once they are cancelled or past.

*Gaps.* All of R1-R4 and AC1-AC4 unimplemented: duplicate system banners are not suppressed while foregrounded, no in-app prompt carries the corresponding primary action, and no dismissal state is persisted.


#### F19.05 - Provide an in-app notification centre

**NOT-DONE.** There is no notification centre and no reminder history. No page, route or view model in src/Forge.App/Features exposes delivered, missed or suppressed reminders; LocalNotificationScheduler.GetPendingAsync (lines 139-150) can list future pending intent but has exactly one caller - ReminderRefreshService.cs:79, which uses it only to count today's already-scheduled items for the cap.

_Gaps:_ All three stories are unimplemented. Suppression reasons are computed by the planner and thrown away, so even the data a centre would show is not retained.

##### S19.05.01 - Show recent and upcoming reminders in a notification centre

**NOT-DONE.**

*Evidence.* No notification centre exists: no NotificationCentre page, no route registration and no reminder-history entity or table in any EF configuration or migration. The only listing capability is GetPendingAsync, which returns future non-suppressed items from a Preferences JSON blob and is never bound to a screen.

*Gaps.* All of R1-R5 and AC1-AC4 unimplemented: no delivered/missed/suppressed/upcoming grouping, no Today/Earlier/Upcoming sections, no per-row state or action, no empty state, and no 90-day retention cleanup.

##### S19.05.02 - Add reduce-frequency controls from reminder history

**NOT-DONE.**

*Evidence.* There is no reminder history to act on and no frequency-reduction control anywhere. NotificationSettingsPage.xaml offers three on/off checkboxes and quiet-hours times; there is no per-category frequency setting, and ReminderPreferences (ReminderSchedulingPolicy.cs:17-28) models a single DailyNotificationCap with no per-category granularity.

*Gaps.* All requirements and acceptance criteria unimplemented: no Reduce this category action on a delivered row, because neither the row nor the action exists.

##### S19.05.03 - Surface missed reminders from Today without increasing notification volume

**NOT-DONE.**

*Evidence.* Missed reminders are never surfaced on Today. TodayViewModel and InsightsDataService.BuildTodaySummary (InsightsDataService.cs:545-601) compose the session title, rings, a focus action and recent activity from sets, workouts, hydration and plans only - no reminder state is read, and no reminder history exists to read.

*Gaps.* All requirements and acceptance criteria unimplemented: nothing distinguishes a missed reminder from one that was never scheduled, and Today has no reminder surface of any kind.


### E20 - Gamification, Streaks and Achievements

**Epic verdict: PARTIAL.** This is the healthiest epic in the range and the only one with a screen verified working on a device. src/Forge.Domain/Engagement/ holds a coherent, documented design: AchievementEvaluator awards ten badges from the active profile's own logged training with measured (never estimated) progress, TrainingRhythmAnalyzer delegates to ConsistencyAnalyzer so two screens cannot disagree about weeks, Streak stores no counter at all and recomputes everything from workout rows, and EngagementEthicsPolicy enforces two prohibited-copy lists that the tests assert every producible string against. AchievementsPage.xaml renders those badges with per-badge rings, a plain-language rule, a why-this-matters line and a share action; StreaksPage.xaml renders weekly rhythm, protected periods and a gamification toggle. Both are routed (EngagementFeatureRegistration.cs:42-43) and reachable from ProgressViewModel.cs:30-31.

_Gaps:_ Personal challenges do not exist in any form - F20.03 has no code whatsoever. Levels, points and long-term progression rings do not exist. The trophy cabinet has no filters and no badge detail. Share cards have no preview step before the OS share sheet and no theme choice, and the share path calls Share.RequestAsync while the PNG's FileStream is still open. Several F20.01 and F20.02 criteria are unmet because Forge deliberately removed the mechanics they describe; those are backlog defects rather than code gaps and are called out per story.

#### F20.01 - Design forgiving streaks that support recovery

**PARTIAL.** Forge replaced the daily streak with a weekly rhythm and documented the reasoning in docs/design/engagement-ethics.md. src/Forge.Domain/Engagement/TrainingRhythmAnalyzer.cs counts weeks containing any session, and Streak.ProtectedPeriod lets a user declare illness, injury, a planned deload or life so those weeks are stepped over rather than counted against them; StreaksPage.xaml surfaces the run, the protection state and supportive copy from EngagementEthicsPolicy.

_Gaps:_ Judged against the criteria as written, the day-level mechanics they describe do not exist: no per-day streak, no freezes and no 48-hour recovery window. The doc justifies the first two explicitly; the recovery window is simply absent.

##### S20.01.01 - Count planned rest days as streak-preserving days

**PARTIAL.**

*Evidence.* Rest days cannot break the count, which is the outcome this story wants: TrainingRhythmAnalyzer works in weeks and a week counts if it contained any session, so an ordinary rest day is invisible to the calculation, and docs/design/engagement-ethics.md lines 22-42 records that decision and why. R3's 'display the reason a day was preserved' is met at week granularity - StreaksPageViewModel.cs:270-275 emits a per-week row with a 'Protected' status and a description, and line 243-245 states how many weeks were stepped over. R4 is met: all dates are local DateOnly values derived from workout rows, and nothing is stored as a counter (engagement-ethics.md lines 66-73), so a timezone change cannot move a historical day.

*Gaps.* R1 and AC1/AC2/AC4 unmet as written: there is no day-level streak, so a scheduled rest day does not 'preserve' anything at day granularity, no two-day ring exists, and a completed planned recovery habit is not a concept in the code. Backlog defect rather than code defect: docs/design/engagement-ethics.md is an explicit, reasoned decision to remove the daily streak (Streak has no CurrentDays, BestDays or LastCountedDate, and StreakTests asserts their absence by reflection). The criteria should be rewritten in weeks; the code should not be changed to match them.

##### S20.01.02 - Add streak freezes with transparent limits

**DEFERRED.**

*Evidence.* docs/design/engagement-ethics.md lines 107-109 removes this mechanic by name and gives the reason: 'Streak freezes. A limited supply of forgiveness still frames recovery as consuming a scarce resource, and it still runs out on the person who needed it most. FreezesRemaining is gone. The replacement is unlimited and free, because rest is not something anybody should have to spend.' The removal is enforced, not merely documented - Streak (src/Forge.Domain/Engagement/Streak.cs) carries no freeze field, and the doc records that StreakTests asserts the absence of the removed members by reflection. The replacement, unlimited ProtectedPeriod declarations, is implemented and reachable from StreaksPage.

##### S20.01.03 - Offer a compassionate missed-day recovery path

**PARTIAL.**

*Evidence.* The compassionate path exists in a different shape: StreaksPageViewModel.MarkProtectedAsync (lines 177-187) lets the user declare a TrainingInterruption for today onward, EndProtectionAsync closes it, and while it runs the screen states that the record is unchanged (Apply, lines 261-265). R3 is enforced mechanically rather than by review: EngagementEthicsPolicy.ProhibitedPressureTerms lists 'failed', 'lazy', 'lost everything' and similar as phrases, and the engagement tests assert every string those screens can produce against it, which is what AC3 asks for. R4 is trivially satisfied because no recovery action adds volume - none exists.

*Gaps.* R1 and AC1/AC4 unmet: there is no 48-hour recovery window, no missed-day detection and no restoration flow. Nothing is offered on Today - the protection action lives on the Consistency screen and must be found and pressed by the user, so someone who misses a day and opens Today sees no recovery options at all. R2 unmet: reschedule-workout and complete-a-light-habit actions do not exist; the only choice is to declare a protected period. AC2 cannot occur.


#### F20.02 - Award achievements and progression fairly

**PARTIAL.** src/Forge.Domain/Engagement/AchievementEvaluator.cs and Achievement.cs define ten badges with deterministic local rules, measured progress and an earned timestamp, evaluated over the active profile's own data via EngagementDataService.cs:243-267 and rendered on AchievementsPage.xaml with a ring, the rule, the progress count and the reason the badge is good for the person. Verified on device by the requester: ten cards render from genuinely logged data with progress fractions matching real rows.

_Gaps:_ Strength and Volume badge categories were deliberately retired, levels and points do not exist, celebrations are static text with no motion, and there is no badge detail surface offering a next action.

##### S20.02.01 - Award badges across strength, consistency, volume and exploration

**PARTIAL.**

*Evidence.* R2 is fully met: every definition in src/Forge.Domain/Engagement/Achievement.cs has a deterministic local rule, a measured progress fraction and an earned timestamp, and AchievementsPageViewModel.ToCard (lines 158-176) shows progress and the earned date from the same count that decides the unlock - documented at lines 36-41 as measured, never estimated. R4 is met: the exploration badge rewards four distinct movement patterns, which spreads load rather than encouraging maximal attempts. AC4 is met: EngagementDataService gates evaluation on GamificationEnabled and AchievementsPageViewModel.Apply (lines 141-147) substitutes the disablement message, while nothing about logging changes.

*Gaps.* R1 and AC1 unmet as written: there is no Strength category and no Volume category. docs/design/engagement-ethics.md lines 116-122 retires both deliberately - a total-volume badge 'rewards more, which in practice means junk volume and overuse injury', and a personal-record badge 'rewards attempting a maximal single, which is the highest-risk thing an untrained lifter can do'. The doc goes further than a rule list: EngagementMetrics omits total volume and PR counts entirely so such a rule cannot be written by accident. Backlog defect: R1/R3 and AC1 should be rewritten, since the code's position is better reasoned than the criterion. AC3's 500-event timing cannot be judged by reading and is left out of this verdict.

##### S20.02.02 - Add levels and long-term progression rings

**NOT-DONE.**

*Evidence.* There is no level system. Searched for a level, point or experience concept in the engagement domain: Achievement.cs, AchievementEvaluator.cs, EngagementMetricsBuilder.cs and Streak.cs contain none, and the only 'Level' matches in src/ are unrelated (log levels in ForgeStartup.cs, app-lock levels, and a media pack level). AchievementsPage.xaml:81-87 draws a dx:RadialProgressBar per badge, but that is per-badge progress, not a long-term progression ring, and no screen shows a current level, points to the next level or a source explanation.

*Gaps.* All of R1-R4 and AC1-AC4 unimplemented: no point sources, no per-day point cap to prevent grinding volume, no level state and no explanation popup. Note that a points-and-levels mechanic would need to be reconciled with docs/design/engagement-ethics.md before it is built, since a cumulative points ladder is close to the total-volume badge that document retires.

##### S20.02.03 - Celebrate milestones with coordinated motion

**PARTIAL.**

*Evidence.* A celebration surface exists and is driven by real state: EngagementSnapshot.NewlyEarned feeds AchievementsPageViewModel.Apply (lines 149-155), which sets HasCelebration and a 'New: <title>' sentence, and AchievementsPage.xaml:48-52 renders it as a card with a SemanticProperties.Description. Because it is a static label, R1's reduced-motion outcome and AC1/AC4 are satisfied by construction - there is no confetti to suppress - and R4/AC3 hold because the celebration is a bound property that cannot block a save.

*Gaps.* R1's coordinated motion is not implemented: src/Forge.App/Motion/ForgeAnimations.cs:159 and MotionTokens.cs:13,25 define a celebration motion token, and the Achievements page never calls it - a repository search shows no animation invocation from AchievementsPage.xaml.cs, so the token is unused by this surface. R3 unmet: the celebration card has no dismiss control; it disappears only on the next load. R2's three-second bound and R5's haptics are not applicable because no timed celebration and no haptic call exist, and AC2's timing cannot be judged by reading.

##### S20.02.04 - Explain badge rules and next safe actions

**PARTIAL.**

*Evidence.* R1 is met on the card itself: every badge shows Description (the rule in plain language), the measured progress count as ProgressDetail, and a WhyItMatters line, all bound in AchievementsPage.xaml:89-97 and composed in AchievementsPageViewModel.ToCard (lines 158-176). R3 is met and is a deliberate position: there are no secret badges - all ten definitions are always listed with their thresholds, and docs/design/engagement-ethics.md tabulates every rule. R4 is addressed: each card carries a single AccessibleDescription covering title, category, description and earned state in visual order (line 175), with child labels not individually suppressed.

*Gaps.* R2 and AC1/AC2/AC4 unmet: there is no badge detail popup and no next-action generation at all. A locked badge shows its rule and progress but never suggests a next step, so the safety constraints the criteria attach to that suggestion (beginner-safe prerequisites, no volume increase above a bound) have nothing to apply to. AC3's screen-reader ordering cannot be fully judged by reading, since the card exposes one composed description rather than an ordered sequence of stops.


#### F20.03 - Support personal challenges without social pressure

**NOT-DONE.** Personal challenges do not exist. A repository-wide search for Challenge and PersonalChallenge returns zero matches in src/, tests/ and docs/. There is no challenge entity, no EF configuration, no migration column, no template catalogue, no page and no route.

_Gaps:_ All three stories are entirely unimplemented: no templates, no durations, no active-challenge limit, no progress tracking and no pause or resume.

##### S20.03.01 - Create personal challenges from safe templates

**NOT-DONE.**

*Evidence.* No challenge templates exist. Searched all of src/ for a challenge concept: no matches. The nearest surface, StreaksPage.xaml, offers only protected-period declaration and the gamification toggle; AchievementsPage.xaml offers only badge cards.

*Gaps.* All of R1-R5 and AC1-AC4 unimplemented: no workout, hydration, protein or mobility templates, no 7/14/28-day durations, no plan-constraint clamping and no three-active limit. R5's absence of invite or leaderboard surfaces is vacuously true because no challenge surface exists.

##### S20.03.02 - Track challenge progress and completion locally

**NOT-DONE.**

*Evidence.* No challenge progress tracking exists, and nothing in the codebase subscribes to workout, nutrition, hydration or recovery persistence events - EngagementDataService.RefreshAsync recomputes from tables on demand when a screen loads, and there is no event pipeline a challenge projection could hang off.

*Gaps.* All of R1-R4 and AC1-AC4 unimplemented: no progress updates, no completed/remaining day counts, no abandon-with-history behaviour and no challenge-completion achievements.

##### S20.03.03 - Pause and resume challenges without penalty

**NOT-DONE.**

*Evidence.* No pause or resume exists because no challenge exists. The only pause-like concept in the engagement domain is Streak.ProtectedPeriod, which suspends judgement of training weeks, not of a challenge, and ConsistencyAnalyzer.PausedAfterDays (line 107) which affects rhythm copy only.

*Gaps.* All of R1-R4 and AC1-AC4 unimplemented. R4's neutral copy requirement would be satisfied by EngagementEthicsPolicy if a pause surface existed, but it does not.


#### F20.04 - Present trophies and shareable achievements

**PARTIAL.** A trophy cabinet and an on-device share card both exist and are reachable. AchievementsPage.xaml lists every badge, earned and locked, with a starter empty state, and AchievementsPage.xaml.cs:30-53 captures a hidden fixed-width DXBorder to a PNG and hands it to the platform share sheet with no network involved. The share path re-checks the copy against EngagementEthicsPolicy.IsPublishable before raising (AchievementsPageViewModel.cs:118-125).

_Gaps:_ No filters and no badge detail; no preview before the share sheet; no theme choice; no cache cleanup; and the file is shared while its stream is still open.

##### S20.04.01 - Build a trophy cabinet with filters and empty states

**PARTIAL.**

*Evidence.* R2 is largely met: every card shows the rule, the measured progress, and either the earned date or 'Progress: N of M' (AchievementsPageViewModel.cs:161-163). R3 is partly met: AchievementsPage.xaml:54-57 shows a controls:EmptyState for a brand-new user with starter guidance rather than a blank list, satisfying AC1. The XAML comment at lines 59-65 documents a considered layout decision - a BindableLayout rather than a fixed-height DXCollectionView nested in a ScrollView - which is the right call for correctness even though it costs virtualisation.

*Gaps.* R1 and AC2/AC4 unmet: there are no earned / in-progress / locked filters at all, so there is no filter-specific empty state and no Clear filter action. R2's 'safe next step if not earned' is not implemented (see S20.02.04). R4 and AC3 unmet structurally rather than by measurement: the BindableLayout inside a ScrollView materialises every card, so 200 definitions would all be realised at once; no measurement is needed to say the virtualisation the criterion assumes is absent.

##### S20.04.02 - Generate shareable achievement cards on device

**PARTIAL.**

*Evidence.* R1 is met and AC2/AC4 hold: AchievementsPage.xaml.cs:30-53 binds the hidden ShareCard DXBorder to the selected achievement, calls CaptureAsync, writes the PNG into FileSystem.CacheDirectory and opens the share sheet - no network call is involved anywhere in the path. R2 is met: the card shows title, category, description and 'Forge - earned locally' branding (AchievementsPage.xaml:116-127) and no health metric. The share is gated on IsUnlocked and re-validated against the ethics policy before it can leave the device (AchievementsPageViewModel.cs:121). The XAML comment at lines 9-12 records that the card is a plain DXBorder rather than a ContentPresenter, avoiding the binding-context trap.

*Gaps.* R3 and AC1 unmet: there is no preview step - OnShareRequested makes the card visible, captures it, hides it again and calls Share.RequestAsync directly, so the user never sees the exact image before the share sheet opens. R4 unmet: the PNG is written to the cache directory with a timestamped name and never deleted; there is no seven-day or any other cleanup. Likely defect worth a device check: at lines 43-52 the FileStream created by File.Create is an 'await using' local whose disposal runs at the end of the method, so Share.RequestAsync is invoked while the stream is still open and possibly unflushed - the shared PNG may be truncated or empty. R5's two-second timing cannot be judged by reading.

##### S20.04.03 - Let users choose privacy-safe share card themes

**NOT-DONE.**

*Evidence.* There is no share-card theming. AchievementsPage.xaml:116-127 defines exactly one ShareCard layout with a fixed ShareCardWidth and the app's standard ElevatedCard style; there is no theme model, no theme picker, no preview surface to update, and no entitlement gate on sharing (src/Forge.Domain/Commerce/FeatureGate.cs is not referenced by the engagement feature).

*Gaps.* All of R1-R4 and AC1-AC4 unimplemented: no bundled themes, no preview update, no premium gate with a free default, and no theme validation rejecting sensitive fields. R2's prohibition on hidden personal data fields is vacuously satisfied by the single hard-coded card.


#### F20.05 - Enforce ethical gamification controls

**PARTIAL.** Both halves of this feature have real substance. Streak.GamificationEnabled (src/Forge.Domain/Engagement/Streak.cs:109, 123-126) suppresses badge evaluation and rhythm framing and is toggleable from StreaksPage; EngagementEthicsPolicy defines ProhibitedPressureTerms and ProhibitedRewardPatterns as checkable data with a documented rationale, and the share path re-validates against it before any copy leaves the device.

_Gaps:_ The disable switch lives only on the Consistency screen, not in Settings, and the Achievements screen offers no path to it; and reminders are only partly gated by it.

##### S20.05.01 - Make gamification fully disableable without breaking training

**PARTIAL.**

*Evidence.* R1 is largely met for the surfaces that exist: EngagementDataService.SetGamificationEnabledAsync flips Streak.GamificationEnabled, AchievementsPageViewModel.Apply (lines 141-147) replaces the badge summary with EngagementEthicsPolicy.GamificationDisablementMessage, StreaksPageViewModel binds GamificationDisabled, and Streak.AllowsSupportiveReminders (line 229) returns false when it is off so the streak-protection notification is not scheduled. R2 is met: the flag is a preference on the Streak row and no achievement row is deleted, so AC3's restoration is simply the next recompute. R3 and AC2/AC4 are met by construction: nothing in the workout, nutrition, hydration or recovery paths reads GamificationEnabled, so logging is unaffected.

*Gaps.* R1 partially unmet: levels and challenge prompts do not exist to be disabled, and the flag does not reach Today or Progress - ProgressViewModel.cs:30-31 always lists Consistency and Achievements as destinations regardless of the setting, so AC1's 'streak rings, badge prompts and level cards are absent' is only true once those screens are opened. The toggle is not surfaced in Settings at all; it exists solely on the Consistency screen. AC4's 'zero badges' is satisfied for evaluation but not asserted anywhere outside the domain tests.

##### S20.05.02 - Block dark patterns in gamification rules and copy

**PARTIAL.**

*Evidence.* This is enforced as a mechanism rather than a guideline. src/Forge.Domain/Engagement/EngagementEthicsPolicy.cs holds two lists doing different jobs - ProhibitedPressureTerms for shaming and urgency copy, listed as phrases so ordinary sentences are not blocked by accident, and ProhibitedRewardPatterns for copy that is pleasant while rewarding unsafe behaviour - with the reasoning documented in the type's remarks and in docs/design/engagement-ethics.md lines 99-146. R2 is enforced structurally: freezes, expiring badges and purchasable protection were removed from the domain, and EngagementMetrics deliberately omits total volume, PR counts and consecutive training days so a banned rule cannot be written. R4 is met: every definition declares a WhyItMatters string that is rendered on the card. R5 is met in practice - the policy assertions live in Forge.Domain.Tests, which .github/workflows/ci.yml runs, so introducing banned copy fails the build.

*Gaps.* R3 and AC3 unmet: the Achievements screen has no path to reduce or disable gamification - AchievementsPage.xaml offers no toggle and no overflow action, so a user on that surface must navigate back to Progress and then into Consistency, which is more than two taps. The disable control is also absent from Settings.
