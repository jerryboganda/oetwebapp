# Mocks Module PRD

> Date: 2026-05-07  
> Owner: Platform Engineering  
> Scope: OET mock exam authoring, learner delivery, reports, bookings, admin operations, and expert review integration.

## Purpose

The Mocks module must let admins assemble authentic OET-style mock exams from published platform content, and let learners take those mocks through a guided, exam-faithful flow that produces clearly labelled practice reports. Mocks orchestrate existing skill modules; they do not bypass content publish gates, scoring services, rulebooks, AI grounding, storage, or learner-safe DTO projections.

## Current Flow

- Admin content papers are authored and published through `ContentPaper` and related assets.
- Admins create `MockBundle` rows under `/admin/content/mocks`, then attach ordered `MockBundleSection` rows that reference published `ContentPaper` records.
- Learners browse `/mocks`, configure a bundle in `/mocks/setup`, start `/v1/mock-attempts`, launch each section through `/mocks/player/[id]`, and view `/mocks/report/[id]` after report generation.
- Productive-skill tutor review can reserve wallet credits, and bookings/live-room concepts exist for scheduled speaking or final-readiness mocks.

## Target Flow

1. Admin authors each Listening, Reading, Writing, and Speaking paper through the canonical paper CMS.
2. Admin creates or edits a mock bundle with type, profession scope, provenance, source/QA state, release policy, watermark/randomisation settings, and ordered sections.
3. Publish is blocked until the bundle has the correct section shape, published/provenanced papers, compatible professions, and required OET section order.
4. Learner chooses a mock type, profession, target country when Writing is included, strictness, delivery mode, and review options.
5. Backend creates one `MockAttempt` and a `MockSectionAttempt` per bundle section.
6. Each section launches the matching skill workspace with the mock attempt and section identifiers.
7. Section completion is treated as trusted only when it links to owned, server-side skill evidence. Client-side proctoring remains advisory.
8. Submitting a mock requires all required sections to be complete before report generation queues.
9. Reports aggregate per-subtest scores, review states, timing, proctoring, booking advice, remediation, and a practice Statement of Results only when final scores are present.

## Functional Requirements

- Admins can create, edit, archive, publish, add sections, and reorder sections for mock bundles from the admin UI.
- Admin section attachment uses searchable published `ContentPaper` choices rather than blind raw IDs.
- Learners can resume in-progress mock attempts and cannot submit a multi-section mock until every required section is complete.
- Writing pass/readiness decisions remain country-aware and must route through canonical scoring services.
- Pending Writing/Speaking review must keep results provisional; pending sections must never render as zero-score official-style rows.
- Learner booking reschedule/cancel flows must not allow learner-controlled terminal status escalation.
- Live-room state is server-audited and must never expose tutor-only/interlocutor-only material to learners.
- Admin operations can inspect analytics, risk signals, bookings, and item analysis without exposing learner answer keys.

## Mission-Critical Constraints

- Scoring: all raw-to-scaled and pass/fail logic routes through `OetScoring` or `lib/scoring.ts`.
- Content: mock bundles reference `ContentPaper`; raw files are never stored directly by mock code.
- Reading: learner DTOs never expose `CorrectAnswerJson`, explanations, or accepted synonyms before allowed release.
- Writing/Speaking/AI: all AI feedback remains grounded through the AI gateway and rulebook APIs.
- Statement of Results: use `OetStatementOfResultsCard` through the adapter only, keep the practice disclaimer, and do not restyle the card.
- Security: proctoring is advisory, rate-limited, and never an automatic pass/fail signal.

## Acceptance Criteria

- Given an admin opens `/admin/content/mocks`, when they select a draft bundle, then they can edit metadata and save through `PUT /v1/admin/mock-bundles/{id}`.
- Given a bundle has multiple sections, when an admin moves a section up or down, then the UI calls the reorder endpoint with every section id exactly once and refreshes the list.
- Given a learner starts a multi-section mock, when only one section is complete, then `POST /v1/mock-attempts/{id}/submit` returns a validation error and does not queue a report.
- Given a learner sends a generic booking PATCH with `status=completed`, then the backend rejects it and preserves the existing booking status.
- Given a report has pending/non-numeric subtest scores, then the learner report shows a provisional warning and does not render the Statement of Results card.
- Given a report has four final numeric subtest scores, then the Statement of Results card renders with the practice label and supplied profession/country context when present.
- Given remediation generation reads a current report payload, then it derives weaknesses from `subTests`/`subtestScores` using `OetScoring` constants and remains idempotent.

## Non-Goals

- Building a new content storage layer for mocks.
- Replacing Reading, Listening, Writing, or Speaking grading engines.
- Automating official OET result claims.
- Adding camera-based or biometric proctoring.
- Reworking billing packages beyond existing review-credit and entitlement hooks.

## Source Documents

- `docs/MOCKS-MODULE-PLAN.md`
- `docs/MOCKS-MODULE-PLAN-V2.md`
- `docs/CONTENT-UPLOAD-PLAN.md`
- `docs/SCORING.md`
- `docs/OET-RESULT-CARD-SPEC.md`
- `docs/READING-AUTHORING-PLAN.md`
- `docs/LISTENING-MODULE-PLAN.md`
- `docs/speaking/PRD.md`
