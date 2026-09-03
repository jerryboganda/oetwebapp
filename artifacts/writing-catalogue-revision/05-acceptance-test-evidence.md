# Acceptance Test Evidence

Commands (Windows host, repo root `OET Project Web App`):
- `dotnet build backend/OetLearner.sln -p:UseSharedCompilation=false` → 0 errors.
- `dotnet test … --filter "FullyQualifiedName~WritingLetterTypeTaxonomyTests|…WritingPackLetterTypeTests|…WritingExamClosureTests|…WritingTaskUnderstandingTests|…WritingLearnerPathwayServiceTests|…WritingScenarioListTests"` → **97/97 pass**.
- `pnpm exec tsc --noEmit` → clean. `pnpm exec eslint` on 6 touched frontend files → 0 errors (4 pre-existing warnings).
- `pnpm vitest run lib/writing/letter-type-taxonomy.test.ts lib/writing/zod.test.ts lib/rulebook/context.test.ts` → all pass (incl. 7 new). Component suites (`WritingTaskBuilder`, admin analytics) → 21/21 pass.
- Pristine-HEAD baseline worktree (`d62fc13c0`): all 19 sampled failures reproduce without this change → pre-existing.

| Test | Expected | Actual | Status | Evidence |
|---|---|---|---|---|
| WR-DIFF-01 web library: no Difficulty | no filter | Profession+LetterType+Search only (`library/page.tsx:124-168`) | PASS | code inspection + tsc |
| WR-DIFF-02 mobile | same (shared Next.js via Capacitor; no separate mobile writing UI) | `capacitor-web/src` has no writing UI; `android/` is a native shell | PASS | grep (0 hits) |
| WR-DIFF-03 cards: no Level badges | letterType+profession badges only | `library/page.tsx:195-199` | PASS | inspection + search (0 `Level [1-5]` hits in catalogue) |
| WR-DIFF-04 no Level 1-5 presentation dependency | none | repo-wide grep: zero catalogue hits | PASS | grep |
| WR-DIFF-05 Profession+LetterType+Search regression | work | filters compose into single `ListScenariosAsync` query; 97-set green | PASS | tests |
| WR-RP-01/02 Response absent every profession | absent | zero active producers (grep); UI never had it; backend arms removed | PASS | grep + 36 taxonomy tests |
| WR-RP-03 Medicine→Discharge shows Isabel Garcia | LT-DG row | migration statement 1 (verified at deploy; local DB unavailable) | PASS (code) / VERIFY-AT-DEPLOY (data) | migration file + query in 06 |
| WR-RP-04 zero active LT-RP rows | 0 | migration statement 2 + gate; verified at deploy | PASS (code) / VERIFY-AT-DEPLOY (data) | same |
| WR-RP-05 classifier cannot emit LT-RP | impossible | normalize→OT; onboarding rejects; prompt bans; `SaveOnboarding` test posts RP→stored OT | PASS | service test |
| WR-TAX-01 valid set excludes RP | excluded | 6-code set; `IsValid(LT-RP)=false` | PASS | taxonomy tests |
| WR-TAX-02 includes Other Letters | present | `LT-OT` + label `Other Letters` | PASS | frontend (7) + backend tests |
| WR-TAX-03 every profession exposes OT | all 13 | `writingLetterTypesForProfession` per-profession test; vet excludes only NM | PASS | frontend tests |
| WR-TAX-04 All is filter-only | rejected as stored value | `IsValid("all")=false`; zod rejects `['all']` | PASS | backend + frontend tests |
| WR-CLS-01..05 known mappings | RR/UR/DG/TR/NM | theory rows incl. legacy aliases | PASS | taxonomy tests |
| WR-CLS-06/07 ambiguous/unsupported → OT | OT | null/empty/unknown/gp-referral rows | PASS | taxonomy + import theory |
| WR-CLS-08 response-like → never RP | OT | RP/RESPONSE/UPDATE/REPLY/advice rows | PASS | taxonomy + import theory + onboarding test |
| WR-DATA-01 Isabel Garcia → LT-DG | Medicine/LT-DG | migration | PASS (code) / VERIFY-AT-DEPLOY (data) | migration + 06 |
| WR-UI-01 filters Profession+LetterType+Search | exact stack | library page | PASS | inspection |
| WR-UI-02 no Response in menu | absent | all menus enumerate 6-code set | PASS | grep + tests |
| WR-UI-03 Other Letters in menu | present | library/showcase/focus/admin/builder | PASS | tests + inspection |
| WR-UI-04 no Level badges | none | cards | PASS | inspection |
| WR-UI-05/06 mobile/web parity | same code path | Capacitor wraps same Next.js app | PASS | architecture |
| WR-REG-01..06 profession/type/search/count/practice/model-answer | preserved | single composed indexed query (`Profession,LetterType` index reused); migration touches only `LetterType`; ModelAnswer FK (`ScenarioId`) untouched; 97-set green | PASS | tests + inspection |

## Known pre-existing failures (NOT this change; baseline-verified)

`CriticalFlowsTests.WritingRevision_*` / `WritingSubmission_*` (409/entitlement
integration), `WritingAssessmentPreflightTests` (4× missing-`AuthorId`
fixtures), `LearnerSpecRegressionTests` legacy-grading expectations
(`writing_v11_required`), `WritingAssessmentV11RuleEngineTests` re-line data
drift, `VideoVisibilityScopeAdminTests`, `LearnerServicePerformanceTests`.
Additionally a concurrent peer session is actively fixing/retesting nearby
areas (its GroupBy fix already turned the 3 `WritingScenarioListTests`
failures green); suite totals fluctuate while both sessions run `dotnet test`
against the same `bin/` — final numbers must be re-taken on a quiet tree
(see 07).
