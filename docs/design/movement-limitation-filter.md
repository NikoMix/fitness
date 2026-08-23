# Filtering the library by a declared movement limitation

Status: implemented. This document is the reasoning behind `MovementLimitationDeclaration` and the
limitation card on the exercise library, and it exists because the decisions are about honesty
rather than about code.

## The problem this feature has

Onboarding asks whether there is anything Forge should work around and stores the answer verbatim on
`UserProfile.MovementLimitations`. Until now nothing read it. Not the library, not substitution, not
planning. A person could type "left shoulder impingement" during setup, and Forge would show them
overhead pressing forever without ever acknowledging that they had said anything.

`ExerciseFilter.FromDeclaredInjuries` existed and was unit tested, and its only callers were the
tests. So both halves were present and nothing joined them.

## The two things that are hard

### The input is free text and the filter is not

`ExerciseFilter` knows nine coarse body areas and the movement patterns each one makes a poor idea.
People do not type those nine words. They type "both knees", "lumbar disc", "rotator cuff tear",
"recovering from pneumonia".

`MovementLimitationDeclaration` bridges the two. It splits the text into phrases, normalises each
one - lower case, punctuation stripped, crude singularisation so "knees" reads as "knee" - and looks
for an area anywhere in the resulting words. Longer terms are claimed first, so "lower back pain" is
read once, as the lower back, rather than a second time as the bare "back" sitting inside it.

A small synonym table maps free-text spellings onto the same nine areas. **Every entry has to be
defensible as "this word means that joint"**, not as "people with this usually cannot do that".
`lumbar` is the lower back. `rotator cuff` is the shoulder. `achilles` attaches at the ankle. That
rule is what keeps the table inspectable, and it is why `hamstring` is not in it: a hamstring is a
muscle, not one of the nine regions, and mapping it to "hip" so that hinging disappears would be
Forge quietly inventing a clinical opinion.

### Anything it cannot read must come back out

The failure case is the whole point. `UninterpretedPhrases` holds every phrase Forge could not
place, **exactly as the user typed it**, and the library quotes them back:

> Forge could not interpret "recovering from pneumonia", so nothing has been left out for that.
> Judge those movements yourself.

Silence here would be the dishonest option, and the worst possible one: someone declares a
limitation, sees a filtered list, and reasonably concludes it was accounted for. A filter that
half-understands and says nothing is more dangerous than no filter, because it manufactures
confidence it has not earned.

## What the screen claims, and what it refuses to claim

Filtering a library on health grounds is a claim. The card states what was read, names the whole
patterns being removed, and then says plainly what Forge does not know:

> This is a blunt filter, not medical advice. Forge cannot assess an injury and does not know which
> movements are safe for you, so it removes a pattern rather than guessing which movements inside it
> you could tolerate. Nothing is blocked, and you can show everything at any time.

Three deliberate choices sit in that sentence:

- **Coarse on purpose.** Excluding a whole pattern is a wider net than excluding individual
  movements. Forge cannot tell which squat variation a particular knee tolerates, so it does not
  pretend to, and it errs toward removing more rather than guessing.
- **Narrowing, never blocking.** The exclusions filter a browsing list. Nothing is disabled, no
  exercise refuses to be logged, and one tap shows everything.
- **On by default.** Someone who declared an injury and was shown every movement anyway has been
  ignored, which is the state this replaced. But a list that quietly hides things is its own
  problem, so the card is always visible whenever a limitation was declared - the filter never acts
  without saying so.

## Which profile it reads

`ExerciseDataStore` selects the profile with `ActiveProfileSelector.SelectActive` rather than taking
the oldest row, and reads declared equipment from the same profile in the same pass. On a shared
device those differ, and filtering one person's library by another person's injuries would be worse
than not filtering at all.

Selection runs over the materialised list, because it orders by `DateTimeOffset` and SQLite cannot
translate that.

## What is deliberately not done yet

- Planning, substitution and the workout builder still ignore declared limitations. Only the library
  reads them. Extending it should reuse `MovementLimitationDeclaration` rather than re-parse the
  text, or the two will drift.
- There is no way to edit limitations from the library; it is a profile field, changed in setup.
- The nine areas and their patterns live in `ExerciseFilter.InjuryMovementExclusions` and are the
  single authority. `ExerciseFilter.RecognisedInjuryAreas` publishes them so the free-text reader
  cannot hold a second, hand-copied list that silently drifts out of date.
