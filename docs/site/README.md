# Forge legal site

The public legal site published to GitHub Pages. This folder holds the site *shell* — template,
stylesheet, configuration. The words come from `docs/legal/*.md`, and the build is
`tools/legal/Build-LegalSite.ps1`.

## Why this exists

Google Play will not accept the Health Apps declaration without a publicly hosted privacy policy
URL, and that review takes 4-8 weeks with no published SLA. Nothing else on the project blocks
release for that long, so the URL is the critical path.

GitHub Pages was chosen because it costs nothing and adds no infrastructure, which is the only
hosting choice consistent with ADR-0001's no-backend, no-running-cost decision. An app whose entire
premise is that it stores nothing remotely should not pay for a server to host the page that says
so.

## Turning Pages on

Pages is **not currently enabled** on this repository. Only someone with admin rights can enable
it; the workflow cannot do it for itself.

1. Go to **Settings → Pages** in the repository.
2. Under **Build and deployment**, set **Source** to **GitHub Actions**. Do not pick "Deploy from a
   branch" — the workflow uploads a built artifact, not a branch.
3. Merge this branch to `main`. The `Publish legal site` workflow runs automatically on any change
   under `docs/legal/`, `docs/site/`, `tools/legal/` or the workflow file itself.
4. Watch the run in **Actions → Publish legal site**. The `deploy` job prints the live URL.

The repository is public, so Pages is available on any plan and no extra cost applies.

### Resulting URLs

| Page | URL |
| --- | --- |
| Landing | `https://nikomix.github.io/fitness/` |
| Privacy policy | `https://nikomix.github.io/fitness/privacy/` |
| Data safety summary | `https://nikomix.github.io/fitness/data-safety/` |
| Delete my data | `https://nikomix.github.io/fitness/delete-my-data/` |
| Terms of service | `https://nikomix.github.io/fitness/terms/` |
| Support | `https://nikomix.github.io/fitness/support/` |
| Medical disclaimer | `https://nikomix.github.io/fitness/medical-disclaimer/` |
| Third-party licences | `https://nikomix.github.io/fitness/licences/` |

The privacy policy URL is the one the stores need.

### The first deploy will fail on purpose

The workflow builds with `-FailOnTodo`, so it refuses to publish while any `TODO(owner)` placeholder
is unfilled. That is deliberate: a published privacy policy that literally reads "TODO" would fail
store review and waste the multi-week declaration window, which is far more expensive than a failed
build. Fill the placeholders listed in the build log, push, and the deploy succeeds.

If you genuinely need the site live before the placeholders are resolved, run the workflow manually
from **Actions → Publish legal site → Run workflow** with **allow_placeholders** ticked. The pages
then render the placeholders as visible highlighted markers rather than hiding them.

## Custom domain later

Pass a different root to the build and the canonical URLs and sitemap follow:

```
pwsh tools/legal/Build-LegalSite.ps1 -BaseUrl https://forge.example/
```

Page-to-page links are relative, so they keep working at any base path without changes. Add the
domain under **Settings → Pages → Custom domain** and set `baseUrl` in `site.json` to match.

## Files here

| File | Purpose |
| --- | --- |
| `site.json` | Site name, language, base URL, page list and nav order |
| `template.html` | The page shell, with `{{placeholder}}` tokens the build fills |
| `assets/forge.css` | The only stylesheet. Hand written, no framework |
| `assets/favicon.svg` | Self-hosted icon, so no request escapes the origin |

`site.json` controls what is published. A document in `docs/legal/` that is not listed in `pages`
is not published — that is how the store declaration drafts in `docs/legal/store/` stay internal.

## Constraints this site must keep

- **No JavaScript.** Every page is fully readable with scripting disabled.
- **No third-party requests.** No CDN, no webfonts, no analytics, no embeds. The workflow asserts
  this on every publish and fails if anything sneaks in.
- **No cookies and no storage.**
- **Accessible.** Semantic headings, `lang`, a skip link, descriptive titles, table header scopes,
  visible focus, and contrast comfortably above WCAG AA in both light and dark mode. The privacy
  and deletion pages score 100 for Accessibility, Best Practices and SEO in Lighthouse.

These are not decoration. The credibility of a privacy policy that is served with a tracker on it
is zero.
