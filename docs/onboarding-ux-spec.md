# Onboarding — UX Spec

## Principles

Action-first, one concept per step, exam-aware copy, short. Skippable and replayable. Personalised by profession/progress. No fake stats, no claim of official OET affiliation, never blocks paid content. Tours never auto-repeat after completion (re-shown only on a major `TOUR_VERSION` bump).

## Journeys

1. **First login → setup:** `/onboarding` (3-step intro: what OET is, how the platform works, what to expect) → `/goals` (profession, exam date, target scores, weak areas, study hours, **exam mode**, **confidence**) → `/diagnostic`. Backed by `LearnerUser.Onboarding*`.
2. **Dashboard tour:** auto-starts once on first `/dashboard` visit (or replay from Help). Anchors: `learner-dashboard-next-action`, `learner-dashboard-skills`, `learner-dashboard-today`, `learner-dashboard-readiness`, `learner-help-launcher`, plus a centered welcome.
3. **Module tours:** auto-start on first visit to each hub (`/listening`, `/reading`, `/writing`, `/speaking`); anchor the hub chooser (`<module>-hub`) + exam-fact steps.
4. **Admin / Expert / Tutor:** auto-start on first `/admin` · `/expert` · `/tutor` visit; anchor `workspace-nav` + workflow concept steps. Expert tour complements the existing setup wizard.
5. **Replay / Help:** the "?" launcher in the top nav opens a role-aware drawer to replay any tour, with a mock-vs-practice explainer and guide/support links.

## Tour content (source of truth)

Copy lives in `lib/onboarding/tours/*.ts`. Verified OET facts encoded: Listening 3 parts/42 Q/audio-once; Reading Part A 15-min lock + B/C 45-min; Writing 1 task/45 min (5+40)/criteria review; Speaking 2 role plays/3-min prep/~5-min/warm-up not assessed; 0–500 scale, 350 = B, no composite.

## `data-tour` selector convention

`data-tour="<role/surface>-<element>"`. Live anchors: `learner-dashboard-next-action|skills|today|readiness`, `learner-dashboard-checklist`, `learner-help-launcher`, `listening-hub`, `reading-hub`, `writing-hub`, `speaking-hub`, `workspace-nav`. Add new anchors to real components only (never CSS classes/text/random ids); absent anchors are skipped.

## Empty states

The dashboard already uses `LearnerEmptyState` for new users ("Welcome to your OET workspace" → Start Onboarding / Set Goals) and for no-plan/no-tasks states. Recommended additional copy to standardise across modules (brief §11):
- **No tests assigned:** "Your tutor hasn't assigned a test yet. Start a self-practice module or view your study plan." → Start Reading/Listening Practice · View Study Plan.
- **No Writing feedback yet:** "Your Writing submission is awaiting tutor review. Feedback will appear here once released." → View Submission · Try Another Task.
- **No Speaking session:** "Speaking works best with a live role-play. Book a tutor session or try AI practice." → Book Session · Try AI Practice.
- **No results yet:** "Complete your first practice or mock to start building your progress profile." → Start Diagnostic.

## Implementation map

- Engine: `lib/onboarding/{tour-types,tour-driver,tour-storage,tour-events,tour-registry}.ts` + `lib/onboarding/tours/*`.
- React: `components/onboarding/{tour-provider,tour-auto-trigger,tour-launcher,help-center-drawer,onboarding-checklist}.tsx` + `tour.css`.
- Wiring: `app/providers.tsx` (provider), `components/layout/app-shell.tsx` (auto-trigger), `top-nav.tsx` (launcher), `sidebar.tsx` (`workspace-nav`), `app/page.tsx` (dashboard anchors + checklist), `app/goals/page.tsx` (exam mode/confidence), module hubs (`<module>-hub`).
- Persistence: `LearnerOnboardingTour` + `GET/PATCH /v1/onboarding/tours`; profile fields on `LearnerGoal`.

## How to replay / disable

- **Replay:** Help "?" → drawer → any tour (fires `tour_replayed`); the dashboard checklist's "Take the platform tour" also replays.
- **No-repeat:** gated by persisted `completed`/`skippedTours` + `lastSeenTourVersion ≥ TOUR_VERSION`.
- **Disable auto-start:** completed/skipped flags suppress it; bumping `TOUR_VERSION` re-enables a one-time re-show after content changes.
