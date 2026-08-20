# ADR-0001: Keep Forge entirely local, with no backend

- **Status**: Accepted
- **Date**: 2026-08-20

## Context

The brief asked for a fitness app that is "completely local" for the first iteration, with an
"optimized mobile app without large service landscape (and infrastructure cost)".

A conventional fitness app leans on a backend for accounts, cloud sync, a food database, and
analytics. Each of those carries recurring cost, operational burden, a security surface, and
regulatory weight, because the data involved is health data.

## Decision

**v1 has no Forge-operated backend.** The device is the sole system of record. Every feature
is designed to work with the network permanently unavailable.

Specifically:

- **No accounts server.** A profile is created locally. What the brief called "login/sign-up"
  is realised as an optional biometric or PIN app lock protecting on-device data, which is the
  actual threat model for a personal device. Credential-based sign-up against a local store
  would be security theatre: it protects nothing, and it adds friction at the most fragile
  moment in the funnel.
- **No cloud database.** SQLite via EF Core, encrypted with SQLCipher, with the key in
  platform secure storage.
- **No remote food or exercise catalogue.** Both ship as versioned assets inside the app.
- **No analytics or crash reporting that phones home.** Diagnostics stay on-device and are
  shared only by explicit, per-incident user action through the OS share sheet.
- **No server-side receipt validation.** Purchases are validated by the platform billing
  client. The limits of this are documented rather than hidden.

## Consequences

**Positive**

- Near-zero running cost. The business does not need revenue to avoid losing money.
- A genuinely strong privacy position: health data never leaves the device. This simplifies
  GDPR obligations, makes the Apple Privacy Nutrition Label and Google Play Data Safety
  declarations unusually clean, and is a real marketing asset rather than a compliance chore.
- Offline is not a feature to be built; it is the default. No sync conflicts, no partial
  state, no spinner waiting on a network.
- Latency is disk latency. Logging a set never waits on a round trip.

**Negative and how each is handled**

- **No multi-device continuity.** Accepted for v1.
- **Uninstalling destroys data irreversibly.** This is the sharpest edge of the decision and
  it is why backup, export and restore (epic E26) are treated as correctness requirements
  rather than conveniences, with prominent in-product messaging.
- **No server-side receipt validation.** A determined user can tamper with local entitlements.
  For a low-price consumer fitness app this is a commercially acceptable risk; the alternative
  is standing up and paying for infrastructure to protect a small amount of revenue.
- **No remote kill switch or server-side configuration.** Mistakes ship until the next store
  release, which raises the bar on pre-release testing.
- **Food database is a shipped asset.** It must be trimmed to respect the app-size budget and
  refreshed via app updates.

## Forward compatibility

v2 may add optional cloud sync. To keep that possible without a painful migration, the
persistence layer uses stable identifiers and timestamps on every entity from the start, and
identity is modelled as a seam an account can later attach to. That costs almost nothing now
and preserves the option.
