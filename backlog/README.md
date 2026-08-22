# Forge Backlog as Code

The Forge product backlog is authored as YAML in this folder and **synchronised into GitHub
Issues** by `tools/backlog-sync`. The YAML is the source of truth; GitHub is the working
surface.

## Why

- **Reviewable** — backlog changes arrive as pull requests with a readable diff.
- **Idempotent** — re-running the sync updates existing issues instead of duplicating them.
- **Resumable** — GitHub throttles content creation, so a 600+ issue import must survive
  being interrupted and resumed. Every item carries a stable `key` used for matching.
- **Portable** — the backlog survives migration between trackers.

## Hierarchy

```
Epic      (E##)             a durable product capability area, owned by one domain
 └─ Feature  (F##.##)       a shippable slice of that capability
     └─ Story  (S##.##.##)  a single vertical increment, <= ~2 days of work
```

Epics, features and stories all become GitHub issues. Features are linked as GitHub
**sub-issues** of their epic, and stories as sub-issues of their feature, so the native
GitHub hierarchy and progress rollup work out of the box.

## Files

| Path | Purpose |
| --- | --- |
| `epics/E##-*.yml` | One file per epic. Authored by hand or by an agent. |
| `taxonomy.yml` | Canonical domains, waves, labels, personas and milestones. |
| `schema.json` | JSON Schema used to validate every epic file before sync. |

One file per epic is deliberate: it lets many contributors (or agents) author the backlog in
parallel without merge conflicts.

## Keys are permanent

A `key` (`S07.03.02`) is the join between YAML and GitHub. It is embedded in the rendered
issue body as an HTML comment marker:

```html
<!-- forge:key=S07.03.02 type=story -->
```

**Never renumber a key.** To retire an item set `status: dropped`; the sync closes the issue
rather than deleting it, preserving history and links.

## Workflow

```bash
# 1. Validate the YAML against the schema (fast, offline, runs in CI)
pwsh tools/backlog-sync/Invoke-BacklogSync.ps1 -Validate

# 2. See exactly what would change, without touching GitHub
pwsh tools/backlog-sync/Invoke-BacklogSync.ps1 -DryRun

# 3. Apply. Safe to interrupt and re-run; progress is checkpointed.
pwsh tools/backlog-sync/Invoke-BacklogSync.ps1 -Apply
```

## Reporting status back: `verification/`

The sync only ever pushes the backlog *out*. Nothing in it notices that the code now does what a
story asked for, which is why the backlog once said the same thing on the day the app was
feature-complete as it had on the day the first line was written.

Verification streams read each item's acceptance criteria against the code and record a verdict in
`verification/<range>.json`, one entry per key:

```json
{ "story": "S10.02.01", "verdict": "PARTIAL", "evidence": "file.cs:12-40 ...", "gaps": "AC3 unmet ..." }
```

| Verdict | Meaning | Effect on the issue |
| --- | --- | --- |
| `DONE` | every acceptance criterion met | closed as completed, evidence in the closing comment |
| `DEFERRED` | deliberately out of scope for v1 | closed as not planned, reason in the closing comment |
| `PARTIAL` | some criteria met, some not | `status:partial` + a comment listing the gaps |
| `NOT-DONE` | not implemented, or too thin | `status:not-started` + a comment listing the gaps |
| `UNCLEAR` | could not be established from the code | `status:needs-review` + a comment |

```bash
# Check the verdict files are internally consistent (offline)
pwsh tools/backlog-sync/Invoke-BacklogReconcile.ps1 -Validate

# Resolve real issue numbers and print the plan
pwsh tools/backlog-sync/Invoke-BacklogReconcile.ps1 -DryRun

# Apply
pwsh tools/backlog-sync/Invoke-BacklogReconcile.ps1 -Apply
```

Two rules are enforced rather than trusted, because both failures are silent:

- **`DONE` with no `evidence` fails validation.** Once the context is gone, an unexplained closure
  is indistinguishable from a mistake.
- **`PARTIAL` or `NOT-DONE` with no `gaps` fails validation.** A gap nobody wrote down is a gap
  nobody can act on.

The gap text is posted **onto the issue**, not left in the report. `status:partial` describes itself
as "gaps recorded on the issue"; for one release that was untrue, and 621 issues carried a label
whose detail existed only in a file nobody opened. The reconcile keeps exactly one gap comment per
issue and rewrites it in place, so re-verifying does not stack duplicates.

Re-running reconciles rather than re-applies: a superseded status label is replaced instead of
stacked, an issue whose verdict has regressed away from `DONE` is reopened, and anything already in
the right state is skipped. Use `-Backfill` to add gap comments to issues labelled by an older run
that predates them.

## Authoring rules

Every item must be independently understandable by a contributor with no prior context.

1. **Titles** are imperative and specific. `Add rest timer with haptic completion cue`,
   not `Rest timer`.
2. **Acceptance criteria** are written `Given / When / Then` and are objectively testable.
   Avoid "works well", "is fast" — state the threshold.
3. **Implementation notes** name the concrete artifacts: project, folder, class, DevExpress
   control, platform API. This is what makes an issue pick-up-and-go.
4. **Grounding** links the documentation that proves the approach is viable.
5. **Stories are vertical.** A story delivers observable user value or a testable technical
   capability, never "write the ViewModel" in isolation.
6. **Every story declares `platforms`.** If behaviour differs per platform, say how it
   degrades. Windows has no health platform; that is a design constraint, not a bug.

## Waves

`wave` expresses scheduling, `domain` expresses ownership. Work is parallelised by domain
inside a wave, which is what keeps merge conflicts low: two domains rarely touch the same
files. See `docs/roadmap.md` for the wave plan.
