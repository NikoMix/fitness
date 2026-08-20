# Backlog Enrichment Pass

You are DEEPENING existing backlog stories, not rewriting them.

## Absolute rules - violating any of these breaks the sync

1. **NEVER change** any `key` value (epic, feature or story). Keys are permanent identifiers
   already mapped to GitHub issues.
2. **NEVER change** the epic/feature/story hierarchy, ordering, or the `title`, `wave`,
   `size`, `domain`, `persona` or `platforms` of an existing item.
3. **NEVER add or remove** stories or features. The count must be identical before and after.
4. **NEVER** introduce `windows` or `maccatalyst` platforms below wave 6.
5. The file must still validate against `backlog/schema.json`. No new top-level fields.

## What you ARE changing - for EVERY story in your assigned files

**`requirements`** - raise to **at least 4**, ideally 5-6. Add the requirements the original
author omitted: limits, thresholds, units, error handling, empty state, permission denial,
offline behaviour, and the boundary conditions. Every requirement must be specific enough to
test. Delete nothing that is already there.

**`acceptanceCriteria`** - raise to **at least 3**, ideally 4-5. Keep the existing ones. The
added criteria must cover the paths the original missed: the failure case, the empty/first-run
case, the interrupted case, and the accessibility case where relevant. Each must be
objectively verifiable with a concrete threshold, in Given/When/Then form, with a stable `id`
(AC1, AC2, ...).

Weak: `then: the screen loads quickly`
Strong: `then: the screen renders its first meaningful frame within 400 ms on a Pixel 6a`

**`implementation.notes`** - expand to a substantial paragraph (**aim for 400-900 characters**).
It must answer four questions:
  - Where does this code live? Name the project, folder and class.
  - What does it use? Name the exact DevExpress control or platform API.
  - What is the tricky part, and why is the chosen approach correct?
  - What would a careless implementation get wrong?
Also populate `implementation.devexpress`, `implementation.apis` and `implementation.touches`
where they are missing and applicable.

**`grounding`** - add **at least one** real reference to every story that lacks one. Use
official documentation only: learn.microsoft.com, docs.devexpress.com, developer.android.com,
developer.apple.com, w3.org/WAI. Include a `note` saying why the reference matters.
**Never invent a URL.** If you cannot find a genuine reference for a story, leave `grounding`
absent and add an entry to `openQuestions` instead.

**`testing`** - add concrete tests where missing.

## Reference standard
`backlog/epics/E01-platform-foundation.yml` is the quality bar. Match its depth.

## Style
- Do NOT use em dashes anywhere in YAML content.
- Use `>-` folded scalars for prose.
- Emit YAML only. Never wrap a file in a code fence.

## Verify before finishing - MANDATORY
For each file you edited, confirm it parses AND that counts are unchanged:

```powershell
cd 'C:\Users\mixni\.copilot\repos\copilot-worktrees\fitness\nikomix-feature-potential-barnacle'
Import-Module powershell-yaml
foreach ($n in @('<YOUR-FILES>')) {
  $y = Get-Content "backlog\epics\$n.yml" -Raw | ConvertFrom-Yaml | ConvertTo-Json -Depth 30 | ConvertFrom-Json
  $st = @($y.features | ForEach-Object { @($_.stories) })
  $minReq = ($st | ForEach-Object { @($_.requirements).Count } | Measure-Object -Minimum).Minimum
  $minAc  = ($st | ForEach-Object { @($_.acceptanceCriteria).Count } | Measure-Object -Minimum).Minimum
  $minLen = ($st | ForEach-Object { $_.implementation.notes.Length } | Measure-Object -Minimum).Minimum
  $noG    = @($st | Where-Object { -not $_.PSObject.Properties['grounding'] -or -not $_.grounding }).Count
  "{0}: features={1} stories={2} minReq={3} minAC={4} minNotes={5} storiesWithoutGrounding={6}" -f `
     $n, @($y.features).Count, $st.Count, $minReq, $minAc, $minLen, $noG
}
```

Targets: `minReq >= 4`, `minAC >= 3`, `minNotes >= 350`, and `storiesWithoutGrounding` as close
to 0 as you can honestly achieve. Feature and story counts MUST match what you started with.

Then run the repository validator, which must report "Backlog is valid":

```powershell
pwsh -NoProfile -File tools\backlog-sync\Invoke-BacklogSync.ps1 -Validate
```
