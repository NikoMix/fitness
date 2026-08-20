# Accessibility audit: shared controls

Scope: `src/Forge.App/Controls/` shared components. The pass focused on semantics, traversal, touch targets, colour/state, text scaling, motion, and contrast risks.

## Fixed

- `MetricTile` now announces caption first and expands common units, for example `Bodyweight, 82.5 kilograms`, instead of reading the visual number first. Fixed-width columns and caption truncation were removed so 200% text scaling has room to wrap. Visual child labels are hidden from the accessibility tree so the composite description is announced once.
- `StatRow` no longer truncates the value column; both sides wrap and share the row width to reduce overlap risk at large text sizes. Visual child labels are hidden so the label/value pair is announced as one row.
- `SectionHeader` keeps the heading semantic level, wraps long titles, gives the optional action a 48-unit minimum width, and exposes action description/hint only on the button rather than folding it into the header description.
- `EmptyState` keeps the headline as a heading, hides the decorative glyph from the accessibility tree, removes the action from the container description, and gives the button its own description/hint.
- `SkeletonPlaceholder` was checked: it stops animation when hidden/unloaded and skips pulsing when Android animator/transition scale is zero or iOS Reduce Motion is enabled.

## Checked

- Interactive controls use shared button styles with `TouchTargetMin` 48 units; `SectionHeader` now also enforces minimum width.
- No component hard-codes hex colours. XAML uses DevExpress semantic roles. `MetricTile.Accent` remains a public API and should continue to be supplied from semantic theme colours by callers.
- No meaningful state is conveyed by colour alone in these shared controls. Empty state uses text plus a decorative glyph; loading state has a semantic loading description.
- Reduced opacity on text was not found in controls. Skeleton opacity animation affects a non-text loading block only.

## Needs device verification

I could not verify actual TalkBack or VoiceOver focus grouping, rotor heading navigation, live announcement timing, launcher icon masking, or 200% text layout on physical Android/iOS devices from this environment. Automated build checks prove the XAML/SVG compiles, but final accessibility sign-off still needs device testing with screen readers, large text, bold text, reduce motion, and common Android launcher masks.

