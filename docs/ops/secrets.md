# GitHub Actions secrets

Moved to [`docs/release/signing-and-secrets.md`](../release/signing-and-secrets.md).

That page is the single inventory of every secret and repository variable the release
pipeline uses, how to generate each one, which of them belong on the `store-release`
environment rather than on the repository, and how to rotate them. It also covers the Google
Play and App Store Connect upload credentials, which this page previously listed only as a
future intention.

Nothing has changed about the rule: do not commit signing material, and never paste a secret
value into an issue, a pull request, a commit message or a log.
