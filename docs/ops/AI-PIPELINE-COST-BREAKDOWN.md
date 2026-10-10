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
- **Live voice** audio goes browser → provider and never passes through the API, so two inputs are combined:
  connected minutes (first provider-session mint → last saved turn, capped at 25 min) × the per-minute rate, and
  the token usage the browser forwards at stop (`POST /v1/speaking/realtime/sessions/{id}/usage`, stored as a
  `SpeakingPatientTurns` row with role `live_usage`: provider, model, token counts, no content). When the owner has
  saved BOTH token rates for a provider and the report is a known kind (the provider's own end-of-session total, or
  OpenAI's per-response usage), the session is priced from tokens; Gemini's per-message metadata is shown but not
  priced because its cumulative/per-turn meaning is not established. Everything else is priced from minutes.
  Until the owner saves a rate the row is labelled "Assumed" (OpenAI $0.12/min, Gemini $0.04/min starting
  estimates). Sessions whose mint audit was wiped by retention before this release have no recorded provider and are
  shown as "Not priced" (the Speaking total is flagged partial). From this release the sweep keeps the
  `provider:model` text of the mint row (no personal data), so newer sessions stay attributable.
- **Per-unit averages**: letter = (grading + reviewer) ÷ letters. Speaking card = single-card grading + review +
  audio judgement + one live session. Full mock = combined grading + one review + 2 × (audio judgement + live
  session). Shared items are counted once; totals are sums of the same rows, so they reconcile.
- **Recent graded runs** group a learner's pipeline rows within 45 minutes (usage rows carry no submission id).

## Speaking model and effort

`speaking.grade` runs Claude Opus 5.5 at effort **high** on the paid API (`AiGatewayService.GradingEffort`; it was
`max` before 2026-10-10) and the Claude Max sidecar is pinned to `WRITING_CLAUDE_EFFORT` (default `high`). A blank
model on the `anthropic` hop now resolves to `claude-opus-5-5` (`AiPipelineStore.ResolvePlanAsync`), never to the
provider row's Sonnet-class default. An explicit saved model is never overridden; the Model check flags it.

Writing grading (`writing.grade`, `writing.sample_score`) now runs the same approved effort on the paid API: adaptive
thinking at **high** with the 128k output ceiling (it sent no effort before, so Claude's default applied). The
built-in default time limit of the Writing API hop is 300 s (was 150 s); a saved order keeps its own number, so raise
"Seconds per attempt" on that step if high-effort grades hit the limit.

## Reconciliation

`GET /v1/admin/ai/pipelines/cost-breakdown/reconciliation?window=` (button "Run reconciliation") recomputes the totals
through independent paths: stage components + other features vs the full usage ledger, gross vs ledger + live voice,
call counts, Claude models missing from the rate card, the re-pricing delta vs stored estimates, graded letters vs
completed Writing evaluations, and live voice attribution/reporting. It is admin-only, on demand, read-only.
