# PROGRESS — Listening Module V2

> Ralph-style progress log. Newest entries on top. Each wave concludes with a checkpoint.

## Wave 26 — Phase 9 learner surface closure and review hardening (complete, 2026-05-13)

Phase 9's active learner/admin surface is now closed as an implemented route
slice rather than a docs-only plan. The live Listening player keeps the single
active route, but its reusable player shell pieces are extracted, documented in
Storybook, tested independently, and wired back into the production player. The
post-review hardening also closes two strict-mode integrity gaps found during
the independent pass: strict audio resume now pauses while validation is
pending and stays fail-closed on network/API errors, and multi-extract Part B
audio gates the review transition against the whole Part B cue window rather
than the first workplace clip only.

### Wave 26 Implementation Slice

1. Keep the active learner route at `app/listening/player/[id]/page.tsx`, but
    delegate the visible player chrome to `ListeningIntroCard`,
    `ListeningAudioTransport`, `ListeningPhaseBanner`, and
    `ListeningSectionStepper` under `components/domain/listening/player/`.
2. Add an opt-in Storybook scaffold (`storybook`, `build-storybook`,
    `.storybook/*`, `docs/STORYBOOK.md`) plus Listening V2 stories for the
    extracted player components. Storybook stories remain excluded from the
    default `tsc`/lint paths by design.
3. Ship the admin per-question deep-dive route at
    `/admin/analytics/listening/question/{paperId}/{number}` with filtered
    accuracy, distractor histogram, and contextual misspelling panels, linked
    from the hardest-questions admin analytics table.
4. Convert the strict exam and paper-mode Listening Playwright specs into live
    learner-page contract tests, with diagnostics capture and exact R10/no-V2-
    advance assertions. They remain smoke-level route contracts because the
    seeded `lt-001` paper is not a complete multi-part free-navigation fixture.
5. Harden strict audio resume to pause while the V2 resume verdict is pending,
    resume only after server approval, and warn/stay paused when validation
    fails.
6. Treat the active section audio cue window as the min start / max end across
    all authored extracts in that section, so Part B cannot unlock review after
    only its first workplace extract.
7. Add list semantics and `aria-current="step"` to the section stepper so the
    extracted component exposes its progression state to assistive technology.

### Wave 26 Checkpoint

Complete for the Phase 9 learner/admin surface closure and review hardening.

- Extracted player components are imported by the live route and covered by
    focused Vitest component tests.
- Storybook has opt-in scripts/config/docs and valid five-section Listening V2
    component stories.
- Admin question deep-dive has unit coverage for routed filtering, empty data,
    non-admin access, invalid routes, and partially numeric route params.
- Strict audio resume uses the authoritative server mode policy and is
    fail-closed while validation is pending and after validation failure.
- Strict Part B review remains locked until every authored workplace extract in
    the section reaches its `audioEndMs`.
- Full V2 save/submit DTO handoff, all-parts paper final review, and a seeded
    multi-part paper free-navigation E2E remain explicit later migration work.

Validation:

- `cmd /c npx vitest run app/listening/player/[id]/__tests__/audio-resume.test.tsx app/admin/analytics/listening/question/[paperId]/[number]/page.test.tsx components/domain/listening/player/__tests__/listening-player-components.test.tsx` — 21/21 passing.
- `cmd /c npx vitest run tests/unit/listening app/listening/player/[id]/__tests__/cbla-fidelity.test.tsx app/listening/player/[id]/__tests__/cue-point-seek.test.tsx app/listening/player/[id]/__tests__/audio-resume.test.tsx lib/listening/v2-api.test.ts lib/listening-api.test.ts components/domain/listening/player/__tests__/listening-player-components.test.tsx app/admin/analytics/listening/question/[paperId]/[number]/page.test.tsx` — 73/73 passing.
- `cmd /c npx tsc --noEmit` — clean.
- `cmd /c npm run lint` — clean.
- `cmd /c npx playwright test tests/e2e/listening/listening-exam-mode-strict-locks.spec.ts tests/e2e/listening/listening-paper-mode-free-nav.spec.ts --project=chromium-learner` — blocked before page load because the local API could not start without Postgres (`Failed to connect to 127.0.0.1:5432`; Playwright auth bootstrap then failed with `ECONNREFUSED ::1:5198`).

## Wave 25 — Strict player FSM resume and phase advance bridge (complete, 2026-05-12)

Strict Exam/OET-Home player navigation now hydrates from the V2 server FSM after
resume and uses the two-step `/advance` protocol for the forward phase changes
inside the active route. This closes the stale-server-state gap left after the
Wave 21 start/readiness bridge without attempting the broader save/submit or
paper free-navigation migration in the same patch.

### Wave 25 Implementation Slice

1. Add shared TypeScript helpers that map `ListeningFsmState` values to the
    active player's section/phase model and normalize server window milliseconds
    for display timers.
2. Hydrate strict resumes from `/v1/listening/v2/attempts/{id}/state` so a
    resumed `a2_audio`, `c1_review`, etc. opens on the matching section/phase
    instead of falling back to A1.
3. Route strict preview -> audio, audio -> review, and review -> next preview
    transitions through `listeningV2Api.advance(...)`, including the existing
    confirm-token retry loop, before mutating local UI state.
4. Fail closed with a visible player error when a strict phase transition is
    rejected, and guard duplicate Next/transition clicks while an advance is in
    flight.
5. Preserve the legacy answer save/submit path for this wave because the active
    result flow still depends on the full `ListeningReviewDto`; V2 save/submit
    remains a later explicit migration.

### Wave 25 Checkpoint

Complete for strict-route server FSM alignment after start.

- Strict resume no longer reopens A1 when the server state is already in a later
    section/phase.
- Strict preview expiry posts the next audio state through the V2 confirm-token
    advance before playback starts.
- Strict audio/review forward moves use the same V2 advance helper before the
    UI leaves the current phase/section.
- Non-strict practice/paper/learning behavior keeps the existing local flow.

Validation:

- `cmd /c npx vitest run tests/unit/listening/transitions.parity.test.ts app/listening/player/[id]/__tests__/cbla-fidelity.test.tsx app/listening/player/[id]/__tests__/audio-resume.test.tsx` — 29/29 passing.
- `cmd /c npx vitest run tests/unit/listening app/listening/player/[id]/__tests__/cbla-fidelity.test.tsx app/listening/player/[id]/__tests__/cue-point-seek.test.tsx app/listening/player/[id]/__tests__/audio-resume.test.tsx lib/listening/v2-api.test.ts lib/listening-api.test.ts` — 55/55 passing.
- `cmd /c npx tsc --noEmit` — clean.
- `cmd /c npm run lint` — clean.

## Wave 24 — R08 Part B/C annotation tools and in-app zoom (complete, 2026-05-12)

The active Listening player now exposes the R08 candidate tools on the real
production route without reintroducing removed per-mode player shells. Part A
remains free of highlight/strikethrough controls, while Part B/C multiple-choice
questions support stem highlight, option strikethrough, keyboard radio-group
navigation, and bounded in-app question zoom.

### Wave 24 Implementation Slice

1. Add `BCQuestionRenderer` for Part B/C multiple-choice questions with stem
    highlight, right-click option strikethrough, keyboard-accessible strike
    buttons, roving radio focus, and Arrow/Home/End radio navigation.
2. Add `ZoomControls` with bounded 90%-130% question zoom, reset, disabled edge
    states, and a live current-zoom announcement.
3. Wire the active player route to render `PartARenderer` for Part A and
    `BCQuestionRenderer` for Part B/C, with zoom applied to the active question
    surface.
4. Make Part A and B/C renderer text inherit the zoomed parent through `em`
    sizing so the visible question content actually scales.
5. Keep browser/system zoom allowed for accessibility and align the effective
    backend policy view with `BrowserZoomAllowed: true` while leaving the legacy
    EF storage column untouched.
6. Add focused component and route-level coverage for Part A exclusion, Part B/C
    tools, zoom controls, route-level zoom application, and radio keyboard
    navigation.
7. Refresh the R08 rulebook citation map, canonical Listening frontend docs,
    and PRD Wave 24 implementation note.

### Wave 24 Checkpoint

Complete for R08 web-player candidate tools.

- Part A clinical-note questions do not expose highlight or strikethrough tools.
- Part B/C questions expose stem highlight and option strikethrough controls.
- Strikethrough is available through both context-menu and keyboard-accessible
    controls.
- B/C answer choices follow ARIA radio keyboard behavior with roving focus.
- In-app zoom is bounded, resettable, announced, and applied to the visible
    question surface while preserving browser/system zoom.
- Independent review initially found a zoom inheritance blocker and a radio
    keyboard interaction gap; both were fixed before final validation. Final
    review found no blockers.

Validation:

- `cmd /c npx vitest run tests/unit/listening/BCQuestionRenderer.test.tsx tests/unit/listening/ZoomControls.test.tsx tests/unit/listening/PartARenderer.test.tsx app/listening/player/[id]/__tests__/cbla-fidelity.test.tsx` — 23/23 passing.
- `cmd /c npx vitest run tests/unit/listening app/listening/player/[id]/__tests__/cbla-fidelity.test.tsx app/listening/player/[id]/__tests__/cue-point-seek.test.tsx lib/listening/v2-api.test.ts lib/listening-api.test.ts` — 43/43 passing.
- `cmd /c npx tsc --noEmit` — clean.
- `cmd /c npm run lint` — clean.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening --no-restore` — 152/152 passing.
- VS Code diagnostics for touched files — clean.

## Wave 23 — Backend-authored pathway launch targets (complete, 2026-05-12)

The 12-stage pathway now launches exact backend-authored scoped player routes
instead of frontend-inferred route families. Each actionable row from
`/v1/listening/v2/me/pathway` can carry an `actionHref` that points to a paper
the learner is allowed to access and includes the exact `pathwayStage` needed
for scoped attempt creation/resume.

### Wave 23 Implementation Slice

1. Add `ListeningPathwayLaunchTargets` as the shared backend helper for
    canonical stage-to-mode URL generation.
2. Extend the pathway DTO with nullable `actionHref` and resolve launch paper
    ids from published objective-ready Listening papers that pass entitlement
    checks.
3. Add backend endpoint coverage for all canonical stage routes, completed
    stage suppression, missing objective-ready papers, and entitlement-denied
    papers.
4. Treat `diagnostic` as a real relational Listening mode for scoped pathway
    launches, API session mode normalization, and player mode derivation.
5. Scope implicit relational session resume by `pathwayStage` so scoped launches
    do not pick up unrelated generic in-progress attempts.
6. Thread `pathwayStage` through `GET /listening-papers/papers/{paperId}/session`,
    `getListeningSession(...)`, player session lookup, player attempt start, and
    active-attempt route preservation.
7. Update `/listening/pathway` and `PathwayBoard` to consume backend `actionHref`
    values, use stage-specific action copy, and render only unlocked/in-progress
    actionable links.
8. Remove the obsolete lint suppression in `lib/wizard/sanitize-html.ts` so the
    repo lint gate returns clean.

### Wave 23 Checkpoint

Complete for exact stage launch targets and scoped player entry.

- Pathway tiles no longer infer coarse routes on the frontend.
- Backend launch URLs are relative, escaped, canonical-stage mapped, and
    suppressed for locked/completed/unanchored stages.
- Learners only receive launch links for objective-ready papers that entitlement
    allows.
- Diagnostic pathway launches create/resume relational `Diagnostic` attempts.
- Stage-scoped player session lookup avoids reusing unrelated generic attempts.
- Independent review found no blockers; its entitlement-denial regression
    recommendation was implemented before final validation.

Validation:

- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningV2PathwayLaunchTargetEndpointTests --no-restore` — 5/5 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningRelationalRuntimeTests --no-restore` — 14/14 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening --no-restore` — 152/152 passing.
- `cmd /c npx vitest run app/listening/pathway/page.test.tsx tests/unit/listening/PathwayBoard.test.tsx lib/listening/v2-api.test.ts lib/listening-api.test.ts app/listening/player/[id]/__tests__/cbla-fidelity.test.tsx` — 24/24 passing.
- `cmd /c npx tsc --noEmit` — clean.
- `cmd /c npm run lint` — clean.
- VS Code diagnostics for touched files — clean.

## Wave 22 — Pathway stage-scoped attempt progression (complete, 2026-05-12)

Listening pathway recompute now treats submitted attempts as stage evidence
instead of broad mode evidence that can cascade through the 12-stage curriculum.
A submitted attempt can qualify at most one stage per recompute, and relational
attempts may carry an explicit `ScopeJson.pathwayStage` so focused launches map
to the exact pathway stage they were created for.

### Wave 22 Implementation Slice

1. Add `ListeningAttemptScope` as the shared parser/builder for pathway attempt
    scope JSON.
2. Make `ListeningPathwayProgressService` include `ScopeJson`, require exact
    scoped-stage matches when a scope exists, and consume qualifying attempt ids
    once per recompute.
3. Extend `ListeningAttemptStartRequest` and `ListeningLearnerService` with an
    optional `pathwayStage` parameter, validated against the canonical 12-stage
    list.
4. Store `pathwayStage` in relational `ListeningAttempt.ScopeJson` for new
    scoped starts.
5. Scope in-progress relational attempt reuse by requested pathway stage while
    preserving unscoped legacy reuse.
6. Accept legacy scope keys (`stageCode`, `stage`) in the shared parser for
    existing/test data compatibility.
7. Thread `pathwayStage` through `lib/listening-api.ts` and preserve it across
    the active Listening player start URL.
8. Add regression coverage for unscoped single-use, exact scoped matching,
    later-stage scopes not backfilling earlier stages, legacy scope keys,
    invalid scope values, non-string scope values, and scoped in-progress reuse.

### Wave 22 Checkpoint

Complete for backend pathway progression correctness and start-scope plumbing.
The pathway UI still links through coarse route families; future work should
replace those links with backend-authored launch targets that include the exact
`pathwayStage` for each stage.

- One broad `Learning` attempt no longer completes several foundation/drill/
    minitest stages as recompute unlocks them.
- Scoped attempts qualify only their declared pathway stage.
- Invalid explicit pathway scopes do not silently become broad unscoped attempts.
- Generic unscoped starts and scoped starts keep separate in-progress attempts.
- The active player preserves `pathwayStage` through start/resume URL replacement.
- Independent review found no blockers after the shared-helper cleanup.

Validation:

- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningPathwayProgressServiceTests --no-restore` — 10/10 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningRelationalRuntimeTests --no-restore` — 12/12 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening --no-restore` — 145/145 passing.
- `cmd /c npx vitest run app/listening/player/[id]/__tests__/cbla-fidelity.test.tsx app/listening/player/[id]/__tests__/cue-point-seek.test.tsx` — 13/13 passing.
- `cmd /c npx tsc --noEmit` — clean.
- `cmd /c npm run lint` — clean.
- VS Code diagnostics for touched files — clean.

## Wave 21 — Active player strict readiness bridge (complete, 2026-05-12)

The active learner Listening player now participates in the V2 R10 readiness
contract for strict starts. Exam/Home launches must complete the readiness
probe, persist the snapshot through `/v1/listening/v2`, pass the server
`intro -> a1_preview` transition including confirm-token replay, and only then
mount/start the existing legacy player flow. Practice and paper launches keep
their existing behavior.

### Wave 21 Implementation Slice

1. Wire `TechReadinessCheck` into `app/listening/player/[id]/page.tsx` for
    strict `exam`/`home` starts.
2. Persist successful readiness via `listeningV2Api.recordTechReadiness(...)`
    before start.
3. Apply the first V2 FSM transition to `a1_preview`, including automatic
    confirm-token echo, before `hasStarted` flips.
4. Derive strict behavior from explicit route mode, mock launch params, and
    the returned server `session.modePolicy` so strict resumes fail closed even
    when `mode` is absent.
5. Keep strict paper audio unmounted until readiness/start or verified resume.
6. Harden `TechReadinessCheck` with local duration measurement, cleanup on
    unmount, generated-audio fallback, and accessible status/alert states.
7. Add route-level Vitest coverage for happy path, rejected transition,
    strict mock launch params, V2 resume failure, server-policy strict resume,
    and pre-start audio gating.

### Wave 21 Checkpoint

Complete for the active-route strict start/readiness bridge. This is not yet a
full V2-routed player: answer save, submit, per-section phase changes, and the
main audio/review machine still use the legacy route-local flow.

- Strict Exam/Home starts are blocked until readiness succeeds.
- Readiness is persisted server-side before the first strict transition.
- The first server FSM transition must apply before the player begins.
- Strict resume fails closed when V2 state cannot be verified.
- Mock strict launch params no longer collapse Listening sections to practice.
- Strict mode does not mount the paper audio before readiness/start.
- Independent review found bypasses twice; all findings were fixed and the
    final review passed.

Validation:

- `cmd /c npx vitest run app/listening/player/[id]/__tests__/cbla-fidelity.test.tsx app/listening/player/[id]/__tests__/cue-point-seek.test.tsx lib/listening/v2-api.test.ts tests/unit/listening/transitions.parity.test.ts` — 22/22 passing.
- `cmd /c npx tsc --noEmit` — clean.
- `cmd /c npm run lint` — clean.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningV2AdvanceEndpointTests` — 8/8 passing.
- VS Code diagnostics for touched files — clean.

## Wave 20 — R10 backend tech-readiness enforcement (complete, 2026-05-12)

Listening V2 now has a server-side R10 readiness contract for strict-mode starts:
learners can record an audio readiness snapshot, the FSM enforces a fresh
successful snapshot before `intro -> a1_preview` when readiness is required, and
non-owned attempt ids are hidden behind the same not-found path as missing ids.

### Wave 20 Implementation Slice

1. Add `POST /v1/listening/v2/attempts/{attemptId}/tech-readiness` for learner
    attempts.
2. Persist `TechReadinessJson` with `audioOk`, capture time, optional device
    label, sample duration, and client diagnostics.
3. Enforce `ListeningPolicy.TechReadinessRequired` and
    `TechReadinessTtlMs` before strict attempts can leave `intro` for
    `a1_preview`.
4. Return structured 422 `AdvanceResultDto` rejections with
    `tech-readiness-required` or `tech-readiness-expired`.
5. Keep non-owned attempt ids indistinguishable from missing ids across state,
    advance, tech-readiness, and audio-resume routes.
6. Update the typed frontend API client to record readiness and consume
    structured 422 advance rejections.

### Wave 20 Checkpoint

Complete for backend/API-client R10 readiness enforcement. The routed learner
player and strict-mode frontend readiness gate remain pending follow-up work.

- Strict-mode `intro -> a1_preview` rejects missing or expired readiness.
- Fresh `audioOk=true` readiness allows the existing confirm-token flow to
    proceed unchanged.
- Non-owner attempts return 404 for the learner state, advance, readiness, and
    audio-resume endpoints.
- Frontend API helpers expose structured readiness rejections instead of
    throwing them away.
- Independent review initially found two medium issues; both were fixed and the
    follow-up review passed.

Validation:

- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningV2AdvanceEndpointTests` — 8/8 passing.
- `cmd /c npx vitest run lib/listening/v2-api.test.ts` — 5/5 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening` — 137/137 passing.
- VS Code diagnostics for touched files — clean.

## Wave 19 — Backend privacy and FSM hardening (complete, 2026-05-12)

Listening V2 backend guards now close the post-Wave-18 gaps found by the
follow-up specialist review: teacher roster analytics are restricted to teaching
staff roles, class CRUD/member paths hide non-owned class existence, free-nav
state changes cannot persist arbitrary strings, corrupt navigation JSON is
repaired safely, and admin attempt exports ignore non-Listening evaluations that
share an attempt id.

### Wave 19 Implementation Slice

1. Add `TeachingStaffOnly` authorization for teacher class routes and move the
    `/v1/listening/v2/teacher/*` routes onto that policy.
2. Validate teacher class member ids before insert: blank ids return bad request,
    unknown/inactive learner ids return not found, and duplicate inserts remain
    idempotent.
3. Make teacher class CRUD/member lookups owner-scoped so missing and non-owned
    class ids both follow the not-found path.
4. Add canonical FSM state helpers and reject unknown `toState` values before
    free-navigation modes can persist them.
5. Repair missing/malformed `NavigationStateJson` on state reads; terminal
    attempts repair to `submitted` with locks instead of reopening at `intro`.
6. Filter admin export evaluations by `SubtestCode == "listening"`.
7. Add HTTP/service regression tests for each guard.

### Wave 19 Checkpoint

Complete for the focused backend privacy/FSM hardening slice.

- Learners can no longer create or query teacher class routes; expert/sponsor/admin
    teaching staff can.
- Non-owned class ids do not reveal existence across CRUD, member, or analytics
    paths.
- Null/blank member ids no longer produce server errors.
- Free-navigation modes reject unknown state names with a structured 422 result.
- Corrupt navigation JSON repairs without reopening submitted attempts.
- Admin Listening exports include only Listening evaluations.
- Independent review initially found three medium issues; all were fixed before
    final validation.

Validation:

- `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~ListeningAdminAttemptExportEndpointTests|FullyQualifiedName~ListeningV2AdvanceEndpointTests|FullyQualifiedName~ListeningV2TeacherClassAnalyticsEndpointTests|FullyQualifiedName~TeacherClassServiceTests"` — 19/19 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening` — 133/133 passing.
- VS Code diagnostics for touched files — clean.

## Wave 18 — Admin Listening attempt JSON export (complete, 2026-05-11)

Admins with content-read permission can now export the complete JSON payload for
a single Listening attempt. The export supports normalized Listening V2 attempts
and legacy `Attempt` rows, while learner and teacher surfaces remain unchanged
and privacy-safe.

### Wave 18 Implementation Slice

1. Add explicit export DTOs for attempt, answer, and evaluation JSON.
2. Add `IListeningAnalyticsService.ExportAttemptAsync(...)` with normalized
    `ListeningAttempt` first and legacy `Attempt` fallback.
3. Expose `GET /v1/admin/listening/attempts/{attemptId}/export` under the
    existing `/v1/admin/listening` `AdminContentRead` group.
4. Return 404 for missing Listening attempts.
5. Write a metadata-only `ListeningAttemptExported` audit event after successful
    exports without copying raw answer payloads into audit details.
6. Add endpoint registration, success, denial, missing-attempt, legacy, and audit
    regression coverage.

### Wave 18 Checkpoint

Complete for admin-only full attempt JSON export.

- Authorized admins can export full normalized and legacy Listening attempt JSON.
- Admins without `content:read` and learners receive forbidden responses.
- Missing attempt ids return 404.
- Successful exports are audit-tracked with source/user/paper/count metadata, and
    the audit details explicitly avoid raw learner answer strings.
- Independent review found no blockers; its audit and permission-coverage
    recommendations were implemented before final validation.

Validation:

- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningAdminAttemptExportEndpointTests` — 5/5 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~EndpointRegistrationTests` — 16/16 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening` — 126/126 passing.
- VS Code diagnostics for touched files — clean.

## Wave 17 — Teacher analytics CSV export (complete, 2026-05-11)

The teacher class analytics workspace now supports a privacy-safe CSV export for
the selected class and reporting window. Export rows are built from whitelisted
summary, part-accuracy, hardest-question, and aggregate distractor-count fields;
raw learner-entered wrong answers remain excluded from the page and the export.

### Wave 17 Implementation Slice

1. Add an `Export CSV` action beside refresh in `/listening/classes`.
2. Generate export rows from explicit teacher-safe analytics fields only.
3. Use the existing browser-side `exportToCsv(...)` helper with a class/window
    filename.
4. Clear stale analytics while a reload is in flight and after reload errors.
5. Disable export unless the current analytics request is successful.
6. Migrate touched learner-shell/domain imports to direct file imports.

### Wave 17 Checkpoint

Complete for teacher CSV export.

- Teachers can export class KPIs, part accuracy, hardest questions, and aggregate
    distractor miss counts for the selected window.
- Export cannot leak accidental `wrongAnswerHistogram` payload fields because rows
    are explicitly whitelisted.
- Failed refreshes cannot export stale prior analytics.
- Independent review initially caught the stale-export issue; after the fix, a
    second review found no blocking or non-blocking findings.

Validation:

- `cmd /c npx vitest run app/listening/classes/page.test.tsx lib/listening/v2-api.test.ts` — 7/7 passing.
- `cmd /c npx tsc --noEmit` — clean.
- VS Code diagnostics for touched files — clean.

## Wave 16 — Teacher analytics endpoint authz guard (complete, 2026-05-11)

Teacher class analytics now has HTTP-level regression coverage for the OWASP A01
scope boundary called out in the PRD risk table: non-owners receive a not-found
response for another teacher's class analytics route, with no class id or class
name leaked in the response body.

### Wave 16 Implementation Slice

1. Reuse the existing teacher class analytics endpoint fixture to seed an owned
    class, learner member, authored question, attempt, and answer.
2. Call `/v1/listening/v2/teacher/classes/{classId}/analytics?days=30` as a
    different authenticated learner/teacher id.
3. Assert HTTP 404 and verify the response body does not contain the seeded
    class name or class id.

### Wave 16 Checkpoint

Complete for endpoint authorization regression coverage.

- Service-level non-owner coverage remains in place.
- The HTTP boundary now proves the endpoint converts non-owned class ids to
    not-found without leaking class existence details.
- Independent review found no blocking or non-blocking issues.

Validation:

- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningV2TeacherClassAnalyticsEndpointTests` — 2/2 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening` — 121/121 passing.
- VS Code diagnostics for touched files — clean.

## Wave 15 — Teacher class analytics query hardening (complete, 2026-05-11)

Teacher class analytics now scopes attempts through the class-member table rather
than materializing roster user ids into `Contains` filters. This keeps large
classes off giant `IN (...)` attempt filters while preserving owner scoping,
member counts, empty-class behavior, and the Wave 14 teacher-safe analytics DTO.

### Wave 15 Implementation Slice

1. Keep `GetClassAnalyticsAsync(...)` owner-scoped by `(classId, ownerUserId)`.
2. Count distinct class members directly in the database instead of loading the
    roster into memory.
3. Pass the class id into aggregate analytics and filter both legacy `Attempt`
    rows and normalized `ListeningAttempt` rows with a `TeacherClassMembers`
    join plus `Distinct()`.
4. Preserve admin analytics by calling the aggregate builder without class
    scope.
5. Keep PostgreSQL/server providers filtering `SubmittedAt >= since` in SQL, and
    use a SQLite-safe client-side submitted-at filter after the server-side
    class join when SQLite is the provider.
6. Add a SQLite-backed regression test that proves the join query translates,
    excludes outsiders, excludes out-of-window class-member attempts, and emits a
    SQL command containing a class-member join.

### Wave 15 Checkpoint

Complete for teacher class analytics query hardening.

- Large classes no longer require a materialized roster ID list to scope
    attempts.
- Teacher analytics still sends only aggregate wrong-answer counts and no raw
    learner-entered wrong-answer payloads.
- SQLite desktop/test runtime now avoids the nullable `DateTimeOffset` predicate
    translation failure while keeping the narrow class scope server-side.
- Independent review found no blocking findings; the suggested SQL-shape and
    date-window regression coverage was added before final validation.
- Residual future hardening: capture a PostgreSQL query plan against a seeded
    large class if class analytics becomes a production hot path.

Validation:

- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningClassAnalyticsServiceTests` — 6/6 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningV2TeacherClassAnalyticsEndpointTests` — 1/1 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening` — 120/120 passing.
- `cmd /c npx tsc --noEmit` — clean.
- VS Code diagnostics for touched files — clean.

## Wave 14 — Teacher analytics wire-privacy hardening (complete, 2026-05-11)

Teacher class analytics now redacts sensitive learner-answer aggregates at the
backend wire contract instead of relying on frontend-only mapping. The teacher
route receives a dedicated analytics DTO with aggregate wrong-answer counts and
no raw wrong-answer histogram or common-misspelling payload.

### Wave 14 Implementation Slice

1. Add backend `ListeningTeacherAnalyticsDto` and
    `ListeningTeacherDistractorHeatDto` for teacher-safe class analytics.
2. Change `ListeningClassAnalyticsDto.Analytics` to use the teacher-safe DTO.
3. Map admin distractor histograms to `WrongAnswerCount` inside
    `ToTeacherSafeAnalytics(...)` before returning teacher class data.
4. Preserve admin analytics with raw `WrongAnswerHistogram` and
    `CommonMisspellings` for admin-only use.
5. Align the frontend teacher analytics wire type to the backend-safe shape and
    keep the output mapper defensive against accidental extra raw fields.
6. Add an HTTP endpoint privacy test proving teacher JSON excludes
    `commonMisspellings`, `wrongAnswerHistogram`, and raw learner answer text.

### Wave 14 Checkpoint

Complete for teacher analytics privacy hardening.

- Teacher class analytics no longer sends raw learner-entered wrong answers over
    the backend teacher endpoint.
- Frontend page code continues to consume only `wrongAnswerCount`.
- Independent review found no blocking or non-blocking issues after the endpoint
    serialization test was added.
- Residual future hardening: if teacher/admin analytics split further, move
    admin-only aggregation behind a narrower internal projection so the service
    does less work for teacher-only requests.

Validation:

- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningClassAnalyticsServiceTests` — 5/5 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningV2TeacherClassAnalyticsEndpointTests` — 1/1 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening` — 119/119 passing.
- `cmd /c npx vitest run lib/listening/v2-api.test.ts app/listening/classes/page.test.tsx` — 6/6 passing.
- `cmd /c npx tsc --noEmit` — clean.
- VS Code diagnostics for touched files — clean.

## Wave 13 — Confirm-token contract hardening (complete, 2026-05-11)

The strict-mode two-step section advance contract now works across backend,
typed frontend API, and tests. The backend still returns HTTP 412 for the first
strict-mode advance, but now includes the `AdvanceResultDto` body containing the
confirm token instead of an empty response.

### Wave 13 Implementation Slice

1. Return JSON `AdvanceResultDto` for `confirm-required` responses from
    `/v1/listening/v2/attempts/{attemptId}/advance` with HTTP 412.
2. Document the advance endpoint response shapes with OpenAPI `.Produces(...)`
    metadata for 200, 412, 422, 403, and 404.
3. Add `apiClient.postWithAcceptedStatuses(...)` so typed helpers can opt into
    intentional non-2xx JSON contracts without weakening normal client errors.
4. Route `listeningV2Api.advance()` through the accepted-412 helper.
5. Add backend endpoint coverage for first-call token issuance and second-call
    token replay.
6. Add frontend API-client coverage for accepted non-2xx JSON and Listening V2
    advance call shape.

### Wave 13 Checkpoint

Complete for confirm-token contract hardening.

- Strict CBT/OET-Home advance can now surface the server-issued token to the UI.
- Normal `apiClient` behavior still throws on non-2xx unless a typed helper opts
    into a specific accepted status.
- Residual future hardening: add a consumer-level hook/component test for
    `useListeningNavigation` opening `SectionAdvanceConfirm` from the returned
    token once the V2 CBT player is wired into a routable page.

Validation:

- `cmd /c npx vitest run lib/listening/v2-api.test.ts lib/__tests__/api.test.ts` — 31/31 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningV2AdvanceEndpointTests` — 1/1 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening` — 117/117 passing.
- `cmd /c npx tsc --noEmit` — clean.
- Independent review — no blocking findings; OpenAPI response metadata follow-up applied.
- VS Code diagnostics for touched files — clean.

## Wave 12 — 12-stage pathway UI polish (complete, 2026-05-11)

The V2 pathway endpoint now has a dashboard-native learner surface: the page uses
the shared learner shell, displays a hero summary and accessible progress bar,
and renders the 12 canonical stages as action-aware tiles.

### Wave 12 Implementation Slice

1. Move `/listening/pathway` into `LearnerDashboardShell` with `LearnerPageHero`
    highlights for completed stages, current stage, and best scaled score.
2. Add a progress summary card with `role="progressbar"` and value semantics.
3. Expand `PathwayBoard` with canonical stage labels, concise focus text, status
    styling, and route mappings for unlocked/in-progress stages.
4. Keep locked and completed stages non-actionable while exposing readable status
    text and icons.
5. Add route-level RTL coverage for the page shell, progressbar semantics,
    action link mapping, and canonical 12-stage rendering.

### Wave 12 Checkpoint

Complete for the pathway UI polish slice.

- New learners with the backend-initialized board see a dashboard-consistent
    pathway instead of a bare page.
- In-progress/unlocked stages are keyboard-focusable links with explicit labels.
- The progress indicator now has screen-reader progressbar semantics.
- Residual future hardening: replace coarse route mappings with exact drill/paper
    ids when the backend starts returning stage-specific launch targets.

Validation:

- `cmd /c npx vitest run app/listening/pathway/page.test.tsx tests/unit/listening/PathwayAndAdvance.test.tsx` — 5/5 passing.
- `cmd /c npx tsc --noEmit` — clean.
- VS Code diagnostics for touched pathway files — clean.

## Wave 11 — Teacher class analytics frontend slice (complete, 2026-05-11)

The teacher-class analytics backend contract now has a usable frontend path:
Listening owners can open a class analytics workspace, create classes, add
learners by user id, select a reporting window, and inspect class-scoped score,
part, hardest-question, and distractor evidence.

### Wave 11 Implementation Slice

1. Add `teacherClassApi.analytics(classId, days)` to `lib/listening/v2-api.ts`.
2. Normalize backend analytics into a teacher-safe frontend DTO that omits
    `commonMisspellings` and converts raw `WrongAnswerHistogram` maps into
    aggregate `wrongAnswerCount` values.
3. Add `/listening/classes` as a learner-shell route for owner-scoped class
    analytics, class creation, and adding learners to the selected class.
4. Link the new class analytics workspace from the Listening home page.
5. Add Vitest coverage for API path encoding, teacher-safe DTO mapping,
    already-sanitized backend payloads, page rendering, empty classes, and DOM
    non-rendering of raw wrong-answer strings.

### Wave 11 Checkpoint

Complete for the frontend analytics workspace slice.

- The page renders class KPIs, A/B/C part accuracy, hardest questions, and MCQ
    distractor miss counts without showing raw wrong-answer histogram keys.
- The exported TypeScript DTO for teacher analytics does not expose
    `commonMisspellings` or raw wrong-answer maps to normal frontend consumers.
- The client mapper tolerates both current admin-shaped backend payloads and a
    future already-sanitized teacher-shaped payload.
- Residual future hardening: add browser/E2E coverage once seeded teacher-class
    auth states exist, and consider a dedicated backend teacher DTO so raw maps
    never cross the wire.

Validation:

- `cmd /c npx vitest run app/listening/page.test.tsx app/listening/classes/page.test.tsx lib/listening/v2-api.test.ts` — 9/9 passing.
- `cmd /c npx tsc --noEmit` — clean.
- VS Code diagnostics for touched frontend files — clean.

## Wave 10 — Teacher class analytics backend slice (complete, 2026-05-11)

The first teacher-dashboard backend gap is closed: class owners can request
Listening analytics scoped to their roster, with class membership filtering,
owner-bound lookup, and a teacher-safe payload that omits raw common misspelling
strings while preserving the admin-only full analytics view.

### Wave 10 Implementation Slice

1. Add `GetClassAnalyticsAsync(ownerUserId, classId, days, ct)` to
    `ListeningAnalyticsService` and extract shared aggregate analytics logic.
2. Scope class analytics to `TeacherClass.OwnerUserId` and current
    `TeacherClassMember` rows, returning not-found for non-owned classes to
    avoid class-id enumeration.
3. Add `ListeningClassAnalyticsDto` with class metadata, member count, and the
    shared analytics payload.
4. Expose `GET /v1/listening/v2/teacher/classes/{classId}/analytics` from the
    V2 endpoint group.
5. Redact teacher-facing `CommonMisspellings` so raw Part A wrong-answer text
    remains admin-only.

### Wave 10 Checkpoint

Complete for the backend service/endpoint contract slice.

- Owned classes return class-scoped completed attempts, average scaled score,
    percent likely passing, part averages, hardest questions, and distractor heat.
- Attempts from users outside the class roster are excluded.
- Non-owner access now follows the same not-found path as missing classes.
- Relational Listening V2 scaled scores are documented as the source of truth
    when both normalized attempts and legacy evaluation rows have scores.
- Residual future hardening: add a teacher-specific authorization policy if a
    dedicated teacher role is introduced, add HTTP-level endpoint integration
    tests, and replace large roster `Contains` filters with join-based SQL if
    class sizes grow enough to risk large `IN` lists.

Validation:

- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningClassAnalyticsServiceTests` — 4/4 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening` — 116/116 passing.
- VS Code diagnostics for touched class analytics files — clean.

## Wave 9 — Pathway recompute concurrency hardening (complete, 2026-05-11)

The residual Wave 8 race is closed for the current single-process API topology:
per-user pathway recompute calls are serialized before they read/create the 12
canonical pathway rows, preventing duplicate initialization under concurrent
requests in the same API process.

### Wave 9 Implementation Slice

1. Add a static per-user recompute gate in `ListeningPathwayProgressService`.
2. Move the recompute body behind the gate while preserving the existing public
    `RecomputeAsync(userId, ct)` contract.
3. Add a concurrent initialization regression test using separate `LearnerDbContext`
    instances against a shared in-memory database.

### Wave 9 Checkpoint

Complete for current deployment topology.

- Concurrent recomputes for the same new user now create exactly the canonical
    12 stages, with `diagnostic` unlocked and no duplicate stage codes.
- Residual future hardening: if the API is scaled to multiple web instances,
    replace or supplement the in-process gate with database upsert/advisory-lock
    semantics around `(UserId, StageCode)`.

Validation:

- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningPathwayProgressServiceTests` — 4/4 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening` — 112/112 passing.
- VS Code diagnostics for touched pathway files — clean.

## Wave 8 — 12-stage pathway contract hardening (complete, 2026-05-11)

The next academy-platform gap is closed for the learner pathway data contract:
the V2 pathway endpoint now initializes/recomputes canonical pathway rows before
projection, so new learners see an actionable diagnostic stage instead of a fully
locked board. The slice also tightened V2 grading authorization because pathway
recompute is triggered after grading.

### Wave 8 Implementation Slice

1. Make `/v1/listening/v2/me/pathway` call `ListeningPathwayProgressService`
    before reading rows, then project the canonical 12 stages in fixed order.
2. Persist the qualifying `AttemptId` and `ScaledScore` for both `InProgress`
    and `Completed` pathway stages.
3. Refresh pathway stage attempt/score when a better qualifying attempt appears,
    and clear stale attempt/score when a stage is only `Locked` or `Unlocked`.
4. Tie-break equal scaled scores by latest `SubmittedAt` for deterministic stage
    attribution.
5. Bind V2 grading to `http.UserId()` so one learner cannot grade another
    learner's attempt by guessing an attempt id; forbidden attempts now return
    403 from the endpoint.

### Wave 8 Checkpoint

Complete for the pathway contract hardening slice.

- New pathway learners get all 12 `ListeningPathwayProgress` rows with
    `diagnostic` unlocked and later stages locked.
- Submitted diagnostic attempts complete stage 1 and unlock `foundation_partA`.
- In-progress pathway stages carry the best current qualifying attempt score,
    so the existing pathway page can display meaningful score progress.
- `ListeningGradingService` keeps its existing unscoped grading API for internal
    tests/callers, but the V2 learner endpoint now uses the user-bound overload.
- Independent second-pass review passed. Wave 9 added an in-process per-user
    recompute gate for the current single-instance API topology.

Validation:

- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningPathwayProgressServiceTests` — 3/3 passing.
- `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~ListeningPathwayProgressServiceTests|FullyQualifiedName~ListeningGradingServiceTests"` — 15/15 passing.
- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~Listening` — 111/111 passing.
- VS Code diagnostics for touched backend files — clean.

## Wave 7 — Human score override slice (complete, 2026-05-11)

The popup-priority teacher/human override gap is now implemented for submitted
relational Listening attempts. Assigned expert reviewers can apply a per-question
binary score override with a required audit reason; the learner review shows only
a generic reviewer-adjusted marker, not the internal reason or actor.

### Wave 7 Implementation Slice

1. Extend `ListeningGradingService` with `ApplyScoreOverrideAsync()`.
2. Enforce post-submission only, required reason, override value `0`/`1`, question
    ownership by attempt paper, and active assigned reviewer authorization.
3. Store overrides in `ListeningAttempt.HumanScoreOverridesJson`, regrade through
    `OetScoring.OetRawToScaled`, update answer points, refresh the latest
    Listening `Evaluation`, and write `AuditEvent` details with reason/actor.
4. Expose an expert route at
    `/v1/expert/listening/attempts/{attemptId}/questions/{questionId}/score-override`.
5. Make learner review projection honor overrides while hiding internal audit
    fields from learner payloads.

### Wave 7 Checkpoint

Complete for the human override slice.

- `ListeningGradingService` now scores all authored questions for the attempt
    paper, so missing answer rows remain wrong but still contribute to max raw.
- Overrides require an active `ExpertReviewAssignment` in `Assigned` or `Claimed`
    state for a non-cancelled/non-failed Listening `ReviewRequest`.
- Expert endpoint resolves `ExpertUser.DisplayName` for audit display names.
- Learner review exposes only `{ override, message }` override metadata; the
    full reason and actor stay in `AuditEvent` and expert override result data.
- Independent second-pass review passed. Residual risk: simultaneous overrides
    on different questions of the same attempt are last-write-wins in the JSON
    column; a future normalized override table or optimistic concurrency token
    would harden that edge case.

Validation:

- `dotnet test backend/OetLearner.sln --filter FullyQualifiedName~ListeningGradingServiceTests` — 11/11 passing.
- `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~ListeningLearnerServiceTests|FullyQualifiedName~ListeningRelationalRuntimeTests"` — 19/19 passing.
- VS Code diagnostics for touched backend files — clean.

## Wave 6 — CBT audio integrity guard slice (complete, 2026-05-11)

The next popup-priority CBT integrity gap is now tightened in the legacy/current
Listening player: strict exam/home modes block native media pause/replay/seek
bypasses, while practice mode keeps pause and scrub freedom.

### Wave 6 Implementation Slice

1. Add a small pure helper for audio integrity decisions in
    `lib/listening/audio-integrity.ts`.
2. Wire `app/listening/player/[id]/page.tsx` through scoped programmatic cue
    seeks and allowed programmatic pauses.
3. Block every non-programmatic seek in strict modes, including tiny forward
    seeks that could otherwise ratchet the playhead.
4. Restart audio after unauthorized native/browser pause in strict audio phase.
5. Latch one-play replay blocking on both authored `audioEndMs` and the media
    element's native `ended` event.

### Wave 6 Checkpoint

Complete for this focused audio-integrity guard slice.

- Added `resolveBlockedSeekTarget()` and `shouldResumeAfterBlockedPause()` with
    Vitest coverage for practice mode, strict forward/back seek blocking, tiny
    seek blocking, programmatic cue seeks, and unauthorized pause restart.
- Replaced the previous broad programmatic-seek boolean with a target-scoped
    seek token plus fallback timeout clear.
- Post-review fixes closed three blockers: incremental seek ratcheting,
    programmatic seek bypass window, and replay after native `ended`.

Validation:

- `cmd /c npx vitest run tests/unit/listening/audio-integrity.test.ts` — 3/3 passing.
- `cmd /c npx tsc --noEmit` — clean.
- VS Code diagnostics for touched audio-integrity files — clean.
- Independent review after fixes — no blockers. Residual risk: the pure helper
    is tested, but page-level media-event lifecycle tests would further reduce
    regression risk.

## Wave 5 — Admin publish-gate enforcement slice (complete, 2026-05-11)

The next popup-priority gap is now implemented: Listening publish readiness blocks
incomplete or unsafe authored papers before `Published`.

### Wave 5 Implementation Slice

1. Extend `ListeningStructureService` rather than adding a parallel validator.
2. Enforce source-provenance/legal attestation before publish.
3. Enforce authoring metadata on relational Listening papers and JSON fallback:
    extract cue timings, extract difficulty, per-question skill tag, canonical
    skill-tag vocabulary, transcript evidence text/timestamps, per-question
    difficulty, and wrong-option distractor categories.
4. Update backend fixtures and add focused negative tests for the new gate.
5. Expose the missing `out_of_scope` distractor category in admin authoring
    TypeScript types and the structure editor selector.

### Wave 5 Checkpoint

Complete for the publish-gate slice.

- `ListeningStructureService` now emits blocking errors for:
    `listening_source_provenance`, `listening_extract_links`,
    `listening_extract_timing`, `listening_extract_difficulty`,
    `listening_skill_tags`, `listening_skill_tags_invalid`,
    `listening_transcript_evidence`, `listening_question_difficulty`, and
    `listening_distractor_categories`.
- JSON fallback validation now reads `listeningExtracts`, validates cue windows
    and difficulty, and counts missing wrong-option distractor categories without
    requiring a category on the correct option. JSON MCQs now accept either
    authored option text or `A`/`B`/`C` correct-answer keys.
- Relational fixtures now seed valid extracts, question metadata, evidence
    timestamps, and wrong-option distractor categories so canonical papers remain
    publish-ready under the stricter gate.
- Added focused tests for missing source provenance and missing JSON metadata.
- Post-review fix: provenance validation now requires an exact `legal=<allowed>`
    attestation token in `SourceProvenance`, not merely any non-empty text or a
    substring match.
- Post-review fix: added `out_of_scope` across admin TypeScript types/options,
    backend authoring normalization, JSON-to-relational backfill parsing, learner
    projection parsing, and AI extraction prompt vocabulary.
- Post-review hardening: legacy JSON distractor categories are now validated
    against the canonical vocabulary, with `listening_distractor_categories_invalid`
    emitted for unsupported values.

Validation:

- `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~ListeningStructureServiceTests|FullyQualifiedName~ListeningAuthoringServiceTests|FullyQualifiedName~ListeningBackfillServiceTests"` — 35/35 passing.
- `cmd /c npx tsc --noEmit` — clean.
- VS Code diagnostics for touched files — clean.

## Wave 4 — Popup decision refresh + Part A implementation slice (complete, 2026-05-11)

The attached Listening platform rulebook documents were analysed with a 10-agent
read-only swarm (research, planning, architecture, backend, frontend, UX,
review/security, edge-case debugging, devops, docs). The codebase already has a
substantial Listening V2 foundation, so the refreshed user decisions now serve
as the implementation contract for the remaining gaps.

### Locked user decisions from popup

- Scope: finish all remaining Listening V2 gaps end-to-end, starting by updating
    PRD/PROGRESS and then implementing.
- Canonical sources: both attached markdown documents are authoritative and must
    be reconciled with `docs/LISTENING.md`, rulebook JSON, and this PRD.
- Release priority: Part A OET note-layout renderer, strict Part A
    accepted-variant marking/error feedback, CBT one-play integrity tests, teacher
    override flow, admin authoring/publish gate, dashboards, pathway, and all
    delivery modes.
- Marking: strict normalized exact match + admin-approved accepted variants; no
    partial credit. AI may suggest variants but admin approval is required.
- Workflow: Draft -> Review -> Publish with required lint; publish blocks on
    source provenance/legal attestation, 24/6/12 split, cue timings, transcript
    evidence, skill tags, MCQ distractor categories, and difficulty rating.
- Accessibility: browser/system zoom must remain available for low-vision users;
    in-app zoom cannot be the only route.
- OET@Home: fullscreen exit warns and logs telemetry, but the attempt continues.
- Ops: local VPS volume plus manual post-deploy backfill.

### Wave 4 Implementation Slice

1. Record refreshed decisions in `PRD-LISTENING-V2.md`.
2. Add a dedicated Part A OET clinical-note renderer and wire the player to it.
3. Add focused frontend tests for the Part A renderer.
4. Keep existing strict backend marking via `ListeningGradingService` (exact
     normalized match + accepted variants) and add/extend tests if needed.

### Wave 4 Checkpoint

Complete for the Part A renderer / strict marking slice.

- Added `components/domain/listening/PartARenderer.tsx` and wired it into
    `app/listening/player/[id]/page.tsx` for short-answer Listening questions.
- Added `tests/unit/listening/PartARenderer.test.tsx` covering authored blanks,
    controlled typing, disabled spellcheck/autocomplete, and locked/read-only mode.
- Extended `ListeningGradingServiceTests` to pin strict Part A accepted variants
    without partial credit (`"the   aspirin"` accepted, `"aspirin tablets"` wrong).
- Post-review fix: removed duplicate Part A prompt rendering in the learner
    player by letting `PartARenderer` own the short-answer prompt surface.
- Post-review fix: aligned the legacy `ListeningLearnerService` submit matcher
    with whitespace-collapsing accepted-variant matching and pinned it in
    `ListeningRelationalRuntimeTests` because the current player submits through
    the legacy learner endpoint.

Validation:

- `npx vitest run tests/unit/listening/PartARenderer.test.tsx` — 3/3 passing.
- `dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~ListeningGradingServiceTests|FullyQualifiedName~ListeningRelationalRuntimeTests"` — 13/13 passing.
- `npx tsc --noEmit` — clean.
- VS Code diagnostics for touched files — clean.

## Wave 3a — WS-A schema + EF migration (complete, 2026-05-11)

The full additive schema from PRD §5.2 is now landed. Pure expansion — no
DropColumn / RenameColumn on any pre-existing column. Backend builds clean
and existing `ListeningRelationalRuntimeTests` (6 tests, SQLite in-memory)
all pass against the new model.

### Entity changes ([backend/src/OetLearner.Api/Domain/ListeningEntities.cs](backend/src/OetLearner.Api/Domain/ListeningEntities.cs))

| Entity | Additions |
| --- | --- |
| `ListeningAttemptMode` enum | `Diagnostic = 7` |
| `ListeningDistractorCategory` enum | `OutOfScope = 5` |
| `ListeningExtract` | `TopicCsv`, `DifficultyRating` |
| `ListeningQuestion` | `Version` (default 1), `DifficultyLevel` |
| `ListeningQuestionOption` | `Version` (default 1) |
| `ListeningAnswer` | `QuestionVersionSnapshot`, `OptionVersionSnapshot` |
| `ListeningAttempt` | `NavigationStateJson`, `WindowStartedAt`, `WindowDurationMs`, `AudioCueTimelineJson`, `TechReadinessJson`, `AnnotationsJson`, `HumanScoreOverridesJson`, `LastQuestionVersionMapJson` |
| `ListeningPolicy` | 14 nullable int timing columns + 10 nullable bool flag columns (R05/R06/R07/R08/R10) |
| `ListeningUserPolicyOverride` | `AccessibilityModeEnabled` |
| (NEW) `ListeningPathwayProgress` | 12-stage tracker with unique (UserId, StageCode) |
| (NEW) `TeacherClass` | Cross-skill, owner-scoped |
| (NEW) `TeacherClassMember` | Unique (TeacherClassId, UserId), cascade from class |
| (NEW) `ListeningAttemptNote` | Cascade from attempt |

### DbContext wiring ([backend/src/OetLearner.Api/Data/LearnerDbContext.cs](backend/src/OetLearner.Api/Data/LearnerDbContext.cs))

- 4 new `DbSet` registrations.
- 6 new jsonb column type configs **conditional on `Database.IsNpgsql()`** (SQLite test harness keeps them as TEXT).
- Cascade rules: `ListeningAttemptNote → ListeningAttempt`, `TeacherClassMember → TeacherClass`. Pathway progress no FK to user (anonymisation safe).

### Migration ([backend/src/OetLearner.Api/Data/Migrations/20260511110000_Listening_V2_Schema.cs](backend/src/OetLearner.Api/Data/Migrations/20260511110000_Listening_V2_Schema.cs))

Hand-written. Auto-generation produced 22KB of contamination from snapshot drift (per `/memories/repo/migration-drift-note.md` — known issue). Hand-written file:

- 327 lines, all additive.
- Includes 2 idempotent SQL backfills: seed `Version = 1` on existing question/option rows; seed `QuestionVersionSnapshot = q.Version` on existing answer rows.
- Down() drops only what Up() created — never touches pre-existing columns.

### WS-A Verification

- ✅ `dotnet build` — 0 warnings, 0 errors (1:40)
- ✅ `dotnet test --filter "ListeningRelationalRuntime"` — 6/6 passing in 3s

### Known follow-ups deferred to next session

- `LearnerDbContextModelSnapshot.cs` not updated (would have re-introduced the 5375-line drift). Migration runtime still applies cleanly because of the `[Migration]` + `[DbContext]` attributes inline. Snapshot reconciliation is a separate maintenance PR per the existing memory note.
- `ListeningV2BackfillService` (`BackgroundService`) — planner §1(f) — NOT implemented. Required for in-flight attempts to seed `NavigationStateJson` on first read. Workaround acceptable: `ListeningSessionService` (WS-B) can lazily seed on `GetStateAsync` if state is null.
- `ListeningLearnerLeakAuditTest.cs` — planner §0 deliverable — NOT implemented. Required before WS-B starts.
- `ListeningScoringPathAuditTest.cs` — planner §0 deliverable — NOT implemented. Required before WS-B starts.

---

## Wave 3a — WS-E0 minimum slice (complete, 2026-05-11)

Constants foundation laid. No behavior change, no schema change yet — these
are the canary files that pin the contract for the upcoming WS-A migration
and WS-B service extraction. All 4 files compile clean (`dotnet build` =
0 warnings, 0 errors).

| Deliverable | File | Purpose |
| --- | --- | --- |
| AI feature codes (skill-tag) | `backend/src/OetLearner.Api/Domain/AiEntities.cs` (+13 lines) | New const `AdminListeningSkillTag` so future AI skill-tag classification cannot bypass the gateway (critic HIGH #4). |
| AI feature codes (transcript) | same | New const `AdminListeningTranscriptSegment` for AI transcript time-coding. |
| Platform-only registration | `backend/src/OetLearner.Api/Services/AiManagement/AiCredentialResolver.cs` (+2 entries) | Both codes added to `PlatformOnlyFeatures` so BYOK is physically refused. |
| Timing defaults | `backend/src/OetLearner.Api/Services/Listening/ListeningPolicyDefaults.cs` (NEW) | Per-window FSM defaults (R05/R06/R07/R10). Frozen `ImmutableDictionary` per-Part lookup. |
| Skill-tag vocabulary | `backend/src/OetLearner.Api/Services/Listening/ListeningSkillTags.cs` (NEW) | Closed vocabulary + `IsValid()` predicate. Used by publish-gate lint (WS-A) and analytics aggregation (WS-B). |

### WS-E0 Verification

- ✅ `dotnet build backend/src/OetLearner.Api/OetLearner.Api.csproj` — 0 warnings, 0 errors (1:41).
- ✅ No new runtime code paths invoked yet — pure additive constants/defaults.
- ✅ `LearnerQuestionDto` leak path re-verified by planner: line 1862 projects `q.Options` from a `record` (List&lt;string&gt;), not the entity — no `IsCorrect`/`WhyWrongMarkdown` leak in the JSON-authored path. The relational-entity DTO path leak audit is the remaining WS-E0 deliverable (must be enforced by an xunit `ListeningLearnerLeakAuditTest.cs` before WS-B work starts).

### Critical decisions locked in

1. **AI gateway pre-declaration** — even though no AI skill-tag/transcript code is being written yet, the feature codes are reserved + classified as platform-only **now**, so the next dev who adds AI logic cannot accidentally route through BYOK.
2. **Cross-skill `TeacherClass`** — confirmed by planner §0 — tables named without `Listening` prefix (will be `TeacherClass`, `TeacherClassMember` not `ListeningTeacherClass`).
3. **Already-shipped fields reconfirmed** — `SkillTag` (string?), `DistractorCategory` enum, `AccentCode`, `TranscriptEvidence*` all exist; only **extend** them, never recreate.

---

## Wave 2 — Plan + Design (complete, 2026-05-11)

2 read-only specialists ran in parallel. Outputs archived (38KB + 45KB) to
chat-session-resources. Critical findings absorbed below.

### Planner (file-level execution plan)

- **WS-E0 reconciliation** — verified every entry in PRD §5.2 row-by-row against `ListeningEntities.cs`. Result: **all 45 proposed additions are genuinely missing**. None already exist. The reconciliation memo IS PRD §5.2 itself; no rework needed.
- **Schema migration scope** — additive only. ~25 columns across `ListeningQuestion`, `ListeningQuestionOption`, `ListeningAnswer`, `ListeningExtract`, `ListeningAttempt`, `ListeningPolicy`. 4 new tables (`ListeningPathwayProgress`, `TeacherClass`, `TeacherClassMember`, `ListeningAttemptNote`). 2 enum extensions (`ListeningAttemptMode.Diagnostic`, `ListeningDistractorCategory.OutOfScope`).
- **`ListeningV2BackfillService`** — `BackgroundService` with idempotency key in `Settings` table, seeds default FSM state for in-progress attempts, populates `LastQuestionVersionMapJson` from existing `ListeningAnswer` rows.
- **Service extraction strategy** — `ListeningLearnerService.cs` (2400+ LOC) splits into: `ListeningSessionService` (FSM + advance + audio-resume), `ListeningGradingService` (version-pinned grading, raw→scaled via `OetScoring.OetRawToScaled` only), `ListeningAnalyticsService` (per-Part + per-skill-tag aggregation), `ListeningPathwayProgressService`, `TeacherClassService`. Original learner service kept for DTO projections.
- **FSM transitions** — single source of truth `Services/Listening/ListeningFsmTransitions.cs` (backend) mirrored by `lib/listening/transitions.ts` (frontend); kept in sync by a generated-code parity test.
- **Two-step confirm-token protocol** — `/advance` first call returns `412 PreconditionRequired` with HMAC-signed token `{attemptId, fromState, toState, expiresAt}` valid for `ConfirmTokenTtlMs = 30s`; second call must echo. Prevents R06.10 races.
- **`paywalled` FSM state** — emitted instead of `402` mid-attempt; autosave still allowed on existing in-progress rows. Only `POST /v1/listening/attempts` returns 402 for new attempts.

### Designer (UI/UX spec, 12 surfaces)

- **CbtPlayer / PartARenderer / BCQuestionRenderer** — locked-section visual using greyed-out part chip; `<SectionAdvanceConfirm/>` modal with embedded R06.11 unanswered list; `<UnansweredQuestionsBanner/>` sticky on `*_review` states; `<ZoomControls/>` top-right (0.85 / 1 / 1.15 / 1.30); Tab-key cursor flow in Part A; B/C right-click strikethrough with `Shift+F10` keyboard parity and SR-only "Strike out option" buttons; visual conflict resolution: yellow stem highlights stay even if option struck.
- **PaperModePlayer** — top section nav strip with free clickable parts; sticky red banner for R07.3 final 2-min review; optional `window.print()` view with bubble-sheet CSS.
- **OetHomePlayer** — CBT + fullscreen wrapper + tab-focus-loss toast on `document.visibilitychange` / `fullscreenchange`; telemetry pulse only (no penalty this pass).
- **LearningModePlayer** — replay controls 0.75x/1x/1.25x/1.5x; right-side collapsible transcript with answer-evidence highlighted green; left-side note-taking panel persisted via `ListeningAttemptNote`.
- **DiagnosticPlayer** — 10–15 sampled Qs across A/B/C; per-Part mini-result; routes to recommended pathway stage via `ListeningPathwayProgressService.RecomputeAsync`.
- **TechReadinessCheck** — 4-step gate (resolution → display scale → audio output device → sample-tone); each fail blocks "Begin Exam"; "Skip with warning" logs override + flags attempt.
- **LearnerDashboard / TeacherDashboard / AdminQuestionDeepDive / PathwayBoard** — designs ready in archived report; LearnerDashboard hero card uses scaled score + estimate-disclaimer pill; TeacherDashboard table shows "you own this class" badge to make ownership visible.
- **Motion** — `motion/react` only (NOT framer-motion per AGENTS.md); no motion during `*_audio` states; subtle 200ms fade on transitions.
- **Accessibility** — `role="alertdialog"` on confirm modal with focus trap; SR announcements for state transitions, audio cues (text-only), unanswered counts.

### Cut list (planner §9, P0-only for "rulebook-faithful CBT")

P0 (mandatory for v2.0):

- WS-E0 reconciliation + AI feature code reservation ← **DONE THIS WAVE**
- WS-A schema migration + `ListeningV2BackfillService`
- WS-B `ListeningSessionService` + `ListeningGradingService` (version-pinned)
- WS-B 5 mode policies (`Cbt`, `Paper`, `OetHome`, `Learning`, `Diagnostic`)
- WS-C `CbtPlayer` + `BCQuestionRenderer` (R06 + R08)
- WS-G xunit grading tests + Vitest CbtPlayer test + Playwright R06.1-R06.11 locks E2E
- WS-H `docs/LISTENING-RULEBOOK-CITATIONS.md` mapping every R01–R14 to file + test

P1 (defer if budget tight): `PaperModePlayer`, `LearningModePlayer`, `LearnerDashboard`, `PathwayBoard`, `TeacherDashboard`, `AdminQuestionDeepDive`.

P2 (explicit v2.1): CAT/IRT diagnostic adaptivity, `OetHomePlayer` fullscreen *enforcement* (logged-only this pass), authored content extraction beyond current PdfPig pipeline.

### Dependency DAG (locked)

```text
WS-E0 → WS-A → (WS-B ∥ WS-C-skeleton) → (WS-D ∥ WS-F) → WS-G → WS-H
              ▲                          ▲
              │                          │ (parallelisable internally)
              └─── strict (B needs A columns; C consumes B contracts)
```

### Artefacts archived

- Planner full report: `chat-session-resources/...toolu_01SvJwait7QY5eH3HxLpy8YH...content.txt` (38KB)
- Designer full report: `chat-session-resources/...toolu_01GmQL5zk6isVJxvjLrVoVgj...content.txt` (45KB)

Both should be re-read at the start of the next session before WS-A migration work begins.

---

## Wave 1 — Research (complete, 2026-05-11)

3 read-only specialists ran in parallel. Full reports archived to chat-session-resources.

### Researcher

- `ListeningEntities.cs` ALREADY contains: `SkillTag` (string), `ListeningDistractorCategory` enum (5 values), `AccentCode`, `SpeakersJson`, `TranscriptSegmentsJson` (time-coded), `ListeningAttemptMode` (7 values incl. `Home`, `Paper`). PRD §5 was substantially redundant.
- `ListeningLearnerService.cs` is monolithic (~2400 lines). Needs extraction into `ListeningSessionService` + `ListeningGradingService` before WS-B work.
- `LearnerQuestionDto` at `ListeningLearnerService.cs:1862` may leak `IsCorrect` / `WhyWrongMarkdown` via raw `q.Options` projection — verify + regression test.
- 12-stage `ListeningCurriculumService` already exists but uses aggregated attempts (no dedicated progress table).
- R01.7 partial client enforcement only; R02.4 fully enforced; R06–R10 essentially MISSING.
- Existing 1200-line `app/listening/player/[id]/page.tsx` handles all modes inline — needs decomposition.

### Architect

- Canonical FSM with two-step confirm-token protocol prevents R06.10 double-tap race.
- Server stamps `WindowStartedAt + WindowDurationMs`, emits immutable `audio_timeline[]`; client only renders.
- Audio resume protocol: `/attempts/{id}/audio-resume?cue=…` with force-advance on grace exceedance.
- 10 platform-derived R-codes identified (R02.estimate-disclaimer, R06.confirm-token, R07.final-review-server-stamp, R08.a11y-context-menu, R09.audio-stall, R10.tech-readiness-ttl, R11.pathway-unlock, R12.diagnostic-skip, R13.annotations-persistence, R14.clock-drift).
- BCQuestionRenderer interaction matrix solves stem-highlight vs option-strikethrough conflict; SR fallback via `<button aria-label="Strike out">`.

### Critic (CRITICAL findings — PRD updated)

1. **PRD §5 partially redundant** → §5 rewritten with §5.1 KEEP-AS-IS / §5.2 TRUE ADDITIONS / §5.3 DEFERRED / §5.4 WS-E0 reconciliation pre-step.
2. **jsonb on SQLite** untranslatable → all jsonb columns mapped as `string?` with `HasColumnType("jsonb")` Npgsql-only; never LINQ-into.
3. **Question/option edits invalidate in-flight grading** → added `Version` int + `*VersionSnapshot` on Answer + `LastQuestionVersionMapJson` on attempt.

### Critic HIGH findings (PRD §6 updated)

- AI tagging bypass → pre-declared `AiFeatureCodes.AdminListeningSkillTag` + `AdminListeningTranscriptSegment` + rulebook file requirement.
- Diagnostic adaptive ambiguity → **fixed-form placement** v2; CAT/IRT deferred to v2.1.
- Teacher class authz → OWASP A01 mitigation; `OwnerUserId` filter + Playwright authz test mandatory.
- Paywall mid-attempt → new `paywalled` FSM state; autosave grace on existing in-progress attempts.

### Wave 1 checkpoint: GO

PRD updated. Wave 2 cleared to proceed.

## Wave 0 — Setup (2026-05-11)

- ✅ Clarification popup answered (see `PRD-LISTENING-V2.md` §0)
- ✅ Todo tracker created (10 items)
- ✅ `PRD-LISTENING-V2.md` scaffolded
- ✅ `PROGRESS-LISTENING-V2.md` created
- ⏭ Next: Wave 1 — three parallel read-only research subagents

## Wave 1 — Research (pending)

Goal: map existing Listening surface area; identify exact files/symbols every R-code will touch; produce a delta list against the schema in `PRD §5`.

- [ ] `agency-researcher` — enumerate existing Listening files, symbols, endpoints, tests
- [ ] `agency-architect` — produce target state machine + R-code → enforcement-site mapping
- [ ] `agency-critic` — challenge plan: hidden assumptions, edge cases (network drop mid-extract, browser refresh in CBT lock, etc.)

## Wave 2 — Plan + Design (pending)

- [ ] `agency-planner` — phased execution plan with file-level diffs grouped by workstream
- [ ] `agency-designer` — UI spec for CbtPlayer, PaperPlayer, dashboards, pathway, admin deep-dive

## Wave 3 — Implementation (pending, 4 parallel workstreams)

- [ ] WS-A backend schema/migrations/policy defaults
- [ ] WS-B backend nav state machine + grading + dashboard APIs
- [ ] WS-C frontend CBT player
- [ ] WS-D frontend Paper/OET@Home/Learning/Diagnostic + dashboards + pathway

## Wave 4 — Review + Debug (pending)

- [ ] `agency-reviewer` — independent code review
- [ ] `agency-debugger` — exercise CBT lock + audio cue + autosave edge cases

## Wave 5 — Devops + Docs (pending)

- [ ] `agency-devops` — migration safety review, rollback plan
- [ ] `agency-docs` — `docs/LISTENING.md` + `docs/LISTENING-RULEBOOK-CITATIONS.md`

## Final validation (pending)

- [ ] tsc, lint, vitest, dotnet test, next build
- [ ] Playwright E2E (CBT locks, paper free-nav, highlight tools, readiness gate)
- [ ] R-code citation checklist 100%
