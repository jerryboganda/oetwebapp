# Implementation Plan — AI Learning Companion

## Program rules

- Integration-first; existing project truths win unless an approved refactor is necessary.
- Every work item references feature IDs.
- `TO VERIFY` values become configurable gates, not guessed constants.
- Vertical slices include data/backend/auth/UX/telemetry/tests as applicable.
- Risky features are feature-flagged with rollback.

## Architecture decision summary

- Identity/profile integration:
- Entitlement boundary:
- Credit ledger integration:
- Knowledge/RAG boundary:
- Memory/event model:
- Action/deep-link model:
- AI provider/routing abstraction:
- File/multimodal pipeline:
- Voice architecture:
- Observability/cost pipeline:
- Privacy/deletion/export:
- Admin/content-ops:

## Migration strategy

| Migration | Purpose | Backward-compatible? | Backfill | Rollback | Feature IDs |
|---|---|---:|---|---|---|

## Stage 0 — Validation and foundations

| Work package | Feature IDs / source requirement | Dependencies | Files/services | Tests/evidence | Owner | Status |
|---|---|---|---|---|---|---|

### Stage 0 exit gate

- [ ] repository gap analysis complete
- [ ] source schemas mapped to real schema/migrations
- [ ] content inventory pipeline started
- [ ] entitlement and credit invariants demonstrated
- [ ] evaluation harness skeleton exists
- [ ] request/cost/latency tracing plan works
- [ ] feature flags/kill switches exist
- [ ] external `TO VERIFY` register is current

## Stage 1 — Monetisable OET Core

| Vertical slice | Feature IDs | Backend/data | UX | Security | Telemetry | Tests | Status |
|---|---|---|---|---|---|---|---|

## Stage 2 — Learning Moat

| Vertical slice | Feature IDs | Dependencies | Acceptance evidence | Status |
|---|---|---|---|---|

## Stage 3 — Voice & Ultimate Mentor

> Cannot move to production until voice/Arabic feasibility, unit-economics, calibration, privacy/consent and safety gates pass.

| Vertical slice | Feature IDs | Gate | Acceptance evidence | Status |
|---|---|---|---|---|

## Stage 4 — Multi-Exam

| Work package | Feature IDs | Core abstraction reused | Acceptance evidence | Status |
|---|---|---|---|---|

## Stage 5 — B2B

| Work package | Feature IDs | Tenant isolation evidence | Acceptance evidence | Status |
|---|---|---|---|---|

## Dependency-critical path

1.
2.
3.

## Risks / rollbacks

| Risk | Trigger | Mitigation | Kill switch / rollback | Owner |
|---|---|---|---|---|

## Definition of done per feature

A feature may be marked complete only when all applicable layers are complete: schema/data, backend/domain logic, authorization/entitlement, UI/UX, telemetry/cost, tests, accessibility, privacy/security, documentation, migration/rollback and traceability evidence.
