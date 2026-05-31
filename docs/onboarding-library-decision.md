# Onboarding — Tour Library Decision

## 1. Current stack (constraints)

Next.js 16 App Router + React 19, Tailwind 4 with **class-based dark mode**, motion v12 with reduced-motion profiles, `next-intl`, a per-request **CSP nonce** (middleware), Radix primitives (Dialog/Popover/Tooltip) and an in-house focus-trapped `Modal`/`Drawer`. Commercial SaaS — license matters. SSR/streaming — the tour must be client-only and lazy.

## 2. Candidates considered

| Library | Pros | Cons |
| --- | --- | --- |
| **Driver.js** (v1, MIT) | ~5 kb, zero deps, spotlight overlay + popover, keyboard nav + ESC + focus handling built in, framework-agnostic, simple imperative API, easy to theme via one class + bundled CSS | Popover DOM is its own (theme via CSS overrides); single active tour |
| **Shepherd.js** (MIT) | Very feature-rich, step routing, attachTo | Larger bundle (+ Tippy/Popper), heavier CSS theming to match the clinical system, more API surface than needed |
| **React Joyride** (MIT) | React-native API, beacons | Larger, React-tree coupled (harder with Driver-style overlay), styling via JS props, maintenance cadence |
| **Custom (Radix Popover + Floating UI)** | Total control, perfect design-system fit | We'd re-implement spotlight overlay, scroll-into-view, keyboard nav, focus return — meaningful effort/maintenance |

## 3. License notes

Driver.js, Shepherd.js, and React Joyride are all **MIT** — compatible with commercial SaaS. No copyleft concerns.

## 4. Decision

**Driver.js (v1.x).** Chosen for the smallest footprint, built-in accessibility primitives (overlay focus, keyboard, ESC), and the lowest theming effort to match the clinical design system. The popover is themed with a single `popoverClass` (`oet-tour-popover`) and a scoped stylesheet (`components/onboarding/tour.css`) including a `.dark` block and a reduced-motion guard. The bundled `driver.js/dist/driver.css` is imported (linked, not runtime-injected) so the CSP nonce is not implicated; Driver's positioning uses programmatic element styles, which CSP `style-src` does not govern.

## 5. Implementation plan

- Lazy adapter `lib/onboarding/tour-driver.ts` (`await import('driver.js')`) builds Driver config from a `TourDefinition`: `animate: !reducedMotion`, progress text, Back/Next/Finish, `allowClose`, hooks → analytics + persistence. Steps whose `data-tour` target is absent are filtered (graceful across routes).
- `TourProvider` (mounted after `AuthProvider` in `app/providers.tsx`) exposes `startTour`/`isCompleted`/`dismissTip` and reads/writes server state via React Query.
- `TourAutoTrigger` (in `app-shell`) starts the first-run tour once per surface; `TourLauncher` ("?") + `HelpCenterDrawer` replay any tour.
- Add `driver.js` to `package.json`; run `npm install` in Docker to update the lockfile before building.

## 6. Accessibility posture

Driver.js provides overlay focus, ESC-to-close, and keyboard next/prev. The adapter adds reduced-motion (`animate:false`), the themed popover preserves contrast in light/dark, and the launcher is a labelled button. See `docs/onboarding-qa-checklist.md` for the full WCAG-focused checklist (focus return, screen-reader step announcements, touch targets, zoom).
