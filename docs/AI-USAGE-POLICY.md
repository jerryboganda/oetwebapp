# AI Usage Policy & Options Reference

> **Status:** authoritative. This document is the single source of truth for
> every configurable policy in the AI Usage Management subsystem. The code
> references these options by name; admin UI surfaces them using the same
> vocabulary. Defaults are picked to be **safe, legally defensible, and
> commercially sensible** for an OET exam-grading product. Alternatives are
> listed so the platform can be re-tuned without a code change.

---

## 0. Design principles

1. **Grounding is non-negotiable.** Every AI call routes through
   the coordinator (`IAiGatewayService` / `IDirectAiCallRecorder`) with a
   rulebook-grounded prompt built server-side (R-a retired the TS
   `buildAiGroundedPrompt()`; no production caller remains). The gateway physically refuses ungrounded
   prompts. No policy below may weaken this.
2. **Scoring integrity over convenience.** Any feature whose output materially
   affects a learner's OET score prediction is treated as *scoring-critical*
   and is protected against credential-source drift by default.
3. **Every default is overridable by admins**, never by learners. Learners
   can only toggle preferences the admin allows.
4. **No option breaks the audit trail.** One `AiUsageRecord` is written per
   **physical** provider call, regardless of credential source, provider,
   outcome, or feature.

---

## 1. Credential-source options

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `AiCredentialMode` (per user) | Which source the resolver should prefer | `auto` | `byok-only`, `platform-only`, `auto` |
| `AllowPlatformFallback` (per user) | If BYOK errors, may we transparently use platform credits? | `true` | `false` |
| `AllowByokOnScoringFeatures` (global) | Admin switch: allow BYOK on score-affecting calls at all | `false` | `true` |
| `AllowByokOnNonScoringFeatures` (global) | Admin switch: allow BYOK on practice/conversation/summarisation | `true` | `false` |
| `DefaultPlatformProviderId` (global) | Provider used when no BYOK applies | `digitalocean-serverless` | any registered `AiProvider` |

**Why `auto` + `AllowPlatformFallback=true` by default:** it gives learners the
cost benefit of their own key when it works and a clean experience when it
doesn't. Silent failure is worse than a small banner saying we fell back.

**Why `AllowByokOnScoringFeatures=false` by default:** a learner paying for a
score prediction trusts our grading. If their key silently routes through a
smaller model they self-selected, we still own the UX failure. Keep scoring
homogeneous.

---

## 2. Quota unit options

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `QuotaUnit` (global) | What is metered | `tokens` | `requests`, `usd_estimate` |
| `TokensPerCreditDisplay` (global) | How many raw tokens equal one learner-visible "AI credit" | `1000` | any integer ≥ 100 |
| `CreditRoundingMode` (global) | Rounding for learner display | `ceil` | `floor`, `nearest` |

**Why tokens + 1k display unit:** tokens are the only unit that all providers
report consistently. Showing "3,412,771 tokens" to a learner is hostile;
showing "3,413 credits" is not.

---

## 3. Period & reset-policy options

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `QuotaPeriod` (per plan) | Length of the billing window | `monthly` | `daily`, `weekly`, `rolling_30d`, `never_expire` |
| `QuotaResetAlignment` (per plan) | When the period ticks over | `calendar_month` | `subscription_anniversary`, `utc_midnight` |
| `RolloverPolicy` (per plan) | What happens to unused credits at reset | `expire` | `rollover_capped`, `rollover_full` |
| `RolloverCapPct` (per plan) | If `rollover_capped`, max carry-over as % of plan cap | `20` | 0–100 |
| `DailySafetyCapPct` (per plan) | Hard daily ceiling as % of monthly cap, to stop abuse | `25` | 0–100 (0 disables) |

**Why calendar-month + expire + 25% daily cap:** simple to explain, protects
the platform from a single learner burning a month's budget in a day, avoids
the engineering complexity of rolling windows unless you need them.

**Rolling-30d** is the most "fair" option but requires window-aware counters
rather than period counters. Available when you're ready for that complexity.

---

## 4. Overage policy options

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `OveragePolicy` (per plan) | What happens when quota is exhausted | `deny` | `allow_with_charge`, `auto_upgrade`, `degrade_to_smaller_model` |
| `DenyMessage` (per plan) | Copy shown to learner on `deny` | localised default | any string |
| `OverageRatePer1kTokens` (per plan) | Price per 1k tokens when `allow_with_charge` | `null` | decimal ≥ 0 |
| `AutoUpgradeTargetPlan` (per plan) | Which plan to auto-bump to | `null` | any higher plan code |
| `DegradeModel` (per plan) | Which cheaper model to use on degrade | `null` | any allow-listed model |

**Why `deny` by default:** it's the only overage policy that can't surprise a
learner with a bill. `allow_with_charge` and `auto_upgrade` are valuable but
require the billing UX to show the consent flow. Ship `deny` first, enable
others per plan when billing consent is wired.

---

## 5. Feature-eligibility matrix (admin-editable)

Every feature the gateway serves is classified. Defaults:

| Feature code | Scoring-critical | BYOK default | Platform default | Notes |
|---|---|---|---|---|
| `writing.grade` | ✅ | ❌ | ✅ | Mock / practice grading |
| `writing.sample_score` | ✅ | ❌ | ✅ | Sample scoring |
| `speaking.grade` | ✅ | ❌ | ✅ | Speaking evaluation |
| `mock.full_grade` | ✅ | ❌ | ✅ | Full mock exam grading |
| `writing.coach.suggest` | ❌ | ✅ | ✅ | Inline suggestions |
| `writing.coach.explain` | ❌ | ✅ | ✅ | Why-is-this-wrong explanations |
| `conversation.opening` | ❌ | ✅ | ✅ | AI partner's first in-role utterance |
| `conversation.reply` | ❌ | ✅ | ✅ | AI partner's in-role replies mid-session |
| `conversation.evaluation` | ✅ | ❌ | ✅ | Post-session scoring + rubric (scoring-critical) |
| `reading.explanation.v1` | ❌ | ✅ | ✅ | Post-submit, grounded Reading explanation; advisory only |
| `listening.explanation.v1` | ❌ | ✅ | ✅ | Post-submit, grounded Listening explanation; advisory only |
| `pronunciation.tip` | ❌ | ✅ | ✅ | Pronunciation feedback |
| `pronunciation.score` | ✅ | ❌ | ✅ | Pronunciation attempt scoring / phoneme analysis |
| `pronunciation.linguistic.score.v1` | ✅ | ❌ | ✅ | Gemini native-audio linguistic pronunciation scoring through provider code `gemini-pronunciation-audio` |
| `pronunciation.feedback` | ❌ | ❌ | ✅ | Grounded learner-facing pronunciation coaching |
| `summarise.passage` | ❌ | ✅ | ✅ | Study-notes summarisation |
| `vocabulary.gloss` | ❌ | ✅ | ✅ | Word gloss |
| `admin.content_generation` | ❌ | ❌ | ✅ | Admin tooling, platform only |
| `admin.grammar_draft` | ❌ | ❌ | ✅ | Grammar lesson drafting (grounded), platform only |
| `admin.pronunciation_draft` | ❌ | ❌ | ✅ | Pronunciation drill drafting (grounded), platform only |
| `admin.conversation_draft` | ❌ | ❌ | ✅ | Conversation scenario drafting (grounded), platform only |
| `admin.vocabulary_draft` | ❌ | ❌ | ✅ | Vocabulary term drafting (grounded), platform only |
| `admin.listening_draft` | ❌ | ❌ | ✅ | Listening 42-item structure drafting from PDFs (grounded), platform only |
| `admin.reading_draft` | ❌ | ❌ | ✅ | Reading extraction drafting, platform only; human approval required |
| `companion.chat.v1` | ❌ | ❌ | ✅ | AI Learning Companion turn (persona "Sami"). Non-scoring, but **platform-only**: the prompt carries learner performance history, entitlement scope and credit state, so a learner-supplied key must never see it. |
| `companion.retrieval.v1` | ❌ | ❌ | ✅ | Companion knowledge retrieval (embedding + hybrid search over approved sources). Platform-only for the same reason, and because the candidate source set is entitlement-filtered. |
| `companion.action.v1` | ❌ | ❌ | ✅ | Companion typed platform action (server-resolved targets only). Platform-only — actions read and write learner state. |

**Default routing (locked):** every text-LLM feature code above defaults to
Anthropic `claude-sonnet-4-6` (provider code `anthropic`) with an OpenAI
`gpt-4o` fallback, via `AiFeatureRouteDefaults`. Admin per-feature DB routes
override this; a key-guard in the resolver falls through to the keyed
top-priority provider when no Anthropic key is configured. Exceptions stay on
their own providers: `pronunciation.linguistic.score.v1` (Gemini native audio)
and `class.recording.transcribe.v1` (Whisper STT).

### Direct (non-gateway) AI calls — recorded for traceability

These calls do not flow through the gateway (OCR has no chat route; STT and the
Listening Part A Claude call are direct) but each writes exactly one
`AiUsageRecord` via `IDirectAiCallRecorder`, so they surface in
`/admin/ai-usage` and ai-analytics like any other call. They are NOT in
`AiFeatureRouteResolver.KnownFeatureCodes` (not gateway-routable):

| Feature code | Provider | Notes |
|---|---|---|
| `ocr.listening.parta` | `mistral-ocr` | OCR of Listening Part A question paper + answer key |
| `ocr.content.pdf_fallback` | `mistral-ocr` | Scanned-PDF fallback in `AutoPdfTextExtractor` (covers all content/question-bank imports) |
| `ocr.writing.handwriting` | `mistral-ocr` | OCR of a learner's handwritten Writing submission |
| `listening.parta.extract` | `anthropic` | Claude manifest-structuring (`claude-sonnet-4-6`) with real token counts + cost |
| `stt.speaking.transcribe` | `whisper-asr` | Speaking attempt transcription |
| `stt.pronunciation.transcribe` | `whisper-asr` | Pronunciation ASR transcription |
| `stt.conversation.transcribe` | `whisper-asr` | Conversation ASR transcription |
| `jev.writing.guard` | `typesafe-jev` | TypeSafe SystemOne (Jev) pre-gateway Writing guard: parallel Nouls for injection / rule-evasion / abuse / gibberish. Negative gate only — may block or flag to tutor, never auto-pass. Non-scoring, platform-only; master switch `TypeSafe:Enabled`. |
| `jev.writing.route` | `typesafe-jev` | Jev Writing request routing (Choice + confidence gate); low confidence falls back to the default path. Non-scoring, platform-only. |
| `jev.writing.verify` | `typesafe-jev` | Jev post-gateway citation verification of AI findings (supported / contradicted / not-in-evidence); low-confidence or contradicted findings go to the tutor review queue. Non-scoring, platform-only; never overrides the gateway verdict. |
| `jev.writing.criteria` | `typesafe-jev` | Jev advisory per-criterion Writing signals (parallel Scores in one call), combined with code-owned weights. Display-only radar — never a grade input. Non-scoring, platform-only. |
| `jev.companion.rerank` | `typesafe-jev` | Jev rerank of companion hybrid-retrieval candidates (one Score per candidate, downstream of the entitlement prefilter). Ordering-only, advisory — never a filter of record, never a grading path; rerank outage keeps the original hybrid ordering. Non-scoring, platform-only. |
| `jev.conversation.turn` | `typesafe-jev` | Jev advisory judgment of the learner's latest AI-patient role-play turn (stays-in-role / clinically-appropriate / unsafe Nouls, one parallel call). Informational only — never gates, scores, or ends a session. Non-scoring, platform-only. |
| `jev.response.verify` | `typesafe-jev` | Jev advisory review of a gateway response (evidence relation, addresses-task, unsafe-recommendation; `JevWorkflowAdvisor`). Advisory only — never changes a grade or the gateway verdict; switches `TypeSafe:Enabled` + `ResponseVerifyEnabled`. Non-scoring, platform-only. |
| `jev.development.triage` | `typesafe-jev` | Jev typed triage for internal development/review tooling (AdminBatch class). Never on a learner path. Non-scoring, platform-only. |

**TypeSafe SystemOne (Jev):** judgments only — the model returns typed
Choice/Noul/Score answers, never text. Provider code `typesafe-jev` resolves
its key from `TypeSafe:ApiKey` (server-side only, never a client bundle);
calls flow through `TypeSafeJudgmentService` → `IDirectAiCallRecorder` like
the other direct calls above and are budget-metered in the InteractiveLearning
class. Jev output never overrides the rulebook-grounded gateway verdicts, the
deterministic WritingRuleEngine, or the GEPA placement engine, and its
thresholds are tuned on our own recorded judgments before any flag flips.

**Unified Whisper:** all STT resolves the `whisper-asr` AI-provider row first
(one key covers Speaking, Pronunciation, and Conversation), then legacy
section settings (`Speaking:Whisper:*` / `Conversation:Whisper:*`), then mock.

Admin can toggle the BYOK column per feature. `AllowByokOnScoringFeatures`
global switch gates the scoring-critical rows.

---

## 6. Failure & fallback options

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `ByokErrorCooldownHours` (global) | After a 401/403 on a user key, how long before we retry it | `24` | any integer hours |
| `ByokTransientRetryCount` (global) | How many 429/5xx retries before falling through | `2` | 0–5 |
| `ProviderRetryPolicy` (per provider) | Polly policy preset | `exponential_2_30s` | `none`, `linear_3_10s`, `aggressive_5_60s` |
| `ProviderCircuitBreakerThreshold` (per provider) | Failures before breaker trips | `5` | any integer |
| `ProviderCircuitBreakerWindowSeconds` (per provider) | Rolling window for the counter | `30` | any integer |
| `FailoverOrder` (global) | Ordered list of provider IDs to try on circuit-breaker open | `[digitalocean-serverless]` | any subset of registered providers |

---

## 7. Global safety controls

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `AiGlobalKillSwitch` | Hard-disable all AI calls platform-wide | `false` | `true` |
| `EnforceSpendCaps` | Whether the platform USD caps (global budget + hard-kill, global daily cap, class daily/monthly caps) may refuse a call | `false` | `true` |
| `AiGlobalBudgetUsd` | Hard monthly USD cap across all platform-keyed calls | admin-supplied, no default | any decimal ≥ 0 |
| `AiGlobalBudgetSoftWarnPct` | Email admins at this % of budget | `80` | 0–100 |
| `AiGlobalBudgetHardKillPct` | Auto-engage kill switch at this % | `100` | 0–150 |
| `AiAnomalyDetectionEnabled` | Flag users whose daily spend ≥ `AnomalyMultiplier` × their 7-day median | `true` | `false` |
| `AnomalyMultiplierX` | Multiplier that triggers flagging | `10` | any decimal ≥ 2 |

Kill-switch semantics: when engaged, platform-keyed calls throw
`AiGloballyDisabledException`. **BYOK calls continue** — the switch protects
the platform budget, not learner sovereignty over their own key.

**Platform spend caps are an admin switch, OFF by default (owner directive,
2026-10-02: "delete the monthly, weekly and daily AI caps").** While
`AiGlobalPolicy.EnforceSpendCaps` is off, no platform USD cap refuses a call:
neither the `AiQuotaService` hard-kill nor any `AiBudgetService` reservation
(global month/day, class month/day, scoring borrow) denies, but every budget
period is still reserved, committed and released, so `CurrentSpendUsd`,
`/admin/ai-usage` and the budget alerts keep showing real spend. Turning it on
(`/admin/ai-usage → Budget & Kill-switch`) restores the caps exactly as
described in this section. Not affected by the switch: the kill switch, the
per-feature kill list, per-user AI disable, plan token caps, learner credits and
the per-learner caps listed below. The Claude Max weekly usage estimate on
`/admin/writing-ai` is information only and never switches Writing providers.

**Claude Max is always on (owner hard rule MAX-ALWAYS-ON, 2026-10-02).** The
Writing subscription route (`writing-claude-sub`) is tried first on every grade
and can never be switched off or bypassed by an admin toggle, mode, marker,
threshold or circuit. The admin API refuses it with `max_subscription_always_on`:
`PUT /v1/admin/ai/writing-provider` accepts only `auto`/`claude` (a weekly
threshold or quota marker is stored but inert), and the `writing-claude-sub`
provider row cannot be deactivated or deleted (its other fields stay editable).
The Anthropic API and Codex are used only inside a single grade after Max
actually errors.

**Credit-funded Writing grades skip the plan gate (WAI-01, 2026-10-02).** A
Writing grade that holds a learner credit reservation (or a verified free
sample) is sent with `FreeSampleGrant=true`, exactly as a credit-funded Speaking
grade is: `AiQuotaService` then skips plan resolution, the plan feature list and
the plan token caps, and the grade's tokens are not committed to the learner's
plan counters, because the learner already paid with credits (live 1 Oct 2026:
`feature_not_in_plan` refused paid grades on the default plan). The per-feature
kill list, the kill switch, the platform budget (while `EnforceSpendCaps` is on)
and per-user AI disable still apply. The grant is server-derived only; it is
never read from a request.

**Admin-side AI carries no day or class-month ceilings (owner directive,
2026-09-23).** `AdminBatch`-class calls — `admin.*` content drafts,
listening Part A/B/C extraction, OCR content-PDF fallback, AI-assistant
indexing/embeddings, `class.recording.*`, `tutor.*`, the `ai_assistant.admin`
and `ai_assistant.expert` assistants — skip the global daily cap, the class
daily cap and the class monthly cap entirely. They remain governed by the
global monthly budget (`AiGlobalBudgetUsd` + hard-kill), the kill switch, the
per-feature kill list, and full `AiUsageRecord` accounting. Student-facing
classes (`ScoringCritical`, `InteractiveLearning`) keep every daily and
monthly ceiling, as do all per-learner caps (coach cost cap, companion daily
cap, daily plan regens, plan token caps). Implementation:
`AiBudgetClasses.IsBudgetExempt` + the `admin_batch.unrestricted` quota
trace.

---

## 8. Custody & security options

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `KeyEncryptionStrategy` | At-rest encryption for stored credentials | `aspnet_data_protection` | `aws_kms`, `azure_key_vault`, `hashicorp_vault` |
| `KeyRingPersistencePath` | Filesystem path for Data Protection key ring | `/var/lib/oet/keyring` | any writable path or blob URL |
| `RequireStepUpMfaForCredentialChange` | Force re-auth to add/rotate/revoke keys | `true` | `false` |
| `ValidateOnSave` | Ping provider before storing the key | `true` | `false` |
| `StoreResponseBodies` | Persist full AI response text in audit records | `false` | `true` (requires consent gate) |
| `ResponseSamplingPct` | If stored, % of responses sampled for QA | `0` | 0–100 |
| `CredentialValidateRateLimitPerMinute` | Per-IP limit on the validate endpoint | `5` | 1–60 |

**Why `StoreResponseBodies=false` by default:** audit completeness is served
by hashes + metadata + grounding version. Storing full bodies creates a
cross-user data-leakage surface, an unnecessary retention obligation, and a
larger PII footprint. Enable only with sampling + consent wiring.

---

## 9. Observability & alerting

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `UsageAggregationFrequency` | How often aggregation views refresh | `hourly` | `realtime`, `daily` |
| `AlertEmailRecipients` | Who receives budget/anomaly emails | admins with `SystemAdmin` | explicit list |
| `AlertChannels` | Where alerts go | `[email]` | `email`, `webhook`, `sms`, `slack` |
| `RetainUsageRecordsDays` | How long raw `AiUsageRecord` rows are kept | `395` | 30–3650 |
| `RetainAggregatesDays` | How long aggregate rows are kept | `3650` | 365–∞ |

**Why 395 days of raw retention:** covers a full annual reporting cycle plus
30 days of reconciliation. Long enough for finance, short enough for privacy.

---

## 10. Learner-visible presentation

| Option | Meaning | Default | Alternatives |
|---|---|---|---|
| `ShowBYOKOption` (global) | Is BYOK visible in `/settings/ai`? | `true` | `false` (disables feature entirely) |
| `ShowExactTokenCounts` | Show raw tokens in learner UI | `false` | `true` |
| `ShowFallbackBanner` | Show banner when platform fallback engages | `true` | `false` (silent) |
| `CreditGaugeStyle` | Dashboard widget rendering | `dual_ring` | `single_bar`, `numeric_only` |
| `LowCreditWarningPct` | At what % remaining to warn the learner | `15` | 0–50 |

---

## 11. Option application order (decision log)

When two options conflict, precedence is:

1. `AiGlobalKillSwitch=true` → refuses everything (even BYOK for admin-flagged
   platform-maintenance cases; kill switch has a `scope` enum:
   `platform_keys_only` (default) or `all_calls`).
2. Feature classification → scoring-critical with
   `AllowByokOnScoringFeatures=false` → platform-only, no BYOK.
3. User `AiCredentialMode` → honoured within global policy bounds.
4. `AllowPlatformFallback` → honoured when upstream BYOK fails.
5. Quota state → `OveragePolicy` determines behaviour once exhausted.

Every decision is logged with its decisive rule in the `AiUsageRecord.policyTrace`
field so admin explorer can answer "why did this call use that credential?"

---

## 12. Non-configurable invariants (for clarity)

These are NOT options. Changing them requires a code change and a new major
version of this document:

- Grounding enforcement in `AiGatewayService` (refuses ungrounded prompts).
- Rulebook version header is always stamped on every call's audit row.
- Keys are never returned to the client after save.
- Every AI call produces exactly one `AiUsageRecord` row, regardless of
  outcome (success, provider error, quota denied, kill-switch denied).
- `AiCredentialMode=platform-only` cannot be overridden by a BYOK feature flag.

Scope: these invariants govern platform AI calls made through the coordinator.
The owner's personal subscription agents (§20) are outside that path by design
and are governed by §20 instead.

---

## 18. GitHub Copilot / GitHub Models provider

GitHub Copilot ships as a registered provider via the **GitHub Models REST
API** — OpenAI-compatible chat-completions at
`https://models.github.ai/inference/chat/completions`. Wired through the
existing `IAiGatewayService` like any other provider; no policy bypass.

**Setup**

- Register at `/admin/ai-providers` → preset **GitHub Copilot / Models**
  (sets `Code = "copilot"`, `Dialect = Copilot`).
- Paste a fine-grained GitHub PAT, scope `models:read`. Stored encrypted
  via `IDataProtectionProvider` purpose `"AiProvider.PlatformKey.v1"`.
- Default `IsActive = false` until privacy review completes (writing
  samples and conversation transcripts may carry patient-style PII).
- For org-attributed billing / higher rate limits, swap the base URL to
  `https://models.github.ai/orgs/{ORG_LOGIN}/inference`.

**Auth modes**

- **Platform PAT**: default. Per-feature platform-only allowlists in
  `IAiCredentialResolver` apply unchanged (scoring features refuse BYOK).
- **Admin BYOK PAT**: pasted as `ApiKey`, treated identically.
- **Per-learner GitHub OAuth: NOT supported** — students aren't expected
  to own Copilot subscriptions, and per-user GitHub identity would
  cross-contaminate `AiUsageRecord.UserId`.

**Boundaries**

- Text completions only. **Voice** (TTS / ASR) stays on
  `IConversationTtsProviderSelector`, `IConversationAsrProviderSelector`,
  and `IPronunciationAsrProviderSelector` (ElevenLabs / Azure / Whisper /
  Deepgram / Gemini native audio). Pronunciation provider credentials are
  registry-first for `azure-phoneme`, `whisper-asr`, and
  `gemini-pronunciation-audio`, with a 30-second cache invalidated by admin
  provider/account mutations. ElevenLabs config remains at
  `/admin/content/conversation/settings`.
- **Streaming and tool calling are disabled** for phase 1. Both would
  break the "exactly one `AiUsageRecord` per `CompleteAsync`" invariant.
- Pricing fields on the `AiProviders` row drive cost reporting; defaults
  target `openai/gpt-4o-mini`. Update if `DefaultModel` changes.

See [`docs/AI-COPILOT-SDK-INTEGRATION.md`](AI-COPILOT-SDK-INTEGRATION.md)
for the full architecture.

---

## 19. UBAG browser-AI provider (OpenAI facade)

UBAG ships as a registered provider via its **OpenAI-compatibility facade** —
`POST {BaseUrl}/chat/completions` with `stream:false`, where
`BaseUrl = http://ubag-vps-gateway-1:8080/v1/openai` on the private compose
network. Wired through the existing `IAiGatewayService` like any other
provider; no policy bypass. One facade call = one native UBAG job = exactly
one `AiUsageRecord`, preserving the audit invariant.

**Setup**

- Register at `/admin/ai-providers` → preset **UBAG** (sets `Code = "ubag"`,
  `Dialect = OpenAiCompatible`, `DefaultModel = "mock"`), or deploy the
  backend containing `UbagProviderSeeder` so the row is seeded.
- Paste the tenant PAT (`tenant_oet` / `oet-platform`, service role — issued
  via UBAG `POST /v1/auth/pat`, no expiry, revocable from the UBAG Security
  page), or set `UBAG_OET_PAT` on the oet-api containers so the seeder keys
  the row. Stored encrypted via `IDataProtectionProvider` purpose
  `"AiProvider.PlatformKey.v1"`.
- Default `IsActive = false` until the admin enables features on the
  **UBAG toggle board** at `/admin/ai-providers/ubag` (per-feature
  ON/OFF switches backed by `AiFeatureRoute` rows — instant, no deploy).
- `OET_INTERNAL_AI_HOSTS` on oet-api must list `ubag-vps-gateway-1` (compose
  default), otherwise the provider-URL SSRF guard rejects the container-DNS
  http URL and every UBAG call fails closed. Do NOT add `oet-agent-gateway`
  to that list without verifying the antigravity routes — allowlisting makes
  that dormant row callable through the guarded path for the first time.

**Model naming**

- Facade `model` is a UBAG target (`chatgpt_web`) or `target|setting`
  (`chatgpt_web|GPT-5.6 Sol`); the full list comes from the board's
  **Discover models** button (`GET /v1/openai/models`).
- Keep `DefaultModel = "mock"` so the admin **Test connection** probe runs a
  fast mock job; per-feature `Model` overrides carry the real browser
  targets. Pricing fields stay `0` (browser-session cost model).

**PII position (approved as-is)**

- Learner prompts flow into platform-owned provider browser sessions
  (ChatGPT/DeepSeek web accounts). Approved by the owner for this
  integration; the board still keeps scoring-critical features OFF by
  default behind a confirmation modal until a parallel-evaluation window
  validates browser-model grading quality vs Anthropic.

**Boundaries**

- Text completions, file attachments (PDF/image/audio/video/voice via
  `ubag_attachments`), audio transcription (`POST {BaseUrl}/audio/transcriptions`
  → `{text, ubag_job_id}`), embeddings (`POST {BaseUrl}/embeddings` — exact
  OpenAI shape, deterministic hash vectors, NOT semantic), and JSON coercion
  (`response_format: json_object/json_schema` — completion reduced to its
  first parseable JSON value, loud failure otherwise) all flow through the
  facade. Native-audio (inline bytes) pronunciation scoring stays on Gemini;
  TTS stays on the voice providers. The board's Group E lists every media
  row with its facade mechanism.
- Streaming and tool calling are rejected by the facade (400); forced-tool
  call sites (listening extract/score) are served via JSON coercion +
  client-side emulation instead. Route latency-sensitive conversation
  features only with UX acceptance (browser jobs settle in 10–60s).
- Usage figures are character-based estimates from the facade, not metered
  model tokens; quota/credits metering consumes them as reported.

---

## 20. Owner Agent Console

The **Owner Agent Console** (`/admin/agent-console`, sidecar
`oet-agent-console`) runs **Claude Code**, **OpenAI Codex**, and **OpenCode**
on the production VPS. Claude and Codex use the owner's **own** subscriptions
(Claude Max, ChatGPT Business); OpenCode connects only through provider OAuth
methods selected in the console. Provider terms and costs vary. It is an
engineering/operations tool for the owner, **not** an AI feature of the
product. Runbook: [`docs/ops/OWNER-AGENT-CONSOLE.md`](ops/OWNER-AGENT-CONSOLE.md);
wire contract: [`agent-console/CONTRACT.md`](../agent-console/CONTRACT.md);
repo-rule exception: `AGENTS.md` → "Owner Agent Console exception".

**Who may use it**

- **Owner only.** Access requires role `admin` + verified email +
  `system_admin` + `auth_account_id` listed in `OwnerAgent:OwnerAccountIds`
  (env only — never a runtime setting, DB row or `/admin/settings` value) +
  a password-and-TOTP unlock ticket. Other admins, tutors, experts and
  learners never get access, not even read-only.
- One unlock (password + current TOTP) is valid for a fixed 60 minutes
  (`OwnerAgent:UnlockMinutes`) on that browser, carried in an HttpOnly,
  Secure, SameSite=Strict cookie bound to the signed-in session; it is never
  extended and there is no per-action step-up — enabling Autopilot, Ship,
  GitHub token changes and engine connect/logout need only the unlock (each is
  audited). Lock, sign-out and authenticator re-enrolment revoke it early.

**Isolation from product AI traffic**

- The engines are **never** registered as `AiProvider` rows, feature routes,
  BYOK credentials or fallback targets. Learner traffic could reach any active
  provider row (explicit provider pin on `/v1/ai/complete` - closed 30 Sep 2026,
  admin-only now; lowest-priority fallback - `AiGatewayService` now skips
  keyless subscription-sidecar rows; assistant fallback - closed the same day, see below), so
  registering a subscription there would put learner traffic on the owner's
  personal quota. The Speaking grading pin is the one deliberate exception (owner
  directive 30 Sep 2026, see `docs/speaking/ai-providers.md`).
- Only the API's `OwnerAgent`-policy endpoints relay to the sidecar, over the
  internal network `oet_agent_ctl`. No learner request, scheduled job,
  background worker or other admin action may call it.
- Engine output never reaches learners directly: code ships through an
  `agent/*` PR and the owner's Ship click; database changes pass the Guard
  (owner approval or pre-snapshot, per mode) and are audited.

**Metering and audit (carve-out from §0.4 / §12)**

- **No `AiUsageRecord` is written** for owner console turns. Console usage is
  shown per session where the engine reports it; platform learner metering
  does not apply.
- Evidence instead: hash-chained `AuditEvent` rows with
  `ResourceType = "OwnerAgent"` (unlock, connect/logout, token updates,
  session start, messages, approvals, mode changes, ship, kill switch,
  update — never secrets, message bodies truncated to 200 chars); per-session
  JSONL transcripts in the sidecar; Postgres `log_statement = 'mod'` on role
  `oet_owner_agent` (best-effort — the API-side chain is authoritative).
- The console shows engine-reported usage per session (tokens,
  `costUsd` where the engine reports it, rate-limit windows) so the owner can
  see exposure if a vendor starts billing headless/SDK usage separately.

**Credentials**

- Sign-in completes inside the container through native OAuth: the
  SDK-bundled `claude auth login`, Codex ChatGPT device-code login, and
  OpenCode provider OAuth. The console relays only safe authorization URLs,
  method labels and callback codes; it never reads, copies, logs or returns
  provider credentials.
- Provider API-key entry is not exposed. `ANTHROPIC_*`,
  `CLAUDE_CODE_USE_*`, `OPENAI_API_KEY` and `CODEX_API_KEY` are stripped from
  engine environments; Codex is pinned to `forced_login_method = "chatgpt"`
  plus the Business workspace id. OpenCode provider records are projected to
  safe metadata and its OAuth credentials remain in its native auth store.
- One credential store per machine: never copy `auth.json` or
  `.credentials.json` between machines (stricter than the vendors require).
- GitHub access uses two fine-grained, repo-scoped PATs (agent PAT, Ship
  PAT). Tokens are write-only through the console and never returned.

**Data protection**

- The owner verifies data-use and training settings for every connected
  provider account before use, and re-checks after a plan, workspace or
  vendor-policy change.
- Anything a session reads (DB rows, logs, files) is sent to the vendor as
  prompt context under the owner's subscription terms. Keep reads of
  learner data minimal and purpose-bound (support, debugging, data repair);
  prefer ids and aggregates; never bulk-export learner data into a session.
  Reading learner-authored content, `docker logs`, web results or GitHub
  comments **taints** the turn (see the runbook).
- Transcripts are redacted of known secrets and token patterns before
  persistence and **retained 90 days**, then purged. GDPR erasure procedure:
  runbook → "Transcript retention and GDPR erasure".

**Rule for any future non-owner use (Phase 5)**

- Anything another admin — or any non-owner principal, scheduled job or
  learner request — can trigger **must use API-key / access-token
  credentials billed to the platform through the coordinator** (one
  `AiUsageRecord` per physical call, grounding enforced). The owner's
  subscriptions are never used for it.
- Admin draft features may reach these engines only after the three leak
  paths are closed (per-row `AllowedUserIds` / `ExcludeFromFallback`
  enforced in `AiGatewayService.CompleteAsync` and
  `AiAssistantGateway.ResolveProviderAsync`; learner provider pin removed
  from `/v1/ai/complete`), and then only for owner-triggered calls.
  Status 30 Sep 2026: the provider pin is closed (admin-only) and the
  `AiGatewayService` fallthrough skips keyless sidecar rows; the
  `AiAssistantGateway` fallback and the assistant route seeder now skip keyless sidecar rows too;
  still open: `/v1/ai/complete` with
  `task=GenerateContent` still has no per-user quota or rate limiter.

**Vendor-terms position**

- Anthropic permits an end user to sign in to the unmodified Claude Code
  binary with their own subscription, including on a hosted machine, but
  forbids third parties from collecting or intermediating Claude.ai
  credentials, and sizes Pro/Max limits for ordinary individual use.
  Anthropic's announced move of Agent SDK usage to a separate credit pool
  is **paused, not cancelled** — "$0 marginal cost" holds only while it
  stays paused. OpenCode may connect to third-party OAuth providers; check
  the selected provider's terms and billing before use. Sources and quotes:
  runbook → "Vendor terms".

### Owner directive 2026-09-30 — subscription route extended to `speaking.grade`

The rules above govern the **owner's personal console engines** and remain in force for them.
Two dedicated subscription sidecars (`oet-writing-claude`, `oet-writing-codex`) were already
registered as `AiProvider` rows for Writing grading by the 2026-09-29 owner directive
([`docs/ops/WRITING-AI-PROVIDERS.md`](ops/WRITING-AI-PROVIDERS.md)); that directive supersedes
the "never `AiProvider` rows / never learner-triggered traffic" isolation rule for those two
rows. On 2026-09-30 the owner extended the same route to Speaking grading:

- **What:** `speaking.grade` (classic and v1.1 assessors) tries the `writing-claude-sub` row
  (Claude Opus 5.5, effort `high`) first, and falls back to the default route (Anthropic API)
  when that call fails. Implemented by `SpeakingGradeChain`; configured by
  `Speaking__Grading__PinnedProviderCode` / `Speaking__Grading__PinnedModel` (empty provider =
  off). Details and revert: [`docs/speaking/ai-providers.md`](speaking/ai-providers.md).
- **Still under this policy (§0, §12):** the call goes through the coordinator and the gateway:
  grounding enforced, one `AiUsageRecord` per physical call (priced `0.00` on the sidecar row),
  kill switch, feature policy, platform budget reservation (spend booked; it refuses a call only
  while `EnforceSpendCaps` is on, off by default since 2026-10-02, see §7) all apply. The provider
  circuit breaker does NOT: `writing-claude-sub` is exempt by owner rule MAX-ALWAYS-ON
  (`AiCircuitBreakerStore.IsAlwaysOn`), so its circuit never opens and failures are not counted
  against it; a failed call falls back inside the same grade only. Learner credit accounting is
  unchanged: the gateway never debits Speaking; the hold taken at card reveal is committed once,
  after a grade exists.
- **Shared allowance and lane:** the sidecar runs one request at a time and shares the Claude
  Max allowance (and the `oet_agent_home` login) with Writing and the console, so Speaking and
  Writing grades queue behind each other. Anthropic sizes Max limits for ordinary individual
  use: a vendor-side suspension would take Writing and Speaking subscription grading down
  together, and the automatic fallback to the API route is what keeps Speaking grading alive.
- **Learner data:** Speaking transcripts are sent to Anthropic through the CLI under the
  subscription's terms; the "Data protection" rules above apply, and confirming that model
  training is off on the dedicated account is the owner's check. Session persistence is now
  **off**: the Claude sidecar runs the CLI with `--no-session-persistence --tools ""` (every
  built-in tool removed; `--allowedTools` alone never removed any) and
  `CLAUDE_CODE_DISABLE_AUTO_MEMORY=1`, so `/home/agent/.claude/projects/-tmp` on the
  `oet_agent_home` volume (shared with the owner console) receives no new files once the new image
  is deployed. Files written there by earlier images are purged by the owner; the flags are
  validated in the running container before the image is deployed (both steps:
  [`docs/ops/WRITING-AI-PROVIDERS.md`](ops/WRITING-AI-PROVIDERS.md) §9). Other CLI bookkeeping under
  the config directory (credential refresh, caches) is outside those switches. The Codex sidecar has
  none of these switches and whether `codex exec` keeps session files on disk is unverified; it
  carries Writing letters only, never Speaking.
- **Provider pin (closed 2026-09-30):** `POST /v1/ai/complete` ignores the request's `provider`
  unless the caller is an admin (`RulebookEndpoints.ResolveRequestedProvider`), so a learner or
  expert cannot name the `writing-claude-sub` row. Learner-triggered Writing features still reach the
  lane through their feature routes, under plan quota and credit accounting. Still open on that
  endpoint: `task=GenerateContent` classifies to `admin.content_generation` (admin-batch, no per-user
  quota, platform key) and the endpoint has no rate limiter.
- **Implicit fallthrough (closed 2026-09-30 for the gateway):** the keyless sidecar rows carry the
  lowest `FailoverPriority` values (1 and 2), so the gateway's "no pin, no route" default (the lowest
  priority active keyed text-chat row) would have picked `writing-claude-sub` for every feature
  without a route once the row was active. `AiGatewayService` now skips marker-key rows there: the
  sidecar rows are reached only by an explicit pin (`SpeakingGradeChain`, the Writing pipeline, an
  admin) or a feature route set on purpose. The AI assistant's own default-row selection
  (`AiAssistantGateway`) had the same lowest-priority pick and got the same marker-key filter in
  the same PR (a keyless sidecar row is never the assistant's default, and the assistant route seeder
  never seeds a route onto one).
- **Provider row key marker:** the registry only hands the seeded marker back as a key while the
  row's `BaseUrl` host is on `OET_INTERNAL_AI_HOSTS`, so an admin re-pointing a sidecar row at a
  vendor URL cannot leave it looking credentialed.
