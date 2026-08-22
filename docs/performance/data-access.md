# Data access performance

## The premise this work started from was wrong

Forge encrypts its local database with SQLCipher, and SQLCipher derives the key with **256,000
rounds of PBKDF2-HMAC-SHA512**. Measured on a desktop:

| | Per connection |
| --- | --- |
| Key passed as a passphrase | **469 ms** |
| No encryption at all | 5 ms |

Forge opens a `DbContext` - and therefore a connection - per operation. From those two facts it
looked as though nearly half a second of key derivation was being paid on essentially every read,
which would make the data-session seam the largest performance problem in the app.

**It was not.** That cost is paid once per **physical** connection, not once per open.
`Microsoft.Data.Sqlite` pools the underlying handle, and re-issuing `PRAGMA key` on a handle that
already has one is free. Measured against the real `ForgeDbContextFactory`:

| Operation | Cost |
| --- | --- |
| Context per operation, key applied, steady state | **0.9 ms** |
| Eight sequential sessions from a cold pool | 532 ms total - one derivation, then seven free |
| The same eight with pooling disabled | 8 x 422 ms |
| Read after 130 s idle | 39 ms - the pool does not prune it |

`docs/performance/README.md` had already recorded this in its "what was tried and did not help"
table, and it was right. Rescoping the session seam - pooled contexts, longer-lived sessions, a
single app-wide connection - would have bought approximately nothing, at real risk to thread safety
and profile separation.

## What was actually costing the time

A repeat launch on an Android emulator derived the SQLCipher key **seven times, for 6370 ms**.
Eleven other connection opens in the same launch cost 0.5-3.2 ms each.

The seven were not spread across the app's reads. They were concentrated in startup, and the reason
is a single line in the wrong place.

`SqlitePragmaConnectionInterceptor` applied four pragmas as each connection opened:

```
PRAGMA key = '...'          -- records the key, derives lazily
PRAGMA foreign_keys = ON    -- connection state, no I/O
PRAGMA busy_timeout = 5000  -- connection state, no I/O
PRAGMA journal_mode = WAL   -- READS THE DATABASE HEADER
```

`PRAGMA key` is cheap on its own: SQLCipher records the key and defers derivation until something
reads a page. `journal_mode` is what reads a page. So the batch turned *opening a connection* into
*decrypting the database*, and it did that on every connection - including connections that were
never going to run a query.

EF opens a lot of those. Counting distinct `sqlite3` handles through a startup showed **five
physical connections created inside `DatabaseInitializer.InitializeAsync` alone**, four of them by
`RelationalDatabaseCreator.Exists`, which exists only to answer whether the file can be opened:

```
NEW PHYSICAL  via RelationalConnection.Open <- SqliteDatabaseCreator.Exists <- RelationalDatabaseCreator.ExistsAsync <- DatabaseInitializer.AdoptPreMigrationDatabaseAsync
NEW PHYSICAL  via ... <- HistoryRepository.ExistsAsync <- DatabaseInitializer.AdoptPreMigrationDatabaseAsync
NEW PHYSICAL  via ... <- Migrator.MigrateAsync
NEW PHYSICAL  via ... <- HistoryRepository.GetAppliedMigrationsAsync
```

Each of those probes paid a full key derivation for a page it never wanted. The sixth derivation
came from `LocalDatabaseEncryption`, and the seventh from the first screen loading on a second
thread while startup finished.

## The change

**`journal_mode` moved out of the per-connection batch and into `DatabaseInitializer`, where it runs
once.** WAL is one of the very few SQLite pragmas that is *persistent*: it is recorded in the
database header and stays in effect for every later connection and every later process. Setting it
per connection was only ever re-stating something already true, and reading the header to do it.

With it gone, the open-time batch performs no I/O at all, so a connection that never queries never
derives a key. The connections still get created - that is EF's business - but they now cost
nothing.

**`LocalDatabaseEncryption.CanOpenAsync` stopped throwing its work away.** It probes whether the
database opens with the current key, which genuinely requires reading a page and therefore genuinely
costs a derivation - 1198 ms on the emulator. It then called `SqliteConnection.ClearAllPools()`
unconditionally, so startup derived the same key again moments later for the real context. It now
uses the same connection string the app uses and clears the pool only when the probe *fails* - the
case where the file is about to be replaced and a pooled handle would block it. On success the warm
connection is exactly what the first context should get.

**`DatabaseInitializer` holds one connection open** across migration and the integrity check, so the
sequence cannot be re-leased onto a different handle part way through.

## Measurements

Desktop, A/B in one process, repeat launch through `DatabaseInitializer.InitializeAsync`:

| | Wall | Physical connections | Key derivations |
| --- | --- | --- | --- |
| Before | 1872 ms | 5 | **5** (1792 ms) |
| After | **380 ms** | 5 | **0** |

`journal_mode` reports `wal` in both, which is the point: the mode was never coming from the
per-connection pragma.

Android emulator (x86_64, Debug, `-p:EmbedAssembliesIntoApk=true`), repeat launch with an existing
encrypted database, read from `ForgePerf` phase marks and per-open instrumentation:

| Phase | Before | After |
| --- | --- | --- |
| `db-encryption-ready` - `db-key-ready` | 1846 ms | see below |
| `db-schema-ready` - `db-encryption-ready` | 20760 ms | see below |
| Key derivations per launch | **7 (6370 ms)** | see below |

**Attribute these carefully.** The desktop A/B runs both arms in one process minutes apart and is
solidly attributable. The emulator numbers were taken on a host shared with five other build
streams, where the same launch varied by more than a factor of two between runs, so the phase deltas
carry that variance. The load-independent evidence is the derivation *count*.

## What was rejected, and why

| Option | Why not |
| --- | --- |
| **`AddPooledDbContextFactory`.** Pools `DbContext` instances and their connections. | Solves a problem Forge does not have. The connection is already pooled underneath; context construction measured ~8 ms. It would also give every pooled context a change tracker with a lifetime nobody controls, which is precisely the shape `ProfileScope` is fail-closed to protect against. |
| **A longer-lived session scope**, so a screen's several reads share one connection. | Buys nothing measurable - the reads already share one pooled handle - and costs the guarantee that a session cannot outlive a profile switch. A stale scope is a data-separation bug, and worse than slow. |
| **One long-lived connection for the app's lifetime.** | Fastest and by far the most dangerous. `SqliteConnection` is not thread-safe and Forge reads on background threads, so it would need serialising, which reintroduces the contention WAL exists to avoid. It also interacts badly with `LocalDataErasureService`, which deletes `forge.db` outright: on Android that unlinks the inode under any open handle, leaving the app writing to a file nobody can see. |
| **A bounded Forge-owned connection lease**, capping how many physical connections can ever exist. | Considered seriously, because derivation cost really is per physical connection. Rejected on measurement: after the `journal_mode` fix the only remaining derivations are one at startup and one per genuinely concurrent reader, and Forge's reads are sequential - there is no `Task.WhenAll` over sessions anywhere in the app. A whole connection-ownership layer, with its own disposal and erasure hazards, to save one derivation is a bad trade. |
| **SQLCipher's raw-key form** (`PRAGMA key = "x'<64 hex>'"`), which skips derivation entirely and measured 24 ms. | Out of scope by instruction, and now unnecessary. It was reverted after a SIGSEGV that turned out to be `Cache=Shared`, so it may well be safe - but it has never been shown to be, and there is no latency left that would justify re-litigating a security-critical native path. |
| **A compiled model** (`dotnet ef dbcontext optimize`), which would attack the 2502 ms EF model build that `docs/performance/README.md` identifies as the real database startup cost. | Not attempted here. `ForgeDbContext` applies global query filters for soft delete, which compiled models restrict, and the generated files are whole-file artefacts with the same merge hazard as migrations. Worth doing deliberately at integration, not on a parallel branch. |

## What could regress, and what pins it

`ConnectionReuseTests`:

- **`Opening_a_connection_costs_far_less_than_reading_from_one`** is the important one. It measures
  a physically new connection twice - opened only, and opened then read - and requires the first to
  cost less than a quarter of the second. Anything put back into the open-time pragma batch that
  touches a page fails it. Verified to fail with `journal_mode` restored (1100 ms open against
  947 ms read) rather than merely assumed to.
  It is expressed as a ratio, not a millisecond budget, because this suite shares a loaded host and
  both measurements move together.
- **`Repeated_sessions_share_one_physical_connection`** counts distinct `sqlite3` handles across
  eight sequential sessions and requires exactly one. This is what would break if connection pooling
  were ever defeated - at which point every read in the app silently becomes a key derivation.
- **`The_connection_pool_is_enabled`** states that dependency directly, since it is invisible in the
  connection string until someone turns it off.
- **`Write_ahead_logging_survives_without_being_set_on_every_connection`** guards the other
  direction: WAL must still actually be on. `Cache=Private` gave up shared cache on the strength of
  WAL providing the concurrency instead, so losing it quietly would undo that trade.

Not covered, and worth knowing:

- **`LocalDataErasureService` deletes `forge.db` while pooled handles are open.** On Android that
  unlinks the inode rather than failing, so anything still holding the old handle writes into a file
  that no longer has a name. This is pre-existing - ordinary pooling already keeps handles open - and
  it is not in this branch's files, but the successful-probe connection is now one more handle that
  outlives `EnsureEncryptedAsync`. It belongs to whoever owns erasure.
- **iOS is unmeasured.** Every device number here is an x86_64 Android emulator. Key derivation on
  real ARM hardware is slower than on an emulator backed by a desktop CPU, so the absolute savings
  on a physical phone should be larger, not smaller - but nobody has run it.
