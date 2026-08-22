# What a Forge export contains

Forge has no servers and holds no copy of your data, so a subject access or portability request is
not something you email anyone. You produce the file yourself, on your device, and it is complete
the moment it is written.

This page states exactly what goes into that file and — just as importantly — what does not.

## The two exports, and why they are not the same button

| | **Get a copy of my data** (default) | **Include every profile on this device** |
| --- | --- | --- |
| Contains | Records Forge can attribute to you | Every record on the device |
| Suits | A portability or access request | Moving a device you own to a new one |
| On a shared device | Safe | Discloses other people's health data to you |
| How you get it | The default | A checkbox, plus a confirmation |

Forge supports several profiles on one device — a couple sharing a tablet, a family, a coach
demonstrating on their own phone. That makes "export my data" and "export this device" different
requests with different consequences, so they are different choices rather than one operation with
a hidden default.

The personal export is the default because getting it wrong in that direction produces a file
missing some rows, and getting it wrong in the other direction hands somebody another person's
weight history, food log and training. Under UK and EU GDPR that data is special category, and an
export that quietly included it would be a disclosure performed by the privacy feature itself.

## What a personal export includes

Every record that carries an owner and whose owner is you, plus your own profile row: your name,
goals, height, units and setup.

Which kinds of data carry an owner is not a list maintained by hand. Forge derives it from the
records themselves, so a part of the app that gains per-profile separation starts appearing in
personal exports without anybody having to remember to add it.

Deleted entries are included. Forge removes an entry from your screens immediately but keeps the
row until the database is erased, and a file claiming to be a copy of your data should not hide
data that still exists.

## What a personal export leaves out, and why

Most of Forge is not separated per profile yet. Workout history, the food log, hydration, plans,
check-ins and the exercise and food catalogues are currently shared: the rows carry no owner, so
Forge genuinely cannot tell which of them are yours.

Two honest options existed. Include them and warn, or leave them out and say so.

Forge leaves them out. Including them would mean a file described as "your data" containing
somebody else's training and meals, with nothing in the file to tell them apart — and the person
receiving that file would have no way to separate them either. Guessing is the failure that cannot
be undone once the file has been shared.

**Every export names what it left out**, in the app after the export and in the file itself:

- the JSON document carries a `notice` describing the file in plain English and a `notIncluded`
  list;
- the zip carries the same text as `README.md`;
- the app shows it on screen, before and after the export.

No screen and no file says "this is all your data" when it is a subset.

If Forge finds records of a kind that *does* carry an owner but where the owner was never recorded
— which happens to older entries when a part of the app gains per-profile separation — those are
reported too, by name and count, rather than quietly dropped.

## What you receive

Choosing to include spreadsheets gives you a single `.zip` containing:

| File | What it is |
| --- | --- |
| `README.md` | Plain English: when it was made, whose data it covers, what was left out. |
| `forge-export.json` | Every exported record, structured for another program to read. |
| `<Kind>.csv` | The same records as spreadsheets, one file per kind of record. |

Turning spreadsheets off gives you the JSON document on its own.

Both are open formats readable without Forge, which is the "structured, commonly used,
machine-readable format" Article 20 asks for. The spreadsheets exist because a person who asks for
their data and receives a file only a programmer can open has not really received it.

## Importing a file back

Importing is riskier than exporting, because a file can quietly overwrite or duplicate data you
already have. Forge takes the cautious option at every fork:

- **A workout you already have is skipped.** Matching is on the workout's name and start time, not
  on identifiers from another app that mean nothing here. Importing the same file twice changes
  nothing the second time.
- **Nothing is overwritten.** An import only adds. It never edits a set, a session or a catalogue
  entry you already had.
- **Deleted data is not resurrected.** A workout you deleted still counts as one you have, so
  re-importing an old file cannot bring it back.
- **Imported records are attributed to you**, never to whoever the file came from. Once training
  data carries an owner, an import with no active profile is refused rather than writing records
  nobody can be shown to own.
- **A failed import leaves nothing behind.** The whole import is one database transaction. If it
  cannot finish — a bad row, a full disk, you closing the app — every row it had written is rolled
  back and your log is exactly as it was.

An export file cannot be restored as a backup. A restore replaces the whole device and would delete
everything the file does not mention, so Forge refuses rather than presenting that as a recovery.

## Backups are a different thing

A backup is deliberately whole-device. It exists so you can put a device back the way it was, and a
backup that dropped the other profiles would restore a device with their history missing.

Keep that in mind if you share a backup file: it contains everybody on the device.

## What this does not solve

Until the rest of Forge is separated per profile, a personal export is a genuinely partial record.
The app says so every time rather than implying otherwise, but the honest summary of today's state
is: your profile and your body measurements travel with you; your training and nutrition are still
pooled with everyone else on the device and stay there.
