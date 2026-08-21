# Legal content tooling

Two scripts that keep Forge's legal text correct in both places it appears.

## Why

Forge's privacy policy, terms, medical disclaimer and licences have to exist inside the app **and**
at a public URL the stores link to. Store reviewers compare them. If the published policy and the
in-app policy disagree, that is a rejection risk and, for a privacy policy, a live legal exposure,
because the published document makes commitments about how the app behaves.

Keeping two copies in sync by discipline alone does not work. It had already failed here: the
hand-written constants in `src/Forge.App/Features/Legal/LegalContent.cs` and the Markdown in
`docs/legal/privacy-policy.md` had different sections and different wording before these scripts
existed.

So `docs/legal/*.md` is the single source of truth, and everything else is generated from it.

## `Build-LegalSite.ps1`

Builds the public site, and along the way emits the app-facing artefacts.

```
pwsh tools/legal/Build-LegalSite.ps1
```

Output goes to `artifacts/site` by default, which `.gitignore` already excludes. Generated HTML is
never committed, so it cannot drift from its source.

| Output | Purpose |
| --- | --- |
| `index.html` and one folder per page | The published site |
| `assets/` | Stylesheet and favicon |
| `sitemap.xml`, `robots.txt`, `.nojekyll` | Discovery, and stopping Jekyll from eating files |
| `legal-content.json` | Machine-readable copy the app can embed as a `MauiAsset` |
| `legal-content.cs.txt` | Same content as C#, for reference |
| `tools/legal/generated/LegalContent.g.cs` | The exact C# the in-app screens should use |

Useful switches:

- `-OutputPath <path>` — where to build. The workflow uses `_site`.
- `-BaseUrl <url>` — override the canonical root, for a custom domain.
- `-FailOnTodo` — fail if any `TODO(owner)` placeholder is unfilled. The publish job uses this.

### The Markdown dialect is deliberately small

Front matter, `##` and `###` headings, paragraphs, bullet and numbered lists, pipe tables, `**bold**`,
`` `code` `` and `[links](target/)`. Anything else is a hard error.

That strictness is the point. A generator that quietly passes through what it does not understand
produces broken markup on a legal document, and nobody notices until a reviewer does. The build
also validates that internal links resolve to real page slugs and that every `TODO(owner: ...)`
marker is closed on a single line so it stays greppable.

### Front matter keys

| Key | Required | Meaning |
| --- | --- | --- |
| `title` | yes | Page `<h1>` and nav label |
| `slug` | yes | URL segment. `.` means the site root |
| `description` | yes | `<meta name="description">` |
| `summary` | yes | The lede paragraph under the title |
| `order` | no | Documentation aid; nav order comes from `site.json` |
| `effective` | no | Renders an effective date and a sitemap `lastmod` |
| `inApp` | no | Name of the `LegalContent` property to generate. Each `##` becomes one `LegalSection` |

Only documents listed in `docs/site/site.json` are published, which is how the store declaration
drafts under `docs/legal/store/` stay internal.

## `Test-LegalContentSync.ps1`

Fails if the in-app copy has drifted from the published copy.

```
pwsh tools/legal/Test-LegalContentSync.ps1
```

It rebuilds from the Markdown through the real generator — rather than reimplementing the parser,
because two parsers eventually disagree and a drift checker that drifts is worse than none — then
extracts the section titles and bodies actually present in `LegalContent.cs` and compares them.
Differences are reported section by section.

To adopt the generated content in one step:

```
pwsh tools/legal/Test-LegalContentSync.ps1 -UpdateInPlace
```

That rewrites `src/Forge.App/Features/Legal/LegalContent.cs`, so run it deliberately and review the
diff. It is currently the intended migration path off the hand-written constants.

### Running it in CI

This script is not wired into `.github/workflows/ci.yml`, which is owned elsewhere. Once the app has
adopted the generated file, add it beside the other `tools/ci` checks:

```yaml
- name: Check legal copy is in sync
  shell: pwsh
  run: ./tools/legal/Test-LegalContentSync.ps1
```

Until then it will report drift, which is accurate rather than broken.

## How the app should consume this

Two options, in order of preference.

**1. Generated C#.** Run `-UpdateInPlace`, commit the generated `LegalContent.cs`, and add the sync
check to CI. The in-app screens keep working exactly as they do now, because the generated file has
the same shape: `LegalContent.PrivacyPolicy`, `.TermsOfService`, `.MedicalDisclaimer` and
`.Licences`, each an `IReadOnlyList<LegalSection>`. No page code changes.

**2. Embedded JSON.** Ship `legal-content.json` as a `MauiAsset` and read it at runtime. More
flexible, but it moves a compile-time guarantee to runtime and needs deserialisation and failure
handling for no real benefit while the content ships with the binary anyway.

Either way the in-app screens should also link out to the public URL, so a user can read the same
document without the app and so the deletion route stays reachable from the store listing.
