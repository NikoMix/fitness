# Release process

Moved to [`docs/release/`](../release/README.md).

This page described a manual `workflow_dispatch` release that took a hand-typed version and
used `github.run_number` as the store build number. Both have been replaced: releases are now
triggered by a git tag, and the build number is derived from that tag so it is reproducible
and cannot go backwards. Keeping the old description here would have left two contradictory
runbooks in the repository.

| You want | Read |
| --- | --- |
| To cut a release | [`docs/release/runbook.md`](../release/runbook.md) |
| To choose a tag | [`docs/release/versioning.md`](../release/versioning.md) |
| To set up or rotate signing credentials | [`docs/release/signing-and-secrets.md`](../release/signing-and-secrets.md) |
| To fill in a store console | [`docs/release/store-listing.md`](../release/store-listing.md) |
| To know what is blocking the launch | [`docs/release/launch-gates.yml`](../release/launch-gates.yml) |

The launch gate has not changed: the Google Play Health Apps declaration takes four to eight
weeks with no published SLA, requires a publicly hosted privacy policy first, and sets the
launch date.
