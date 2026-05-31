# Onboarding — QA Checklist

## Functional

- [ ] New learner: `/onboarding` → `/goals` (exam mode + confidence persist and reload correctly) → `/diagnostic`.
- [ ] First `/dashboard` visit auto-starts the dashboard tour once; anchors spotlight Next action, Skills, Today's plan, Readiness, Help launcher.
- [ ] Reload `/dashboard` → tour does **not** re-appear.
- [ ] First visit to `/listening`, `/reading`, `/writing`, `/speaking` each auto-starts its module tour once; never inside a player (`*/player/*`, `*/mocks/*`).
- [ ] First `/admin`, `/expert`, `/tutor` visit auto-starts the matching tour; `workspace-nav` is spotlighted.
- [ ] Help "?" → drawer lists role-appropriate tours; "Replay" runs the tour after the drawer closes; completed tours show a "Completed" marker.
- [ ] Onboarding checklist: items reflect real state (profession/exam date/targets/tour/diagnostic); card hides once all done; "Take the platform tour" replays.
- [ ] `/onboarding-tour` redirects to `/dashboard`.

## No-repeat / state

- [ ] `completed`, `skippedTours`, `dismissedTips`, `lastSeenTourVersion` persist via `GET/PATCH /v1/onboarding/tours` and survive logout/login and another device.
- [ ] Skipping a tour (X/ESC) records it as skipped and it does not auto-reappear; it is still replayable from Help.
- [ ] Bumping `TOUR_VERSION` re-shows a completed tour exactly once.

## Role gating

- [ ] A learner never sees admin/expert/tutor tours; an expert sees expert (+ tutor) tours; an admin sees the admin tour; a sponsor sees none.
- [ ] `/v1/onboarding/tours` is reachable by every authenticated role and rejects anonymous requests.

## Accessibility (WCAG-focused)

- [ ] Keyboard: Tab/Shift+Tab cycle controls; Enter activates Next/Back/Finish; **ESC closes**.
- [ ] Focus is trapped in the popover while open and **returns to the launcher** (or trigger) on close.
- [ ] Popover has an accessible name; step changes are perceivable to screen readers.
- [ ] Reduced motion: with `prefers-reduced-motion`, Driver animation is off and CSS transitions are disabled.
- [ ] Contrast passes in light and dark; no information conveyed by colour alone.
- [ ] Target element is scrolled into view and not fully covered by the popover.
- [ ] Touch targets ≥ 44px; usable at 200% browser zoom; mobile layout works (sidebar-anchored steps skip gracefully on mobile).
- [ ] Run `@axe-core/playwright` over an active tour overlay (extend `tests/e2e/shared/accessibility.spec.ts`).

## Analytics

- [ ] `tour_started/step_viewed/completed/skipped`, `help_center_opened`, `checklist_item_completed`, `welcome_exam_mode_set/confidence_set` fire with expected props and reach `/v1/analytics/events`.
- [ ] No duplicate tour events from the server (persistence only).

## Build / regression (run in Docker)

- [ ] `docker exec oet-local-web npm install` (adds `driver.js`, updates lockfile).
- [ ] `npm run docker:tsc`, `npm run docker:lint`, `npm run docker:test`, `docker exec oet-local-web npm run build`.
- [ ] `docker exec oet-local-api dotnet build`; `dotnet ef migrations add VerifyOnboardingTour` reports **no model changes**; `dotnet ef database update`.
- [ ] `docker exec oet-local-api dotnet test`; `docker exec oet-local-web npm run test:e2e:smoke`.
- [ ] No regressions to auth, existing onboarding/goals/diagnostic, module players, or admin/expert consoles.
