# Implementation Plan — AI Learning Companion in `oetwebapp`

> Derived from `REPO_GAP_ANALYSIS.md` (audited 2026-09-06). This is the repository-specific plan, not the
> generic architecture in `ARCHITECTURE_AND_INTEGRATION.md`. Where the source plan and this repository
> disagree, the **source invariant** is preserved and the **technical implementation adapts** — see the
> decision records DR-001…DR-003 in the gap analysis.

## Guiding constraint

The audit found 42 `EXISTS` and 83 `PARTIAL` against 53 `MISSING`. The companion is an **orchestration,
grounding and governance layer** over engines that already ship. Every slice below is written as
"call the existing service" unless the gap analysis proves nothing exists.

**Nothing gets rebuilt that already works.** In particular: planning (`IStudyPlanGenerator`), readiness
(`ReadinessComputationService`), spacing (`SpacedRepetitionService`/`Sm2Scheduler`), mastery
(`AdaptiveDifficultyService`), grading (`WritingSubmissionEvaluationPipeline`, `SpeakingEvaluationPipeline`),
voice role-play (`Services/Conversation/**`), credits (`IAiPackageCreditService`), entitlement
(`IEffectiveEntitlementResolver`, `IContentEntitlementService`), telemetry (`IAiUsageRecorder`), quota
(`IAiQuotaService`), and the action substrate (`IAiToolRegistry` + `AiToolInvoker` + `AiFeatureToolGrant`).

## Dependency order

```
S0.1 feature codes + policy rows
        │
S0.2 flags & kill switches ──────────────┐
        │                                │
S0.3 knowledge schema (pgvector)         │
        │                                │
S0.4 learner tool-grant lockdown  ◄──────┘   (security gate: must pass before any learner mount)
        │
S0.5 evaluation harness skeleton
        │
        ├──► S1.1 context resolver ──► S1.2 retriever ──► S1.3 prompt composer ──► S1.6 frontend
        │            │                      │                                          ▲
        │            │                      └──► S1.2b Stage-1 corpus indexer          │
        │            │                                                                 │
        │            └──► S1.4 destination registry ──► S1.5 action layer ─────────────┘
        │
        └──► S1.7 commercial (quota + credits + paywall)  ──────────────────────────────┘
                     │
                     └──► S1.8 trust/ops hardening (exam mode, exfiltration, audit, a11y, RTL)
```

`S0.4` is a hard gate. `S1.8` items that are safety-critical (exam mode F-155, entitlement prefilter F-154,
memory controls F-047) ship **with** the slice they protect, not after it.

---

## Stage 0 — foundations

### S0.1 Feature codes and policy rows
**Files:** `backend/src/OetLearner.Api/Domain/AiEntities.cs` (`AiFeatureCodes`), `docs/AI-USAGE-POLICY.md` §5.
Add `companion.chat.v1`, `companion.retrieval.v1`, `companion.action.v1`. All three are **non-scoring** and
**platform-only** — the companion touches learner performance data, so BYOK is refused exactly as
`conversation.reply` and `writing.coach.*` are.
**Gate:** `AiEntities.cs:297` documents that a missing doc row "is a bug caught by `AiFeatureEligibilityTests`" — but **that test does not exist in the repository**. This slice writes it for real: every `AiFeatureCodes` constant must appear in the policy matrix, every `admin.*` and every `companion.*` code must be in `AiCredentialResolver.PlatformOnlyFeatures`.
**Serves:** F-048, F-049, cross-cutting telemetry.

### S0.2 Flags and kill switches
| Flag | Default | Purpose |
|---|---|---|
| `companion.enabled` | **OFF in production** | Master switch for the whole surface |
| `companion.retrieval.enabled` | OFF | Disable RAG independently, keep chat alive |
| `companion.actions.enabled` | OFF | Freeze action execution without killing chat |
| `companion.credits.enabled` | OFF | Stop all new credit consumption, preserve balances |
| `companion.score_display.enabled` | **OFF, gated** | TV-006/TV-007 — numeric bands stay disabled until calibration |

Backend: `Configuration/FeatureFlagOptions.cs` + `IRuntimeSettingsProvider` (DB-over-env, 30s cache) so a
switch flips without a deploy — the source requires exactly that. Learner-visible flag `ai_learning_companion`
is added to the allow-list `switch` in `Endpoints/LearningContentEndpoints.cs` (learner flags are an explicit
allow-list; anything else 404s), consumed by `useFeatureFlagMap`.
**Serves:** cross-cutting kill switches, F-138 gating, F-064 calibration gate.

### S0.3 Knowledge schema
**Migration:** hand-authored `Data/Migrations/YYYYMMDD090000_AddCompanionKnowledge.cs`, inline
`[DbContext]` + `[Migration]`, raw idempotent Postgres SQL, `LearnerDbContextModelSnapshot.cs` untouched.

- `CompanionSource` — source id, type, **authority class**, exam + version, profession, subtest/skill,
  required entitlement scope, approval state, owner/approver, checksum, storage locator, confidentiality,
  canary tag, retired/superseded.
- `CompanionChunk` — parent source, ordinal, text, **exact location** (page / slide / timestamp),
  `Embedding vector(1536)` (HNSW), content hash, index release id.
- `CompanionKnowledgeRelease` — release id, included source versions, index checksum, evaluation report ref,
  approver, published at, rollback target, status.

Authority classes: `OFFICIAL_CURRENT_FACT`, `DR_HESHAM_APPROVED_METHOD`, `PROFESSION_APPROVED_METHOD`,
`COURSE_MATERIAL`, `PLATFORM_SUPPORT`, `CANDIDATE_EVIDENCE`, `ADMIN_OVERRIDE`.

pgvector is already provisioned (`pgvector/pgvector:pg17`, `HasPostgresExtension("vector")`, and
`WritingScenarioEmbedding` already uses `vector(1536)`), so this is additive, not infrastructural.
**Serves:** F-013, F-026, F-027; unblocks F-154.

### S0.4 Learner tool-grant lockdown — SECURITY GATE
**Why:** `AiAssistantOrchestrator` resolves tools by feature code, and the admin branch grants
`RunCommandTool`, `DeployTool`, `WriteFileTool`, `GitTool`, `QueryDatabaseTool`. Role is derived server-side
from claims (`user.IsInRole`) and grants are deny-by-default, so the boundary is sound **today** — but
nothing locks it, and the companion is about to make the learner branch reachable in production.

**Test:** assert that resolving tools for `ai_assistant.learner` and every `companion.*` feature code returns
only an explicit allowlist, and that each dev tool is absent. Fails loudly if someone adds a grant row or a
wildcard later.
**Serves:** cross-cutting security; blocks S1.6.

### S0.5 Evaluation harness skeleton
`tests/companion/golden/*.json` + a runner. Case classes: grounded answer with citation; insufficient
evidence → explicit unknown; authority conflict surfaced not blended; locked-content extraction attempt;
multi-turn reconstruction attempt; navigation/deep-link; clinical-boundary refusal; exam-integrity refusal;
distress response. Thresholds stay `TO VERIFY` (TV-004/TV-005) — the harness reports, it does not assert a
guessed number. Critical entitlement/payment fabrication is the one **zero-tolerance** assertion.
**Serves:** F-153, F-154, cross-cutting QA.

---

## Stage 1 — Monetisable OET Core

### S1.1 Companion context resolver — **DONE**
**New:** `backend/src/OetLearner.Api/Services/Companion/CompanionContextResolver.cs`.
Server-trusted envelope assembled from: `ClaimTypes.NameIdentifier` → `IEffectiveEntitlementResolver.ResolveAsync`
→ `LearnerUser` + `LearnerGoal` (profession, exam type, exam date, targets, country) → `IAiQuotaService`
allowance → `IAiPackageCreditService.GetSnapshotAsync` projection → surface id.
Client-supplied tier/entitlement/profession fields are **hints only** and are always reloaded server-side.
**Serves:** F-003…F-006, F-010, F-143; prerequisite for everything below.

### S1.2 Entitlement-safe retriever — **DONE**
**New:** `Services/Companion/CompanionRetriever.cs`, modelled on the proven
`Services/AiAssistant/Indexing/CodebaseRetriever.cs` (vector 0.7 + keyword, graceful keyword-only fallback
when pgvector is unavailable).

Pipeline order is **mandatory** and testable:
1. resolve trusted context (S1.1);
2. **entitlement prefilter** — restrict the candidate source universe by approval state, exam/version,
   profession, and `EffectiveEntitlementSnapshot` scope **before** any vector or lexical search (F-154);
3. hybrid retrieval over `CompanionChunk`;
4. authority resolution — official current fact > approved methodology > profession-specific over generic >
   newest approved version; **conflicts are surfaced, never blended** (F-026, F-029);
5. evidence packing with per-source verbatim-span caps and rolling per-user retrieval-volume caps;
6. recheck `IContentEntitlementService.AllowAccessAsync` before returning any protected location.

### S1.2b Stage-1 corpus indexer — **DONE (not yet run in any environment)**
Index the **115 versioned rulebooks** through `DbBackedRulebookLoader` (so admin edits and versions flow
through rather than reading raw JSON), plus the destination registry (S1.4) and a minimal support/FAQ set.
Scoped to **one approved profession** pending TV-002. Course PDFs, videos and workshops are Stage 2.
**Serves:** F-013, F-023, F-024.

### S1.3 Prompt composer — replaces the generic learner prompt — **DONE**
**Changes:** `Services/AiAssistant/SystemPrompts/SystemPromptProvider.cs` gains an async companion path;
`AiAssistantOrchestrator` uses it for the learner branch.
Composes: persona (`Companion:PersonaName`, default `Jana`) + authority-labelled evidence + bounded learner
context + guardrails. Guardrails are explicit: clinical boundary (teach communication, never diagnose or
prescribe), distress boundary (supportive, never counselling, never link failure to worth, never state pass
probability), no numeric band while `companion.score_display.enabled` is OFF, retrieved text is **data not
policy**, and "say you don't know" when evidence is insufficient.
**Serves:** F-048, F-049, F-053, F-152, F-153; the persona config keeps TV-030 open.

### S1.4 Destination registry — **DONE**
**New:** `Services/Companion/CompanionDestinationRegistry.cs`. Structured records: route id, title, type,
profession/exam visibility, required entitlement, resolver, mobile/web support, retired/renamed state.
Resolution goes through the existing `PlatformLinkService.BuildWebUrl`.
**Serves:** F-023; unblocks F-098…F-101.

### S1.5 Action layer — grants, not a new subsystem — **DONE, with two deviations**
Register typed actions as `AiTool` rows granted **only** to `companion.action.v1`:
`OPEN_RESOURCE`, `START_PRACTICE`, `CONTINUE_LAST_ACTIVITY`, `SAVE_NOTE`, `SAVE_VOCABULARY`,
`ADD_PLAN_ITEM`, `SHOW_ALLOWANCE`, `OPEN_UPGRADE`, `CREATE_SUPPORT_REQUEST`.

`save_user_note` and `bookmark_recall_term` already exist in `Services/AiTools/Tools/BuiltInTools.cs` and are
reused. `AiToolInvoker` already enforces the call cap, validates arguments against a JSON schema, and writes
one `AiToolInvocation` row plus one `AuditEvent` per call — that is the source's `ActionDefinition` contract
already implemented. **The server resolves every target; a model-produced URL is never followed.**
**Serves:** F-098, F-100…F-104, F-110, F-111.

**Deviation 1 — grant target.** The grants are attached to `ai_assistant.learner`, not `companion.action.v1`,
because that is the feature code `AiAssistantOrchestrator.GetFeatureCode` actually resolves for a learner turn.
Moving the learner branch onto the companion codes is real work — it needs `AiQuotaPlan` rows for
`companion.chat.v1` first — and belongs with S1.7 cost attribution. Safety does not depend on which of the two
codes is used: both are in `AiToolRegistry.LearnerFacingFeatureCodes`, so both are filtered through
`LearnerSafeToolCodes`.

**Deviation 2 — `CREATE_SUPPORT_REQUEST` not built.** There is no learner-facing ticket store in this
repository; `CustomerSupportCase` is an admin-opened access grant. The companion routes to `/support` through
the destination registry instead. Inventing a ticketing table to satisfy one action would be the wrong call.

**Also added, not in the original plan:** `Endpoints/CompanionKnowledgeAdminEndpoints.cs` — the indexer had no
caller, so the corpus could never be built. `GET /v1/admin/companion/knowledge/status` and
`POST .../reindex`, both under `AdminAiConfig`.

### S1.6 Frontend — finish what is already built — **DONE except citations UI and paywall card**
1. Mount `AiAssistantProvider` in `app/providers.tsx` inside `AuthProvider` (needs `session.accessToken` and
   role), beside `AuthenticatedNotificationCenter`.
2. Rewrite `components/domain/ai-assistant/AiAssistantPanel.tsx` to consume `useAiAssistantContext()`,
   deleting the local-state stub. `hooks/use-ai-assistant.ts` already implements connect/stream/threads/cancel.
3. `AiAssistantMessages.tsx`: render through `components/ui/markdown-content.tsx` instead of plain text.
4. Add citations with source labels, server-allowlisted quick actions, an allowance/credit chip, a mode
   indicator and the contextual paywall card.
5. `app/companion/page.tsx` — full-screen tutor inside `LearnerDashboardShell` (inherits `requiredRole="learner"`).
6. i18n: `messages/{en,ar}/companion.json` (flat dotted keys) + `MESSAGE_MODULES` in `i18n.ts`.
   RTL already works via `app/layout.tsx` `<html dir>`.
**Serves:** F-097, F-141, F-142, F-160, F-161, F-167.

**Not yet done in this slice:** citations UI (item 4) and the contextual paywall card — the paywall depends on
S1.7, and citations depend on the retrieval trace being surfaced over SignalR, which the hub does not carry
yet. `/companion` is deliberately absent from `tests/e2e/learner/learner-smoke.spec.ts`: while the flag ships
off the page renders its "not enabled" state, so a heading assertion would fail.

### S1.7 Commercial core — **DONE for metering and paywall; no chargeable companion action exists to wrap**
Allowance and counter through `IAiQuotaService.TryReserveAsync/CommitAsync` with a companion `AiQuotaPlan`
row (`rolling_30d` is already supported — that is exactly the source's Free cap shape).
Chargeable actions through `IAiPackageCreditService`, wrapped in `IAiCreditReservationService`
reserve → commit / release so a failed turn **never** consumes credit. Exact charge shown and confirmed
before the action runs. Paywall returns capability, reason, price and benefits from configuration.
**No new wallet, no new sellable product** (DR-001, DR-002).
**Serves:** F-135, F-139, F-140, F-141, F-142, F-149, F-151.

**What was actually needed.** `AiAssistantGateway` already reserved and committed through `IAiQuotaService`
around every learner turn, so metering was not missing — *legibility* was. A quota refusal reached the learner
as a sentence in the transcript, which is the wrong shape for a paywall. `GET /v1/companion/session` now
answers "may this learner chat, why not, and where do they go" **before** they type, and
`app/companion/page.tsx` renders an upgrade card instead of an input box that would fail on the first message.
The access check is read-only by construction — it calls `GetUserPolicyAsync`, never `TryReserveAsync`, so
looking at the page cannot consume allowance.

**No `IAiCreditReservationService` wrapping was added, deliberately.** None of the five companion actions costs
a credit: they resolve links, read a balance and write a plan item. Reserve → commit / release exists to stop a
*failed chargeable operation* from consuming credit; wrapping free actions in it would be ceremony. Making a
companion action chargeable is a pricing decision (DR-002, TV-018) and is not one to make in code.

**Free tier.** The seeded `free` `AiQuotaPlan` lists specific feature codes and none of them is the learner
assistant, so a free-tier learner sees the paywall rather than a small allowance. Granting free-tier access is
a one-row change to `AllowedFeaturesCsv` — left for the owner, because it is a commercial decision, not a bug.

### S1.8 Trust and operations — **PARTIAL**
Prompt-injection boundary; exfiltration caps and canary monitoring before any paid corpus is indexed;
**exam-mode awareness (F-155) — the companion must refuse hints during a protected attempt**; companion
memory controls (view / correct / delete / reset, F-047); `AiInteractive` rate limiting; audit on every
action and credit movement; WCAG 2.2 AA pass on the new surface; kill-switch drill evidence.

**Done:** prompt-injection boundary (evidence blocks are labelled data, not policy, in
`CompanionPromptComposer`); per-source verbatim and volume caps in `CompanionRetriever`; exam-mode refusal
(F-155); memory **view / delete / reset** via `/v1/companion/memory`, with the delete predicate scoped by user
id *and* by authoring feature code so the companion can never delete a note the learner wrote themselves
(`CompanionMemoryIsolationTests`); audit and invocation rows on every tool call, already provided by
`AiToolInvoker`; rate limiting via the `PerUser` policy on both companion route groups.

**Not done:** memory **export**; exfiltration canary monitoring (needs a paid corpus indexed first, so it is
correctly ordered after the reindex); a recorded kill-switch drill; a formal WCAG 2.2 AA audit of
`/companion` — the surface uses the repo primitives, `aria-live` on connection state, `role="alert"` on
errors and labelled icon buttons, but it has not been run through the a11y suite.

---

## Explicitly deferred, with owners

| Scope | Reason | F-IDs |
|---|---|---|
| Full content ingestion, Error DNA, adaptive plan horizons | Stage 2 | F-014…F-022, F-030, F-033…F-038, F-044 |
| Voice / Ultimate mentor | Stage 3 gate: Arabic/voice spike (TV-008…TV-013) | F-045, F-113…F-122 |
| Multi-exam packs | Stage 4 | F-168…F-173 |
| B2B multi-tenancy | Stage 5 | F-174…F-184 |
| Numeric Writing/Speaking bands asserted by the companion | TV-006 / TV-007 calibration | F-064 (companion claim only) |
| New subscription tiers | DR-002 — commercial sign-off | F-136, F-137, F-138, F-144 |
| Cohort benchmarking, gamification | Source non-goals | F-084, F-121 |

Deferred means tracked with an owner and a gate. It never means removed from the matrix.

## Verification per slice

```bash
pnpm run ship:gate
pnpm exec tsc --noEmit
pnpm exec vitest run components/domain/ai-assistant hooks/__tests__/use-ai-assistant.test.ts
dotnet test backend/OetLearner.sln --filter "FullyQualifiedName~Companion|FullyQualifiedName~EndpointRegistrationTests|FullyQualifiedName~AiFeatureEligibility"
python scripts/ai-learning-companion/validate_traceability.py
```

New routes join the `[InlineData]` list in `EndpointRegistrationTests.Program_RegistersFeatureRoutes`
(which also asserts antiforgery on non-GET). `app/companion` joins the route table in
`tests/e2e/learner/learner-smoke.spec.ts`.

**Release evidence** per the source: F-IDs served, migration state, test and evaluation report, security and
accessibility findings, latency/cost observations, flag states, resolved vs open `TO VERIFY` gates, rollback
instructions, and known issues with severity and owner.
