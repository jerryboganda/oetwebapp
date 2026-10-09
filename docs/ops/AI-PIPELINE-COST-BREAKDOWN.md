# AI pipeline cost breakdown and admin guide (owner directive 2026-10-10)

Surface: `/admin/ai-pipelines` (permission `ai_config`). Everything below is read-only reporting except the
live-voice rate, which is an audited setting. Nothing here writes the saved provider order.

## What the page shows

| Section | Source |
| --- | --- |
| How to manage AI pipelines | `components/domain/admin/ai-pipelines/pipeline-guide.tsx`; diagrams are drawn from the saved order. "Download PDF" prints only the guide (browser Save as PDF). |
| Model check (each stage card) | `GET /v1/admin/ai/pipelines` → `approvedClaudeModel`, `recentModels` (model ids the route really sent in the last 7 days, from `AiUsageRecords.Model`), `effortNote`. |
| Cost by stage | `GET /v1/admin/ai/pipelines/cost-breakdown?window=today|7d|30d|all` → `AiCostBreakdownService`. |
| Live voice rate | `PUT /v1/admin/ai/pipelines/live-voice-rates` (audited as `AiLiveVoiceRateUpdated`). Stored as a `FeatureFlags` row `ai_live_voice_rate_per_min:<provider>` (rate = first token of the description). |
| Keys & providers, Self-check | Restored 2026-10-10: they were dropped from the page by commit `f98a28f0f`. |
| Benchmark a model | `GET/POST /v1/admin/ai/benchmark-runs[/run]` (existing endpoints). |

## How each number is produced

- **Per-call USD** is re-priced at report time by `AiUsageLedger` from provider-reported tokens, including the
  prompt-cache write/read buckets, at the list price of the model actually sent (`AiModelRateCard`, verified
  2026-10-10). The provider row's flat price is only a fallback for models not on the card. The per-call
  `CostEstimateUsd` stored at write time is unchanged (per-user/platform budget enforcement still uses it).
- **Subscription routes** (`writing-claude-sub*`, `writing-codex-sub*`) are never a charge. Their cost is `$0`
  ("Subscription — $0 incremental API cost"); for Claude models an API-equivalent value is shown separately.
- **Promotional credits**: gross API consumption, credits consumed in the window, estimated remaining and estimated
  out-of-pocket (gross − credits consumed) are four separate numbers. Spend is matched to a grant by provider code.
- **Live voice** audio goes browser → provider and never passes through the API, so it is metered in connected
  minutes (first provider-session mint → last saved turn, capped at 25 min) × the per-minute rate. Until the owner
  saves a rate the row is labelled "Assumed" (OpenAI $0.12/min, Gemini $0.04/min starting estimates). Calibrate once:
  provider live-voice invoice ÷ metered minutes.
- **Per-unit averages**: letter = (grading + reviewer) ÷ letters. Speaking card = single-card grading + review +
  audio judgement + one live session. Full mock = combined grading + one review + 2 × (audio judgement + live
  session). Shared items are counted once; totals are sums of the same rows, so they reconcile.
- **Recent graded runs** group a learner's pipeline rows within 45 minutes (usage rows carry no submission id).

## Speaking model and effort

`speaking.grade` runs Claude Opus 5.5 at effort **high** on the paid API (`AiGatewayService.GradingEffort`; it was
`max` before 2026-10-10) and the Claude Max sidecar is pinned to `WRITING_CLAUDE_EFFORT` (default `high`). A blank
model on the `anthropic` hop now resolves to `claude-opus-5-5` (`AiPipelineStore.ResolvePlanAsync`), never to the
provider row's Sonnet-class default. An explicit saved model is never overridden; the Model check flags it.

Writing grading: the paid-API hop sends no explicit effort, so Claude's own default (medium on Opus 5.5) applies.
This is reported, not changed: forcing high there changes grading behaviour and the 150 s hop budget.
