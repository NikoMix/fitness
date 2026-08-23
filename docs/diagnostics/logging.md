# Diagnostics: the on-device log, redaction, and the crash boundary

Status: implemented. This document records the budget, the redaction design and the trades taken,
because most of them are not obvious from the code and one of them deliberately makes the log
harder to read.

## The gap this closed

`MauiProgram.cs` configured logging like this, and this was all of it:

```csharp
#if DEBUG
        builder.Logging.AddDebug();
#endif
```

**A Release build registered no logging provider at all.** Every logging call in the app wrote to
nothing. That is around twenty call sites across eleven files, and they are not incidental — they
are the places the app already knows something has gone wrong:

| Where | What it was trying to say |
| --- | --- |
| `DatabaseInitializer` | migration failed; SQLite integrity check failed; integrity check could not complete; a pre-migration database was adopted |
| `ForgeStartupService` | database startup failed; a plaintext database was converted to an encrypted one |
| `AppLockCoordinator`, `AppLockPresenter`, `AppLockLifecycleEvents` | unlock outcomes; the lock screen could not be presented |
| `WorkoutSummaryPageViewModel` | the summary would not build; the screen could not be left |
| `ActiveWorkoutPageViewModel`, `TrainViewModel`, `ExerciseMediaCatalogue`, `MediaFeatureRegistration` | plan load failures, last-performance lookups, media catalogue lookups |

Everything downstream that promises to "log the exception and enter recovery" had nowhere to log.

This matters more here than in most apps because Forge is **local-only by design**. There is no
crash-reporting service and no telemetry backend, and there will not be one — that is a product
decision recorded in `docs/adr/0001-local-first-no-backend.md`, not an oversight. The on-device
file is therefore the only evidence that will ever exist when something goes wrong for a real
user.

**Explicitly out of scope, permanently:** network transmission of any kind, analytics SDKs, and
crash-reporting services. Any of them would contradict the privacy policy, the Play Data Safety
declaration and the store listing at the same time.

## The budget and rotation policy

| Setting | Value | Why |
| --- | --- | --- |
| Bytes per file | 512 KiB | A redacted entry measures 120–260 bytes, so one file holds roughly 2,000–4,000 of them. Forge writes on the order of ten per ordinary launch and a burst on failure, so one file already spans hundreds of launches. |
| Files retained | 3 (active + 2 archives) | Chosen from the failure it has to survive. A crash loop writes the same entry repeatedly; with the whole budget in one file the loop would erase the original fault before anyone read it. Two archives mean the launch that first went wrong is still on disk when the noise arrives. |
| **Ceiling** | **1.5 MiB** | Small enough that it can never be why a phone runs out of space, and small enough that a mail or messaging app will carry it without complaint. A log nobody can attach to a message is not evidence. |
| Minimum level | `Information` | Every site listed above is Information or higher. Admitting `Debug` would multiply the volume without adding one of them. |
| Queue capacity | 1024 entries, drop newest | Logging must never block the app and must never grow without bound. Dropped entries are counted and the count is written out when the writer catches up, so the file says it lost entries rather than quietly having fewer. |

Rotation is by rename, on write, **before** the entry is appended, so the cap is a real ceiling
rather than a ceiling plus one entry. `forge.log` → `forge.1.log` → `forge.2.log` → deleted.

Fixed names rather than timestamps, so the set of files the app can ever have is finite and known:
erasure knows exactly what to delete, sharing knows exactly what to attach, and no stale file from
a build six months ago survives because its name did not match a glob.

The one deliberate exception: **an entry larger than the whole file cap is written anyway.**
Losing it would be worse than one oversized file, and it is always the interesting one.

`DiagnosticLogOptionsTests.The_default_budget_is_one_and_a_half_mebibytes` states the ceiling as a
test, so raising it is a deliberate act with a diff attached rather than a default that drifts.

## Where the files live, and how erasure reaches them

`<FileSystem.AppDataDirectory>/diagnostics/`. App-private on both platforms — no other app and no
file manager can read it.

That location is not incidental. `LocalDataErasureService` deletes the contents of
`AppDataDirectory` and `CacheDirectory` with `SearchOption.AllDirectories`, so **"delete my data"
reaches the log without erasure needing to know the log exists.** A log of the deleted data
surviving the deletion would be a real breach, not a cosmetic defect.

Two hazards in that interaction are handled explicitly:

1. **The sink re-creates its own directory.** Erasure deletes files first and directories second.
   A single log line landing between those two passes leaves a fresh `diagnostics/forge.log`, the
   non-recursive directory delete then fails on a non-empty directory, and a user who asked to be
   forgotten is told their data could not be erased. `DeleteMyDataPageViewModel` therefore wraps
   the erasure in `IDiagnosticLog.SuspendForErasure()`, which closes the handle and refuses writes
   until the erasure returns.
2. **An unlinked file still accepts writes.** On Android, deleting a file that is open unlinks the
   inode rather than failing — the same hazard `docs/performance/data-access.md` records for
   `forge.db`. `RollingLogFile` re-checks that the active file still exists before each write and
   reopens if it does not, so the sink comes back by itself instead of writing into a file nobody
   can see for the rest of the launch.

A user who wants only the log gone can delete it on its own from Settings → Data management,
without erasing their training history.

## Redaction

This is the part that had to be right, and it is why the task was not trivial.

Forge holds body weight, injuries in free text, food logs, workout history and profile names. Much
of that is GDPR **Article 9 special-category data**. Forge encrypts its database with SQLCipher
specifically so that it is not readable at rest. A log that captured any of it would have quietly
created an unencrypted second copy of the most sensitive thing the app stores, sitting next to the
encrypted one.

### The threat model

The leak is **not** deliberate logging. Nobody writes `logger.LogInformation("weight {Weight}")`
on purpose. It is **exception messages and file paths**, which arrive already carrying values
nobody chose to include:

```
ArgumentException: 'Left knee - ACL reconstruction 2019' is not a valid note.
FormatException: The input string '82.4' was not in a correct format.
IOException: Could not open /data/user/0/com.nikomix.forge/files/Alexandra-export.json
```

So the design is inverted from the usual one. Rather than looking for known-bad content, it treats
every variable region of a line as suspect and keeps only what is recognisably structural: type
names, property names, counts, durations.

### The rules, in order

| # | Rule | Replaces with |
| --- | --- | --- |
| 1 | Email addresses | `<email>` |
| 2 | `file://` and `content://` URIs | `<path.ext>` |
| 3 | Windows paths, either separator | `<path.ext>` |
| 4 | POSIX paths of two or more segments | `<path.ext>` |
| 5 | ISO dates and date-times, and `d/m/yyyy` | `<date>` |
| 6 | Quoted runs that are **not** a dotted identifier | `'<redacted>'` |
| 7 | A health term, a separator, and everything after it to `;`, `|` or end of line | `term: <redacted>` |
| 8 | A number carrying a unit Forge measures people in | `<measurement>` |
| 9 | Digit runs of 7 or more | `<number>` |
| 10 | Bare numbers within 48 characters of a health term | `<number>` |
| 11 | Length caps: 512 for a message, **240 for an exception message**, 4096 for a whole exception | `…` |

Order is load-bearing and the code says why at each step.

Rule 7's boundary set is `;`, `|` and end of line, and deliberately **not** `,` or `.`. That is a
fix for a leak found on a device, not a preference: with a comma ending the value,
`injury note: Left knee - ACL reconstruction 2019, avoid deep flexion` came out as
`injury note: <redacted>, avoid deep flexion`. Free text has commas and full stops in it, so
treating them as boundaries redacts the first clause of an injury description and prints the rest
— which is worse than not running at all, because the line looks redacted.

The rules run over the **whole rendered line**, not only over the exception, so a message argument,
a scope value and an exception message are all treated with equal suspicion.

**Scopes are not written at all.** A scope value is caller-supplied data with no message template
constraining it, which makes it one of the easiest routes for a body weight to reach a file.
`BeginScope` returns a no-op so callers still work; their values simply do not reach the disk.

**The redactor fails closed.** A regex timeout or an argument fault collapses the whole input to a
marker rather than falling back to the original text. A redactor that returns its input when it
fails is a redactor that leaks exactly when it matters.

The two failure markers are distinct from the ordinary one — `<redacted: the redaction rules did
not finish>` and `<redacted: the redaction rules failed>` — and that distinction was earned. On a
device, a MAUI layout warning with nothing sensitive in it came out as a bare `<redacted>`, which
looked like an over-eager rule. It was a **250 ms regex timeout on the first match of a cold
Release build**: the identical warning two seconds later came through intact. The budget is now
2 s, which the drain thread can afford because it is not the UI thread, and the file now says
which of the two things happened.

### What this deliberately costs

**Rule 6 loses the text of a LINQ expression.** That is a real loss: it is how the `DateTimeOffset`
ordering fault was diagnosed. The trade was taken anyway, because a LINQ expression can quote a
constant a user typed, and the same fault is still locatable from the exception type and the stack
frame. Over-redaction produces a log that is harder to read; under-redaction produces a breach.

**Rules 8 and 10 over-redact.** `3 in` is treated as three inches. A count near the word "sleep"
is blanked. This is the safe direction and it is accepted.

### What is deliberately kept, and stated on screen

- **Entry timestamps.** They say when Forge was running, which is a weak signal about when
  somebody trained. Without them the file cannot be correlated with "it happened this morning".
- **Exception type names and stack frames**, with source paths reduced to `<path.cs>`.
- **GUIDs.** A random identifier with no lookup table off the device is what tells you the same row
  failed twice. It is named in the on-screen disclosure rather than quietly kept.

### What was tried and rejected

| Option | Why not |
| --- | --- |
| **Prefix-matching the health terms** (`injur*`, `name*`, `fat*`) | Quietly wrong. `name` matches `namespace`, `fat` matches `fatal`, `age` matches `agent`. Each turns a common word in an ordinary exception into a trigger that strips the numbers out of the line, for no privacy gain. Terms are matched as whole words with the variants spelled out. |
| **Adding `exercise`, `set`, `rep`, `workout` to the terms** | They are structural nouns in this app, present in a large share of diagnostic lines. "Imported 60 exercises" would become "Imported `<number>` exercises". An exercise name is a catalogue entry, not a fact about a person. |
| **`of` as a value separator** | The most collision-prone preposition in .NET diagnostics — "out of range", "index of", "profile of type". It removed more type names than it would ever have removed health data. |
| **Redacting the whole line when any health term appears** | Too blunt. It deletes exactly the diagnostics the feature exists to preserve. Replaced by a 48-character proximity window. |
| **Redacting all URLs** | A URL in an exception message is almost always a framework's own documentation link. Useful and provably impersonal. The path rules are written to leave them: the first attempt matched `https://` as a drive root and turned every URL into `http<path><path>`, which `A_support_url_survives` now pins. |
| **Encrypting the log file** | Considered. It would defeat its own purpose: the only route off the device is the user attaching it to a message, and a file the recipient cannot open is not evidence. Redaction is the control; the file is written so it does not need to be secret. |
| **Hashing identifiers instead of removing them** | A hash of a body weight has about ten thousand possible inputs. It is reversible by inspection and would look like protection. |

### What was proved

`DiagnosticLogRedactorTests` is written as attacks rather than examples — 25 tests, and the second
half is as important as the first. Attacks that must fail:

- a body weight with a unit (`82.4 kg`) and, separately, **without one** (`Body weight 82.4 was rejected`) — the second is the shape a validation exception actually takes, and only the proximity rule catches it;
- an injury after a label (`injury: Left knee ACL reconstruction`) and quoted inside an `ArgumentException`;
- a quoted number with **no health word anywhere on the line** (`FormatException: The input string '82.4'`);
- an export filename built from a profile name, arriving as a POSIX path, a `file://` URI, and a Windows path with either separator;
- an email address, a training date, a food log entry, a set of body measurements, a 16-digit reference.

Diagnostics that must survive: exception type names; `'Exercise.Id'` because it is a dotted
identifier; `Imported 60 exercises in 412 ms after 3 retries`; `128 MB`; an English sentence with
two apostrophes in it; a support URL; and **the reason a file could not be opened, alongside the
redacted path** — the path rule matched greedily once and ate the rest of the sentence with it.

`ForgeFileLoggerProviderTests` proves the rules are actually *wired*, which is a separate defect:
a real `ILogger.LogError` carrying an exception with a body weight and an injury in it, read back
off a real file, asserting the values are absent and the exception type is present. An isolated
redactor the sink forgets to call is the same defect as no redactor at all.

## The crash boundary

`ForgeCrashBoundary` subscribes three handlers, once, at composition:

| Hook | Behaviour |
| --- | --- |
| `AppDomain.CurrentDomain.UnhandledException` | Write immediately, leave a breadcrumb when terminating. |
| `TaskScheduler.UnobservedTaskException` | Write, then `SetObserved()`. A faulted fire-and-forget task is worth recording and is not worth ending a training session over. |
| `AndroidEnvironment.UnhandledExceptionRaiser` | The one that actually fires on Android: a managed exception escaping a Java-invoked frame — which is every UI callback — reaches this before `AppDomain` sees anything, and in several cases instead of it. |

**What it can and cannot do.** An unhandled exception on the UI thread kills the process whatever
Forge does about it. `e.Handled` is deliberately left alone: setting it swallows the exception and
lets the app carry on over state nobody can account for, trading a visible crash for invisible
corruption of the only copy of the user's training history.

What it does instead is make the death informative:

1. The fault is written **synchronously, bypassing the queue**. A queued entry needs a thread-pool
   continuation to be scheduled, and a process the runtime is already tearing down cannot promise
   one.
2. A `last-crash.txt` breadcrumb is left — three fields, tens of bytes, carrying the exception
   **type** and never its message, so the breadcrumb cannot become the one unredacted copy of an
   exception's text.
3. The next launch reads it, and Settings → Data management says so plainly: *"Forge closed
   unexpectedly on 11 February at 06:30. Nothing you had saved was lost."* No type name, no
   message, no stack — this is the screen pattern that once showed a user a LINQ expression and a
   Microsoft support URL immediately after they finished training.
4. Sharing the log acknowledges the crash, so the notice appears until it is acted on and not
   afterwards.

`Capture` swallows everything. This is the last handler in the process; an exception raised here
has nowhere to go and would replace a diagnosable crash with an undiagnosable one.

**The same fault is recorded once, not twice.** On Android a terminating exception reaches both
`AndroidEnvironment.UnhandledExceptionRaiser` and `AppDomain.UnhandledException` — measured, not
assumed: one deliberate crash produced two identical entries with two stack traces. That halves
the useful history in a crash loop, which is exactly the case the three-file retention exists for.
The boundary suppresses the second by **reference equality**, so only the same exception object
arriving twice is dropped; two genuinely different faults are always both recorded even when their
types and messages match. The breadcrumb is written on both paths regardless, because it is
idempotent and losing it is worse than rewriting it.

## Startup cost

The constraint was explicit: a stream took Android cold start from ~27 s to ~10 s by removing five
redundant SQLCipher key derivations, and that must not be spent back.

**Nothing on the startup path touches the disk, and nothing resolves a platform path.**
`AddForgeDiagnostics` subscribes three event handlers and allocates two objects. It does not read
`FileSystem.AppDataDirectory`, create a directory, open a file, create the channel or probe
anything. All of that happens on the drain thread when the first entry is written, after the shell
is up.

Two tests state this rather than a comment: `Constructing_the_provider_touches_no_files` and
`The_directory_itself_is_not_resolved_until_the_first_entry`, the second counting how many times
the directory factory is invoked.

### What it measures

`MauiProgram` emits `logging-configured` immediately after the call, so the
`services-registered` → `logging-configured` gap **is** the whole cost. Measured on
`emulator-5554`, Release, `-p:EmbedAssembliesIntoApk=true`, with temporary sub-phase marks inside
the call:

| Run | provider ctor | `builder.Logging` + `AddProvider` | crash boundary | self-test probe¹ | **total** |
| --- | --- | --- | --- | --- | --- |
| Fresh install | 24.5 ms | 20.9 ms | 9.2 ms | 14.7 ms | 69.3 ms |
| Repeat 1 | 108.0 ms | 73.5 ms | 8.3 ms | 19.6 ms | 209.4 ms |
| Repeat 2 | 39.5 ms | 52.5 ms | 7.3 ms | 23.4 ms | 122.7 ms |
| Repeat 3 | 18.6 ms | 15.8 ms | 5.5 ms | 9.4 ms | 49.3 ms |

¹ The self-test probe is a `File.Exists` that exists only under `FORGE_DIAGNOSTICS_SELFTEST` and
is not in any shipping build. Subtracting it, the shipping cost is **40–190 ms, median ~80 ms**.

There is no file I/O in any of that — there is none to do. It is first-touch assembly loading and
type initialisation for `Microsoft.Extensions.Logging` and the diagnostics types, which is why it
tracks host load: on the run where `services-registered` itself was 4562 ms the diagnostics phase
was 209 ms, and on the run where it was 2641 ms the phase was 49 ms.

### What cannot be claimed

**A controlled before/after was not achievable on this host, and the numbers should not be read as
one.** Two Release builds whose diagnostics code was byte-identical measured 324 ms and 69 ms for
the same phase, purely across runs. `program-enter` → `container-built` varied from 3158 ms to
6172 ms between repeat launches of one build. This is the same caveat
`docs/performance/data-access.md` records for its own emulator figures, and it applies here with
equal force: the within-run breakdown above is attributable because the parts are sequential and
sum to the whole, and nothing else here is.

Much of the remaining cost is likely **moved rather than added** — `builder.Build()` constructs
the logging infrastructure regardless, and before this change a Release build never touched
`builder.Logging` in `MauiProgram`, so `Microsoft.Extensions.Logging` loaded inside
`container-built` instead. That is a reasonable inference from where the time sits; it is not
measured, and it is not claimed as measured.

**One thing was measured and acted on.** The first version resolved `FileSystem.AppDataDirectory`
and read the crash breadcrumb eagerly during composition. Both are now deferred. Given the run
variance above, the improvement cannot be quantified honestly — but a platform call that
initialises MAUI Essentials, plus a file probe, both on the critical path to the first frame for
answers nothing needs until a settings screen opens, is wrong whatever the number says.

## Sharing

Settings → Data management → Diagnostic log.

`PrepareForSharingAsync` flushes the queue, concatenates every retained file oldest-first into
`<CacheDirectory>/forge-diagnostics.log`, and hands the path to the platform share sheet. A copy
rather than the live file, because rotation could rename the original out from under the share
sheet mid-transfer. The cache directory is cleared by erasure too.

Source files are opened with `FileShare.ReadWrite`: the sink holds the active file open for
writing on purpose, and the convenience overloads (`File.ReadAllText`) ask for a share mode that
excludes an existing writer, so they throw against a perfectly healthy file. Anything in the app
that reads this file has to open it the same way.

The copy opens with a plain-language header naming what was removed and what was kept. The screen
says the same thing before the button, in the register `docs/design/engagement-ethics.md` sets: it
is a description rather than a claim, "no personal data" is not asserted, and nothing is sent
until the user chooses where.

Both buttons carry `SemanticProperties.Description` and `SemanticProperties.Hint` — a DevExpress
button without one is exposed to Android's accessibility tree as non-focusable text a screen
reader cannot reach.

## Known gaps

- **iOS is unverified.** Everything here is platform-neutral except the
  `AndroidEnvironment.UnhandledExceptionRaiser` hook, which is inside `#if ANDROID`.
  `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` apply on iOS as they
  do everywhere, but no iOS device has run this. `ObjCRuntime.Runtime.MarshalManagedException` is
  the iOS equivalent of the Android hook and is not wired.
- **A native crash writes nothing.** A SIGSEGV — the shape of the fresh-install fault that
  `StartupTimeline` was built to localise — kills the process below the CLR, so no managed handler
  runs. The breadcrumb from the *previous* launch and the log up to that point survive; the fault
  itself does not appear. Android's `ApplicationExitInfo`, which `tools/smoke` already parses, is
  where that evidence lives.
- **A crash before `AddForgeDiagnostics` writes nothing**, because the sink does not exist yet.
  That window is the DevExpress theme assignment and `MauiApp.CreateBuilder`.
- **The disclosure text is not localised.** It is English alongside the rest of the Settings
  strings; `docs/localization/adding-a-string.md` is the route when E24 lands.
