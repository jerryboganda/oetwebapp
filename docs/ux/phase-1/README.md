# Phase 1 — Discovery & Evidence

Capture the current state of every T0/T1 route without changing it. Evidence is input to Phase 4 scoring.

## Deliverables
- `route-screenshots/<route>/{light,dark}-{mobile-360,tablet-768,desktop-1280}.png`
- `content-inventory.md` — user-visible strings per route; flag dev copy.
- `a11y-baseline.md` — axe + Lighthouse runs; record serious/critical violations.
- `analytics-baseline.md` — drop-off funnels pulled from `lib/analytics.ts` events.
- `support-tickets-themes.md` — top 20 friction themes from support log.

## Capture method
- **Web:** Playwright script using existing `tests/e2e/` auth fixtures to visit inventory routes and screenshot at three viewports, both themes.
- **Mobile:** Capacitor debug builds, iOS 17 + Android 14, actual devices where possible; fall back to simulator screenshots.
- **Desktop:** Electron build via `npm run desktop:dev`; full-window capture.

## Exit criteria
- 100% of T0 routes captured light + dark, three viewports.
- axe run committed for every T0 route.
- Content inventory flags every "dev-copy" string (see mobile audit C-1).
