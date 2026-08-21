# fastlane metadata for Forge

This directory is the source of truth for Forge store listing text. It is plain text in git
so listing copy is reviewed like code, diffed between releases, and length-checked in CI by
`tools/release/Test-StoreMetadata.ps1` instead of by a store rejecting an upload.

## Layout

```
fastlane/
  Appfile                          identifiers shared by fastlane commands
  metadata/
    android/en-US/                 uploaded by `fastlane supply` from the release workflow
      title.txt                    30 characters
      short_description.txt        80 characters
      full_description.txt         4000 characters
      changelogs/default.txt       500 characters, used for any version without its own file
    ios/en-US/                     NOT uploaded automatically - see below
      name.txt                     30 characters
      subtitle.txt                 30 characters
      keywords.txt                 100 characters, comma separated, no spaces
      promotional_text.txt         170 characters
      description.txt              4000 characters
      release_notes.txt            4000 characters
      privacy_url.txt
      support_url.txt
      marketing_url.txt
```

## Android is automated, iOS is not

`fastlane supply` uploads `metadata/android` alongside the App Bundle, so what ships to Play
is what is in this repository.

The iOS text is deliberately **not** wired to `fastlane deliver`. Overwriting a live App
Store listing from CI is a one-way operation that store review does not protect you from,
and Apple's listing has fields (age rating answers, App Privacy, in-app purchase review
notes) that this directory does not model. The iOS files are the reviewed draft that a human
pastes into App Store Connect once per release.

If that changes later, note that `deliver` expects `fastlane/metadata/<locale>/`, not
`fastlane/metadata/ios/<locale>/`, so the directory has to move or be symlinked and
`Test-StoreMetadata.ps1` updated to match.

## Screenshots and graphics are not here

They are binary, they are large, and an accidental automated overwrite of a live listing is
not something a rollback fixes - so the release workflow passes `--skip_upload_images` and
`--skip_upload_screenshots`, and screenshots are uploaded by hand.

Required sizes and the shot list are in `docs/release/store-listing.md`.

## Changing the copy

1. Edit the file.
2. Run `pwsh tools/release/Test-StoreMetadata.ps1`.
3. Commit. The release workflow runs the same check, and the publish job runs it blocking.

Do not leave `TODO`, `TBD` or `placeholder` in these files: the validator fails on them,
because draft copy that reaches a public listing is worse than no copy.
