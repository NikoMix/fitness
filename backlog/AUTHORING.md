# Backlog Authoring Guide

Read this before authoring any epic file. It exists so that thirty-two epics written by
different authors read as though one person wrote them.

## Required reading, in order

1. `docs/architecture/overview.md` — the technical ground truth. Do not contradict it.
2. `backlog/schema.json` — the contract. Your file must validate against it.
3. `backlog/taxonomy.yml` — the only legal values for `domain`, `persona`, `platforms`, `concerns`.
4. `backlog/epics/E01-platform-foundation.yml` — the golden example. Match its depth.

## Volume target

Each epic file must contain:

- **4–6 features**
- **14–20 stories in total** across those features

An epic with three thin stories has not been thought through. An epic with forty has not been
decomposed into features properly.

## Hard rules

**Platforms.** v1 is **Android and iOS only**. Never write `windows` or `maccatalyst` in a
v1 story. If a story only makes sense on desktop, mark it `wave: 6`.

**DevExpress first.** Reach for a DevExpress control before anything else, and name the exact
control in `implementation.devexpress`. Only fall back to another library where
`docs/architecture/overview.md` documents a genuine gap, and say which gap you are invoking.

**No vendor leakage.** Never propose putting a DevExpress or MAUI type into `Forge.Domain` or
`Forge.Core`.

**Keys.** Your epic key is assigned. Features are `F<epic>.NN`, stories are `S<epic>.FF.NN`,
numbered from 01 within their parent. A story under `F07.02` is `S07.02.01`, `S07.02.02`, …

## Quality bar

### Titles
Imperative and specific. `Add rest timer with haptic completion cue`, not `Rest timer`.

### Acceptance criteria
Every criterion is objectively verifiable by someone who did not write it.

- ❌ `then: the list scrolls smoothly`
- ✅ `then: scrolling 500 items sustains 60 fps with no frame exceeding 16.6 ms`
- ❌ `then: the data is saved`
- ✅ `then: the set is durable in SQLite within 200 ms and survives an immediate process kill`

Include the failure and edge cases, not only the happy path: empty states, permission denial,
offline, mid-operation interruption, and the smallest and largest plausible input.

### Requirements
State thresholds, units and limits. "Fast" and "responsive" are not requirements.

### Implementation notes
This is the highest-value field. Minimum 60 characters, but aim for a substantial paragraph
that a competent engineer could act on immediately. Name the project, the folder, the class,
the DevExpress control, the platform API. Explain *why* where a choice is non-obvious — the
reasoning is what stops the next person quietly undoing your decision.

Good implementation notes answer: where does this code live, what does it use, what is the
tricky part, and what would a careless implementation get wrong?

### Grounding
Link real, current documentation that proves the approach works. Prefer official vendor and
platform docs. Never invent a URL. If you cannot verify something, put it in `openQuestions`
rather than asserting it.

## Think about the product, not just the ticket

You are not transcribing a specification — none exists. You are designing the product.

For every epic, actively look for:

- **Gaps** the original brief missed but a real user would immediately expect.
- **Failure modes** — what does this feature do when it has no data, no permission, or no network?
- **The first-run experience** — what does a brand-new user with an empty database see?
- **Retention** — what brings someone back tomorrow rather than only today?
- **Safety** — Forge gives exercise and nutrition guidance to real bodies. Where could
  advice cause harm, and what guardrail belongs in the product? Injury risk, unsafe deficits,
  disordered-eating signals, and over-training all deserve explicit handling.
- **Trust** — where would a thoughtful user hesitate before granting a permission or entering
  personal data, and what would reassure them?

Capture worthwhile ideas that fall outside v1 as `wave: 6` rather than discarding them.

## Sizing

`xs` half a day · `s` one day · `m` two days · `l` four days · `xl` eight days

A story larger than `l` should almost always be split. `xl` is a signal you have written a
feature and labelled it a story.

## Style

- British-neutral technical English, plain and direct.
- Write `>-` folded scalars for prose so lines stay readable in review.
- Never use an em dash in YAML content; use a hyphen or restructure the sentence.
- Do not wrap the whole file in a code fence. Emit YAML only.
