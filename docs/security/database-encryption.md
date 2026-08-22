# Database encryption

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

## What this does and does not protect

It protects the database file if it is extracted from the device - from a backup, from a rooted or
jailbroken phone, or from a filesystem image. The key lives in platform secure storage, backed by
the Android Keystore and the iOS keychain.

It does not protect against a running Forge process, because the key is released to it by design. It
is not a second factor and it is not a substitute for the device passcode. The app lock's threat
model already states this and remains accurate.
