# Forge data portability

Forge is local-first: the device is the only system of record. A backup is therefore the recovery copy, not a convenience export.

## Backup format

Full backups are single `.forgebackup` JSON files. Each file contains:

- a manifest with schema version, app version, UTC creation time, per-table record counts, and a SHA-256 content hash;
- a canonical payload containing every persisted SQLite table and row used by the EF Core model.

Forge hashes the serialized payload and stores the digest in the manifest. Restore verifies the digest and rejects corrupted or truncated files before clearing any table. Backups created by a newer backup schema are rejected so an older app cannot misinterpret data.

Restore is transactional in effect: Forge verifies first, then clears and inserts all tables in one database transaction. If anything fails, the existing local database remains unchanged.

## Exports

Forge supports two open export formats:

- complete JSON archive for machine-readable portability;
- ZIP archive with one CSV per table for spreadsheet use.

Exports can be limited by date range and data group: training, nutrition/hydration, or profile/body metrics.

## Imports

Forge previews competitor CSV exports before import. The preview reports detected source app, workouts, sets, date range, and validation errors. Import commits only after confirmation and uses one transaction, so malformed files never produce partial imports.

Supported competitor shapes:

- Strong CSV exports with workout name, date, exercise name, set order, weight/unit and reps columns;
- Hevy CSV exports with title, start time, exercise title, set index, weight and reps columns.

The parser accepts common date formats, kilograms or pounds, and optional duration and distance columns.
