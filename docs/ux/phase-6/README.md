# Phase 6 — Accessibility Remediation (WCAG 2.2 AA)

## Artifacts
- `a11y-gap-register.md` — WCAG failure per route with SC reference (e.g., 1.4.3, 2.1.1, 2.4.7, 2.5.5).
- `keyboard-map.md` — tab order + shortcuts for: writing editor, speaking recorder, mock exam, review queue, admin publish.
- `sr-script.md` — VoiceOver + NVDA walk-through for top 10 flows with expected announcements.
- `color-contrast-report.md` — token pairs failing 4.5:1 / 3:1, with proposed token changes.

## Non-negotiables
- Keyboard reaches every interactive control; no traps.
- Visible focus ring (not browser default) on all focusable elements.
- Form inputs: label + error + describedby, never placeholder-only.
- Media: captions for video lessons; transcripts for audio prompts.
- Touch targets ≥ 44×44 px; spacing ≥ 8 px between targets on mobile.
- Motion honours `prefers-reduced-motion` — see `motion-system` skill.
- Colour never the only signal (always icon/text pair).

## CI gate
Add axe-core Playwright run for a "smoke" route set; fail PR on any new serious/critical violation.

## Exit criteria
- 0 serious/critical axe violations on T0 routes.
- Manual SR pass on top 10 flows with no blockers.
