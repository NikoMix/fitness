# Database encryption

## The page cache is never shared

`ForgeDbContextFactory` sets `Cache=Private`. That is not a default worth ignoring: with
`Cache=Shared`, several connections to the same file share one page cache while each keeps its own
SQLCipher context over those pages, and Forge opens a context per operation, so concurrent
connections are routine rather than exceptional.

On Android that combination **segfaulted inside `sqlcipher_codec_key_derive`**, on a plain launch,
fresh install included:

```
Fatal signal 11 (SIGSEGV), code 128 (SI_KERNEL), fault addr 0x0 in tid ... (.NET TP Worker)
  #00  libe_sqlcipher.so
  #02  libe_sqlcipher.so (sqlcipher_codec_key_derive+26)
  #09  libe_sqlcipher.so (sqlite3_step+750)
```

Nothing in the managed layer can see it. It is a native crash, so there is no exception to catch,
no stack trace in the log, and no failing test on Windows - where the identical code runs cleanly,
which is what made it invisible until the app was launched on a device.

SQLite's own documentation calls shared cache a legacy feature and recommends WAL for concurrency
instead, which the interceptor already enables, so nothing is given up by dropping it.

`ConnectionConfigurationTests` pins the setting and exercises eight concurrent contexts against one
encrypted database.

## The key is passed as a passphrase, and that costs something

Given a passphrase, SQLCipher derives a key with **256,000 rounds of PBKDF2-HMAC-SHA512**. For
Forge's key - 32 bytes straight from a CSPRNG in the platform keystore - that adds no entropy,
because stretching an already-random 256-bit key buys nothing. It costs time, on every connection
rather than once:

| | Per connection open |
|---|---|
| Key passed as a passphrase | **469 ms** |
| Key passed raw | **24 ms** |
| No encryption at all | 5 ms |

Read that table carefully: it is per *physical* connection, not per open. `Microsoft.Data.Sqlite`
pools the handle, and re-issuing `PRAGMA key` on an already-keyed handle is free — a session in
steady state opens in about 0.9 ms.

SQLCipher's raw-key form skips the derivation, and it was tried. It is **not** used, because it
could not be shown to be safe here: the crash above was initially attributed to it, and reverting
the raw key did not fix it. The raw-key form may well be fine - the actual culprit was shared cache -
but it was reverted before that was known, and re-introducing an unverified change to a
security-critical native path to win back latency is the wrong trade while the real fix is
available elsewhere.

This document previously said the real fix was to stop opening a connection per operation, and
that was wrong. Rescoping the data-session seam was measured and buys approximately nothing,
because the connection underneath is already pooled. The actual cost was `PRAGMA journal_mode`
running in the per-connection batch: `PRAGMA key` derives lazily on first page read, but
`journal_mode` reads the database header, so it forced a full key derivation on every physical
connection — including the four that `RelationalDatabaseCreator.Exists` opens purely to ask
whether the file can be opened, and which never run a query. WAL is persistent state in the
header, so setting it per connection only re-stated something already true. It now runs once, at
initialisation. See `docs/performance/data-access.md` for the measurements.

## What was wrong

Forge's privacy policy, its Play Data Safety declaration, its Play Health Apps declaration and its
Apple App Privacy answers all stated that the local database is encrypted at rest with SQLCipher.

It was not. The database was plaintext.

`SQLitePCLRaw.bundle_e_sqlcipher` was listed in `Directory.Packages.props` but referenced by no
project, so the plain `bundle_e_sqlite3` arrived transitively through
`Microsoft.EntityFrameworkCore.Sqlite` and won the provider registration. The version pinned for it
- 3.0.5 - has never been published for that package, which is on its own proof the reference was
never resolved by anything.

`SqlitePragmaConnectionInterceptor` did issue `PRAGMA key`. Against stock SQLite that is an
**unknown pragma, and SQLite ignores unknown pragmas without raising an error**. No exception, no
warning, no failing test. Every code path behaved exactly as it would have with encryption working.

It was found by pulling `files/forge.db` off an emulator and looking at the first sixteen bytes:
`SQLite format 3`, followed by readable `CREATE TABLE` statements.

## Why it mattered more than a normal bug

A wrong answer on a store data-safety form is not a defect report, it is a false declaration to a
regulator about health data. Google Play can remove an app for it. The same sentence was also
rendered inside the app, so users were told their data was encrypted while it was not, and the app
lock's threat model leaned on encryption it did not have - it says in as many words that it "adds no
encryption, SQLCipher already does that".

## The fix

`Microsoft.EntityFrameworkCore.Sqlite.Core` plus an explicit `SQLitePCLRaw.bundle_e_sqlcipher`
reference. The `.Core` package omits the bundle dependency, so the SQLCipher one is the only
provider present. The correct version is **2.1.11**; the SQLCipher bundle has not moved to 3.x even
though `SQLitePCLRaw.core` has.

Confirmed by what ends up in the output directory: `SQLitePCLRaw.provider.e_sqlcipher.dll` and
`runtimes/*/native/e_sqlcipher.dll`, where it used to be `e_sqlite3`.

## Existing databases had to be carried across

Fixing the package alone would have converted a silent privacy failure into loud data loss.
SQLCipher does not treat a plaintext file as unencrypted - it decrypts the header, gets nonsense and
reports `file is not a database`. Every existing install would have failed startup into recovery
mode over a database that was completely intact.

`LocalDatabaseEncryption.EnsureEncryptedAsync` runs in `ForgeStartup` **before the first keyed
connection**. If the file exists and still begins with the plaintext header, it is converted with
SQLCipher's own `sqlcipher_export`, which copies through the SQLite layer - schema and rows, not
bytes - so it cannot leave a half-encrypted file behind.

Three details that are deliberate:

- The conversion writes a side file and only replaces the original once it has finished, so an
  interruption leaves the readable database exactly as it was.
- The `-wal` and `-shm` files are deleted with it. They belong to the plaintext database, and left
  behind SQLite would try to replay them over the encrypted one.
- The connection opens `ReadWriteCreate` rather than `ReadWrite`. An attached database inherits the
  main connection's open flags, so without the create flag `ATTACH` fails with "unable to open
  database" - which is how this was found, not how it was predicted.

Detection is by file header rather than by a flag written somewhere, because re-encrypting an
already-encrypted database would double-encrypt it and lose everything. "Has this been done" must be
answerable from the file itself.

## How it is kept honest

`DatabaseEncryptionTests` reads the bytes on disk: the header must not be the plain SQLite one, the
file must not contain `CREATE TABLE` or any table name, and reopening without the key must throw.
Asking the library whether it encrypted something is worthless here, because the failure being
guarded against is the library reporting success while doing nothing.

`LocalDatabaseEncryptionTests` covers the upgrade: rows survive, the converted file leaks nothing,
an already-encrypted database is left untouched, running twice is harmless, and no side files are
left behind.

## Verified on a device, which is where the last surprise was

Windows tests prove the library behaves. They cannot prove the right native library reaches an
Android package, so this was checked on an emulator against a real database pulled from a previous
install: 25 tables, no migrations history, plaintext, with a profile and sixty exercises in it.

After deploying, the file on the device begins with random bytes, is rejected by stock SQLite as
"file is not a database", and contains neither `CREATE TABLE` nor the string `Bodyweight` - while
the app's profile screen displays "3 days/week, Bodyweight", which is exactly the row that was in
the plaintext file. Encrypted on disk, intact through the app. Startup also reached
`db-seed-complete`, which means the pre-migration adoption ran too: without it `MigrateAsync` would
have failed on a `CREATE TABLE` for a table that already existed.

### The trap that made the first attempt look like a success

A Debug `-t:Install` deploy appeared to work, and the database even looked encrypted. It was not.
Debug uses **Fast Deployment**, which keeps managed assemblies in `files/.__override__/` on the
device rather than in the APK, and it left the previous build's `SQLitePCLRaw.provider.e_sqlite3`
there while adding no `provider.e_sqlcipher`. The app threw
`FileNotFoundException: SQLitePCLRaw.provider.e_sqlcipher` during startup, failed into the
"Forge is still opening your local database" state, and left the database in a state no code had
deliberately produced.

The native side was fine all along - `lib/x86_64/libe_sqlcipher.so` was in the APK. Only the managed
provider was stale.

Deploy with `-p:EmbedAssembliesIntoApk=true` when verifying anything that changes a package
reference. Release sets this already, so it does not affect shipped builds - but a Debug deploy can
otherwise run a mixture of two builds and report the result as if it meant something.

### Emulator contention

`uiautomator` crashes with `UiAutomationService ... already registered!` when two sessions dump the
UI on one emulator, and an app disappearing usually turns out to be `Force stopping ... from pid
NNNN` in logcat rather than a crash. Check for both before believing a failure.

### Release, where trimming and AOT could have undone it

A Release Android build was inspected as well, because `SdkOnly` linking and profiled AOT are not
the same code path as Debug. The APK carries `libe_sqlcipher.so` for both shipping ABIs
(`arm64-v8a`, `armeabi-v7a`) and AOT images for `provider.e_sqlcipher`, `lib.e_sqlcipher.android`,
`batteries_v2` and `core`. There is no `e_sqlite3` in it at all: the plain provider is gone rather
than merely outvoted.

iOS has been confirmed only as far as the build output containing
`SQLitePCLRaw.lib.e_sqlcipher.ios.dll`. Nobody has run it on iOS hardware.

## What this does and does not protect

It protects the database file if it is extracted from the device - from a backup, from a rooted or
jailbroken phone, or from a filesystem image. The key lives in platform secure storage, backed by
the Android Keystore and the iOS keychain.

It does not protect against a running Forge process, because the key is released to it by design. It
is not a second factor and it is not a substitute for the device passcode. The app lock's threat
model already states this and remains accurate.
