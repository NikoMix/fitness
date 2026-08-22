# Database encryption

## The key is passed raw, not as a passphrase

Given a passphrase, SQLCipher derives a key with **256,000 rounds of PBKDF2-HMAC-SHA512**. That is
the right thing to do to a human-chosen password. Forge's key is not one: it is 32 bytes straight
from a CSPRNG, held in the platform keystore. Stretching an already-random 256-bit key adds no
entropy and no security.

It does add time, and not once - on every connection, because Forge opens a context per operation
and the interceptor applies `PRAGMA key` each time a connection opens. Measured on a desktop:

| | Per connection open |
|---|---|
| Key passed as a passphrase | **469 ms** |
| Key passed raw | **24 ms** |
| No encryption at all | 5 ms |

On an emulator this was enough for Android to kill the app during startup with
`ANR ... failed to complete startup`. A phone would have been slower still.

`SqlitePragmaConnectionInterceptor.CreateKeyPragma` therefore emits SQLCipher's raw-key form,
`PRAGMA key = "x'<64 hex>'"`, whenever the key really is 32 bytes. Anything else - a test
passphrase, a hand-set value - keeps the derived form, because there the derivation is doing real
work.

Databases written before this cannot be opened with the raw key, so
`LocalDatabaseEncryption.EnsureEncryptedAsync` re-keys them: it tries the raw form, falls back to
the derived form, and re-encrypts through `sqlcipher_export` if the old one is what opens it. If
neither opens the file it changes nothing, because guessing further risks destroying a database
some other key would open.

Verifying which key opens a file needs care. SQLCipher defers the check, so applying `PRAGMA key`
always succeeds and proves nothing - a page has to be read. And the read has to be a **separate
command**: `Microsoft.Data.Sqlite` executes only the first statement of a batch for
`ExecuteScalar`, so `PRAGMA key; SELECT ...` returns the pragma's own `"ok"` and never touches the
database. The first version of both the check and its test had exactly that bug.

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
