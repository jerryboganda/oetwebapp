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

### The learner is unpinned

The learner's model and provider used to be **hardcoded** (`deepseek-v4.1-flash` on `opencode`), so
nothing in the database, the environment or any admin screen could change the chatbot every real
learner uses. That is now a **default**, not a pin:

1. a thread's chosen model,
2. else the admin default saved at `/admin/ai-assistant/config`,
3. else `AssistantModelCatalog.LearnerDefaultModel` (`glm-5.3-flash` on `z-ai`).

What did **not** change is the learner's *entitlement*. The learner's model list is still the narrow
curated catalog (`AssistantModelCatalog.IsLearnerSelectable`), kept separate from the staff list so a
future widening of the staff picker cannot quietly widen what a learner may choose. Unpinning is about
who serves the turn, not about access.

### Failover

The assistant previously resolved exactly **one** provider and returned nothing when it was
unreachable, which made the default the only provider. Chains are now per-role:

| Role | Chain |
|---|---|
| Learner | Z.AI → `opencode` (`deepseek-v4.1-flash`) → Anthropic |
| Staff | Z.AI / saved route → Anthropic → `opencode` |

The order differs because the entitlement differs: a learner is never moved onto a staff-grade
provider, whereas a failed staff request can afford the longer walk. Each candidate is resolved before
use, so a fallback only appears if it is genuinely reachable, and every **attempt** writes its own
failure record — a permanently broken first provider is visible in `/admin/ai-usage` instead of being
invisible behind a successful second hop.

The "answered by X" label is emitted **after** a call succeeds, because the provider that serves the
turn is not known until then. Emitting it during routing would label a failed-over turn with the
provider that failed.

### Streaming

Streaming used to be gated on the **OpenCode host**, so every other provider silently took the
buffered path — and the assistant then *faked* streaming by slicing the buffered answer into 80-char
bursts, so a learner watched a visible stall followed by a wall of text. The gate is now the probed
`SupportsStreaming` flag, and `OpenCodeStreamingCall` is the generically-named
`StreamingChatCompletionsCall`.

Z.AI has no `stream_options` parameter at all, so it is never sent; usage arrives on the final SSE
chunk regardless, which is how the parser reads it.

The assistant still emits its answer in 80-char slices once the call completes — that is pacing, not a
transport problem, and it is now the only part of the path that is simulated.

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

## 7. What has actually been measured against this key

Measured 2026-10-09 by direct probe of `https://api.z.ai/api/paas/v4` with the owner's own key,
before enabling anything. Facts, not documentation:

| Check | Result |
|---|---|
| Key validity (`GET /models`) | **200.** The key authenticates. |
| Catalogue returned | 11 ids: `glm-4.5`, `glm-4.5-air`, `glm-4.6`, `glm-4.7`, `glm-5`, `glm-5-turbo`, `glm-5.1`, `glm-5.2`, `glm-5.3`, `glm-5.3-flash`, `glm-5.3-flashx` |
| `glm-5.3-flash` (the default) | **429, code 1113 — "Insufficient balance or no resource package. Please recharge."** |
| `glm-4.5-flash` (free tier) | **Runs.** Paid models cannot be used until the account is funded. |
| `GET /models` is not exhaustive | `glm-4.5-flash` runs but is **absent** from the catalogue; `glm-4.7-flash` is absent but was accepted (transient overload `1305`), not rejected as unknown. |

### Forced thinking — confirmed empirically, not assumed

`glm-4.5-flash`, same request, only the token budget varied:

```
max_tokens=32    content=""      reasoning_chars=138  finish_reason=length
max_tokens=512   content="ready" reasoning_chars=718  finish_reason=stop
```

The model spends the budget thinking and then returns **empty content with `finish_reason=length`**.
That is indistinguishable from a bad key, which is exactly the trap `RequiresThinkingBudget` in
`AiProviderConnectionTester` exists to avoid — the connection tester grants a real 512-token thinking
budget rather than the 1 token that would report a working key as broken.

### `response_format` — works, on the model that could be tested

`glm-4.5-flash` with `response_format: {type: "json_object"}` returned `{"ok":true}` — accepted, and
the content really was JSON. **Z.AI's OpenAPI schema is therefore incomplete rather than correct**,
which is the concrete reason the probe exists and the reason this table is the routing authority
instead of the spec.

**Still unverified for `glm-5.3-flash` specifically**, because the balance block prevented the call.
Re-run **Probe capabilities** after funding; it settles it per model and nothing else will.

---

## 8. Known risks — read before enabling anything

1. **The account has no balance.** Every paid model — including `glm-5.3-flash`, the owner-directed
   default — is refused with `1113`. Z.AI cannot be switched on until it is funded. Only the free
   tier runs today.
2. **`glm-5.3-flash`'s `response_format` support is unconfirmed.** Proven working on
   `glm-4.5-flash`; if it turns out to be unsupported on 5.3-FLASH, every JSON-gated feature code
   refuses Z.AI and those stay on Claude. The probe answers this in one click once funded.
3. **Forced thinking cannot be disabled**, and reasoning bills as output tokens. Confirmed above.
   Seeded at `reasoning_effort=low`; alert-only monitoring.
4. **GLM tool-call correctness is unmeasured here.** `SafetyGuard` gates what is *permitted*, not
   whether the model executes correctly. The admin chatbot keeps its mutating toolset by owner
   decision, which is a real risk, not a solved one.
5. **Z.AI rate limits are unconfirmed.** RPM/TPM for pay-as-you-go could not be verified. Z.AI rides
   the shared platform-key gate today rather than a vendor-specific lane; size one after real numbers.
6. **Data residency.** Learner prompts — which can include a learner's own draft Writing letter and
   the clinical detail they typed — go to Zhipu AI. The owner accepted this on 2026-10-09 with Z.AI's
   terms unverified. Re-read them before the learner unpin.

---

## 9. Enabling it, in order

The key is **not** in the system. The row ships inactive, keyless and auto-select off.

1. `/admin/ai-providers` → the **Z.AI (GLM)** row → paste the key → **Save**.
2. **Test** — green means the key and the endpoint are good. It will report *failed* while the balance
   is empty, and that failure is about funds, not credentials.
3. **Probe capabilities** — records what each model actually does and settles `response_format` for
   `glm-5.3-flash`. Do this before any routing.
4. Tick **Active**.
5. Decide **Automatic selection**. Leave it OFF: GLM is reached where a route points at it, which is
   what you want, and OFF is what guarantees adding it changed nothing you did not choose.
6. `/admin/ai-assistant/config` to set the learner/staff defaults; the catalog default is
   `glm-5.3-flash`.

Rollback at any point: turn auto-selection off, clear the feature routes pointing at `z-ai`, or set the
row inactive. No deploy is required for any of these.

---

## 10. Rollback

The provider is inert by default, so rollback is mostly subtraction:

- Turn **Automatic selection** off → nothing unrouted can reach it.
- Clear the `AiFeatureRoutes` rows pointing at `z-ai`.
- Deactivate the row (or clear the key) from `/admin/ai-providers`.
- Remove the GLM hop from the four pipeline stages — the owner-set order lives in `AiPipelineStore`,
  so do it from `/admin/ai-pipelines`, never by editing `AiPipelineStages` in code.
- Code rollback: `gh workflow run production-deploy.yml -f sha=<previous-sha>`. The migration is
  additive and its `Down` drops only the new table and the new column.