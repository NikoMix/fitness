# Data portability

Status: landed. Export is scoped to one profile by default; whole-device export is an explicit,
confirmed choice.

Resolves item 21 of `docs/design/multi-profile.md`.

## The defect

`ForgeBackupService`, `ForgeDataExporter` and `ForgeDataImporter` copied whole tables. On a device
with more than one profile, "download my data" therefore produced a file containing **every**
profile's weight history, food log and training.

That is the privacy feature performing a disclosure of special-category data. Under GDPR Article 20
a portability export covers the requesting data subject; it does not cover whoever else happens to
share the hardware.

## What changed

### Attribution is derived, not declared — `ProfileAttributionMap`

For every table in the EF model, the map works out a SQL predicate confining it to one profile:

| Case | Predicate | Notes |
| --- | --- | --- |
| Type implements `IProfileOwned` | `"UserProfileId" = @profileId` | The ordinary case. |
| Type is `UserProfile` | `"Id" = @profileId` | You are not owned by a profile, you are one. A subject access request plainly covers your own row. |
| EF-owned type sharing its owner's table | its owner's predicate | Same row, same filter. |
| EF-owned collection in its own table | `"fk" IN (SELECT "pk" FROM owner WHERE ...)` | Reachable only through the owner's key; recurses. |
| Anything else | none — **unattributable** | Reported to the user, never exported. |

Nothing in that table names an entity type. Attribution comes from
`typeof(IProfileOwned).IsAssignableFrom(clrType)`, exactly as `ProfileDataAreas` derives Separated
from Shared. **A type that adopts the seam in another branch becomes exportable with no edit to the
exporter.** `ScopedExportTests` asserts that property directly rather than asserting today's list,
so it keeps testing the right thing as features migrate — and fails if somebody replaces the
derivation with a hand-maintained list.

Everything is fail-closed. A table whose owner column cannot be located, whose ownership foreign
key is composite, or which several entity types mapped to it disagree about, is unattributable.
Several entity types can share one table through inheritance; the table counts as attributable only
if **all** of them agree on the same predicate, mirroring `ProfileDataArea.Separation` requiring
every type in an area to be owned.

`ProfileScope.None` produces `WHERE 1 = 0` — the same fail-closed answer `OwnedBy` gives. An export
that cannot say whose data it is produces an empty file and a message saying so.

### Why SQL text and not LINQ

The exporter reads tables through ADO.NET, so scoping is applied as SQL built from model metadata.
The alternative — resolving `Set<T>()` for a type discovered at runtime — needs
`MakeGenericMethod`, which works on Android and throws on an ahead-of-time compiled iOS build.
`ProfileStore` already carries that scar: its `DeletableEntityTypes` is an explicit list for the
same reason.

### Parameters are built through EF's own type mapping

SQLite has no `Guid` and no `DateTimeOffset` type. EF stores both as text in a format it chooses,
and a hand-formatted parameter that differs by a single character compares unequal and silently
returns nothing.

This is not hypothetical. EF writes Guids as **upper-case** text; `Guid.ToString()` produces
lower-case. The first run of `ScopedExportTests` caught exactly that difference. Parameters are
therefore created with `IRelationalTypeMappingSource.FindMapping(...).CreateParameter(...)`, so the
value is written the same way EF wrote the column.

The same fix repaired a pre-existing bug: the date-range filter formatted its bounds with `"O"`
(`2026-01-02T10:00:00.0000000+00:00`) while EF stores `2026-01-02 10:00:00+00:00`. Compared as
text, `T` sorts after a space, so a from-date silently excluded rows it should have matched. Export
date ranges were quietly wrong.

### The result reports what it could not attribute

`DataExportResult` carries the audience and a list of `ExportOmission`. `Describe()` renders both,
and the text is written into the file as well as shown on screen — a file is what the person keeps,
and the caveat has to travel with it.

The names and wording of omissions come from `ProfileDataAreas`, so the export, the profile
switcher and the deletion dialog describe the same gap in the same words instead of drifting apart.
This is the same discipline `ProfileDeletionPlan` uses when it reports what a delete **retains**.

`IsComplete` is false for any scoped export with omissions, so no screen can round a subset up to
"here is all your data".

### Rows whose owner was never set

A type joins the profile boundary by gaining a `UserProfileId`, and every row that already existed
takes whatever the migration defaults to — for a non-nullable Guid, the empty one. Those rows
belong to a real person and match no scope, so they would drop out of every personal export
silently: the table is attributable, so nothing would have flagged them.

That is the same class of failure this whole change exists to prevent, pointed the other way, and
it is near-certain the moment training data adopts the seam on an existing device. Attributable
tables are therefore also counted for unowned rows, and any that exist are reported as
"<area> not assigned to anybody: N records". `ScopedExportTests` pins it.

## Import: what happens on a collision

Import is the direction that can destroy data. Every fork resolves conservatively.

| Situation | What Forge does |
| --- | --- |
| The file holds a workout the profile already has | Skip the whole workout. Do not merge, do not append. |
| Matching identity | Natural key: workout title plus start time. Identifiers from Strong or Hevy mean nothing here. |
| The matching workout was deleted | Still counts as present. A delete is a stronger statement than a stale copy in an old file. |
| The file names an exercise that exists | Reuse it. Never edit it — the catalogue is shared between profiles. |
| The file names an exercise that exists but was deleted | Create a new one. Reusing it would resurrect the deleted row by implication. |
| A row cannot be written | Roll the whole import back. |
| No profile is active | Allowed only while none of the imported types carry an owner; refused the moment one does. |

Merging was rejected deliberately: it would silently rewrite a set the user logged themselves, and
they would have no way of telling which numbers came from where. Appending was rejected because it
turns a second import of the same file into a duplicated training history.

Every written row that implements `IProfileOwned` is stamped with the importing profile, set
through EF's change tracker rather than CLR reflection — the change tracker knows the mapping and
needs no `MakeGenericMethod`. Because the stamp is driven off the interface, it starts applying to
training data the moment training data adopts the seam.

### Transactional, verified

The import runs inside one transaction and saves per workout, so the change tracker stays bounded
on a large history while atomicity is preserved. `ImportSafetyTests` cancels an import after the
first workout has been written to the connection but before the commit, then asserts the database
holds nothing at all. `BackupServiceTests` does the equivalent for restore, interrupting after the
clear and before the commit and asserting the original rows survive.

These run against real SQLite. The in-memory provider has no transactions worth the name and does
not reproduce the text storage of a Guid or a `DateTimeOffset`, so a green in-memory suite would
have proved nothing about either property.

### An export is not a backup

Restoring a scoped export would delete everything the file does not mention — including every other
profile — while presenting itself as a recovery. `RestoreBackupAsync` refuses any file without a
backup manifest and says why.

## Formats

`ExportFormat.Portable` produces one zip holding `README.md`, `forge-export.json` and one CSV per
kind of record. It is the default on the portability screen.

Article 20 asks for a structured, commonly used, machine-readable format; JSON satisfies that. The
CSVs and the README exist because a person who asks for their data and receives a file only a
programmer can open has not really received it. `Json` and `Csv` remain available for callers that
want one or the other.

## Known gaps

- **The whole-device option lives only on `DataPortabilityPage`.** `ExportDataPage` is scoped-only
  and has no way to widen, which is the safe asymmetry, but the new page needs a link from settings
  and a `ForgeRoutes.DataPortability` constant. Until then its route is declared locally in
  `BackupFeatureRegistration`.
- **A personal export is still a small subset.** Only `BodyMetric`, `Streak` and the profile row
  carry an owner, so training and nutrition are reported as omitted every time. That number
  improves on its own as the seam is adopted; nothing here needs revisiting.
- **Soft-deleted rows are exported.** They are data Forge still holds, so including them is the
  honest answer, but it means an export is not the same as what the app shows.
