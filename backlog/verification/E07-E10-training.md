# Backlog verification: E07-E10 (training core)

Read-only reconciliation of `backlog/epics/E07-*.yml` through `E10-*.yml` against the code on `nikomix/feature/verify-e07-e10-training` (branched from `main` at `3b31c68`). No application code was changed. Verdicts are judged against each story's own `requirements` and `acceptanceCriteria`; `implementation.notes` paths were treated as hints, and most of them are wrong about where the code actually lives.

A feature is DONE only when every story under it is; an epic only when every feature is.

> **Re-verified after `10c0888`, `e4d971b` and `e41e311`.** This report was written at 19:11; three
> commits landed after it and closed gaps it had recorded. Headline finding 1 below is no longer
> true, `S07.03.03` and `S08.02.01` moved NOT-DONE to PARTIAL, and 43 records had their evidence
> re-cited. The summary table and the story sections reflect the re-verified state.

## Summary

| Epic | Title | Stories | DONE | PARTIAL | NOT-DONE | DEFERRED | UNCLEAR |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| E07 | Exercise Library and Content Catalogue | 20 | 0 | 16 | 4 | 0 | 0 |
| E08 | Exercise Media, Video and Form Guidance | 20 | 2 | 8 | 10 | 0 | 0 |
| E09 | Training Plan Builder and Programmes | 20 | 0 | 13 | 7 | 0 | 0 |
| E10 | Workout Execution and Exercise Mode | 22 | 5 | 10 | 7 | 0 | 0 |
| **Total** | | **82** | **7** | **47** | **28** | **0** | **0** |

## Headline findings

Four things a reader of this backlog would reasonably assume are working, and are not.

**1. ~~No plan ever drives a workout, and the target shown mid-set is a constant.~~ Fixed by `10c0888`.**
This was true when the report was written and is not true now. `TrainViewModel.cs:195-198` passes a
plan-day id into the active workout route, `WorkoutPersistenceService.cs:210-239` builds the session
queue from that plan day and records `TrainingPlanId`/`PlanDayId` on the session, and
`PlanWorkoutProjection.cs:61-91` copies the planned sets, load, reps and rest onto each queued
exercise, so the "Target" tile now shows a prescription rather than a hard-coded 60 kg and 8 reps.
The schema change is carried by migration `20260822204327_PlanWorkoutLink`. What remains is
presentation rather than wiring: "Set 1 of 3", target RPE and a prescribed-rest line are still
missing above the fold (`S10.01.01`), and a completed session still links to a plan day but not to
the plan *version* that produced it (`S09.05.02`).

**2. Exercise video is wired end to end and can never play.**
`ExerciseMediaCatalogue` resolves media only from `IMediaCache`
(`src/Forge.Infrastructure/Media/ExerciseMediaCatalogue.cs:14-27`), and `IMediaCache.DownloadAsync` is
called from nowhere in `src` — only from tests. So the cache is always empty, `HasMedia` is always false,
and the whole `MediaElement` card is hidden by `IsVisible` (`ExerciseVideoPage.xaml:28`). The Play Asset
Delivery path that does download packs is a *different service*: the detail page gates its Watch button on
`IMediaPackService` (`ExerciseVideoAvailability.cs:34-58`) while the video page reads `IMediaCache`. A user
who downloads a video pack gets a Watch button that leads to "No motion asset is installed for this
exercise in v1." Separately, the source string is built as `embed://…` / `filesystem://…`
(`ExerciseVideoViewModel.cs:111-116`), which a `MediaSource` converter resolves as an absolute non-file
URI rather than a local file — so even a populated cache would not play.

**3. Injury-aware filtering exists, is unit-tested, and is unreachable.**
`ExerciseFilter.FromDeclaredInjuries` maps declared injuries to excluded movement patterns and `Matches`
honours them (`src/Forge.Domain/Training/ExerciseFilter.cs:50-62, :110-133, :142-145`). Its only callers
are `tests/Forge.Domain.Tests/Training/ExerciseFilterTests.cs:68` and `:79`. The library's
`BuildFilter()` omits the `injuries` argument entirely
(`src/Forge.App/Features/Exercises/ExerciseLibraryViewModel.cs:528-533`), and `UserProfile.MovementLimitations`
is free text that onboarding collects and nothing consumes. The safety story that E07 leans on — including
its "not medical advice" gate — does not exist at runtime.

**4. "Compare against last time" on the workout summary is a placeholder sentence.**
`Comparison` is initialised to *"You showed up. Next time Forge will compare this against your previous
effort."* (`src/Forge.App/Features/Workout/WorkoutSummaryPageViewModel.cs:47-48`) and is only ever
overwritten by the error message at `:137`. No delta for top working set, total volume or completed sets
is computed anywhere. PR detection beside it is real and correctly scoped, which makes the placeholder
read as though the comparison is real too.

Also worth naming: `PlanScheduler.ShiftForMissedSession` is implemented and tested but called from nowhere
(`src/Forge.Domain/Planning/Scheduling.cs:25-44`), and the schedule page renders a "Shifted" label that can
never become visible (`PlanSchedulePage.xaml:42`). And the plan editor cannot edit a single target value —
sets, reps, load, RPE and rest are all display-only, and *Add exercise* inserts a literal "New exercise"
(`PlansFeatureViewModels.cs:222-252`).

### Where the backlog is wrong rather than the code

- **E07/E08 assume 800 exercises.** The app deliberately ships 60 original, fully-written entries
  (`src/Forge.Infrastructure/Content/exercise-catalogue.json`), and `docs/media-strategy.md` reasons about
  60 throughout. Every "800 exercises" and "800 x 6 MB = 4800 MB" criterion is therefore unmeetable as
  written. Stories are marked PARTIAL on their substantive gaps, not on the count alone.
- **E09's push:pull threshold.** Every `AC5` in E09 specifies 2:1; `VolumeBalanceAnalyzer` uses 1.5:1 with
  a written justification (`src/Forge.Domain/Planning/VolumeBalanceAnalyzer.cs:17-23`). The code is
  arguably the better rule; the two should be reconciled deliberately.
- **S10.03.01's AC1** expects *Repeat last* to fill a draft; the implementation commits the set outright,
  which is a defensible product decision that the criterion does not allow for.

### Where a second opinion would help

- **S08.02.01** — I concluded video playback is dead end-to-end from static reading. **Resolved by
  `e4d971b`:** playback now resolves store-delivered pack files through `MediaSource.FromFile`, and
  the story is PARTIAL rather than NOT-DONE. A device run is still worth doing to confirm the
  download-then-watch journey end to end.
- **S10.02.03** (keep-awake) — I marked it DONE because both acceptance criteria pass, even though its
  "pausing beyond 15 minutes" requirement is vacuous, pause having never been built (S10.05.02).
  **Resolved:** hold DONE, and the vacuous-AC signal is now recorded as a note against S10.05.02, so the
  pause gap carries the evidence that two other stories lean on it.
- **S07.05.01** — substitution is the strongest code in E07, but scoring omits mechanic and contraindication
  tags because neither is modelled. PARTIAL may read harshly for what is otherwise a complete piece of work.

### Not verifiable by reading

Every frame-rate, millisecond and text-scale criterion in these four epics (60 fps scrolling, 150 ms search,
100 ms commit, 300/500 ms render budgets, 200 percent text scale, 4.5:1 contrast) was left out of the
verdicts rather than credited or failed. Where a story's substantive behaviour is present and only its
budget is unmeasured, that is stated in the reasoning and the story is not marked down for it.


---

## E07 — Exercise Library and Content Catalogue

### F07.01 — Model exercise metadata and seed provenance — **PARTIAL**

#### S07.01.01 — Define exercise taxonomy and required metadata — **PARTIAL**

Exercise carries primary/secondary muscles, equipment, pattern, force type, difficulty and a laterality flag, and Forge.Domain references neither MAUI nor DevExpress (AC2). Mechanic, plane of motion, goal tags and contraindication tags do not exist, and no validation rejects a seed row missing primaryMuscles before the SQLite write (AC1).

*Evidence:* src/Forge.Domain/Training/Exercise.cs:7-66; src/Forge.Domain/Training/MovementPattern.cs:8-42; src/Forge.Infrastructure/Persistence/SeedContent/SeedContentImporter.cs:42-83 writes with no field validation

*Gaps:* No mechanic, plane of motion, goal tags or contraindication tags on Exercise; AC1 pre-write validation absent; AC3/AC4 never measured against an 800-record catalogue because only 60 exist.

#### S07.01.02 — Import an 800 exercise offline seed catalogue — **PARTIAL**

The import is genuinely versioned, idempotent and offline, and it preserves user-created rows across a catalogue refresh (AC2 behaviour is implemented and commented). But the catalogue holds 60 exercises, not 800, and ships as plain embedded JSON rather than compressed exercises.v1.json.br.

*Evidence:* src/Forge.Infrastructure/Content/exercise-catalogue.json (60 records, 89 KB, version 2); src/Forge.Infrastructure/Persistence/SeedContent/SeedContentImporter.cs:30-83; src/Forge.App/Composition/ForgeStartup.cs:161-171

*Gaps:* 60 of 800 exercises (AC1 fails); no Brotli-compressed asset and so no 8 MB package measurement; no resumable/progress-reporting import.

#### S07.01.03 — Gate seed content through licensing provenance — **PARTIAL**

Provenance is real and enforced: the catalogue and every record must declare original-Forge provenance or loading throws. But provenance is one free-text string - there is no licence identifier, reviewer status or copiedSource flag - and no tools/catalogue-lint command exists, so neither acceptance criterion can be executed.

*Evidence:* src/Forge.Infrastructure/Content/SeedCatalogue.cs:69-73 and :103-107 throw without original-content provenance; src/Forge.Infrastructure/Content/exercise-catalogue.json has a provenance string per record; tools/ contains no catalogue-lint

*Gaps:* No licence identifier, reviewer status or copiedSource field; no lint command, so AC1 (non-zero exit naming the exercise key) and AC2 (provenance report) are unimplementable; packaging is not gated.

#### S07.01.04 — Preserve custom exercises beside seeded records — **PARTIAL**

Custom exercises can be created, edited and deleted from the library, IsUserCreated is stamped by the store rather than trusted from the caller, and the seed importer skips user-created rows so a catalogue update cannot overwrite them. Name uniqueness is never checked, and deletion is not blocked when workout history references the exercise.

*Evidence:* src/Forge.App/Features/Exercises/ExerciseLibraryViewModel.cs:311-414; src/Forge.App/Features/Exercises/ExerciseDataStore.cs:66-92 stamps IsUserCreated; ExerciseDataStore.cs:118-150 soft-deletes with no history check; SeedContentImporter.cs:55-59

*Gaps:* No case-insensitive uniqueness rule for custom names; AC2 fails - deletion is never blocked and Archive is never offered, DeleteCustomAsync soft-deletes any user-created row regardless of history.

### F07.02 — Build browse and discovery surfaces — **PARTIAL**

#### S07.02.01 — Render library home with virtualized sections — **PARTIAL**

The library is a single virtualized dx:DXCollectionView with a skeleton placeholder while loading, and the search/filter path is off the UI thread. It is one flat ranked list with a collapsible chip panel, not the sectioned home the story describes, and there is no Goals axis anywhere.

*Evidence:* src/Forge.App/Features/Exercises/ExerciseLibraryPage.xaml:285 (DXCollectionView), :97-178 (chip panel), :270-274 (skeleton); src/Forge.App/Features/Exercises/ExerciseLibraryViewModel.cs:47-78

*Gaps:* No Body / Equipment / Patterns / Goals / Favourites / Recently Used sections - Favourites, Recently used and My exercises are mutually exclusive scope chips; no Goals taxonomy exists; AC1 500 ms and AC2 60 fps unmeasured.

#### S07.02.02 — Add interactive body-map muscle browsing — **NOT-DONE**

There is no body map anywhere in the repository - no drawable, no view, no region model and no accessible text fallback list of muscle regions.

*Evidence:* Repository-wide search for BodyMap / body-map across src, tools and tests returns zero matches; the only muscle affordance is the alphabetical MuscleChips list in src/Forge.App/Features/Exercises/ExerciseLibraryViewModel.cs:567-570

*Gaps:* Entire story unimplemented: no front/back views, no 14 selectable regions, no screen-reader region announcements.

#### S07.02.03 — Browse by equipment, movement pattern and goal — **PARTIAL**

Equipment and movement-pattern chips are derived from the loaded catalogue and combine OR-within-axis / AND-across-axes, and selections survive a reload. There is no goal taxonomy, and no chip shows a count.

*Evidence:* src/Forge.App/Features/Exercises/ExerciseLibraryViewModel.cs:554-582 builds chips; src/Forge.Domain/Training/ExerciseFilter.cs:138-176 applies them; src/Forge.App/Features/Exercises/ExerciseLibraryPage.xaml:116-162

*Gaps:* AC2 fails - no facet counts are computed or displayed; goal browse (strength, hypertrophy, endurance, mobility, power, warm-up, joint-friendly) does not exist; chips are DXButton, not dx:TokenEdit.

#### S07.02.04 — Handle first-run import and no-result states — **PARTIAL**

A no-result empty state and a Clear action both exist, and the versioned importer makes a kill mid-import safe against duplicates because rows are keyed by seed id and the version marker is written last. The empty state does not enumerate the active filters, and there is no import progress or retry surface.

*Evidence:* src/Forge.App/Features/Exercises/ExerciseLibraryPage.xaml:276-283 (empty states), :69-74 (Clear); src/Forge.App/Features/Exercises/ExerciseLibraryViewModel.cs:296-306, :538-552; SeedContentImporter.cs:42-83

*Gaps:* AC1 fails - the empty state message is a fixed sentence and lists no active filter tokens, and Clear Filters lives in the toolbar rather than in the empty state; no 0-100 import progress and no interaction lock during import; a failed import surfaces a message with no Retry or Diagnostic Details.

### F07.03 — Provide fast search and personal filters — **PARTIAL**

#### S07.03.01 — Add fuzzy search with aliases and gym slang — **PARTIAL**

Search is offline, debounced at 200 ms, runs off the UI thread against a prebuilt in-memory index, and ranks exact name above name prefix above word prefix above muscle/equipment/pattern. It indexes no aliases, no synonyms and no gym slang, and does no fuzzy matching, so the story's own worked example fails.

*Evidence:* src/Forge.Domain/Training/ExerciseSearchIndex.cs:61-79 (score tiers), :220-260 (Entry fields: name, muscles, equipment, pattern, difficulty only), :262-335; src/Forge.App/Features/Exercises/ExerciseLibraryViewModel.cs:497-526

*Gaps:* AC1 fails - 'RDL' matches nothing because no alias/slang field is indexed and matching is prefix/substring, not fuzzy; there is no FTS5 table; the search control is dx:TextEdit, not dx:AutoCompleteEdit.

#### S07.03.02 — Filter by available equipment — **PARTIAL**

Declared equipment is read from the profile and drives the alternatives screen, where a per-session override toggle re-ranks immediately. The library itself has only catalogue-derived equipment chips: there is no global-versus-session distinction, no Hide unavailable toggle and no unavailable state on a card.

*Evidence:* src/Forge.App/Features/Exercises/ExerciseAlternativesViewModel.cs:129-141 (chips seeded from profile, session override); src/Forge.App/Features/Exercises/ExerciseDataStore.cs:26-32; src/Forge.App/Features/Exercises/ExerciseCardViewModel.cs:11-71 has no availability member

*Gaps:* AC1 and AC2 both fail in the library - no Hide unavailable switch and no 'Missing Equipment' label naming the missing item; equipment chips are an inclusion filter, not an availability filter.

#### S07.03.03 — Respect declared injuries and movement limitations — **PARTIAL**

Upgraded from NOT-DONE by `e4d971b`. The report previously found limitation filtering that existed in the domain but never reached a user; the app now supplies it. `MovementLimitationDeclaration` interprets the free-text profile field, `ExerciseLibraryViewModel` builds a filter from the declared limitations by default, and the library carries both an unread-limitation prompt and not-medical-advice copy. What it does not have is the structured taxonomy the criteria describe: a declaration is mapped onto whole movement patterns, so an overhead or shoulder limitation hides Push and Pull wholesale — overhead press disappears, but so do neutral-grip rows and pull movements that the limitation does not implicate.

*Evidence:* src/Forge.Domain/Training/MovementLimitationDeclaration.cs:31-163; src/Forge.Domain/Training/ExerciseFilter.cs:50-62, :120-143, :152-155; src/Forge.App/Features/Exercises/ExerciseLibraryViewModel.cs:127-158, :580-586, :598-629; src/Forge.App/Features/Exercises/ExerciseLibraryPage.xaml:82-96

*Gaps:* AC1 only partially met - filtering is by movement pattern, not by structured overhead / deep-knee / spinal-loading / high-impact contraindication tags, so it over-hides; there is no Hide contraindicated / Show with warning / Ignore mode choice; AC2 fails because no first-use notice requires Continue before filters apply.

#### S07.03.04 — Persist favourites and recently used exercises — **PARTIAL**

Favourites toggle from both the card and the detail page and persist through the repository, and opening a detail page records LastUsedUtc in the one place every route into an exercise passes through, so both survive process death (AC1). Recently used is uncapped.

*Evidence:* src/Forge.Domain/Training/Exercise.cs:55-65; src/Forge.App/Features/Exercises/ExerciseDetailViewModel.cs:197-204; src/Forge.App/Features/Exercises/ExerciseLibraryViewModel.cs:416-431; src/Forge.Domain/Training/ExerciseFilter.cs:150

*Gaps:* AC2 fails - the RecentlyUsed scope is 'LastUsedUtc is not null' with no 30-item cap and no eviction, so every exercise ever opened stays in Recently used forever.

### F07.04 — Explain detail pages and safe execution — **PARTIAL**

#### S07.04.01 — Build detail page with muscle visualisation — **PARTIAL**

The detail page renders name, summary, pattern description, a labelled fact table (pattern, primary muscle, secondary muscles, equipment, difficulty, force, sides), derived setup, numbered steps, cues, mistakes and safety, entirely from the local database. Primary and secondary muscles are distinguished by label rather than colour, which satisfies that criterion. There is no muscle visualisation.

*Evidence:* src/Forge.App/Features/Exercises/ExerciseDetailViewModel.cs:215-246; src/Forge.Domain/Training/ExerciseGuidance.cs:156-170 labels 'Primary muscle' and 'Secondary muscles' separately; src/Forge.App/Features/Exercises/ExerciseDetailPage.xaml

*Gaps:* No muscle visual of any kind - the story asks for muscle visualisation and the page is text only; AC1's 300 ms open budget is unmeasured.

#### S07.04.02 — Add step-by-step execution and coaching cues — **PARTIAL**

Every one of the 60 seeded exercises has 3-9 execution steps and at least 2 coaching cues - I counted them in the shipped asset - and the detail page numbers the steps and renders the cues. Breathing timing and structured tempo do not exist as fields, so the second acceptance criterion cannot be met for any exercise.

*Evidence:* src/Forge.Infrastructure/Content/exercise-catalogue.json: 60/60 records have 3-9 executionSteps and >=2 coachingCues; src/Forge.App/Features/Exercises/ExerciseDetailViewModel.cs:229-234; src/Forge.Domain/Training/Exercise.cs:31-37 has no breathing or tempo member

*Gaps:* AC2 fails - no Breathing and no Tempo section exists anywhere, and there is no 'not applicable' marker either; no content lint enforces the step/cue counts, they happen to hold.

#### S07.04.03 — Surface mistakes, safety notes and prerequisites — **PARTIAL**

All 60 seeded exercises carry at least 2 common mistakes and at least 1 safety note, and both render as their own sections with the section hidden when empty. Prerequisites do not exist as a concept, and no limitation warning can appear because contraindication tags do not exist.

*Evidence:* src/Forge.Infrastructure/Content/exercise-catalogue.json: 60/60 have >=2 commonMistakes and >=1 safetyNotes; src/Forge.App/Features/Exercises/ExerciseDetailViewModel.cs:226-227, :242-245

*Gaps:* AC2 fails - no contraindication tags, so no limitation warning is rendered above the instructions; intermediate and advanced exercises declare no prerequisites and no explicit 'none'; no lint.

#### S07.04.04 — Link prerequisites, regressions and progressions — **NOT-DONE**

There is no relationship model on Exercise, no prerequisite/regression/progression links in the seed asset, no relationship rail on the detail page and no link validation.

*Evidence:* src/Forge.Domain/Training/Exercise.cs:7-66 has no relationship members; src/Forge.Infrastructure/Content/exercise-catalogue.json record keys are id, name, pattern, primaryMuscle, secondaryMuscles, equipment, difficulty, forceType, executionSteps, commonMistakes, coachingCues, safetyNotes, isUnilateral, provenance

*Gaps:* Whole story unimplemented; the nearest surface is the substitution list, which is computed rather than authored and carries no prerequisite/regression/progression semantics.

### F07.05 — Recommend alternatives and substitutions — **PARTIAL**

#### S07.05.01 — Rank substitutions by intent and availability — **PARTIAL**

Substitution is the strongest thing in E07. Candidates must clear a pattern-compatibility gate before scoring, equipment availability is applied as a hard filter with a separate explanation for 'nothing trains this' versus 'you cannot reach the things that do', and the score weighs muscle overlap, force type, laterality and difficulty distance. Mechanic and contraindications are not considered because neither exists.

*Evidence:* src/Forge.Domain/Training/ExerciseSubstitution.cs:138-200 (gate, equipment split, ranking), :214-246 (scoring), :106-117 (related patterns); tests/Forge.Domain.Tests/Training/ExerciseSubstitutionTests.cs

*Gaps:* Scoring omits mechanic and contraindication tags, neither of which is modelled; limitation-compatible ranking (AC2) cannot happen because there are no limitation tags; AC1's 100 ms budget across 800 records is unmeasured against a 60-record catalogue.

#### S07.05.02 — Show alternatives from details and unavailable cards — **PARTIAL**

The detail page has a Find Alternative action that reaches a dedicated alternatives screen showing every ranked substitute with a plain-language reason, an equipment toggle row and an explanation when nothing qualifies. It is a pushed page rather than a bottom sheet, and there is no Substitute action on an unavailable result card because result cards have no unavailable state.

*Evidence:* src/Forge.App/Features/Exercises/ExerciseDetailViewModel.cs:180-185; src/Forge.App/Features/Exercises/ExerciseAlternativesViewModel.cs:143-170; src/Forge.App/Features/Exercises/AlternativeExerciseViewModel.cs; src/Forge.App/Features/Exercises/ExerciseCardViewModel.cs:11-71

*Gaps:* AC2 fails - result cards carry no Substitute action and no unavailable state; Find Alternative is shown unconditionally rather than only when a substitution exists; the sheet is a full page, not a dx:BottomSheet.

#### S07.05.03 — Let users exclude exercises from suggestions — **NOT-DONE**

There is no exclusion concept: no 'Not for me' action on detail or substitution cards, no exclusion entity, no Show excluded toggle.

*Evidence:* Repository-wide search for NotForMe / Exclusion across src returns nothing under Training or Exercises; src/Forge.Domain/Training/ExerciseSubstitution.cs:138-200 has no exclusion input

*Gaps:* Whole story unimplemented.

#### S07.05.04 — Report catalogue quality for release readiness — **NOT-DONE**

No catalogue diagnostics exist. tools/ contains ci, legal, perf, release, smoke and backlog-sync only; there is no coverage measurement for metadata, instructions, substitutions or provenance and nothing exits non-zero on a threshold miss.

*Evidence:* tools/ directory listing contains no catalogue-lint or diagnostics project; the only content validation is the runtime provenance throw in src/Forge.Infrastructure/Content/SeedCatalogue.cs:69-73

*Gaps:* Whole story unimplemented; no release gate on catalogue quality.


**E07 epic verdict: PARTIAL**


---

## E08 — Exercise Media, Video and Form Guidance

### F08.01 — Choose media asset strategy and manifest — **PARTIAL**

#### S08.01.01 — Define the v1 media size budget and delivery mix — **PARTIAL**

docs/media-strategy.md is a real analysis: a per-option size table across 1080p/720p/480p MP4, animated WebP/AVIF, bundled essentials and on-demand cache, compared against the 40 MB per-ABI budget, ending in a text-first recommendation. The app follows it - nothing is bundled and text guidance is always present. There is no build-time budget gate and the arithmetic is stated for 60 exercises, not the story's 800.

*Evidence:* docs/media-strategy.md:1-18 (size table and 40 MB comparison), :43-45 (recommendation); src/Forge.Infrastructure/Media/ExerciseMediaCatalogue.cs:25-27 returns Absent with a text fallback

*Gaps:* AC1 fails - no tools/media-budget and no signing-time failure on a bundled-media total; AC2 fails literally - the document reasons about 60 exercises and never states 800 x 6 MB = 4800 MB. The backlog assumes an 800-exercise catalogue the app deliberately does not ship.

#### S08.01.02 — Create versioned media manifest linked to exercises — **NOT-DONE**

There is no media manifest. No media-manifest.json, no ExerciseMediaItem, no MediaManifestImporter, and no exerciseId/type/angle/duration/bytes/checksum/caption/fallback record anywhere. Only the text-only-detail criterion holds, and it holds because there is no media at all.

*Evidence:* src/Forge.App/Resources/Raw contains only AboutAssets.txt; src/Forge.Core/Abstractions/Media/ExerciseMediaDescriptor.cs:4-9 is a five-field runtime record with no angle, duration, checksum or caption; no manifest validation exists

*Gaps:* AC1 unimplementable - no manifest and no validation command; no checksum, angle, caption-track or accessibility-fallback metadata; no manifest versioning against cache metadata.

#### S08.01.03 — Evaluate platform on-demand resource options — **DONE**

Both platform options were evaluated and implemented rather than left open. docs/media/android-asset-delivery.md documents pack names, bundletool local testing, internal app sharing and Play Console setup; docs/media/ios-on-demand-resources.md documents tags, MAUI/iOS build tagging, prefetch categories, App Store Connect implications and Apple size limits. IMediaPackService is the resulting abstraction with a real platform implementation and an unsupported fallback, and an HTTPS cache exists as the third route.

*Evidence:* docs/media/android-asset-delivery.md:1-77; docs/media/ios-on-demand-resources.md:1-74; docs/media-strategy.md:35-41; src/Forge.Core/Abstractions/Media/IMediaPackService.cs:101; src/Forge.App/Services/Media/PlatformMediaPackService.cs:14; src/Forge.App/Services/Media/UnavailableMediaPackService.cs:14

#### S08.01.04 — Validate media licence and attribution metadata — **NOT-DONE**

No media item stores creator, licence, source, release status or attribution requirement - there is no media item record at all. There is no tools/media-lint, no packaging gate and no in-app media attribution screen.

*Evidence:* src/Forge.Core/Abstractions/Media/ExerciseMediaDescriptor.cs:4-9 (no licence fields); tools/ has no media-lint; src/Forge.App/Features/Legal contains no MediaAttributionPage (docs/legal/licences.md covers package licences, not media)

*Gaps:* Whole story unimplemented; both acceptance criteria unimplementable.

### F08.02 — Play demonstration media on exercise details — **PARTIAL**

#### S08.02.01 — Embed MediaElement demonstration playback — **PARTIAL**

Upgraded from NOT-DONE by `e4d971b`. The report previously found a player that could never play: nothing populated `IMediaCache`, the detail page gated on `IMediaPackService` while the video page read `IMediaCache`, and the source string was prefixed `embed://` or `filesystem://`, which is not a valid MediaElement source. All three are fixed. `ExerciseMediaCatalogue` now resolves media from the store-delivered packs the app actually downloads, and playback uses `MediaSource.FromFile`/`FromResource`, so a downloaded pack produces a playing video.

*Evidence:* src/Forge.Infrastructure/Media/ExerciseMediaCatalogue.cs:46-105; src/Forge.App/Features/Media/ExerciseVideoViewModel.cs:89-105, :148-153; src/Forge.App/Features/Media/ExerciseVideoPage.xaml:28-44, :89-124; src/Forge.App/Features/Media/ExerciseVideoPage.xaml.cs:59-62; src/Forge.App/Features/Media/Library/VideoLibraryViewModel.cs:294-306

*Gaps:* AC2 still fails - a missing or corrupt asset only rewrites a label and offers no Retry or Text Guidance action; AC3 still fails - no release package-budget check exists; bundled packaged sources are still not returned by ExerciseMediaCatalogue, and the checksum-verified cache is not implemented.

#### S08.02.02 — Add loop, speed, scrub and frame-step controls — **PARTIAL**

A scrubber with drag start/complete handling, a frame-step pair at 1/30 s, a play/pause toggle, a full-screen toggle and speed buttons are all implemented in code-behind against real MediaElement APIs, and every control carries a semantic description. Since `e4d971b` this is reachable, because playback now resolves a source (S08.02.01). The speed set still does not match the story and loop is hard-coded with no control.

*Evidence:* src/Forge.App/Features/Media/ExerciseVideoPage.xaml:52-83; src/Forge.App/Features/Media/ExerciseVideoPage.xaml.cs:8 (FrameStep 1/30 s), :68-97; src/Forge.App/Features/Media/ExerciseVideoViewModel.cs:95-102 clamps 0.25-2.0

*Gaps:* Speeds offered are 0.5/0.75/1/1.25, not the required 0.25/0.5/1.0/1.5; no loop control - ShouldLoopPlayback is hard-coded True at ExerciseVideoPage.xaml:36; frame-step is not labelled 'Step 0.1s' and there is no capability probe; selected speed state is not announced (AC1).

#### S08.02.03 — Support pinch zoom, angle switching and landscape lock — **NOT-DONE**

There is a full-screen toggle that hides the nav and tab bars and grows the player, but no pinch zoom, no angle model or switcher, and no orientation lock or restore. There is no IOrientationService.

*Evidence:* src/Forge.App/Features/Media/ExerciseVideoPage.xaml.cs:115-135 (full screen is a height change only); no angle field on src/Forge.Core/Abstractions/Media/ExerciseMediaDescriptor.cs:4-9; repository-wide search finds no orientation service

*Gaps:* No 1.0x-3.0x pinch zoom or one-tap reset; no front/side angles, no per-exercise remembered angle and no position-preserving switch (AC1); no landscape lock or portrait restore (AC2).

#### S08.02.04 — Respect data saver, reduced motion and autoplay settings — **PARTIAL**

Autoplay suppression is real and platform-correct: reduced motion is read from Android animator/transition scales and from UIAccessibility on iOS, data saver from ConnectivityManager.RestrictBackgroundStatus, and ShouldAutoPlay is the conjunction of a playable source and that policy. No cellular download can start because no automatic download exists at all, and an unmetered-only download preference is exposed in settings.

*Evidence:* src/Forge.App/Features/Media/MauiMediaPlaybackPolicy.cs:13-43; src/Forge.App/Features/Media/ExerciseVideoViewModel.cs:77; src/Forge.App/Features/Settings/ViewModels/UnitsSettingsPageViewModel.cs:58-61

*Gaps:* No global media autoplay setting with Never / Wi-Fi cached only / Always cached only; AC1's 'a Play button is visible' holds only in the branch where media exists, which it never does.

### F08.03 — Make media accessible and text complete — **PARTIAL**

#### S08.03.01 — Add captions and subtitle track support — **NOT-DONE**

There is no caption model, no caption track, no caption toggle and no caption persistence, and no lint could fail on a missing one.

*Evidence:* src/Forge.Core/Abstractions/Media/ExerciseMediaDescriptor.cs:4-9 has no caption member; src/Forge.App/Features/Media/ExerciseVideoPage.xaml has no caption overlay; no CaptionTrack type exists

*Gaps:* Whole story unimplemented.

#### S08.03.02 — Provide audio descriptions and text-only fallback — **PARTIAL**

The text-only fallback is genuinely always present rather than one tap away: the video page renders execution steps, coaching cues and common mistakes below the player unconditionally, and the exercise detail page carries the same content plus setup, facts and safety. Audio description scripts do not exist.

*Evidence:* src/Forge.App/Features/Media/ExerciseVideoPage.xaml:96-119 (three sections, always visible); src/Forge.App/Features/Media/ExerciseVideoViewModel.cs:61-64; src/Forge.App/Features/Exercises/ExerciseDetailViewModel.cs:224-245

*Gaps:* AC1 fails on count - the fallback carries 3 of the required 5+ E07 sections and cannot carry breathing or tempo because neither exists (S07.04.02); AC2 fails - no audio description cue model, no enable switch.

#### S08.03.03 — Add accessible media control semantics — **PARTIAL**

Every media control carries SemanticProperties.Description, the player itself is described, and the scrubber has both a description and a hint. Focus order follows visual order because the controls are declared in that order in a single stack. Minimum touch targets are not asserted on these controls and selected state is not exposed.

*Evidence:* src/Forge.App/Features/Media/ExerciseVideoPage.xaml:44 (player description), :52-57 (slider description and hint), :62-83 (per-button descriptions)

*Gaps:* AC1 fails on state - the speed buttons announce a name but no selected state; AC2 unverified - none of the media buttons sets MinimumHeightRequest/MinimumWidthRequest, unlike the workout screen which uses the TouchTargetPrimary token.

#### S08.03.04 — Validate media accessibility coverage before release — **NOT-DONE**

No media accessibility report exists; there is no tool to measure caption, fallback, audio-description or control-semantic coverage and nothing exits non-zero.

*Evidence:* tools/ contains no media-lint; no accessibility rules file for media exists (docs/accessibility/sweep-evidence.md covers the general UI sweep, not media coverage)

*Gaps:* Whole story unimplemented.

### F08.04 — Add form guidance beyond video — **PARTIAL**

#### S08.04.01 — Show annotated stills and do or do-not comparisons — **NOT-DONE**

There are no annotated stills and no do/do-not comparison surface. No AnnotatedStill model, no Form feature folder, no still assets.

*Evidence:* src/Forge.App/Features/Exercises contains no Form directory; src/Forge.App/Resources/Raw contains only AboutAssets.txt

*Gaps:* Whole story unimplemented.

#### S08.04.02 — Time cue text to movement phases — **NOT-DONE**

The nearest thing is SynchronizedDescriptions, which slices the execution-step list evenly across the video's duration and shows whichever index the current position lands in. That is not a cue timeline: there is no phase name, no start or end time, no authored cue text, and no manual step-through mode when media is unavailable.

*Evidence:* src/Forge.App/Features/Media/ExerciseVideoViewModel.cs:64 (steps mapped to indices), :80-93 (ratio-based index selection)

*Gaps:* No TimedFormCue model with phase/start/end/text; AC1's 0.75-1.25 s window is meaningless against a ratio split; AC2 fails - there is no cue mode to open and no manual advance.

#### S08.04.03 — Add tempo metronome and breathing prompts — **NOT-DONE**

There is no tempo helper. No 4-part tempo notation, no metronome, no audio/haptic/visual tick generation and no drift control. Haptics exist only as a general motion affordance for button feedback.

*Evidence:* No TempoGuideService or TempoGuideView anywhere in src; src/Forge.App/Motion/ForgeAnimations.cs:208-217 is the only HapticFeedback use and is tied to reduced-motion preferences

*Gaps:* Whole story unimplemented.

#### S08.04.04 — Explain range of motion and setup checkpoints — **PARTIAL**

Setup guidance is derived rather than authored and lands in the 2-5 range for every exercise: an equipment line, a pattern description, a unilateral note when applicable, a difficulty-banded caution and a safety pointer. Nothing classifies a checkpoint as required, optional or limitation-sensitive, and there is no range-of-motion note.

*Evidence:* src/Forge.Domain/Training/ExerciseGuidance.cs:117-151 produces 2-5 ordered setup steps; src/Forge.App/Features/Exercises/ExerciseDetailViewModel.cs:224

*Gaps:* AC1 partially met - the count is right but no required/optional status is shown; AC2 fails - no range-of-motion note and no limitation-sensitive text, because structured contraindication tags do not exist (S07.03.03).

### F08.05 — Support private form recording and media storage — **PARTIAL**

#### S08.05.01 — Record user form checks strictly on device — **NOT-DONE**

There is no form-check recording feature: no recorder page, no MediaPicker or camera capture for video, no privacy notice, no 30-second cap and no local clip storage.

*Evidence:* src/Forge.App/Features/Exercises contains no FormCheck directory; no IFormCheckStorage exists; the only camera use in the app is barcode scanning under src/Forge.App/Features/Scanning

*Gaps:* Whole story unimplemented.

#### S08.05.02 — Compare user recording side by side with reference — **NOT-DONE**

There is no side-by-side review surface, no second player, and no user clip to compare against.

*Evidence:* No FormCheckComparePage or FormCheckCompareViewModel anywhere in src; src/Forge.App/Features/Media contains one player page

*Gaps:* Whole story unimplemented and blocked on S08.05.01.

#### S08.05.03 — Cache, evict and manage downloaded media — **PARTIAL**

FileSystemMediaCache is a real cache: a JSON manifest, a single-writer gate, atomic temp-then-move downloads, last-access tracking and LRU eviction through MediaCachePolicy, with size accounting before and after transfer. A storage screen reports database and media bytes and reclaims media without touching history. The cap is a fixed constant, not user-selectable, and there is no checksum verification or free-storage precondition.

*Evidence:* src/Forge.Infrastructure/Media/FileSystemMediaCache.cs:10 (80 MB cap constant), :94-187 (eviction then download), :189-212 (evict); src/Forge.App/Features/Settings/Services/StorageUsageService.cs:27-70; src/Forge.App/Features/Settings/DataManagementPage.xaml:24-27

*Gaps:* Cap is 80 MB and hard-coded - not a 500 MB default and not user-selectable between 100 MB / 500 MB / 1 GB / unlimited; AC2 fails - no device free-storage check and no low-storage message offering Manage Cache; no SHA-256 verification; the cache screen shows no per-item last access.

#### S08.05.04 — Park AI pose estimation for post-v1 exploration — **DONE**

The parking is honoured in the product and recorded in the backlog. A repository-wide search of v1 code and strings for 'AI form', 'form score', 'pose score', 'skeleton overlay' and 'automatic form correction' returns zero matches, so no user-facing string promises any of it, and the epic's own nonGoals plus this story's requirements record the on-device-only constraint with the 30 fps and 250 MB thresholds.

*Evidence:* Repository-wide search across src for AI form / form score / pose score / skeleton overlay / automatic form correction returns 0 matches; backlog/epics/E08-exercise-media-and-form.yml nonGoals ('No AI pose estimation, rep scoring or skeleton overlay in v1; that exploration is parked in wave 6') and story S08.05.04 requirements


**E08 epic verdict: PARTIAL**


---

## E09 — Training Plan Builder and Programmes

### F09.01 — Offer ready-made programmes for common goals — **PARTIAL**

#### S09.01.01 — Seed beginner and goal-based programme templates — **PARTIAL**

Six original templates ship with real content - full-body beginner, upper/lower, push-pull-legs, 5x5, hypertrophy and home bodyweight - each with named days, weekday assignments, ordered exercises and per-set rep ranges, RPE and rest. Templates are static and owned by nobody, and adopting one produces an owned deep copy, so a user copy can never be overwritten by a template change (AC2 holds by construction).

*Evidence:* src/Forge.Domain/Planning/PlanTemplateCatalogue.cs:10-37 (6 templates), :39-59; src/Forge.Domain/Planning/PlanEntities.cs:47-107 CreateEditableCopy; tests/Forge.Domain.Tests/Planning/TrainingPlanCopyTests.cs

*Gaps:* Six templates, not the required seven - the minimal-equipment template is missing (AC1 fails); templates carry no goal, experience level, equipment list or safety notes; they are compiled constants rather than a versioned import; AC3 (missed-occurrence state), AC4 (equipment-blocked sessions) and AC5 (2:1 push-pull ratio - the analyzer uses 1.5:1) are all unmet.

#### S09.01.02 — Preview template fit before plan creation — **PARTIAL**

A template screen lists all six with a preview of each day and its first four exercises, and selecting one previews before adoption. There is no filtering of any kind and no fit assessment.

*Evidence:* src/Forge.App/Features/Plans/PlansFeatureViewModels.cs:84-133; src/Forge.App/Features/Plans/PlanTemplatesPage.xaml:1-123

*Gaps:* AC1 fails - there are no goal, experience, equipment or days-per-week filters at all; AC2 fails - no equipment mismatch warning; the preview shows no equipment list, no estimated duration range and no primary muscle groups.

#### S09.01.03 — Copy a template into an editable personal plan — **PARTIAL**

Adopting a template deep-copies days, exercises and sets under the adopting profile with fresh identifiers and navigates straight into the editor, leaving the template untouched (AC2 holds).

*Evidence:* src/Forge.App/Features/Plans/PlansFeatureViewModels.cs:116-126; src/Forge.Domain/Planning/PlanEntities.cs:47-107; src/Forge.App/Features/Plans/PlanPersistenceService.cs

*Gaps:* AC1 partly fails - there is no version concept on TrainingPlan at all, so nothing can be 'version 1', and the requirement that the copy opens 'with all targets editable' is not met because the editor cannot edit sets, reps, load, RPE or rest (S09.02.02).

### F09.02 — Build custom plans with days and blocks — **PARTIAL**

#### S09.02.01 — Create a plan with training days and ordered blocks — **PARTIAL**

A plan can be created, named, saved and reloaded, days can be added and moved earlier, and ordering is by an Ordinal that persists. The editor is a skeleton beyond that.

*Evidence:* src/Forge.App/Features/Plans/PlansFeatureViewModels.cs:155-220 (Load, Save, MoveDayUp, AddDay); src/Forge.Domain/Planning/PlanEntities.cs:111-130; src/Forge.App/Features/Plans/PlanEditorPage.xaml:21-27, :49-53

*Gaps:* No move-down or drag reorder and no day deletion; no block-type UI, so warm-up, superset, circuit and cooldown blocks cannot be created even though PlanBlockType models them; no 1-14 day bound; AC2 fails - a zero-day plan is unreachable because LoadAsync auto-inserts Day 1, and Save is never blocked or field-annotated.

#### S09.02.02 — Add exercise targets for sets, reps, load, RPE and rest — **NOT-DONE**

The model supports sets, rep range, load, RPE, rest and warm-up flags, and the editor displays them - but nothing in the UI can change any of them. Add exercise inserts a literal 'New exercise' with a fixed 3 sets of 8-10 at RPE 8 and 90 s rest, and the only per-exercise action is '+ set', which copies the previous set. There is no numeric editor, no validation and no inline error.

*Evidence:* src/Forge.App/Features/Plans/PlansFeatureViewModels.cs:222-276 (AddExercise hard-codes name, pattern, muscle and targets; AddTargetSet copies the previous set); src/Forge.App/Features/Plans/PlanEditorPage.xaml:54-74 renders Prescription as a read-only label with a single '+ set' button; src/Forge.Domain/Planning/PlanEntities.cs:172-201

*Gaps:* AC1 and AC2 both fail - no sets/reps/RPE/rest/load input exists, so no range can be enforced and no value can be entered and read back; no exercise picker from the catalogue either, so a plan cannot reference a real exercise.

#### S09.02.03 — Represent supersets and circuits in the builder — **NOT-DONE**

GroupKey exists on PlannedExercise but the builder assigns it automatically as 'A1' or 'A2' by index parity with no user control. There is no superset or circuit block editor, no 2-5 or 2-12 bound, no rounds and no shared rest value.

*Evidence:* src/Forge.App/Features/Plans/PlansFeatureViewModels.cs:243 (GroupKey = index % 2 == 0 ? "A1" : "A2"); src/Forge.Domain/Planning/PlanEntities.cs:156-160; src/Forge.App/Features/Plans/PlanEditorPage.xaml:59 renders GroupKey read-only

*Gaps:* AC1 and AC2 both fail - grouped blocks cannot be created, sized, given rounds or given a shared rest, and children cannot be added, removed or reordered within a group.

#### S09.02.04 — Adapt a plan when equipment or availability changes — **NOT-DONE**

There is no plan adaptation. Equipment cannot be marked unavailable against a plan, weekly availability changes propose nothing, and there is no change preview.

*Evidence:* No AdaptPlanUseCase or Adaptation folder anywhere; src/Forge.App/Features/Plans contains list, templates, editor, schedule and persistence only; ExerciseSubstitution is never called from any plan code path

*Gaps:* Whole story unimplemented; substitution logic exists in the domain but is wired only to the exercise-library alternatives screen.

### F09.03 — Apply periodisation and progression rules — **PARTIAL**

#### S09.03.01 — Add linear and double progression models — **PARTIAL**

Both models are implemented precisely and unit-tested, and double progression does reach a user: the coaching next-session recommendation applies it against the latest logged set and explains the result in kilograms with a 5 percent session cap. But no plan carries a progression rule, no plan-side next-target preview exists, and linear progression is called from nowhere in the app.

*Evidence:* src/Forge.Domain/Planning/ProgressionModel.cs:72-98; src/Forge.Domain/Coaching/NextSessionRecommender.cs:79-102; src/Forge.App/Features/Coaching/Services/CoachingDataService.cs:30-60; grep for ProgressionModel across src/Forge.App returns no hits

*Gaps:* AC1 fails - linear progression with a configured increment is never applied anywhere; progression rules cannot be attached to an exercise or block in a plan; the 'next target preview' surface does not exist in the plan builder.

#### S09.03.02 — Add percentage-of-1RM and RPE autoregulation models — **PARTIAL**

RPE autoregulation reaches a user through the coaching recommendation, converting reps-in-reserve delta into a load step and capping the increase at 5 percent with a stated rationale. Percentage-of-1RM is implemented and tested but called from nowhere.

*Evidence:* src/Forge.Domain/Planning/ProgressionModel.cs:100-124; src/Forge.Domain/Coaching/NextSessionRecommender.cs:79-122; grep for PercentageOfEstimatedOneRepMax across src/Forge.App returns no hits

*Gaps:* AC1 unreachable - no percentage target can be configured or displayed, there is no training-max source shown, and the model rounds to two decimals rather than to a configured plate increment; AC2's -10 to +5 percent band is not enforced as a band - the cap is one-sided at +5 percent.

#### S09.03.03 — Schedule deload weeks and progression pauses — **PARTIAL**

A deload model exists that reduces load by a percentage and drops one set with a floor of one, and it reaches a user through the coaching deload recommendation, triggered by an acute:chronic load ratio or a performance-decay threshold with a stated caveat. Nothing schedules a deload week on a plan and there is no progression pause.

*Evidence:* src/Forge.Domain/Planning/ProgressionModel.cs:126-139; src/Forge.Domain/Coaching/DeloadRecommender.cs:11-32; no DeloadRule type exists and no plan member references one

*Gaps:* AC1 fails - no set-count percentage deload and no deload week on a plan; AC2 fails - no 1-4 session progression pause exists; no deload or pause indicator on the calendar or in a target preview.

### F09.04 — Schedule plans on the training calendar — **PARTIAL**

#### S09.04.01 — Place fixed-day sessions on a SchedulerView calendar — **PARTIAL**

PlanScheduler maps each plan day to its weekday and projects occurrences forward without creating any workout log, and the schedule page renders them as a date grid with a shifted marker. The window is four weeks, the surface is a plain collection view rather than dx:SchedulerView, and an occurrence cannot be tapped.

*Evidence:* src/Forge.Domain/Planning/Scheduling.cs:46-61; src/Forge.App/Features/Plans/PlansFeatureViewModels.cs:358-373 (weeks: 4, 28 cells); src/Forge.App/Features/Plans/PlanSchedulePage.xaml:33-47

*Gaps:* AC1 fails on horizon - 4 weeks are projected, not 12; AC2 fails outright - the cells have no gesture, no detail sheet and no Start workout, so a scheduled session cannot begin a workout; no optional start time; not a dx:SchedulerView.

#### S09.04.02 — Support flexible frequency-based scheduling — **PARTIAL**

Flexible scheduling is implemented: the target sessions per week is clamped to the available days, spacing is spread evenly across the week, and days cycle so a 3-day plan across 6 sessions does not repeat the same day twice in a week. Today's occurrence is surfaced through the same scheduler by the insights and reminder services.

*Evidence:* src/Forge.Domain/Planning/Scheduling.cs:63-82; src/Forge.App/Features/Insights/Services/InsightsDataService.cs:612; src/Forge.App/Services/Notifications/ReminderRefreshService.cs:147; tests/Forge.Domain.Tests/Planning/PlanSchedulerTests.cs

*Gaps:* AC1 fails - there is no minimum-rest-day concept, so nothing prevents Workout B being suggested the next day; AC2 fails - completion does not recalculate the remaining sessions for the week, the projection is purely positional and ignores what was logged.

#### S09.04.03 — Reschedule missed sessions without failure language — **NOT-DONE**

PlanScheduler.ShiftForMissedSession is implemented and tested, and deliberately shifts rather than deletes or breaks a streak - but it is called from nowhere in the app. There is no missed detection after the local day ends, no occurrence state persisted, and no Move/Skip/Keep sheet. The schedule page renders a 'Shifted' label that can never become visible, because nothing ever produces a shifted occurrence.

*Evidence:* src/Forge.Domain/Planning/Scheduling.cs:25-44; grep across src/Forge.App for ShiftForMissedSession returns no hits - the only PlanScheduler calls are Schedule(...) at PlansFeatureViewModels.cs:362, InsightsDataService.cs:612 and ReminderRefreshService.cs:147; src/Forge.App/Features/Plans/PlanSchedulePage.xaml:42

*Gaps:* AC1 and AC2 both fail - no missed occurrence is ever detected, displayed or resolved; no neutral skip reason is recorded; occurrences are recomputed from scratch on every load and have no durable state.

### F09.05 — Manage plan library, versions and sharing — **PARTIAL**

#### S09.05.01 — Maintain a searchable local plan library — **PARTIAL**

A plan library page lists the user's plans with a day and frequency summary and lets exactly one be made active, writing the change through the persistence service.

*Evidence:* src/Forge.App/Features/Plans/PlansFeatureViewModels.cs:11-81; src/Forge.App/Features/Plans/PlanListPage.xaml:1-55

*Gaps:* No search by name or goal; no archived state at all, so AC2 cannot hold - archiving does not exist; no active/archived/template-derived grouping; AC1's 200-plan 60 fps scroll is unmeasured.

#### S09.05.02 — Duplicate plans and create immutable versions — **NOT-DONE**

TrainingPlan has no version member, there is no duplicate command, and no immutable version is created when a plan with completed workouts is edited. Workout history cannot be linked to a plan version because a WorkoutSession carries no plan reference at all.

*Evidence:* src/Forge.Domain/Planning/PlanEntities.cs:9-40 has no Version property; src/Forge.Domain/Training/Exercise.cs:69-99 WorkoutSession has no plan or plan-version member; src/Forge.App/Features/Plans/PlansFeatureViewModels.cs has no duplicate command

*Gaps:* Both acceptance criteria unimplementable - no version identity exists and nothing links a workout to the plan that generated it.

#### S09.05.03 — Export and import plans as files — **NOT-DONE**

There is no plan-level export or import. The app has a whole-database backup/export path, but nothing produces or consumes a plan file with a schemaVersion, metadata, exercises, schedule and progression rules, and there is no duplicate-id resolution.

*Evidence:* src/Forge.App/Features/Plans contains no sharing surface; src/Forge.Infrastructure/Backup/ForgeDataImporter.cs is the whole-profile data path, not a plan document

*Gaps:* Whole story unimplemented; both acceptance criteria unimplementable.

#### S09.05.04 — Share compact plans by QR code when safe — **NOT-DONE**

There is no QR sharing for plans. Barcode scanning exists for food, but no plan payload, no size threshold and no QR generation or import path.

*Evidence:* src/Forge.App/Features/Scanning is food-barcode only; src/Forge.Domain/Nutrition/Barcodes; no PlanQrPayload or PlanQrSheet exists

*Gaps:* Whole story unimplemented and blocked on S09.05.03.

### F09.06 — Evaluate plan quality and training load — **PARTIAL**

#### S09.06.01 — Estimate session duration from sets and rest — **PARTIAL**

SessionDurationEstimator sums a per-set execution allowance and every inter-set rest except the last, and the editor shows a per-day estimate plus a week total that recomputes on every structural edit. It produces a single figure, not a range, and models no warm-up, circuit or cooldown time.

*Evidence:* src/Forge.Domain/Planning/SessionDurationEstimator.cs:13-27; src/Forge.App/Features/Plans/PlansFeatureViewModels.cs:288-301

*Gaps:* AC1 fails - a point estimate is produced, not a range, so 'no wider than 20 percent' has nothing to measure; warm-up, circuit rounds and cooldown are not modelled; AC2's 200 ms recompute is unmeasured.

#### S09.06.02 — Calculate weekly volume by muscle group — **PARTIAL**

VolumeBalanceAnalyzer does compute SetsByMuscleGroup alongside movement patterns, counting working sets only and weighting secondary muscles below primary. Neither the weighting nor the display matches the story: secondaries get integer max(1, sets/2), not 0.5 per set, and the plan editor renders only the movement-pattern totals, so muscle-group volume never reaches a user.

*Evidence:* src/Forge.Domain/Planning/VolumeBalanceAnalyzer.cs:30-54 (AddMuscle with sets/2), :56-64; src/Forge.App/Features/Plans/PlansFeatureViewModels.cs:303-308 iterates SetsByMovementPattern only

*Gaps:* AC1 fails numerically - 3 sets of bench give triceps 1, not 1.5, because the weighting is integer division; the muscle-group panel does not exist in any screen; there is no fixed display list of the ten named groups.

#### S09.06.03 — Warn when a plan is badly skewed or unsafe — **PARTIAL**

Push/pull and squat/hinge imbalance warnings are computed with a documented threshold and an infinite-ratio case for a missing counterpart, and the plan editor surfaces the first warning with a neutral fallback line when there is none.

*Evidence:* src/Forge.Domain/Planning/VolumeBalanceAnalyzer.cs:17-23 (1.5 threshold), :49-51, :66-93; src/Forge.App/Features/Plans/PlansFeatureViewModels.cs:303-310; src/Forge.App/Features/Plans/PlanEditorPage.xaml:29-41

*Gaps:* Threshold is 1.5:1, not the 2:1 the story and every AC5 in this epic specify, and there is no two-consecutive-week condition; no >30 hard sets or <4 sets hypertrophy rule; no suggested action; AC2 fails - warnings cannot be dismissed and there is no plan version to scope a dismissal to.


**E09 epic verdict: PARTIAL**


---

## E10 — Workout Execution and Exercise Mode

### F10.01 — Run the active workout screen for fast logging — **PARTIAL**

#### S10.01.01 — Show current exercise with target versus actual — **PARTIAL**

The screen shows the current exercise, a Target and Actual weight tile pair, a superset station and round label, the session's logged sets below the fold and a recovery banner. But the target is fabricated: every queue entry is constructed with a hard-coded 60 kg and 8 reps, because no plan drives the session - the only route into a workout is Train's Start workout with no plan parameter. Set number, target reps, target RPE, rest target and the next-exercise preview are all absent.

*Evidence:* src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs:828-834 (BuildQueueEntry hard-codes 60m, 8), :836-847 (falls back to 20 kg / 8 reps); src/Forge.App/Features/Workout/ActiveWorkoutPage.xaml:52-55 (Target/Actual tiles); src/Forge.App/Features/Train/TrainViewModel.cs:14-16 is the sole entry point and passes no plan

*Gaps:* AC1 fails - 'Set 1 of 3', target reps, target RPE and rest target are not rendered and the target load shown is a constant, not a prescription; no next-exercise preview; no plan-to-workout link exists anywhere in the app.

#### S10.01.02 — Log a completed set with no more than two taps — **DONE**

One tap on Log set commits the draft; a plus tap followed by Log set is exactly two taps. The commit is durable before the UI moves on: LogSetAsync awaits SaveLoggedSetAsync, which is serialised onto a single write queue so overlapping saves of the same snapshot cannot race, and the set is written as a SetEntry row alongside the state snapshot, so an immediate process kill cannot lose it.

*Evidence:* src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs:297-343 (log, await save, then rest); :362-372 (single-tap +/- for weight and reps); src/Forge.App/Features/Workout/ActiveWorkoutSession.cs:99-120 (serialised persistence queue); src/Forge.App/Features/Workout/WorkoutPersistenceService.cs:176-185 (AddMissingSetEntries then SaveChanges); src/Forge.App/Features/Workout/ActiveWorkoutPage.xaml:316-323

#### S10.01.03 — Keep primary actions thumb-reachable and accessible — **PARTIAL**

The primary action row sits at the bottom of the page, the mid-set increment buttons set both MinimumWidthRequest and MinimumHeightRequest to the TouchTargetPrimary token of 64, checkboxes use TouchTargetMin of 48, and every actionable control carries a SemanticProperties.Description. Live announcements are raised for logging, rest completion and rest skipping through SemanticScreenReader.

*Evidence:* src/Forge.App/Features/Workout/ActiveWorkoutPage.xaml:62-94, :316-336; src/Forge.App/Resources/Styles/ForgeTokens.xaml:73-74 (TouchTargetMin 48, TouchTargetPrimary 64); src/Forge.App/Features/Workout/ActiveWorkoutPage.xaml.cs:75-76

*Gaps:* Controls expose a Description but not a separate SemanticProperties.Hint, so AC2's 'name, value and action hint' is only partly met; AC1's 200 percent text-scale behaviour cannot be established by reading and needs a device check.

### F10.02 — Run rest timers and device wake behaviour — **PARTIAL**

#### S10.02.01 — Auto-start rest timer after set completion — **DONE**

Rest is resolved and started in the same awaited path as the set commit, from the exercise's own prescription with an app default fallback. The countdown is a dx:RadialProgressBar driven by progress recomputed from the timer's absolute end time, with +15, +30, +60 and -15 one-tap adjustments and a Skip, and warm-up sets get their own shorter prescription.

*Evidence:* src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs:321-325, :374-394; src/Forge.Domain/Workout/ActiveWorkoutState.cs:391-409 (ResolveNextRest); src/Forge.Domain/Workout/RestPrescription.cs; src/Forge.App/Features/Workout/ActiveWorkoutPage.xaml:109-137

#### S10.02.02 — Alert when rest expires in foreground and background — **PARTIAL**

Background expiry is handled properly: a local notification is scheduled for the timer's wall-clock end time and cancelled when rest is skipped, and the UI reconciles from TargetEndUtc on every resume rather than trusting a tick, so a lock, a call or a suspend cannot desynchronise it. Foreground expiry, though, produces only a text change and a screen-reader announcement.

*Evidence:* src/Forge.App/Features/Workout/RestNotificationScheduler.cs:16-47; src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs:265-295 (reconcile from wall clock, announce once); src/Forge.App/Features/Workout/ActiveWorkoutPage.xaml.cs:45-55

*Gaps:* AC1 fails - no haptic and no audio cue fire on expiry; HapticFeedback is used only for button feedback in src/Forge.App/Motion/ForgeAnimations.cs:215; AC3 fails - there is no Android foreground service, which RestNotificationScheduler.cs:21-25 documents as a deliberate choice against exact alarms.

#### S10.02.03 — Keep the screen awake only during active workouts — **DONE**

Both the active workout page and the full-screen rest timer capture the previous KeepScreenOn value on appearing, force it on, and restore it on disappearing - so finishing a workout navigates to the summary and releases the wake lock immediately, and Forge never leaves the device awake after the session. The rationale (chalked hands between sets) is recorded on the type.

*Evidence:* src/Forge.App/Features/Workout/ActiveWorkoutPage.xaml.cs:8-13, :35-36, :62; src/Forge.App/Features/Workout/RestTimerPage.xaml.cs:33-34, :56

#### S10.02.04 — Handle pocket mode and accidental input — **NOT-DONE**

There is no pocket mode. No lock toggle, no hold-to-commit guard and no accidental-input protection anywhere in the workout feature.

*Evidence:* Repository-wide search for pocket / WorkoutInputGuard across src returns no matches; src/Forge.App/Features/Workout/ActiveWorkoutPage.xaml:320 binds Log set directly to the command with no guard

*Gaps:* Whole story unimplemented; both acceptance criteria unimplementable.

### F10.03 — Support quick-log variants and lifting utilities — **PARTIAL**

#### S10.03.01 — Repeat the last set and adjust by increments — **PARTIAL**

Repeat last copies load, reps, warm-up, failure and reps-in-reserve from the previous set for the current exercise, and weight and reps have one-tap plus/minus. Two behaviours diverge from the story: Repeat last immediately commits rather than filling a draft, and it silently logs the current draft when no previous set exists rather than being disabled with an explanation.

*Evidence:* src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs:345-360 (copies then calls LogSetAsync unconditionally), :362-372; src/Forge.App/Features/Workout/ActiveWorkoutPage.xaml:316-323

*Gaps:* AC1 fails as written - Repeat last produces a committed set, not a draft; the load step is a hard-coded 2.5 kg rather than the plate increment, so AC2 only holds for a 2.5 kg gym; there is no RPE control at all, so the RPE +/- 0.5 requirement is unmet; no disabled state or explanation when there is no previous set.

#### S10.03.02 — Generate warm-up sets and calculate plates — **PARTIAL**

The plate calculator is real and complete: metric and imperial inventories with a matching bar weight, per-side plate counts grouped by denomination, a remainder explanation, and it is reachable in one tap from the workout screen as a bottom sheet pre-filled with the current load, and separately from Train. Warm-up sets can be flagged and are excluded from working volume. There is no warm-up generator.

*Evidence:* src/Forge.Domain/Workout/PlateInventory.cs:37-46 (imperial default); src/Forge.Domain/Workout/PlateCalculator.cs; src/Forge.App/Features/Workout/WorkoutPreferenceStores.cs:99-100 (selects inventory from the unit preference); src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs:428-445; src/Forge.App/Features/Workout/ActiveWorkoutPage.xaml.cs:78-82; src/Forge.Domain/Workout/WorkoutSummaryCalculator.cs:19 excludes warm-ups

*Gaps:* AC1 fails - no warm-up generator exists; warm-up sets must be entered manually one at a time with the checkbox ticked.

#### S10.03.03 — Capture RPE, RIR, partials, failures and AMRAP — **PARTIAL**

Reps-in-reserve and a to-failure flag are captured per set, persisted, shown in the set list's flags and consumed by the coaching recommendation, so failure genuinely suppresses progression. RPE itself, partial reps and AMRAP are not captured.

*Evidence:* src/Forge.App/Features/Workout/ActiveWorkoutPage.xaml:87-94 (Warm-up, Failure, RIR); src/Forge.Domain/Workout/ActiveWorkoutState.cs:75-105; src/Forge.Domain/Coaching/NextSessionRecommender.cs:79-98

*Gaps:* No RPE field (only RIR), so the 'mutually consistent but neither overwrites the other' requirement is untestable; no partial-rep count and no AMRAP flag, so AC2 fails; AC1 fails - the summary has no missed-targets section because there are no targets to miss (S10.01.01).

#### S10.03.04 — Record drop sets and mechanical variations — **NOT-DONE**

There is no drop-set model and no drop-set UI. A set has no parent/child relationship and no drop ordinal.

*Evidence:* src/Forge.Domain/Workout/ActiveWorkoutState.cs CompletedWorkoutSet has no parent set or drop members; no DropSetEntry type or DropSetSheet exists anywhere in src

*Gaps:* Whole story unimplemented; both acceptance criteria unimplementable.

### F10.04 — Execute supersets, circuits and timed formats — **PARTIAL**

#### S10.04.01 — Execute supersets with shared rest — **DONE**

This is the best-built story in E10. Exercises can be grouped into a superset from the queue, members are held adjacent, the header shows the station label and the round number, and shared rest is gated on SupersetCycle.IsRoundComplete - which tests the sets actually logged rather than the position in the ring, so logging only the first station starts no rest and completing the round starts it in the same awaited path as the commit. Advancing happens after the rest decision so the decision is made from the station just finished, and Break dissolves a group left with one member.

*Evidence:* src/Forge.Domain/Workout/ActiveWorkoutState.cs:294-325 (group), :351-409 (members, advance, ResolveNextRest gating); src/Forge.Domain/Workout/SupersetCycle.cs; src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs:321-338, :514-603; src/Forge.App/Features/Workout/ActiveWorkoutPage.xaml:258-270; tests/Forge.Domain.Tests/Workout/SupersetCycleTests.cs

#### S10.04.02 — Execute circuits with rounds and station progress — **NOT-DONE**

There is no circuit execution. PlanBlockType.Circuit exists in the planning model but nothing executes rounds or stations: no total-rounds, no station complete/skip/modify actions and no circuit progress. The superset header shows a round number, which is the closest surface, but it has no round total and no station state.

*Evidence:* src/Forge.Domain/Planning/PlanEntities.cs:204-214 (Circuit is a plan block type only); no CircuitExecutionState or CircuitView exists in src; src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs:849-853 renders a station label with no round total

*Gaps:* AC1 and AC2 both fail - 'station 4 of 5, round 2 of 3' cannot be displayed and no circuit progress is persisted.

#### S10.04.03 — Run EMOM, AMRAP and Tabata timers — **NOT-DONE**

There are no timed formats. No EMOM, no AMRAP countdown, no Tabata intervals and no timed-format engine; the only timer in the app is the rest countdown.

*Evidence:* Repository-wide search for EMOM / Tabata / AMRAP across src returns no matches; src/Forge.Domain/Workout/RestTimer.cs is the only timer type

*Gaps:* Whole story unimplemented; all acceptance criteria unimplementable.

### F10.05 — Handle mid-workout changes and interruption recovery — **PARTIAL**

#### S10.05.01 — Swap, skip, reorder and add exercises mid-workout — **PARTIAL**

Skip, reorder and add-unplanned all exist, are session-scoped and persist the state snapshot immediately, and no plan is mutated - trivially, since no plan is involved. But Swap does not substitute: it simply makes the tapped queue row current, and ExerciseSubstitution is never called from the workout screen. Add unplanned picks the first catalogue exercise not already queued rather than letting the user choose.

*Evidence:* src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs:447-466 (Swap = SetCurrentExercise, no substitution), :468-497 (AddUnplanned takes FirstOrDefault), :499-512; src/Forge.Domain/Training/ExerciseSubstitution.cs is referenced only from src/Forge.App/Features/Exercises

*Gaps:* AC1 fails - swapping offers no same-pattern substitute and preserves no target context; AC2 fails - the summary has no unplanned-work section; adding an exercise is not a choice, it is whichever row happens to be first.

#### S10.05.02 — Pause, resume, extend and cut sessions short — **NOT-DONE**

There is no pause. No paused-at timestamp, no active-versus-total duration split, no resume-to-exact-state semantics beyond ordinary recovery, and no cut-short that records remaining exercises as not performed. Duration is measured from start to completion with no exclusions.

*Evidence:* src/Forge.Domain/Workout/ActiveWorkoutState.cs:41-44 (Elapsed = CompletedUtc-or-now minus StartedUtc, no pause term); src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs has no pause command among its 32 [RelayCommand] members, and its only Resume members are ResumeSensorsAsync and the recovery kind

*Gaps:* AC1 and AC2 both fail - paused time cannot be excluded because pause does not exist, and finishing early records nothing about the exercises not performed. S10.02.03 and S10.06.01 both lean on this gap; see the note against this story in the .md companion.

> **Note.** S10.02.03 (keep-awake) is marked DONE because both of its acceptance criteria pass against what was built - the previous KeepScreenOn value is captured, forced on, and restored on the way out. But its requirement reads "starting a workout enables keep-awake and completing, cancelling or **pausing beyond 15 minutes** disables it", and the pause half of that has no precondition to fire on, because pause is this story and this story was never built. That AC is therefore vacuous rather than met. It is recorded here, against the gap it depends on, rather than held against S10.02.03, which did its own job: marking that story down would misattribute this gap to it. S10.06.01 leans on the same absence - the workout summary can show a single duration but no active-versus-total split, because there is no paused time to exclude. Whoever builds pause should re-check both.

#### S10.05.03 — Recover active workouts after crash or process death — **DONE**

Recovery is thorough. Every set is written as a SetEntry row and the state snapshot is updated in the same transaction, guarded by AddMissingSetEntries so a replay cannot duplicate; set identifiers are Guid v7 assigned once at log time. On launch an unfinished session is found, classified by WorkoutRecoveryPolicy against a 12-hour window, and offered as Resume or Stale with Finish and Discard actions. When the snapshot row is missing or unusable the state is rebuilt from the SQLite session and its sets, and the rest timer reconciles from its absolute end time rather than a tick. The SQLite DateTimeOffset trap is handled explicitly by materialising before ordering.

*Evidence:* src/Forge.App/Features/Workout/WorkoutPersistenceService.cs:113-163 (client-side ordering, RebuildState fallback), :166-185 (AddMissingSetEntries); src/Forge.Domain/Workout/WorkoutRecoveryPolicy.cs; src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs:784-808, :756-775; src/Forge.Domain/Workout/ActiveWorkoutState.cs:89-105; tests/Forge.Domain.Tests/Workout/WorkoutRecoveryPolicyTests.cs; tests/Forge.Domain.Tests/Workout/ActiveWorkoutStateSerializationTests.cs

#### S10.05.04 — Handle phone calls, low battery and audio route changes — **NOT-DONE**

Nothing handles interruptions. There is no phone-call awareness, no battery monitoring and no audio-route handling. Timer target times do survive an interruption, but that is the general wall-clock reconciliation from S10.05.03, not interruption handling; the audio criteria are vacuous because the app plays no timer audio at all.

*Evidence:* No WorkoutInterruptionService or Interruptions folder in src; no Battery or audio-route API use anywhere in src/Forge.App; src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs:265-295 is generic reconciliation

*Gaps:* AC2 is only vacuously true - there is no speaker audio to suppress; no low-battery warning and no option to disable keep-awake on low battery.

### F10.06 — Complete workouts with summary and feedback — **PARTIAL**

#### S10.06.01 — Summarize volume, duration and adherence on completion — **PARTIAL**

The summary shows total volume load over working sets, the working-set count, duration and a per-muscle volume breakdown, all recomputed from durable SetEntry rows rather than stored aggregates, and warm-ups are correctly excluded. It also fails safe: a summary failure logs the exception and shows a fixed sentence rather than interpolating the message, which is a repeat-defect guard the codebase earned.

*Evidence:* src/Forge.Domain/Workout/WorkoutSummaryCalculator.cs:18-33; src/Forge.App/Features/Workout/WorkoutSummaryPageViewModel.cs:99-124, :126-142; src/Forge.App/Features/Workout/WorkoutSummaryPage.xaml:23-41

*Gaps:* No active-versus-total duration split (no pause exists); no total rep count; no planned-versus-completed exercises, because a session is never linked to a plan; AC2's 500 ms budget unmeasured.

#### S10.06.02 — Highlight PRs and compare against last time — **PARTIAL**

PR detection is genuine: heaviest load, best set volume and best estimated 1RM per exercise, compared against the owner's prior sets only, with the previous-set query scoped by profile and filtered client-side to avoid the SQLite DateTimeOffset translation failure. But 'compare against last time' does not exist - the Comparison property is initialised to a fixed sentence and is only ever overwritten by the error message, so no delta for top set, volume or completed sets is ever computed.

*Evidence:* src/Forge.Domain/Workout/WorkoutSummaryCalculator.cs:35-74; src/Forge.App/Features/Workout/WorkoutPersistenceService.cs:313-329 (owner-scoped previous sets); src/Forge.App/Features/Workout/WorkoutSummaryPageViewModel.cs:47-48 (hard-coded Comparison), :137; src/Forge.App/Features/Workout/WorkoutSummaryPage.xaml:19

*Gaps:* No rep PR; no per-exercise volume PR; the entire comparison-to-last-time half of the story is a placeholder string that reads to a user like a real feature - the same defect class as the hard-coded streak numbers; AC2's 10000-set 500 ms budget unmeasured.

#### S10.06.03 — Collect session RPE and subjective feedback — **NOT-DONE**

WorkoutSession.SessionRpe exists on the entity and is persisted by the schema, but nothing in the app ever reads or writes it: there is no session RPE prompt, no feeling tags and no skip affordance, because there is nothing to skip.

*Evidence:* src/Forge.Domain/Training/Exercise.cs:88 (SessionRpe on WorkoutSession); grep for SessionRpe across src/Forge.App returns no matches; src/Forge.App/Features/Workout/WorkoutSummaryPage.xaml:1-59 has no feedback surface

*Gaps:* Both acceptance criteria unimplementable - no feedback is ever collected, so nothing links to the session id.

#### S10.06.04 — Show a satisfying completion moment — **PARTIAL**

The success surface is correctly gated: it is only reached after CompleteAsync commits the session, so nothing celebrates an unsaved workout, and it is a static glyph plus headline with a semantic description, which means the reduced-motion criterion holds by construction. There is no celebration to speak of.

*Evidence:* src/Forge.App/Features/Workout/ActiveWorkoutPageViewModel.cs:731-755 (complete then navigate); src/Forge.App/Features/Workout/WorkoutSummaryPage.xaml:15-21 (static success card)

*Gaps:* No celebration animation at all, so the 3-second cap and the skip affordance are vacuous rather than met; the reduced-motion branch is untested because there is no motion branch to switch away from.


**E10 epic verdict: PARTIAL**

