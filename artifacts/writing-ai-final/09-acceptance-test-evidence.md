# 09 — Acceptance Test Evidence

Backend: `dotnet test … --filter "FullyQualifiedName~Writing|Rulebook|WritingEngineParity"`
Frontend: `pnpm vitest run lib/writing lib/rulebook`. Full logs in CI.

| Test | Expected | Actual | Status | Evidence |
|---|---|---|---|---|
| A catalogue complete, uncertain → LT-OT | no exclusion | taxonomy normalizes unknown/retired → LT-OT; import mapping same | PASS | `WritingLetterTypeTaxonomyTests`, `Import_maps_retired_response…` (other actor), `WritingPackLetterTypeTests` |
| B Other Letters every profession | exposed | universal fallback; builder/library/focus list LT-OT; vet only excludes LT-NM | PASS | code trace + `letter-type-taxonomy.test.ts` (7) |
| C long response | 1 job, valid result | unchanged happy path | PASS | `EvaluateAsync_CanonicalCriteriaScores_MapsGradeWithoutFabrication` |
| D short response | allowed, assessed | no minimum anywhere (contract/zod/pipeline) | PASS | code trace + blank/short path |
| E one-line response | allowed, low score | graded normally by AI | PASS | no-minimum chain (same as D) |
| F empty response | valid zero assessment, no validation error | deterministic zero, 0 provider calls, 0 credit hold | PASS | `EvaluateAsync_BlankLetter…`, `CreateSubmissionAsync_EmptyLetter…`, preflight empty test |
| G canonical input, no live OCR | snapshots only | no OCR/PDF ref in grading path | PASS | code trace |
| H unreadable eliminated | prepared tasks grade | gate + extract-from-pdf + bridge; fail-closed preserved for unprepared | PASS | preflight tests (3 new), gate tests |
| I grading sources, no exemplar scoring | case+task+criteria+rulebook | canary test: rubric input lacks Model Answer text | PASS | `EvaluateAsync_GradingInput_NeverContainsModelAnswerText` |
| J pre-generated Model Answer | exists before release | publish gate `model_answer_not_approved` | PASS | gate tests |
| K reuse across candidates | same saved version | pregen copy path (both blank + normal) | PASS | code trace + batch tests |
| L no regen on normal submit | 0 generation calls | repeat batch runs add 0 calls; submit path prefers Ready+visible | PASS | `GenerateMissing_*` |
| M one submit / one job | 1 logical job | content guard + claim + reuse | PASS | double-create test (1 row, 1 call) |
| N double tap | 1 job | same as M | PASS | same test |
| O network retry | reuse existing | same key → same row; different key + same content → same row | PASS | double-create test |
| P concurrent duplicates | 1 execution | claim fencing + reservation idempotency (live 10-way NOT run here — post-deploy check) | PARTIAL | unit tests; live check scripted in 06 |
| Q provider 429 | backoff, no double charge | gateway max-3 + shared reservation; release-on-failure | PASS | code trace (`AiGatewayService`, `AiCreditReservationService`) |
| R provider down | letter preserved, resumable | row-before-grade + `failed` + `retry-grade` + grading-page retry card | PASS | code trace + new endpoint |
| S exemplar quality | grounded, approved | 180–200 words + grounding validator + approve gate; 26 legacy bodies corrected | PASS | batch tests + rulebook diff |
| T practice AI-only | no tutor selector | untouched | PASS | code trace (no tutor UI added) |

Totals this change: backend writing+rulebook selection 216 tests → 213 pass,
3 pre-existing HEAD failures (V11RuleEngine re_line ×2, FactMap ×1 — proven
identical on pristine HEAD worktree, §11); frontend lib/writing + lib/rulebook
1526 pass, 6 pre-existing snapshot failures (`pdf-policy-release/` stale copy,
untouched).
Pre-existing suite-wide reds unrelated to Writing (legacy V1 attempt routes,
video visibility, learner-service perf) also proven identical at HEAD.
