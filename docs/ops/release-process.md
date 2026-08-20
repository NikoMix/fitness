# Release process

Forge releases are produced by the manual `Release` GitHub Actions workflow.

## Launch gate: Google Play Health Apps declaration

Forge uses health data. Google Play requires a Health Apps declaration before publishing Health Connect permissions, and approval can take 4-8 weeks. Start this declaration early; it is a launch gate, not a final submission task.

## Before running the workflow

1. Confirm the release branch is green in CI.
2. Confirm `.github` release secrets are configured as described in `docs/ops/secrets.md`.
3. Confirm the Android upload key, Apple certificate, and provisioning profile are current.
4. Choose the semantic display version, for example `1.0.0`.

## Running the workflow

1. Open GitHub Actions.
2. Select `Release`.
3. Run `workflow_dispatch` with the chosen version.
4. Download the Android AAB and iOS IPA artifacts.

The workflow stamps:

- `ApplicationDisplayVersion` from the manual version input.
- `ApplicationVersion` from `github.run_number`, which maps to Android `versionCode` and iOS `CFBundleVersion`. Run numbers are monotonically increasing for the workflow, so store build numbers always move forward.

## Store upload TODOs

- TODO: Upload the signed AAB to Google Play Console after adding Play Developer API credentials and selecting the target track.
- TODO: Upload the signed IPA to TestFlight after adding App Store Connect API credentials and confirming the beta/release lane.

These upload steps are documented but intentionally not automated until API credentials and release governance are in place.
