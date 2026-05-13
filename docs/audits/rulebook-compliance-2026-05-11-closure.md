# Rulebook Compliance — 2026-05-11 / 2026-05-12 Closure Addendum

> Date: 2026-05-12
> Source audit: [`rulebook-compliance-2026-05-10.md`](./rulebook-compliance-2026-05-10.md)
> Status: closed for v1 launch (with two doc-level deferrals tracked below)

## Why this doc exists

The 2026-05-10 audit was filed before two big closure waves landed on `main`:

1. `9d8003c feat(launch): complete writing compliance and release gates` — replaced the Writing AI grader stub with `WritingEvaluationPipeline`, wired `WritingRuleEngine`, rewrote `WritingCoachService`, added the Writing E2E spec.
2. `824f638 fix(rulebook): apply Momus review fixes (B1, F1-F6) + hybrid grader tests` — full critical+major hybrid grader, reading enforcement, letter-type SSO.
3. `3916e94 fix(production): harden rulebook grading deployment blockers` — production hardening for prompt-compliant `passRequires` JSON, mixed-provider AI gateway selection, and Reading Part-A no-resume break expiry.

The original audit body therefore reads as if every P0/P1 item is unresolved. This addendum reconciles the audit against `main` and applies the residual P2/P3 doc-level closures.

## What was closed where

| Audit item | Severity | Closure | Evidence |
| ---------- | -------- | ------- | -------- |
| P0-1 | P0 | ✅ Resolved on `main` | `9d8003c` — `WritingEvaluationPipeline.cs` (846 lines), routed via `IAiGatewayService`, scored via `OetScoring.GradeWriting`, audited via `IAiUsageRecorder`. |
| P1-1 | P1 | ✅ Resolved on `main` | `9d8003c` — `WritingRuleEngine.Evaluate` invoked synchronously in pipeline; deterministic findings merged with AI; 41/41 detectors locked. |
| P1-2 | P1 | ✅ Resolved on `main` | `824f638` — Hybrid grader (deterministic-first, AI-only-for-semantic). Coverage matrix in `lib/rulebook/writing-coverage.ts`. |
| P1-3 | P1 | ✅ Resolved on `main` | `d144967` — Letter-type SSO from canonical TS const; display labels in a separate map. |
| P2-1 | P2 | ✅ Resolved on `main` | `9d8003c` — `WritingCoachService` rewritten to delegate to `WritingRuleEngine` + AI gateway. |
| P2-2 | P2 | ✅ Resolved on 2026-05-13 | First-class `WritingRuleViolation` table, analytics endpoint, migration, and tests are live. |
| P2-3 | P2 | ✅ Resolved on `main` | `9d8003c` — `tests/e2e/learner/writing-rule-engine-violations.spec.ts` (38 tests across 6 projects). |
| P2-4 | P2 | ✅ Resolved on 2026-05-12 | New academic-integrity reminder rendered in `app/mocks/page.tsx` and `app/reading/paper/[paperId]/page.tsx` pre-attempt block. |
| P2-5 | P2 | ✅ Resolved on 2026-05-12 | Literal disclaimer string appended to `app/reading/paper/[paperId]/results/page.tsx` aside (non-practice attempts only). |
| P3-1 | P3 | ✅ Resolved on 2026-05-12 | `ReadingZoomControls` exposes hint via `aria-describedby` + `title` + visually-hidden caption. |
| P3-2 | P3 | ⚪ Out of code scope | Content-quantity targets tracked operationally. |
| P3-3 | P3 | ✅ Resolved on 2026-05-13 | Writing drill loader now validates coverage for all 12 canonical professions across all 6 drill types; seed abbreviation files plus generated profession-specific drills are locked by Vitest. |

## 2026-05-12 changes applied here

1. `docs/audits/rulebook-compliance-2026-05-10.md` — appended a Resolution column to every P0/P1/P2/P3 row.
2. `app/reading/paper/[paperId]/results/page.tsx` — added the literal disclaimer `"This is an estimate, not an official OET conversion."` (P2-5).
3. `app/reading/paper/[paperId]/page.tsx` — added the academic-integrity reminder banner on the pre-attempt block (P2-4) and the in-app-zoom hint on `ReadingZoomControls` via `aria-describedby` + visible tooltip + sr-only caption (P3-1).
4. `app/mocks/page.tsx` — added the academic-integrity reminder under the page hero (P2-4).
5. `docs/audits/WRITING-DRILL-PROFESSION-COVERAGE.md` — updated from schedule to closure ledger for the 12-profession drill bank (P3-3).
6. `docs/audits/rulebook-compliance-2026-05-11-closure.md` (this file) — closure manifest.

## Residual items tracked but not closed in code

- No P2/P3 rulebook-compliance code gaps remain from this closure set. Future content expansion can add additional drill variants, but every canonical profession now has at least one validated drill per type.

## Validation

| Check | Command | Result |
| ----- | ------- | ------ |
| Type-check | `npx tsc --noEmit` | reported under Track D in `docs/CLOSURE-2026-05-12.md` |
| Vitest (Reading + Mocks landing) | `npx vitest run app/reading app/mocks lib/rulebook` | reported under Track D |
| Lint | `npm run lint` | reported under Track D |
| Visual check | Manual: Reading results page renders the literal disclaimer for a non-practice attempt | scheduled for production smoke after deploy |

## Cross-links

- [`docs/CLOSURE-2026-05-12.md`](../CLOSURE-2026-05-12.md) — top-level manifest for the May 2026 closure.
- [`docs/STATUS/writing-rulebook-compliance-audit-2026-05-10.md`](../STATUS/writing-rulebook-compliance-audit-2026-05-10.md) — backend-side closure record from `9d8003c`.
- [`docs/WRITING-RULEBOOK-PROGRESS.md`](../WRITING-RULEBOOK-PROGRESS.md) — Writing rulebook 13-task delivery log.
