# Onboarding & Product-Tour — Project Discovery Report

_Discovery for the onboarding + product-tour + walkthrough system. Produced from a read-only scan of the codebase plus official OET fact verification (oet.com / occ.org.au)._

## 1. Stack summary

- **Frontend:** Next.js 16 App Router, React 19, TypeScript, Tailwind CSS 4 (class-based dark mode via `next-themes`), motion v12 (`motion/react`) with reduced-motion profiles, `next-intl` i18n (only the Writing module is currently translated; the rest ship hard-coded English).
- **State/data:** TanStack React Query (central `queryKeys` factory in `lib/query/hooks.ts`), Zustand stores, `apiClient`/typed helpers in `lib/api.ts`.
- **Providers:** `app/providers.tsx` → `NextIntlClientProvider → ThemeProvider → TooltipProvider → QueryProvider → AuthProvider → (bridges) → children`.
- **Backend:** ASP.NET Core Minimal API, EF Core, PostgreSQL, SignalR. Endpoints under `backend/src/OetLearner.Api/Endpoints`, entities in `Domain/`, `LearnerDbContext`, migrations `YYYYMMDDhhmmss_Name` (dev `AutoMigrate` true; prod manual).
- **Desktop/mobile:** Electron + Capacitor.

## 2. User role map

`UserRole = 'learner' | 'expert' | 'admin' | 'sponsor'` (single role per `CurrentUser`).
- **learner** — student workspace (`/`, `/dashboard`, modules, `/goals`, `/diagnostic`).
- **expert** — reviewer console (`/expert/**`); **"tutor"** is a UI surface of the same `expert` role at `/tutor/**` (classes + writing-only). Both authenticate via `useExpertAuth()`.
- **admin** — `/admin/**`, gated by `adminPermissions` (21 permissions; `system_admin` is a super-permission).
- **sponsor** — institution surface (out of scope for tours in this build).

## 3. Route map (relevant)

- Learner: `/` & `/dashboard` (same dashboard component), `/onboarding`, `/goals` (+ `/goals/study-commitment`), `/diagnostic`, `/listening`, `/reading`, `/writing`, `/speaking` (+ players: `*/player/[id]`, `*/mocks/[sessionId]`, listening `audio-check`/`test-rules`, reading `paper/[paperId]`, speaking `sessions/[id]/prep`), `/mocks`, `/study-plan`, `/readiness`, `/feedback-guide`, `/billing/plans`.
- Expert: `/expert`, `/expert/queue`, `/expert/learners`, `/expert/review/{writing,speaking,listening}/[id]`, `/expert/calibration`, `/expert/onboarding` (existing 6-step setup wizard).
- Tutor: `/tutor`, `/tutor/writing/queue`, `/tutor/writing/reviews/[id]`, `/tutor/classes`.
- Admin: `/admin/**` (9 nav groups: Overview, Content, Learner Plans, Governance & Rubrics, Reviews & Quality, AI & Automation, People & Access, Billing & Growth, System), incl. mock wizard `/admin/content/mocks/wizard/[bundleId]/{listening,reading,writing,speaking,bundle,review}`.

## 4. Component map (reused by this build)

- Shell: `components/layout/app-shell.tsx` (+ `learner/expert/admin/sponsor-dashboard-shell.tsx`), `sidebar.tsx`, `top-nav.tsx`.
- UI primitives: `components/ui/{button,card,badge,modal(Modal+Drawer),form-controls(Select,RadioGroup,…),stepper,progress,motion-primitives}`; `components/domain/{learner-surface,learner-skill-switcher,learner-empty-state,ProfessionSelector}`.
- Analytics: `lib/analytics.ts` (singleton + `TRACKED_EVENTS` union → `POST /v1/analytics/events`), `hooks/use-analytics.ts`.
- Existing onboarding: `app/onboarding` (3-step intro), `app/onboarding-tour` (legacy static slideshow — **repurposed** to redirect), module `welcome`/`pathway` pages, `app/expert/onboarding`.

## 5. Existing onboarding gaps (closed by this build)

| Gap | Resolution |
| --- | --- |
| No anchored/spotlight tour engine (only static card slideshow) | Driver.js engine + `TourProvider` + registry |
| No per-tour "seen/skipped/dismissed" persistence | `LearnerOnboardingTour` entity + `/v1/onboarding/tours` |
| No first-entry module tours | Listening/Reading/Writing/Speaking tours + `TourAutoTrigger` |
| No onboarding checklist | `OnboardingChecklist` on the dashboard |
| No Help/replay center | `TourLauncher` ("?") + `HelpCenterDrawer` |
| No exam-mode / confidence capture (5.1) | Added to `LearnerGoal` + `/goals` form |
| No admin/expert onboarding tours | `admin`/`expert`/`tutor` tours |

## 6. UX confusion points the tours address (verified vs OET)

- Reading is **two timed blocks** (Part A 15 min, locked; B+C 45 min) — not one 60-minute timer.
- Listening audio **plays once** in strict mock.
- Writing/Speaking are **profession-specific**; Writing is criteria-reviewed, not auto-scored.
- Speaking warm-up is **not assessed**; device check is microphone-only for AI practice.
- OET reports **no composite score** — each sub-test stands alone (350 = Grade B).
- Strict **mock** vs **practice** mode distinction (explained in the Help center).

## 7. Where onboarding triggers

`components/onboarding/tour-auto-trigger.tsx` (mounted in `app-shell`) starts the matching tour once on first visit to the exact route: `/dashboard` (and `/`), `/listening`, `/reading`, `/writing`, `/speaking`, `/admin`, `/expert`, `/tutor`. Gated by persisted completion/skip + `TOUR_VERSION`; never inside an exam player; never repeats.

## 8. Files created / 9. Files modified

See `docs/onboarding-ux-spec.md` §Implementation map and the PR diff. New: `lib/onboarding/**`, `components/onboarding/**`, `backend/.../Domain/LearnerOnboardingTour.cs`, migration `20260619000000_AddOnboardingTourAndGoalFields.cs`, the five `docs/onboarding-*.md`. Modified: `app/providers.tsx`, `app/page.tsx`, `app/goals/page.tsx`, `app/onboarding-tour/page.tsx`, `components/layout/{app-shell,top-nav,sidebar}.tsx`, module hub pages, `lib/{analytics,api,mock-data,query/hooks}.ts`, backend `Entities.cs`/`LearnerDbContext.cs`/`LearnerEndpoints.cs`/`LearnerService.cs`/`Contracts/Requests.cs`/`ModelSnapshot`.

## 10. Risks & assumptions

- `driver.js` must be installed (`npm install`) in Docker before build/type-check; it adds a linked CSS file (no runtime `<style>` injection that CSP would block).
- The EF migration is hand-authored with `[Migration]`/`[DbContext]` attributes + matching snapshot edits; verify with `dotnet ef migrations add` (expect "no changes") then `database update`.
- Tour copy is hard-coded English to match the app's prevailing surfaces; it can be lifted into `messages/en/onboarding.json` later.
- Auto-trigger uses a 1s delay for async dashboards to mount anchors; missing anchors are skipped gracefully.

## 11. Implementation plan

Delivered in phases P0 (foundation) → P1 (learner) → P2 (module tours) → P3 (admin/expert/tutor) → P4 (docs/tests). See the approved plan file and `docs/onboarding-ux-spec.md`.
