# OET Platform — Final AI Architecture Plan

*Built from the live audit. Every recommendation references real code locations.*

---

## Executive Summary

**Three critical bugs are leaking money before any model choice matters.** Fix these first, then the model selection plan below holds:

1. **F-037 — Cost recorder writes `$0` to every row.** Your admin dashboards, learner usage views, and forecasts all show zero. You're flying blind. Until this is fixed, *no model selection decision should be made from current cost dashboards.*
2. **F-051 — Anthropic prompt caching is documented and flagged but not actually wired.** The Anthropic provider transport at `AiProviderRegistry.cs:241` sends `system` as a flat string. Your rulebooks (70 JSON files, 12 speaking cards, full Writing Rulebook) are being re-billed at full price on every call. **This alone is probably costing you 60–80% on Writing and Speaking grading.**
3. **F-050 — `UsageDebit` exists in the credit ledger but no gateway debit path was found.** Candidates may be using AI without their credits actually decrementing. This breaks the entire credit-package business model.

**Fix order: F-051 first (biggest cost win), F-037 second (visibility), F-050 third (revenue protection).** Then ship the model plan below.

---

## Part 1 — Pre-flight Fixes (BEFORE changing any models)

### Fix 1: Implement Anthropic prompt caching in the transport layer

**File:** `backend/src/OetLearner.Api/Services/Rulebook/AiProviderRegistry.cs:203-253`

**Problem:** Speaking defaults at `AiFeatureRouteResolver.cs:42-73` flag `PromptCachingEnabled = true`, the README at `Services/Rulebook/README.md:73,110-120` documents it, the speaking helper at `ConversationHub.SpeakingRoleplay.cs:25-26` builds the right structure — but the actual Anthropic POST sends `system = request.SystemPrompt` as a plain string. Anthropic ignores caching unless you send `system` as an array of typed blocks with `cache_control`.

**Required change — convert this:**
```json
{ "system": "rulebook + persona text...", "messages": [...] }
```
**Into this:**
```json
{
  "system": [
    { "type": "text", "text": "<rulebook block>", "cache_control": {"type": "ephemeral"} },
    { "type": "text", "text": "<persona/task block>", "cache_control": {"type": "ephemeral"} }
  ],
  "messages": [...]
}
```

Add the `anthropic-beta: prompt-caching-2024-07-31` header (or whatever the current beta header is — verify against Anthropic docs).

**Expected impact:** 90% discount on the rulebook portion of every cache-hit call. For Writing grading where the rulebook + criteria + anchor exemplars are ~12K tokens out of ~14K input, that's roughly a **75% reduction in Writing grading cost.** Similar for Speaking content grading. This single fix is worth more than every model swap in the rest of this plan combined.

### Fix 2: Wire actual cost calculation into `AiUsageRecorder`

**File:** `backend/src/OetLearner.Api/Services/Rulebook/AiUsageRecorder.cs:176-186`

The recorder receives token counts and the resolved provider. The provider row already carries `PricePer1kPromptTokens` and `PricePer1kCompletionTokens` (`AiProviderEntities.cs:5-55`). The quota service at `AiQuotaService.cs:259-342` already calculates this correctly when committing. Either:

- **Option A (clean):** Inject the same rate-card calculator and call it in the recorder before persistence. Reuse the function the quota service uses to avoid drift.
- **Option B (minimal):** Read `AiQuotaCounter.CostAccumulatedUsd` delta during the same transaction.

Either way, the dashboards at `AiUsageAnalyticsService.cs:57-211` and `AiUsageAdminEndpoints.cs:22-208` immediately become accurate.

**Reconcile the split source of truth.** Today the quota counter has the right number and the usage record has zero (F-044). Pick one as canonical — recommend `AiUsageRecord` because it's per-call and joins to user/feature/model — and write the quota commit to populate that field directly.

### Fix 3: Wire the gateway → credit ledger `UsageDebit` path

**Files:** `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs:528-559`, `backend/src/OetLearner.Api/Services/AiManagement/AiCreditService.cs:18-39`

After a successful platform-funded call (NOT BYOK), in the same fail-soft block that commits quota, also call `AiCreditService` to debit the learner's credit balance. The amount should be **1 credit per feature code in the scoring tier**, mapped per the package design we worked out.

This is what makes your Quick Check / Exam Prep Pro / OET Mastery packages actually work. Without this wire, candidates pay $19/$42/$100 and consume unlimited AI.

### Fix 4: Pull `IAiQuotaService` into the AI Assistant gateway

**File:** `backend/src/OetLearner.Api/Services/AiAssistant/AiAssistantGateway.cs:21-27,105-151`

The assistant gateway records usage but doesn't call quota/kill-switch/global-budget checks. Add the same `_quotaService.CheckAsync(...)` pattern that the grounded `AiGatewayService` uses at `AiGatewayService.cs:230-254`. Otherwise a runaway assistant conversation can blow through your monthly budget with no enforcement.

---

## Part 2 — Final Model Assignment Per Feature Code

These are the canonical assignments. Each maps to an existing `AiFeatureCode` in `backend/src/OetLearner.Api/Domain/AiEntities.cs:210-270`. The route table is `AiFeatureRoutes`; defaults are at `AiFeatureRouteResolver.cs`.

### Tier S — High-stakes scoring (paid grading the candidate sees)

| Feature code | Model | Caching | Batch? | Reasoning |
|---|---|---|---|---|
| `writing.score.v1` (called from `WritingEvaluationPipeline.cs:159`) | **Anthropic `claude-sonnet-4-6`** with prompt cache on rulebook + criteria block | **Yes (after Fix 1)** | **Yes — use Anthropic Message Batches API** | Best healthcare-English register + your style canon. Sonnet 4.6 with cache + batch is ~$0.015 per letter. No reason to pay Opus prices here. |
| `speaking.score.v2` (already routed to Sonnet 4.6 per `AiFeatureRouteResolver.cs:42-73`) | **Keep `claude-sonnet-4-6`** for Clinical Communication 5 criteria from transcript | Yes | Yes (batch) | Already the right call. Just turn caching and batching on. |
| `mock.full_grade` | **No new AI call** — `MockReportAggregationService.cs:206-420` is correctly designed as pure aggregation over per-section scores. Don't add an AI writer here. | n/a | n/a | F-023 confirms the aggregator is the right shape. Resist the urge to add a "narrative summary" call — it adds cost without student-visible value. The per-section scores ARE the report. |
| `pronunciation.score` (and the 4 Linguistic Speaking criteria) | **Gemini 3.5 Flash with `thinking_level: high` and native audio input** | Yes (cached prompt) | n/a (synchronous after upload) | Only model in the stack that hears raw audio for prosody/intelligibility/fluency. Sonnet can't. This is the architectural unlock for defensible Speaking scores. Add a new provider row + route entry. |
| Score appeals / disputed re-grading (new feature code: `writing.score.appeal.v1`, `speaking.score.appeal.v1`) | **GPT-5.5 medium reasoning** (NOT high — confirmed sub-optimal in OpenAI's own docs) | Yes (cached) | Optional | Reserved for paid score appeals only. Marketed as "frontier-model second opinion." Low volume, high price tolerance. |

### Tier A — Real-time interactive (live speaking roleplay)

The audit at F-022 / F-053 is right: this is four separate decisions, not one. Current default at `ConversationAiOrchestrator.cs:40-120` is `anthropic-claude-opus-4.7` for both opening and reply — that is **massively overspec'd** and is probably your single largest hidden cost.

| Sub-function | Current | Recommended | Why change |
|---|---|---|---|
| **Live ASR (candidate → text)** | Configurable: ElevenLabs Scribe / Whisper / Deepgram per `ConversationOptions.cs:11-17,47-60` | **ElevenLabs Scribe v2 Realtime** as default | 150ms latency, 90+ languages, integrates with the ElevenLabs voice loop you already pay for. Keep Whisper async path for grading transcripts (cheaper, batch-friendly). |
| **Per-turn AI patient reply (`ConversationReply`)** | `anthropic-claude-opus-4.7` default | **`claude-haiku-4-5`** with cached persona/rulebook block | Live patient turns are short, fast, role-constrained. Opus is wasteful here. Haiku 4.5 keeps latency low and cost down by 10-15x. Already the right call for `speaking.patient.turn.v1` at `AiFeatureRouteResolver.cs:82-113`. |
| **Opening AI reply (`ConversationOpening`)** | `anthropic-claude-opus-4.7` default | **`claude-sonnet-4-6`** with cached persona | Opening sets the scenario tone — slightly more important than mid-conversation turns. Sonnet is the right middle ground; Opus is still overkill. |
| **Live TTS (AI → audio)** | ElevenLabs configurable | **ElevenLabs Flash v2.5** default; pilot v3 Conversational for premium-tier candidates | 75ms latency on Flash v2.5. v3 isn't real-time capable per ElevenLabs' own docs. |
| **Post-session evaluation (`ConversationEvaluation`)** | `anthropic-claude-opus-4.7` default | **`claude-sonnet-4-6`** with prompt caching on rulebook | Post-session is async — quality matters but latency doesn't. Sonnet 4.6 with cache + batch costs ~$0.04 per session vs Opus at ~$0.30+. Same scoring quality for rubric-based evaluation. |

**Net effect on live Speaking cost per 5-min session:** Roughly from ~$0.80 to ~$0.20. That's 4x more headroom in your Mastery package.

### Tier B — Content drafts, hints, coaching (low-stakes, not candidate-shown as a "score")

| Feature code | Model | Caching | Reasoning |
|---|---|---|---|
| `card.draft.v1` (admin generates new speaking cards) | **Keep `claude-sonnet-4-6`** | Yes | Already correct per `AiFeatureRouteResolver.cs:82-113`. |
| `writing.coach.v1` (writing tips while drafting) | **`claude-haiku-4-5`** | Yes | Short outputs, low stakes. Sonnet is overkill. |
| `vocabulary.explain.v1`, `recall.explain.v1` | **`claude-haiku-4-5`** or **Copilot route** | Yes | Bulk explanation tasks. F-029 already lists these as Copilot bulk-route candidates. Cheapest viable. |
| `summarise.session.v1` | **`claude-haiku-4-5`** | Yes | Same. |
| Admin writing draft (`WritingDraftService.cs:14-21`) | **`claude-sonnet-4-6`** | Yes | One-time per template, quality matters for content library. |

### Tier C — Embeddings / RAG / Search

| Feature | Current | Keep as-is |
|---|---|---|
| AI assistant indexing (`EmbeddingService.cs:9-29`) | `text-embedding-3-small` dim 1536 batch 20 | Yes — this is the right choice. Cheap, fast, well-suited for rulebook/content indexing. Don't change. |

### Tier D — Deterministic (NO AI — pure code)

Confirmed by audit:

- Listening MCQ marking — `LearnerEndpoints.cs:201-210`, deterministic against answer key
- Reading MCQ marking — `LearnerEndpoints.cs:191-199`, structured reading-paper routes
- Mock report aggregation — `MockReportAggregationService.cs:206-420`, pure score aggregation
- Stripe webhook handling — `LearnerService.cs:8248-8390`
- Wallet ledger — `WalletService.cs:85-260`
- Credit balance display
- Dashboard rendering
- All `PaymentTransaction` flows

**Resist adding AI to any of these.** They are correctly designed as deterministic and your audit confirms it.

---

## Part 3 — Per-Sub-test Reference Card

| Sub-test | Sync/Async | Models used | Cost per submission (after fixes) |
|---|---|---|---|
| **Writing** | Async (queued via `JobType.WritingEvaluation`) | Rule engine pre-pass → Sonnet 4.6 (cached + batched) for grading | ~$0.015 |
| **Speaking — async grading** | Async | Whisper Large-v3 (transcript) + Sonnet 4.6 (5 Clinical criteria, cached + batched) + Gemini 3.5 Flash (4 Linguistic criteria, native audio) | ~$0.12 |
| **Speaking — live roleplay** | Real-time | Scribe v2 Realtime (STT) + Haiku 4.5 (per-turn reply, cached) + Flash v2.5 (TTS) + Sonnet 4.6 (post-session eval, cached + batched) | ~$0.20 per 5-min session |
| **Listening** | Sync | **Zero AI** — pure rule-based marking | $0.00 |
| **Reading** | Sync | **Zero AI** — pure rule-based marking | $0.00 |
| **Full Mock** | Async | Sum of above sub-tests + zero additional aggregator AI call | ~$0.18 per full mock |

---

## Part 4 — Quick Check SKU Mapping (resolves F-021 / F-047)

The audit confirmed "Quick Check" doesn't exist as a code SKU. The valid product types per `LearnerService.cs:3225-3240` are `review_credits`, `plan_upgrade`, `plan_downgrade`, `addon_purchase`.

**Map the three packages to the existing system:**

| Marketing name | Product type in code | Implementation |
|---|---|---|
| **Quick Check ($19)** | `addon_purchase` with addon code `pkg_quick_check` | New `BillingAddOn` row granting 5 grading credits + 30-day validity, no recurring |
| **Exam Prep Pro ($42)** | `addon_purchase` with addon code `pkg_exam_prep_pro` | New `BillingAddOn` row granting 15 grading credits + 2 mock entitlements + 90-day validity |
| **OET Mastery ($100)** | `addon_purchase` with addon code `pkg_oet_mastery` | New `BillingAddOn` row granting 30 grading credits + 5 mock entitlements + 180-day validity + priority queue flag |

The credit grant happens in the webhook side-effect path at `LearnerService.cs:8860-9020` which already "grants included plan credits or add-on credits" — extend the add-on credit grant logic to write to `AiCreditLedger` with the package's credit count and `ExpiresAt` set per validity window.

---

## Part 5 — Migration Plan (Priority-Ordered)

### Week 1 — The money fixes (do these in this order)

1. **Implement prompt caching in Anthropic transport** (`AiProviderRegistry.cs:203-253`)
   - Add `cache_control` to `system` array serialization
   - Add the beta header
   - Verify against one live Writing grading and one Speaking grading call
   - **Expected: 60–75% drop in input-token cost on Writing/Speaking immediately**

2. **Wire cost calculation into `AiUsageRecorder`** (`AiUsageRecorder.cs:176-186`)
   - Reuse the rate-card math from `AiQuotaService.cs:259-342`
   - Backfill last 30 days from quota counter deltas if possible
   - Verify dashboards at `/v1/admin/ai/usage` now show non-zero cost

3. **Wire `UsageDebit` from gateway to `AiCreditService`** (`AiGatewayService.cs:528-559` → `AiCreditService.cs:18-39`)
   - On successful platform-funded scoring-tier call, debit 1 credit
   - Add 402-style refusal when balance < required credits
   - Test with a real Writing submission flow

4. **Pull quota service into AI Assistant gateway** (`AiAssistantGateway.cs:21-27`)
   - Match the pattern from `AiGatewayService.cs:230-254`

### Week 2 — Model right-sizing

5. **Update `AiFeatureRouteResolver.cs` defaults** for conversation features:
   - `ConversationOpening` → `claude-sonnet-4-6`
   - `ConversationReply` → `claude-haiku-4-5`
   - `ConversationEvaluation` → `claude-sonnet-4-6`
   - Override the `claude-opus-4.7` defaults at `ConversationAiOrchestrator.cs:40-120`

6. **Add a new feature code `pronunciation.linguistic.score.v1`** routed to `gemini-3.5-flash` with audio input enabled
   - Add Google Gemini as a new provider row (the codebase already supports OpenAI-compatible transport; Gemini has an OpenAI-compatible endpoint)
   - Wire it into Speaking async grading pipeline alongside the existing Sonnet 5-criteria call

7. **Enable Anthropic Message Batches API for async jobs:**
   - `JobType.WritingEvaluation`
   - `JobType.ConversationEvaluation`
   - Mock report sub-section grading
   - This is a second 50% discount on top of caching for non-real-time work

### Week 3 — Package launch

8. **Create three `BillingAddOn` rows** for Quick Check / Exam Prep Pro / OET Mastery
9. **Extend addon credit-grant logic** at `LearnerService.cs:8860-9020`
10. **Build candidate-facing package selection UI** with the credits-flow from earlier in our conversation (8-step purchase → spend → expire → upsell)
11. **Wire the upsell triggers** (80% credits used, 7-day expiry warning)

### Week 4 — Observability and trust

12. **Reconcile cost source of truth.** Make `AiUsageRecord.CostEstimateUsd` canonical. Run a verification script comparing `SUM(AiUsageRecords)` vs `SUM(AiQuotaCounters.CostAccumulatedUsd)` — they should match within rounding.
13. **Add per-feature cost telemetry to admin dashboard** so you can see Writing cost vs Speaking cost vs Conversation cost in real time
14. **Set up Sentry alerts** for AI call failures, quota denials, and credit-debit failures (Sentry is already wired per F-005)

---

## Part 6 — Expected Cost Reality After All Fixes

**At 500 active candidates / month, 1,000 Writing submissions, 800 async Speaking submissions, 200 live roleplay sessions, 300 full mocks:**

| Function | Calls/month | Cost/call | Monthly cost |
|---|---:|---:|---:|
| Writing grading (Sonnet, cached, batched) | 1,000 | $0.015 | $15 |
| Speaking async grading | 800 | $0.12 | $96 |
| Live Speaking roleplay (Haiku reply, Flash TTS, Scribe STT) | 200 sessions × 8 turns | $0.025/turn | $40 |
| Live Speaking session eval (Sonnet, cached, batched) | 200 | $0.04 | $8 |
| Full Mock orchestration | 300 | $0.18 | $54 |
| Conversation opening (Sonnet) | 200 | $0.01 | $2 |
| Writing/Vocab/Recall coach (Haiku, cached) | ~5,000 | $0.002 | $10 |
| Embeddings (indexing only, rare) | ~ | low | $5 |
| Admin draft generation | ~50 | $0.05 | $3 |
| AI Assistant (now quota-bounded) | ~2,000 | $0.015 | $30 |
| **Total platform AI cost** | | | **~$263/month** |

**Revenue at the same scale, mid-mix of packages:**
- 200 Quick Check × $19 = $3,800
- 200 Exam Prep Pro × $42 = $8,400
- 100 OET Mastery × $100 = $10,000
- **Total revenue: $22,200**

**Gross AI margin: ~98.8%.** That's healthy enough to absorb Stripe fees, hosting, support, and content production and still leave 85%+ for the business.

For comparison: at *current* defaults (Opus everywhere, no caching, no batching), the same volume would cost roughly **$1,800/month** — about 7x more, dropping gross AI margin to ~92% before any of those other costs. The fixes pay back the engineering investment in the first week.

---

## Part 7 — Open Questions From the Audit, Answered

| Audit question | Answer |
|---|---|
| Which models for high-stakes vs low-stakes? | Sonnet 4.6 for scoring; Haiku 4.5 for coaching/drafts/explanations; Opus 4.7 reserved for NHS CV work outside the platform; GPT-5.5 reserved for paid score appeals only |
| Dual-pass scoring? | **No.** Single Sonnet call with deterministic rule-engine pre-pass (already in `WritingEvaluationPipeline.cs:51-135`). Dual-pass adds cost without measurable accuracy gain on rubric work. |
| Live Speaking model strategy? | Four separate models — Scribe v2 Realtime STT, Haiku 4.5 reply, Flash v2.5 TTS, Sonnet 4.6 eval. See Tier A above. |
| Implement Anthropic prompt caching? | **Yes, urgently.** Single highest-ROI engineering task in the platform. |
| Use vendor Batch APIs? | **Yes** for Writing, Speaking eval, Conversation eval. All are async via `JobType.*` already — batch fits perfectly. |
| Canonical cost source of truth? | `AiUsageRecord.CostEstimateUsd` — per-call, joinable to feature/user/model. Fix recorder, keep counter as roll-up. |
| AI Assistant quota? | **Yes** — pull `IAiQuotaService` into the assistant path. |
| Failure handling for paid AI calls? | Match existing Writing pipeline pattern (`WritingEvaluationPipeline.cs:167-213`) — explicit failed evaluation state, no silent fallback, refund credit on `provider_failure`, retry on `quota_temporary`, surface human-review queue on `kill_switch`. |
| Quick Check SKU? | Map to `addon_purchase` with addon code `pkg_quick_check`. See Part 4. |

---

## Part 8 — The One-Page Summary Faisal Should Pin to His Monitor

> **Defaults to set in `AiFeatureRouteResolver.cs`:**
> - `writing.score.v1` → `claude-sonnet-4-6` (cache + batch)
> - `speaking.score.v2` → `claude-sonnet-4-6` (cache + batch) ✓ already correct
> - `speaking.patient.turn.v1` → `claude-haiku-4-5` ✓ already correct
> - `pronunciation.linguistic.score.v1` → `gemini-3.5-flash` (NEW — add provider row + route)
> - `conversation.opening` → `claude-sonnet-4-6` (was Opus)
> - `conversation.reply` → `claude-haiku-4-5` (was Opus)
> - `conversation.evaluation` → `claude-sonnet-4-6` (was Opus, cache + batch)
> - `card.draft.v1` → `claude-sonnet-4-6` ✓ already correct
> - `writing.coach.v1`, `vocabulary.explain.v1`, `recall.explain.v1`, `summarise.session.v1` → `claude-haiku-4-5`
>
> **Three engineering fixes (in order):**
> 1. Anthropic prompt caching in `AiProviderRegistry.cs:241`
> 2. Cost calculation in `AiUsageRecorder.cs:183`
> 3. Credit debit from gateway → `AiCreditService`
>
> **Three things you never add AI to:**
> 1. Listening/Reading MCQ marking
> 2. Mock report aggregation
> 3. Stripe / wallet / credit transactions

---

Run this plan in the order written. The Week 1 fixes alone will recover enough margin to fund the rest of Q2. Once Week 4 lands, you'll have accurate cost telemetry to validate every assumption in this document against real numbers — and adjust where reality diverges from estimate.

If anything in this plan reads as wrong against what you know about the codebase or the business, tell me and I'll revise. Otherwise this is the document Faisal builds against.
