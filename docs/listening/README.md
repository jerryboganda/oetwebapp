# Listening module — engineering overview

This folder documents the OET Listening exam-prep module: domain, services,
endpoints, frontend, and the cross-skill integrations (Mocks, Pathway,
Teacher Classes). For the **product** requirements see `PRD-LISTENING-V2.md`
at repo root; for **rollout state** see `PROGRESS-LISTENING-V2.md`.

## What ships today

| Surface | Path | Notes |
|---|---|---|
| Learner hub | [`app/listening/page.tsx`](../../app/listening/page.tsx) | Published papers, drills, pathway, mocks entry |
| FSM player | [`app/listening/player/[id]/page.tsx`](../../app/listening/player/[id]/page.tsx) | 5-part (A1/A2/B/C1/C2) state machine, one-play exam mode |
| Diagnostic | [`app/diagnostic/listening/page.tsx`](../../app/diagnostic/listening/page.tsx) | Placement test → pathway recommendation |
| Review page | [`app/listening/review/[id]/page.tsx`](../../app/listening/review/[id]/page.tsx) | Post-submit transcript-evidence replay |
| Admin authoring | [`app/admin/content/listening/page.tsx`](../../app/admin/content/listening/page.tsx) | Paper list + structure CRUD |
| Admin analytics | [`app/admin/analytics/listening/page.tsx`](../../app/admin/analytics/listening/page.tsx) | Class accuracy, distractor heatmap |
| Mock player binding | [`app/mocks/player/[id]/page.tsx`](../../app/mocks/player/[id]/page.tsx) | First subtest in full mocks; audio-readiness gate |

## Backend architecture

- **Domain**: [`Domain/ListeningEntities.cs`](../../backend/src/OetWithDrHesham.Api/Domain/ListeningEntities.cs) — 5-part parts, extracts, questions, options, attempts, answers, policy, overrides, pathway progress, teacher classes.
- **Services**: [`Services/Listening/*`](../../backend/src/OetWithDrHesham.Api/Services/Listening/) — `ListeningLearnerService` (~125 KB), `ListeningSessionService` (FSM), `ListeningGradingService`, `ListeningAnalyticsService`, `ListeningAuthoringService`, etc.
- **Endpoints**: [`Endpoints/Listening*.cs`](../../backend/src/OetWithDrHesham.Api/Endpoints/) — learner (`/v1/listening-papers/...`), V2 FSM (`/v1/listening/v2/...`), admin authoring (`/v1/admin/papers/{id}/listening/...`), admin analytics.
- **Migrations**: `20260430071438_AddListeningModuleEntities.cs`, `20260505224742_AddListeningExtractionDraft.cs`, `20260511110000_Listening_V2_Schema.cs`, `20260521210000_AddListeningAnswerMissReason.cs`.

## Scoring path — invariant

> Scaled scores ALWAYS go through `OetScoring.OetRawToScaled`.
> Inline math (`* 350`, `/ 42`, `* 500`, `* 8.33`) anywhere in
> `backend/src/OetWithDrHesham.Api/Services/Listening/` fails CI via
> [`ListeningScoringPathAuditTest`](../../backend/tests/OetWithDrHesham.Api.Tests/Listening/ListeningScoringPathAuditTest.cs).

## Related docs

- [`grader.md`](./grader.md) — Part A short-answer matching rules + MissReason taxonomy.
- [`exam-modes.md`](./exam-modes.md) — Paper / Computer / OET@Home presentation modes.
- [`authoring.md`](./authoring.md) — content-author workflow, transcript format, TTS pipeline.
