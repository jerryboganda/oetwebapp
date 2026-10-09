# Z.AI (GLM) provider

Owner directive 2026-10-09. This is the runbook for the `z-ai` provider: what it is, how the key
reaches it, how its capabilities are established, and what it is and is not allowed to serve.

**Status as shipped:** registered, keyless, inactive, and NOT auto-selectable. Nothing in production
routes to Z.AI until you paste the key, run **Test**, run **Probe capabilities**, and opt it in.

---

## 1. What it is

| | |
|---|---|
| Vendor | Z.AI (Zhipu AI / BigModel), GLM model family |
| Base URL | `https://api.z.ai/api/paas/v4` (international platform) |
| Provider code | `z-ai` |
| Default model | `glm-5.3-flash` |
| Dialect / category | `OpenAiCompatible` / `TextChat` |
| Seeded priority | `100` — behind `anthropic` (20) on purpose |
| Rates | `$0.15` in / `$0.50` out per 1M tokens for `glm-5.3-flash` |

The China platform (`https://open.bigmodel.cn/api/paas/v4`) is a **different account with a different
balance** even though it accepts the same `{32-hex}.{16-char}` key shape. This integration is pinned
to the international platform deliberately.

Code: `backend/src/OetLearner.Api/Services/Seeding/ZaiProviderDefaults.cs`.

### Why `glm-5.3-flash` and not the flagship

The assistant sends image and document attachments, and many call sites emit strict JSON. `glm-5.3`
is **text-only**. `glm-4.6v` / `glm-4.5v` have vision but **no JSON mode**. The Flash line is the
only current GLM family that is multimodal, tool-calling and JSON-capable at `$0.15/$0.50` per 1M.

---

## 2. Getting the key in

Two independent channels, deliberately:

| Channel | Variables | Purpose |
|---|---|---|
| Dedicated (preferred for first boot) | `ZAI__ApiKey`, `ZAI__BaseUrl`, `ZAI__DefaultModel` | Creates the `z-ai` row at boot |
| Dashboard (the real source of truth) | `/admin/ai-providers` | Paste / rotate the key, encrypted |

**Do not set `AI__ProviderId=z-ai`.** `AI__ProviderId` does double duty: it is both the row code and
the DI name of the legacy env-only provider (`OpenAiCompatibleProvider.Name`). Because `AiGatewayService`
matches `p.Name == request.Provider` *before* consulting the registry, that value would create a
shadow provider reading the env key and the runtime-settings base URL, bypassing the registry's
encrypted key entirely. Dedicated variables make that impossible.

### Create-only, and it now says so

Per the 2026-10-09 directive, an **existing row is never rewritten from the environment**. After the
row exists, the dashboard owns the key, the models and the on/off switch. The bootstrapper now logs
when env is being ignored — previously it returned in silence, which is how an env edit becomes a
mystery six months later. A blank `ZAI__ApiKey` also logs a warning instead of creating nothing
without a trace.

---

## 3. Capabilities are probed, never assumed

This is the part that matters most. **Z.AI's published sources contradict each other**: the OpenAPI
schema binds `glm-5.3-flash` to a vision request shape whose complete key list contains **no
`response_format`**, while the model page advertises Structured Output and recommends `tool_stream`.
Both cannot be true. Documentation cannot settle it; one observed call can.

Press **Probe capabilities** on `/admin/ai-providers`. It calls the live endpoint with your real key
and stores what each model *actually did*: tools, images, documents, JSON mode, streaming,
embeddings, thinking, and whether thinking can be switched off.

- Server-side, on your click. Never a scheduled job, never a CI step.
- Costs a handful of very small requests.
- Re-run it after a vendor model-lineup change.

**Fail closed.** A missing or failed probe means *unknown*, and unknown is treated as incapable.
A feature route onto an unproven model is refused at save time with the specific missing capability
named — so a misconfiguration surfaces as a clear refusal rather than a mystery 400 in production.

The requirement each feature code declares lives in `AiFeatureCapabilityRequirements.cs`, beside the
codes themselves. It is code, not database rows, deliberately: it is a statement about what each
feature *does*, and a new feature code cannot ship without someone deciding what it needs. All 63
routable codes are covered.

---

## 4. Vendor request limits the code enforces

Each of these is a **whole-request HTTP 400** from Z.AI (error code `1214`), not a warning, so they
are resolved centrally in `AiProviderPayloadBuilder` rather than left to call-site discipline:

| Limit | Rule | Handling |
|---|---|---|
| `temperature` | range `(0, 1]`; `0` is rejected | clamped up to the vendor floor |
| `tool_choice` | only `"auto"` exists | anything else is omitted, not sent |
| `max_tokens` | ceiling 131072 | clamped |
| `stream_options` | does not exist on Z.AI | never sent; usage arrives on the last SSE chunk anyway |
| `response_format` | contradicted by Z.AI's own docs | sent only when the probe says the model supports it |

### Forced thinking is shown as forced

GLM-5.3 and GLM-5.3-FLASH have thinking **on by default and it cannot be disabled**. `reasoning_effort`
defaults to `max`. Reasoning output is billed inside `completion_tokens` at the normal output rate —
Z.AI's `usage` object has no separate reasoning field.

So the reasoning control is per-model and **truthful**: the dropdown offers only the levels the
selected model honours, and a forced-on model is rendered as locked with the reason shown. Offering
an "off" that does nothing would be the worst possible outcome.

The row is seeded with `reasoningEffort = low`, the only real lever available. Spend alerting is
**alert-only and never blocks** a user turn.

---

## 5. What Z.AI may serve

| Surface | Role |
|---|---|
| Admin chatbot (`ai_assistant.admin`) | primary, full toolset |
| Learner chatbot (`ai_assistant.learner`) | primary |
| Expert / tutor console (`ai_assistant.expert`) | primary — one feature code serves both `/expert` and `/tutor` |
| `WritingGrade`, `SpeakingGrade`, `WritingReview`, `SpeakingReview` | **last hop only**, after Max → Anthropic API → Codex |
| `LiveVoice` | **never** — realtime speech, which GLM does not do |
| OCR, STT, TTS, phoneme, PDF-extraction, embeddings | refused at route-save time by the capability gate |

### Ordering rules that are not negotiable

- GLM must appear **after** `MaxProvider` in the built-in `WritingGrade` / `SpeakingGrade` order.
  `verify-pipeline-contract.mjs:633-639` **fails the build** if it does not.
- `AiPipelineStore.cs` remains the only writer of the saved order. No code path may write
  `AiPipelineStages.Add(` or `.ChainJson =` outside it.
- A non-Claude scoring switch still requires a benchmark run via `IAiProviderRouteApprovalService`.

### Auto-selection

A provider row is a candidate for "the first active credentialed row" only when
`ParticipatesInAutoSelection` is on. This is **stored per row** — it used to be a hardcoded code
list, which meant a vendor's reachability was fixed in C# and only a deploy could change it.

Every newly seeded or env-created row starts **OFF**, so adding a vendor never silently changes who
answers Reading explanations or vocabulary cards. `/admin/ai-providers` shows **which provider wins
that pick right now** and, for each row, why it cannot.

The introducing migration backfills `true` for every pre-existing row except `opencode` — exactly
the set the old rule admitted — so no live routing changes when it deploys.

---

## 6. Cost accounting was wrong until this landed

The env bootstrapper hardcoded `0.015` / `0.075` **per 1K tokens** = `$15` / `$75` per 1M. Real Z.AI
rates for `glm-5.3-flash` are `$0.15` / `$0.50` per 1M. The seeded values were **100× too high in
both directions**, which would have made the AI usage dashboard actively misleading about exactly the
thing you would use it to judge: whether the last-resort pipeline hops are firing more than expected.

`ZaiProviderDefaults.RatesPer1k` now carries the researched rates for every current GLM model, and
`AiProviderEnvSeedDefaults` derives both the display name and the pricing from a per-code table so a
Z.AI row is never labelled DigitalOcean.

**Free to call:** `glm-4.7-flash`, `glm-4.5-flash`. They appear in the picker with an honest "free"
label and are never a default — a vendor's free tier is the first thing it changes, and the learner
chatbot must not depend on that.

---

## 7. Known risks — read before flipping anything on

1. **`response_format` may be unavailable** on `glm-5.3-flash`. If the probe says no, every
   JSON-gated feature code refuses Z.AI. Those features stay on Claude, or use prompt-based JSON with
   a validating retry. This is the single most likely finding to change part of the design.
2. **Forced thinking cannot be disabled**, and reasoning bills as output tokens. Seeded at `low`;
   alert-only monitoring. If volume proves uncomfortable, move to a non-forced model — at the cost of
   image turns.
3. **GLM tool-call correctness is unmeasured here.** `SafetyGuard` gates what is *permitted*, not
   whether the model executes correctly. The admin chatbot keeps its mutating toolset by owner
   decision, which is a real risk, not a solved one.
4. **Z.AI rate limits are unconfirmed.** RPM/TPM for pay-as-you-go could not be verified. Z.AI needs
   its own concurrency lane and circuit breaker sized after real numbers; today it shares the
   generic platform-key path.
5. **Data residency.** Learner prompts — which can include a learner's own draft Writing letter and
   the clinical detail they typed — go to Zhipu AI. The owner accepted this on 2026-10-09 with Z.AI's
   terms unverified. Re-read them before the learner unpin.

---

## 8. Rollback

The provider is inert by default, so rollback is mostly subtraction:

- Turn `ParticipatesInAutoSelection` off → nothing unrouted can reach it.
- Clear the `AiFeatureRoutes` rows pointing at `z-ai`.
- Remove the GLM hop from the four pipeline stages (owner-set order lives in `AiPipelineStore`).
- Set `is_active = false` on the row, or deactivate from the admin screen.
- Code rollback: `gh workflow run production-deploy.yml -f sha=<previous-sha>` — the migration is
  additive and its `Down` drops only the new table and column.