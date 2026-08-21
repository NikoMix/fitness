# Forge performance budgets

Forge has carried a **2.0 second cold-start budget** in source comments since its first commit.
`MauiProgram` still keeps DevExpress localization disabled to protect it. Until now that number
had never been measured.

This document records what was measured, on what, what it means, and what to do when it moves.

- Harness and how to reproduce: [`tools/perf/README.md`](../../tools/perf/README.md)
- Instrumentation: `src/Forge.App/Composition/StartupTimeline.cs`

---

## 1. What the measurements were taken on

| | |
| --- | --- |
| Device | `emulator-5554`, `sdk_gphone64_x86_64` |
| OS | Android 15, API 35 |
| Device ABI | `x86_64` (ABI list also advertises `arm64-v8a` via translation) |
| Host | Windows, 32 logical CPUs, 61 GB RAM |
| Host state | **Heavily loaded.** 257-450 concurrent build processes, 100% CPU, ~1.7 GB free RAM |
| Emulator state | **Shared.** Other worktrees install the same application id onto it |

**These are emulator numbers taken on a saturated host, and they are indicative only.** Both facts
inflate every figure below. They are recorded rather than hidden because the alternative - quoting
a number without its conditions - is how a budget becomes fiction. Section 7 lists what must be
confirmed on real hardware before any of this is treated as a shipping commitment.

Every result file under `tools/perf/results/` embeds the device, the ABI and the host load, so a
number can always be traced back to the conditions that produced it.

---

## 2. Headline numbers

Cold start, measured as the system's own `Displayed` event (first frame on screen), median of
10-12 runs after 3 discarded warm-ups. Cold process, warm page cache - the repeat-launch case.

| Build | ABI on device | Median | Range | IQR |
| --- | --- | --- | --- | --- |
| Debug | `x86_64` native | **6344 ms** | 5607-7912 | 1194 |
| Debug | `arm64-v8a` translated | 11837 ms | 10476-13724 | 1902 |
| Release, before optimisation | `arm64-v8a` translated | 7754 ms | 6705-11532 | 1062 |
| **Release, after optimisation** | `arm64-v8a` translated | **6881 ms** | 6339-7999 | 421 |
| Release, first launch after install | `arm64-v8a` translated | 9503 ms | 6240-15417 | 2224 |

Three things have to be said plainly about this table.

**The Release rows are not latency numbers.** The Release configuration builds `android-arm64`
and `android-arm` only. The x86_64 emulator advertises `arm64-v8a` in its ABI list because it can
translate ARM, so the shipping APK installs and launches without any error and then runs every
instruction through a binary translator. Measured back-to-back with the same Debug APK forced to
each ABI, translation costs **1.87x** (11837 ms vs 6344 ms). The Release figures are upper bounds
inflated by roughly that factor, not measurements of Release on ARM hardware.

**The spread is large because the host was saturated.** An IQR of ~1000 ms on a ~7000 ms median is
about 15% noise. Differences smaller than that are not real unless the runs were taken
back-to-back under comparable load.

**Debug figures come from an earlier revision of the instrumentation** that wrote marks directly
rather than buffering them, and therefore carry roughly 150 ms of measurement overhead that the
current code does not. They are kept because they are the only *native-ABI* numbers available on
this machine, and native ABI matters more here than the 150 ms does.

### Against the 2.0 s budget

Nothing measured here comes close to 2.0 s. Even the fastest single native-ABI Debug run was
5607 ms. Section 6 proposes what to do about that.

---

## 3. Where the time goes

Phase breakdown from the optimised Release build, medians, in milliseconds since the first line of
managed code. `FIRST FRAME` is the system's `Displayed` event placed on the same axis.

| Phase | At | Segment cost | What happens |
| --- | --- | --- | --- |
| _launch requested_ | -972 | 172 | Android handles the intent before a process exists |
| _process start_ | -800 | 800 | Zygote fork, `libmonodroid`, runtime start, assembly mapping |
| `program-enter` | 2 | - | First statement of `CreateMauiApp` |
| `timeline-probe` | 4 | 2 | Cost of one timeline mark (see §5) |
| `theme-set` | 176 | 172 | DevExpress `ThemeManager` assignment |
| `builder-created` | 255 | 79 | `MauiApp.CreateBuilder()` |
| `devexpress-registered` | 538 | **283** | Six `UseDevExpress*` registrations |
| `maui-configured` | 774 | 237 | CommunityToolkit, MediaElement, notifications, fonts |
| `services-registered` | 971 | 197 | Infrastructure + shell + 18 features |
| `container-built` | 1229 | 257 | `builder.Build()` |
| `db-begin` | 3594 | 2365 | Background database startup starts (off the critical path) |
| **`FIRST FRAME`** | **5741** | **~4500** | **Shell resolve, XAML inflation, first layout, first draw** |

The shape is unambiguous. Of the ~6.9 s to first frame, roughly:

- **~14%** is Android and the .NET runtime starting, before Forge runs at all.
- **~18%** is everything `MauiProgram` does, end to end.
- **~65%** is between the container being ready and the first frame appearing.

That last segment is `App.CreateWindow` resolving `AppShell`, inflating its XAML, constructing the
five `ShellContent` tabs and the first page, and laying out and drawing it. It is the single
largest cost in cold start by a wide margin, and **no part of it is in this branch's ownership**.
Section 6 records that as a finding rather than a fix.

The original 2.0 s budget was placed on the assumption that startup cost lived in registration and
localization. It does not. Even if `MauiProgram` were reduced to zero, cold start on this device
would still be around 5.6 s.

### First launch after install

`-Mode FirstRun` clears app data so the database is created and the catalogue imported. Measured
on the same translated Release build:

| Phase | At | Segment cost |
| --- | --- | --- |
| `container-built` | 1533 ms | |
| `db-key-ready` | 5567 ms | 924 ms to create the key in the Android Keystore |
| **`FIRST FRAME`** | **7765 ms** | |
| `db-schema-ready` | 29883 ms | **22.1 s** to build the EF model and create the schema |
| `db-seed-complete` | 40137 ms | **10.3 s** to import 60 exercises |

First frame costs ~2 s more than a repeat launch. The database work costs a further **32 seconds
after the first frame**, all of it on a background thread. Divide by the 1.87x translation factor
and allow for the saturated host and it is still tens of seconds during which the shell is up but
every data-backed screen is waiting. Section 6 recommends what to do about it.

---

## 4. Runtime behaviour

Measured with `Measure-Runtime.ps1` on the translated Release build, two passes over all five tabs.

### Memory at rest

| | |
| --- | --- |
| TOTAL PSS | 464 MB |
| of which `Other mmap` | 205 MB |
| Java heap | 3.9 MB |
| Native heap | 77 MB |
| Code | 87 MB |

**This figure cannot be used as a budget.** `Other mmap` at 205 MB is dominated by the binary
translator's code cache, which does not exist when the APK runs on the ARM hardware it was built
for. Even the remaining ~260 MB is inflated by translation. What can be said is that Java heap is
tiny (3.9 MB) and the weight is in native and code mappings, which is the expected shape for a
.NET MAUI app with profiled AOT - and that this needs re-measuring on a real device before anyone
sets a number.

### Screen settle time and jank

Time from tapping a tab until the app stops producing frames, and the share of frames that missed
their deadline.

| Tab | First visit | Second visit | Janky frames | Skipped frames, first visit |
| --- | --- | --- | --- | --- |
| Today | 9624 ms | 2139 ms | 56% | 119 |
| Train | 3099 ms | 2829 ms | 41% | 0 |
| **Nutrition (charts)** | **10355 ms** | 2997 ms | 37% | **461** |
| Progress | 4202 ms | 3377 ms | 50% | 42 |
| Profile | 2304 ms | 3780 ms | 40% | 0 |

Two things stand out and both would feel bad on a mid-range device:

1. **First visit costs 3-5x a repeat visit.** Page construction and XAML inflation are not cached
   until a page has been seen once. Every tab a user touches for the first time in a session pays
   this.
2. **Nutrition, the chart-heavy screen, is the worst by a wide margin**: 10.4 s to settle and
   **461 skipped frames** on first render. Even after dividing by the translation factor that is
   several seconds of a visibly stuttering screen. This is the strongest runtime signal in the
   whole exercise and it points at DevExpress chart initialisation plus first-time page
   construction.

Jank of 37-56% across every tab is high, but on a saturated host under binary translation it
should be treated as "needs measuring properly on hardware" rather than as a number to act on.

---

## 5. Claims that were verified rather than assumed

### The database work does not block the first frame — confirmed

`App.OnStart` deliberately does not await `ForgeStartupService`, and the comment there justifies
it against the cold-start budget. That is now measured, not asserted. The harness places the
database marks and the system's `Displayed` event on one timeline:

| Scenario | Database finished |
| --- | --- |
| Repeat launch (Debug, native) | **4676 ms after** the first frame |
| First launch after install (Release) | **32371 ms after** the first frame |

The design holds, and it matters more than the comment claimed: on first launch the database work
runs for over half a minute behind an already-usable shell. Had it been awaited, the app would
have shown nothing for that entire time.

### The 2.0 s budget was never the reason to disable DevExpress localization

`MauiProgram` keeps `useLocalization: false` partly to protect a 2.0 s budget. Since the real
figure is 3-4x that and dominated by shell construction, the localization decision should be made
on the merits when E24 lands, not defended as a startup optimisation. Whatever it costs, it is not
what is keeping Forge above 2 s.

### Database startup is dominated by EF model building, not encryption

`db-schema-ready` was the largest single database segment. The obvious suspect was SQLCipher:
`PRAGMA key` runs on every connection open, and `DbContext` is transient. **That suspicion was
wrong.**

Measured directly against the real `ForgeDbContextFactory` on desktop .NET:

| Operation | Cost |
| --- | --- |
| **EF model build (first `DbContext` in the process, 27 entity types)** | **2502 ms** |
| EF model build, second context in same process | 32 ms |
| `EnsureCreated` on a fresh database | 727 ms |
| `EnsureCreated` on an existing database | 8 ms |
| Connection open **with** SQLCipher key, steady state | 0.3 ms |
| Connection open **without** any key, steady state | 0.3 ms |
| `PRAGMA integrity_check` | 2.7 ms |
| `PRAGMA quick_check` | 1.0 ms |
| `Database.GetMigrations()` | 4.9 ms |

SQLCipher key derivation is not measurable in the steady state, because `Microsoft.Data.Sqlite`
pools connections and the key is derived once. The integrity check is also cheap today. **EF Core
model building is the cost**, it is paid once per process - so on every cold start, not just on
first install - and it is entirely inside `Forge.Infrastructure`.


---

## 6. Changes made, and what each one bought

Only two changes were made, both inside this branch's owned files, and both had to justify
themselves with a measurement.

### Buffered startup marks — removed ~134 ms from the critical path

The first version of `StartupTimeline` wrote each mark straight to logcat. The self-probe - two
marks emitted back-to-back with nothing between them - measured the first write at **135.6 ms** on
the Release build. That is first-use cost: resolving and warming the whole Android logging path,
paid on the UI thread, on the critical path to the first frame, in the configuration that ships.

An instrument that costs 136 ms to measure a 2 s budget is not an instrument, it is a regression.
Marks taken before the shell is up are now buffered in memory - a timestamp read and an array
write - and flushed to logcat from a background thread by `ForgeStartupService`. A
`timeline-anchor` line lets the harness put the buffered marks back onto real time.

| | Before | After |
| --- | --- | --- |
| Cost of one mark (same-run self-probe) | 135.6 ms | **1.6 ms** |
| Release cold start, median | 7754 ms | 6881 ms |
| Release cold start, IQR | 1062 ms | 421 ms |

**Attribute this carefully.** The self-probe measurement is taken inside the same run under
identical conditions, so the **~134 ms** it reports is solidly attributable to the change. The
873 ms drop in the end-to-end median is *not*: host load fell from 354 to 257 concurrent build
processes between the two runs, and that easily accounts for the remainder. The honest claim is
134 ms, not 873 ms.

### Process-age reporting via the platform API — avoided ~160 ms

The first attempt read `/proc/self/stat` to work out how long the process had been alive before
managed code ran. It failed on device and, because the failure went through exception handling and
JIT on a cold path, charged **162 ms** to the very first phase. The instrument was reporting
mostly itself.

Replaced with `Android.OS.Process.StartElapsedRealtime` and `SystemClock.ElapsedRealtime()`, which
are two JNI property reads and cost nothing measurable. The static constructor now reads the ages
**before** taking the stopwatch origin, so if a future fallback is ever slow, its cost is excluded
from phase deltas rather than attributed to the first phase.

### Net effect

Both changes remove overhead this branch introduced; neither speeds up code that existed before.
That is worth stating plainly: **no change in this branch made the pre-existing app faster.** What
the branch delivers is the ability to see where the time goes, and it does so at a measured cost
of 1.6 ms.

### What was tried and did not help

Recorded because a ruled-out hypothesis saves the next person the same day.

| Attempt | Outcome |
| --- | --- |
| **Suspecting SQLCipher key derivation.** `PRAGMA key` runs on every connection open and `DbContext` is transient, so this looked like the obvious database cost. | **Wrong.** Steady-state connection open is 0.3 ms with a key and 0.3 ms without. `Microsoft.Data.Sqlite` pools connections, so the key is derived once. |
| **Suspecting `PRAGMA integrity_check` on every launch.** O(database size), runs unconditionally. | **Not the cost today** - 2.7 ms. Kept as a forward-looking note in §7 because it only grows. |
| **Building Release natively for the emulator** with `-p:RuntimeIdentifiers=android-x64`, so the Release number would not go through binary translation. Four variations tried, including `UseArtifactsOutput=false`, `AppendRuntimeIdentifierToOutputPath=false` and `BuildProjectReferences=false`. | **Abandoned.** Passing the RID as a global property propagates it to the `net10.0` class libraries, whose artifacts paths then do not line up (MSB3030), and forcing past that produced a mismatched runtime pack that failed in ILLink. Worse, the partial attempts left `*_android-x64` directories in `artifacts/obj/`, which contaminated a later Release APK into a 66 MB three-ABI build that **crashed on launch**. That looked like a product defect for a while. Cleaning the intermediates fixed it. The lesson is in `tools/perf/README.md`. |
| **Reducing the DevExpress registration chain.** `devexpress-registered` is 283 ms, the largest single segment in `MauiProgram`. | **Not attempted.** All six modules are used by shipped features, and at 283 ms out of ~6900 ms it is not where the problem is. Splitting the chain to make it *measurable* was worth doing; removing calls was not. |

---

## 7. Recommended budgets

The 2.0 s budget is not achievable and is not close. Keeping it would mean shipping against a
number that is already broken by a factor of three, which teaches everyone to ignore it.

Proposed replacement, expressed against **first frame on a mid-range physical Android device**,
because that is what a user experiences and what the emulator cannot tell us:

| Metric | Proposed budget | Basis |
| --- | --- | --- |
| Cold start (repeat launch), Release, mid-range device | **2.5 s** | Needs confirmation on hardware; see §8 |
| Cold start (repeat launch), Release, flagship device | **1.5 s** | |
| First launch after install, to first frame | **4.0 s** | Measured 9.5 s translated on a saturated host |
| Time to a usable data screen, first launch | **8.0 s** | Currently ~32 s of background database work; see below |
| First render of the chart screen | **1.5 s, under 5 skipped frames** | Currently 10.4 s and 461 skipped frames |
| Memory at rest | **not yet set** | Emulator figure is dominated by the translator; see §8 |
| Regression gate | **+15% on the median** | Roughly the observed run-to-run noise |

These are proposals, not measurements. **The honest position today is that Forge's cold start on
real hardware is unknown**, because every number here was taken on an emulator, on a saturated
host, and for Release through a binary translator. What is known with confidence is the *shape*:
shell construction and first-page render dominate, and that will be true on any device.

The one budget that should be adopted immediately is the process one: **cold start is measured on
every release build, on a physical device, and the number is written down.** A budget nobody
measures is what produced this situation.

### Recommendations that need an owner outside this branch

Reported, not made, because the files belong to other streams. Ordered by measured impact.

1. **First render of the Nutrition chart screen** (`Features/Nutrition/`). 10.4 s to settle and
   **461 skipped frames** on first visit, against 3.0 s on a repeat visit. The worst runtime
   result measured. Worth profiling DevExpress `ChartView` initialisation and whether the chart
   can be created after first layout rather than during it.
2. **Shell and first-page construction** (`Hosting/`, `Features/Today/`). ~4.5 s, about 65% of
   cold start. All five `ShellContent` tabs are declared up front; if their pages are constructed
   rather than deferred, four of them are built before the user can see any of them. This is the
   single biggest cold-start item.
3. **EF Core compiled models** (`Forge.Infrastructure`). Model building costs ~2.5 s per process
   and is the dominant database startup cost - 22 s of the 32 s first-run background work.
   `dotnet ef dbcontext optimize` generates a compiled model and `UseModel()` wires it in, which
   typically reduces model build to near zero. Does not affect first frame; it is what gates the
   first data-backed screen.
4. **Seed import does one query per exercise** (`SeedContentImporter.ImportExercisesAsync`). The
   loop issues a `SingleOrDefaultAsync` per catalogue item, so 60 exercises cost 60 round trips
   plus 60 inserts - measured at 10.3 s on first run. Loading the existing ids once into a
   dictionary and using `AddRange` would make it a handful of statements. Only affects first
   launch, but it is a third of that cost.
5. **`PRAGMA integrity_check` on every launch** (`DatabaseInitializer`). Only 2.7 ms today, so it
   is not a problem yet - but it is O(database size) and Forge's database only grows. Consider
   `quick_check`, or running it only after an unclean shutdown. **Flagged early, not urgent.**
6. **Release APK is 66.2 MB** against a 40 MB budget noted in `Forge.App.csproj`. The comment
   there says restricting to two ABIs fixed this; measured, it did not. The AAB is 65.3 MB, and
   Play splits by ABI so a single-device download is roughly half - but the budget as written is
   not met by the artifact as built. Worth restating the budget in terms of *delivered download
   size* so it is checkable.

---

## 8. What must be confirmed on real hardware

Everything in this document that is a number rather than a shape.

- **Release cold start on ARM.** Never measured natively. Every Release figure here went through
  binary translation, worth ~1.87x. This is the single most important gap.
- **Memory at rest.** Measured at 464 MB PSS, but 205 MB of that is the binary translator's code
  cache. The real figure is unknown and no budget should be set until it is measured natively.
- **Chart screen render.** 10.4 s and 461 skipped frames is alarming, but it is translated and
  host-starved. Confirm before treating the magnitude - though not the ranking - as real.
- **Mid-range behaviour.** The emulator has 32 host cores behind it. A mid-range phone has 8 weak
  ones, and the segment most exposed to that is the ~4.5 s of shell construction and first layout,
  which is CPU-bound and largely single-threaded.

Recommended: run `Measure-ColdStart.ps1` and `Measure-Runtime.ps1` against one physical mid-range
device and one flagship, on a quiet host, and replace sections 2, 3, 4 and 7 with those numbers.

---

## 9. When a number regresses

1. **Check the conditions before the code.** Open the result JSON and compare `Device`, `Abi` and
   `HostLoadBefore` against the run you are comparing to. A different ABI or a busy host explains
   more regressions than code does.
2. **Confirm the build is what you think it is.** A Debug build without
   `-p:EmbedAssembliesIntoApk=true` runs managed code from the device override directory, not from
   the APK you installed. The harness warns; believe it.
3. **Read the phase breakdown, not the total.** The segment that moved names the owner.
4. **Re-measure back-to-back.** Old build, new build, same session, same load. Do not compare
   against a number from last week.
5. **Only then look for a cause,** and re-measure after each change rather than after all of them.

