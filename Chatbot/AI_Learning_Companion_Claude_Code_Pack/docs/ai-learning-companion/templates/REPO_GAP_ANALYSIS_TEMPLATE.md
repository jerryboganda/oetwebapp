# Repository Gap Analysis — AI Learning Companion

> Generated/maintained from the real repository. Do not fill this with assumptions.

## 1. Repository snapshot

- Commit / branch:
- Monorepo/workspaces:
- Frontend stack:
- Backend/API stack:
- Database/ORM:
- Authentication/session:
- Existing learner/profile domain:
- Existing exam/profession domain:
- Course/content domain:
- Entitlement/access-control service:
- Payments/subscriptions:
- Existing AI Credits/wallet:
- Existing Writing/Speaking/Reading/Listening assessment services:
- AI/model providers:
- Vector/search/RAG services:
- Object/file storage:
- Queues/workers/schedulers:
- Notifications:
- Analytics/observability:
- Admin/support tooling:
- Mobile/desktop wrappers:
- CI/CD/deployment:

## 2. Existing capabilities that MUST be reused

| Capability | Existing module/path | Source of truth | Gaps | Integration decision |
|---|---|---|---|---|
| Identity/auth | | | | |
| Learner profile | | | | |
| Course/content | | | | |
| Entitlements | | | | |
| Payments | | | | |
| AI Credits | | | | |
| Assessments | | | | |
| Analytics | | | | |
| Notifications | | | | |
| Support | | | | |

## 3. Feature-by-feature audit

Allowed status values: `EXISTS`, `PARTIAL`, `MISSING`, `BLOCKED`, `DEFERRED_BY_SOURCE`, `NOT_APPLICABLE_WITH_REASON`.

| ID | Requirement | Status | Existing paths/modules | Missing work | Stage | Dependencies | Acceptance evidence |
|---|---|---|---|---|---|---|---|
| F-001 | | | | | | | |

> Populate all rows F-001 through F-184 from `traceability/features.json`; never stop at a sample.

## 4. Cross-cutting gaps

### Security / entitlement

### Data migrations / backwards compatibility

### RAG / source authority

### Memory / provenance

### Billing / credits / unit economics

### Privacy / deletion / export

### Accessibility / RTL

### Observability / cost attribution

### Incident response / kill switches

### QA / golden sets / red-team

### Content operations

## 5. TO VERIFY blockers found in the real repository

| Decision/gate | Current evidence | Technical plumbing ready? | Production behavior | Owner required | Next action |
|---|---|---|---|---|---|

## 6. Contradictions with the specification

For each contradiction, quote the repository behavior/configuration and identify the affected feature IDs. Do not silently choose one side.

## 7. Stage 0 recommendation

Dependency-ordered work only; no speculative rewrite.
