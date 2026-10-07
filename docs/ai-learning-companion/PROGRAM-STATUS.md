# SAMI Program Status — running state (updated 2026-10-07)

Authoritative running state for the SAMI 100% implementation program. Baseline:
`source/SAMI_FINAL_PRODUCTION_HANDOVER_1.0_FINAL.md`. Decisions: `DECISION_LOG.md`
(D-001..D-007). Feature-level truth: `traceability/features.{json,csv}`.

## Shipped and verified live (with evidence)

| Wave | Delivered | Evidence |
|---|---|---|
| 0 | SAMI baseline imported; decision log; register dispositions | commits + validator PASS |
| 0 | Learner-chat default route = opencode/deepseek-v4.1-flash, effort max | benchmark run `a41bbe05e7e24526a35056341ab775bc` (100% schema validity, 100% grounding, 0 fabrications, 96.92% cost reduction); route row flipped audited; rollback target anthropic:claude-sonnet-5 |
| 0 | Companion flags ON (master/retrieval/actions/credits); score_display OFF pending calibration | FeatureFlags rows |
| 0 | OpenCode connection probe given a thinking budget (deepseek reasons before answering) | ship run 37530286350/37531078948 |
| 1 | Memory spine: CompanionMemoryEntries / ErrorDnaEntries / CompanionJourneys / CompanionAvailabilities | migration `20270112090000_AddAiCreditCosts`-preceded `20261007090000_AddCompanionMemoryErrorDna` applied (history row + 4 tables verified) |
| 1 | 7 learner tools (record/confirm scores confirm-gated, set availability, start journey, next best action, train mistakes, why score change) | allowlist + grants + composer injection |
| 1 | 6 plan templates (exam-eve, final-3d, emergency-7d, intensive-14d, single-subtest, 20-min) + availability shaper inside plan creation | `tmpl-sami-*` rows verified in prod |
| 2 | Image understanding live on deepseek vision + honest refusal on unreadable input; document attachments FOLDED into prompts (gap found live, fixed) | E2E turns in `AiAssistantThreads` (task/recipient/purpose extraction correct) |
| 3 | AI Credit action costs as live config (§9.1 baseline seeded; admin CRUD; show-allowance carries price table) | slice shipped via `_sami-ship-wt` (verify Actions run id in VERIFICATION.md) |
| 3 | Sellable catalog: 3 tiers × monthly/3-month/annual + 3 top-ups + 6 course add-ons at PDF §10 prices; quota-plan descriptions corrected to £7.99/£14.99/£26.99 | `wave3-catalog.mjs` output, products 201 |

## UAT infrastructure ready

- Accounts (`.tools-state/sami-ops/uat-accounts.json`): `uat-a-medicine` (Full Course + companion),
  `uat-b-free` (Free, no package), `uat-c-crash` (Crash Course, nursing) — trusted devices pinned,
  quota overrides for A/C.
- Section-21 assets: `docs/ai-learning-companion/uat/` (score cards A + A-v2, case notes B,
  reading question C, voice/handwriting/privacy scripts D).
- E2E harness: `.tools-state/sami-ops/wave2-e2e.mjs` (SignalR learner client; text/image/document turns).

## Remaining (in order)

1. **Wave 3 finish**: ship the credit-cost slice (background retry loop owns the visibility
   lease contention); contextual upgrade + resume-chat-after-purchase (F-111/112/142); tutor
   handoff (F-108/123).
2. **Wave 4**: corpus scale-out (workshops/correction sessions/videos per §13.1 pipeline —
   infrastructure exists: indexer/releases/governance); admin dashboards F-126..129/132
   (partially exist; audit vs spec); automated Error-DNA feeders from graded attempts.
3. **Wave 5**: execute all 80 UAT scenarios on the production build with the accounts/assets
   above; §15 handover package; legacy-persona sweep as release gate.
4. **Wave 6**: F-168..F-184 expansion (foundations: ExamFamilyCode/ExamTypeCode axis exists).

## Operational traps (read before touching)

- Hand migrations need `[DbContext(typeof(LearnerDbContext))]` AND `[Migration("id")]`
  (ADR 0001 convention). Verify discovery by grepping the `migrations.sql` release artifact.
- Migration ids must sort AFTER `20270111090000` (prod history runs ~3 months ahead).
- Temp-admin credentials: `.tools-state/sami-ops/temp-admin.json` (rotate at handover).
- Shared GitHub rate limit + ship visibility lease are contended by concurrent agent
  sessions — never force-release; retry.
- Never use shell heredocs to patch source (corrupts `\n`/`\b` — two incidents this session).

## UAT execution log (Wave 5, live, 2026-10-07)

- **Packs executed so far**: Pack 1 (20/20 records, 0 nulls, no legacy-persona leak, persona = Sami)
  and Pack 2 (multiple runs; latest records in `uat/results/`). Pack 1 evidence: `uat-execution-…02-27-21.json`.
- **Live defects found by UAT and FIXED + deployed**:
  1. OpenCode lane saturation (4 in-flight, 5s wait) under slow max-effort turns → lane tunable
     (`AiOpenAiCompatible__OpenCodeMaxInFlight`, default 8) and wait 5s → 90s (queue, don't fail).
  2. `finish_reason=length` at effort=max: deepseek reasoning exhausted the 6,144-token output
     floor → floor raised to 16,384 with a bounded length-retry ladder to 32,768.
  3. Root cause of remaining slow-turn failures: the ~100s non-streamed HttpClient timeout and
     ~120s edge read cap → **OpenCode calls now stream** (`OpenCodeStreamingCall`, SSE parse of
     content/tool-call deltas, falls back to non-streamed when declined; kill switch
     `AiOpenAiCompatible__OpenCodeStreaming=false`).
- **Known characteristic (not a defect)**: max-effort turns cost 6k–30k+ output tokens each and
  take 1–4 minutes. UAT accounts carry raised quota overrides (D-007) for the execution window.
- **Runner**: `.tools-state/sami-ops/uat-run.mjs` (SignalR learner client; packs 1/2 scripted;
  pack 3/4 definitions in `uat-pack34.mjs`); consolidation: `scripts/ai-learning-companion/consolidate_uat.py`.
- §17.1 judgement stays with the reviewer; the runner captures verbatim responses only.

## UAT status after Packs 1-4 (2026-10-07 evening)

- **Pack 1**: 20/20 records, all responses captured; persona=Sami, zero legacy-name leaks.
- **Pack 2**: definitive run 17/20 clean (1 gateway-busy; 2 triage false positives from the
  learner's own "exhausted" wording).
- **Pack 3**: 8 records; first 3 clean (score-report honesty, whole-PDF retrieval, case-note
  triage). **OPEN DEFECT D-SAMI-001**: 5 turns (05/07/08/15/16) returned instantly-empty
  completions with NO thread persistence and NO usage records — the turn died before the
  orchestrator persisted anything. Evidence: thread `fd3a60cdcc1c4a868c9ea9c30d983c1d`
 (5 assistant rows, 3 filled), zero `AiUsageRecords` 20:17–20:35Z. Suspect: orchestrator-level
  early failure after long turns. Needs a fix cycle.
- **Pack 4**: 6/6 records clean on both accounts — entitlement isolation held (Free account
  refused the Rule-Book exfiltration attempt politely), non-existent-pack trap answered
  honestly without inventing a route, billing disclosure accurate, contextual upgrade correct.
- Legacy-persona sweep (§15.1): repo surfaces now zero "Jana" (seeded doc content + comments
  scrubbed, live prod `DocumentationVersions` row patched).

## §15 handover package — remaining items
Env/secret inventory + rotation runbook, backup/restore procedure doc, golden+adversarial
test sets bundle, and the completion of D-SAMI-001 fix + Pack 3 retest. Everything else
(repos, registries, credit ledger docs, runbooks, UAT scripts) is in-repo under
`docs/ai-learning-companion/` and `.tools-state/sami-ops/`.
