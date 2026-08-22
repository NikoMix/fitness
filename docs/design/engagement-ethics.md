# Engagement ethics: what Forge celebrates, and what it refuses to

Status: implemented in Wave 8. This document is the reasoning behind
`Forge.Domain/Engagement/**`, and it is a deliverable in its own right — the code is short and the
decisions behind it are not obvious.

## The problem this feature has

A streak counter is not a neutral UI element. It is a number that falls when you stop, which makes
"do not stop" the thing the app rewards. In most products that is merely manipulative. In a
**fitness** app it is a health claim, and it is the wrong one.

Rest days are not gaps in training; they are when adaptation happens. Deloads are prescribed, not
conceded. Illness and injury are not choices. An app that shows a person a number which resets
when they recover has taught them that recovering is a loss, and the way to avoid the loss is to
train while ill or hurt. Published streak designs have been implicated in exactly this pattern, and
Forge had one: `Streak` carried `CurrentDays`, `BestDays`, `FreezesRemaining`, `LastCountedDate`
and a per-day history, and the reminder system had a "streak protection" notification to defend it.

So this feature was not tuned. The mechanic was removed and replaced.

## What replaced it

**Consistency measured in weeks, over the period the user's own plan defines.** The unit is the
week, and a week counts if it contained *any* session. That is deliberately forgiving: a run that
only survives perfect weeks breaks for illness, travel and deliberate rest alike, and it punishes
exactly the person who is already struggling to keep going.

Forge already made these decisions once, in `ConsistencyAnalyzer` for the Progress screen. Rather
than invent a second definition, `TrainingRhythmAnalyzer` delegates to it. Two screens showing the
same person two different counts of "weeks in a row" would be worse than either count alone,
because the user would have no way to know which was true.
`TrainingRhythmAnalyzerTests.With_no_protected_periods_it_agrees_exactly_with_the_consistency_analyzer`
is what keeps this an extension rather than a fork.

Inherited unchanged from `ConsistencyAnalyzer`:

- The window starts at the first logged session. Nobody is behind on the weeks before they began.
- The running week is excluded from adherence. A week that has not finished cannot have been missed.
- Each week is credited at most its target. One heavy week cannot paper over an empty one.
- Streaks count weeks containing any session, not weeks that reached the target.
- Returning gets "Welcome back"; a long absence gets "Training has seasons".

### The one extension: protected periods

`ConsistencyAnalyzer` ends a run at the first finished week with no sessions, because absence is
the only signal it has. It cannot tell recovery from drift.

`ProtectedPeriod` gives the user a way to say which it was: illness, injury, a planned deload, or
life. A week touched by one of those is **stepped over** — it is not counted as an active week,
because it did not contain training and claiming otherwise would be fabrication, but it does not
end the run either.

Two details are chosen in the user's favour on purpose:

- **Any day of the week being covered protects the week.** Someone ill Monday to Wednesday who
  then does not train that week has had their week taken by illness. Requiring full coverage would
  withdraw the protection in the most common case.
- **The end date is nullable.** Nobody knows on the first day of flu when they will train again.
  Demanding an end date would either produce a guess the app then treats as fact, or discourage
  recording the period at all.

While a period is running, the screen says so and states that the run is unchanged. It does not
count anything down.

### Nothing is stored as a counter

`Streak` now holds two things: the user's preference about badges, and their declared protected
periods. Every number on the screen is recomputed from workout rows each time it is shown.

That is partly ethics — a stored counter is a thing to protect, and a thing that can be
"repaired", gifted, or restored by a purchase — and partly honesty. A derived number cannot drift
from the logs, so the screen cannot claim a history the database does not contain.

## The badges

Each one is measured over the active profile's own logged training. Each states on its card why it
is good for the person, because a badge whose rationale cannot be written down plainly is one that
should not exist, and putting the reason on the card keeps that check in front of whoever adds the
next one.

| Code | Title | Threshold | Why this is good for the person |
| --- | --- | --- | --- |
| `consistency-first-session` | You started | 1 session | Beginning is the part most people never reach. It deserves marking on its own rather than being folded into a larger total that makes the first session look like 1/50th of something. |
| `consistency-two-weeks` | Two weeks in rhythm | 2 consecutive weeks containing training | Rewards showing up rather than performing. Because the unit is the week and any session counts, no rest day, and no ordinary schedule, can touch it. |
| `consistency-season` | A season of training | 12 weeks in the whole history containing training | Counted cumulatively rather than consecutively, so it **cannot be lost**. Somebody who trains for three months, takes two off with a broken wrist, and returns still has it. |
| `consistency-returned` | You came back | one gap of 14+ days that was ended | Returning is harder than continuing, and it is precisely the moment engagement features are most tempted to make somebody feel worse. This one rewards the return itself. |
| `own-goal-four-weeks` | Your own target, four times | 4 finished weeks at the plan's weekly target | The only target measured is the one the user set in their own plan. Forge never invents a target and never compares people. |
| `recovery-check-ins` | Recovery counted | 10 morning check-ins | Attending to sleep, soreness and energy is what makes it possible to train the right amount. Rewarding the *measurement* of recovery is the cheapest way to make recovery feel like part of training rather than a break from it. |
| `recovery-lighter-week` | You took the lighter week | a below-target week after 3+ weeks at target | Backing off after a hard block is a skill. It is also the single thing a naive badge scheme punishes hardest, so Forge rewards it explicitly. |
| `progression-effort-logged` | You trained by effort | 25 working sets with reps-in-reserve recorded | Recording how hard a set felt is what lets somebody autoregulate to the day they actually had, instead of to the number they wrote down last week. It rewards honesty about a bad day. |
| `progression-gradual` | Progress you can repeat | one exercise improving 3+ times across 21+ days | Rewards strength built gradually. Deliberately **unreachable by testing a maximum**: one big jump improves the running best exactly once, however large it is. |
| `exploration-patterns` | Balanced movement map | 4 distinct movement patterns | Spreading work across patterns keeps load balanced around a joint instead of repeating one stress until it complains. |

Progress towards a locked badge is measured, never estimated — the ring and the "3 of 4" beside it
come from the same count. This is the Progress feature's rule applied to badges: Forge will not
draw a shape it would refuse to describe in words.

## What was deliberately not built

This section is the point of the document.

**A daily streak.** Removed from the domain, not merely hidden. `Streak` has no `CurrentDays`,
`BestDays` or `LastCountedDate`, and `StreakTests` asserts their absence by reflection so that a
future contributor reintroducing one in good faith fails a test rather than passing review.

**Streak freezes.** A limited supply of forgiveness still frames recovery as consuming a scarce
resource, and it still runs out on the person who needed it most. `FreezesRemaining` is gone. The
replacement is unlimited and free, because rest is not something anybody should have to spend.

**Any badge for training on consecutive days**, for weeks without a rest day, or for a "perfect"
week or month. `EngagementEthicsPolicy.ProhibitedRewardPatterns` blocks the copy, and every
definition is checked against it in tests. This is the category that matters most, because the copy
is entirely pleasant: "Trained every day this week!" contains no cruel word at all.

**Total-volume badges.** The old `volume-10k` ("10,000 kg moved") is retired. A cumulative-kilogram
badge rewards *more*, which in practice means junk volume and overuse injury, and it invites a
ladder — 10k, 100k, 1M — where each rung is more work for the same recognition.

**Personal-record badges.** The old `strength-first-pr` is retired. A badge for setting a record
rewards attempting a maximal single, which is the highest-risk thing an untrained lifter can do and
is not how strength is built. `progression-gradual` deliberately measures the opposite shape.

**The measurable surface was narrowed, not just the rule list.** `EngagementMetrics` does not
contain total volume, personal-record counts, or consecutive training days at all, so a rule for
any of them cannot be written by accident. `AchievementEvaluatorTests` asserts this by reflection.
A rule list is a policy; a boundary is a mechanism.

**A second reward path.** `MilestoneDetector` was deleted. It celebrated a "Seven-day rhythm" and a
"10,000 kg total volume" milestone on its own thresholds, bypassing the achievement scheme and its
ethics checks entirely. Two reward paths means two places to get this wrong; there is now one.

**Loss-aversion copy of any kind.** No countdowns, no expiry, no "don't lose your streak", no red
decay, no "at risk". `ProhibitedPressureTerms` lists the specific phrases as phrases rather than
single words, so ordinary sentences are not blocked by accident, and every string the two screens
can produce is asserted against it — including the generated week descriptions.

**Guilt notifications.** The existing streak-protection reminder was kept, because its copy is
already right ("Protect your rhythm / If training no longer fits today, a planned rest day is a
valid choice"), but its trigger was changed: it is now suppressed entirely while a protected period
is running. The one day a streak app would most want to nudge somebody is the day they told it they
are ill, and that nudge is the behaviour this feature exists in order not to have.

**Sharing anything unearned.** The share action re-checks the copy against the policy before
raising, rather than trusting the definition. Copy only the owner sees is a product problem; copy
that reaches somebody else's screen is a public one.

**Anything fabricated.** With no active plan there is no weekly target, so no ring is drawn and the
absence is stated in a sentence. With no history the screen says it has nothing to show and will
not invent a starting point. With no resolvable profile the service returns an empty snapshot and
writes nothing at all.

## Turning it all off

`Streak.GamificationEnabled` suppresses both badge evaluation and rhythm framing, and it is
reachable from the Rhythm screen. Nothing about logging, plans, nutrition or progress changes when
it is off — anything that broke would prove these features were not decoration after all.

## Profile scoping

`Streak` and `Achievement` now implement `IProfileOwned` (phase 1 items 1 and 9 of
`multi-profile.md`). Every read is confined with `OwnedBy`, every write stamps the owner, and the
scope is resolved once per operation so a profile switch cannot land between two reads and mix one
person's sessions with another's badges. An unresolved scope reads nothing and **writes nothing**,
so no row is ever created owned by nobody.

One real bug was found and fixed on the way: `Achievement.Code` carried a **globally** unique
index, so the second person on a shared tablet could never earn a badge the first already held. The
insert would have failed and it would have looked like a bug in the evaluator. It is now unique per
`(UserProfileId, Code)`, asserted against real SQLite.

## Related decisions recorded elsewhere

- `docs/design/engagement-schema-delta.md` — the schema change, and the upgrade hazard in it.
- `docs/design/multi-profile.md` — the scoping seam.
- `Forge.Domain/Analytics/ConsistencyAnalyzer.cs` — the weekly maths this builds on.
