# Speaking Module — AI Provider Matrix

Source-of-truth registration in `backend/src/OetLearner.Api/Services/Seeding/SpeakingAiRouteSeed.cs`. Resolver in `AiFeatureRouteResolver.cs`. Provider registry in `AiProviderRegistry.cs` (AnthropicProvider, OpenAiCompatibleProvider, CloudflareWorkersAiProvider).

| Feature route | Default provider | Default model | Caching | Fallback |
|---------------|------------------|---------------|---------|----------|
| `speaking.score.v2` | Anthropic | `claude-sonnet-4-6` | ephemeral | OpenAI `gpt-4o` |
| `speaking.patient.turn.v1` | Anthropic | `claude-haiku-4-5` | ephemeral | OpenAI `gpt-4o-mini` |
| `card.draft.v1` | Anthropic | `claude-sonnet-4-6` | ephemeral | OpenAI `gpt-4o` |
| `drill.draft.v1` | Anthropic | `claude-sonnet-4-6` | ephemeral | OpenAI `gpt-4o` |
| `speaking.drill.score.v1` | Anthropic | `claude-haiku-4-5` | ephemeral | OpenAI `gpt-4o-mini` |

> **Since Sept 2026** (the table above predates it): Speaking grading (`speaking.grade`) runs on Anthropic
> `claude-sonnet-5` with maximum reasoning, and the live AI patient is OpenAI GPT-Live (default) or Gemini Live.
> See [live-voice.md](./live-voice.md). **Since 30 Sep 2026** grading tries the Claude subscription sidecar
> first, see "Speaking grading chain" below.

## Speaking grading chain (owner directive 2026-09-30)

`speaking.grade` has exactly two gateway call sites (classic `SpeakingAiAssessmentService`, v1.1
`SpeakingSimulationV11AssessmentService`); both go through `SpeakingGradeChain`:

| Level | Provider / model | When |
|-------|------------------|------|
| L1 | `Speaking:Grading:PinnedProviderCode` / `PinnedModel` (default `writing-claude-sub` / `claude-opus-5-5`, the dedicated Claude Max sidecar; effort `high` from the sidecar's own env) | always first, when a provider is pinned |
| L2 | the default route: the feature route row if one exists, else `anthropic` / `claude-sonnet-5` (maximum reasoning as before) | when L1 fails with a provider-side failure |

- **Fails over on:** any provider failure of L1 (HTTP error, timeout, open circuit, inactive or missing
  provider row). **Never fails over on:** quota/budget/feature-policy refusals, duplicate or conflicting
  AI operations, an ungrounded prompt, or caller cancellation. If both levels fail the last error is
  rethrown, so the learner still sees the generic `409 speaking_ai_unavailable` (retryable, no charge).
- **Credit is unaffected:** the gateway never debits Speaking; the 2-credit hold is committed once by
  `SpeakingCreditSettlement` after a grade exists, whichever level produced it.
- **Provenance:** the classic `SpeakingAiAssessment` row now stores the provider and model that actually
  ran (`Provider` / `ModelId`, cut to 32 / 96 characters), so results from the sidecar and the API route
  can be told apart.
- **Reply contract:** a reply that omits any of the nine criteria, or gives a non-numeric score, is
  rejected as `409 speaking_ai_unparseable` (retryable) instead of being stored as a zero grade. The
  grounded system prompt names two criteria `grammar` / `providingStructure` while the JSON template
  says `grammarExpression` / `structure`; the parser accepts both spellings.
- **Requirements:** the `writing-claude-sub` provider row is active; `OET_INTERNAL_AI_HOSTS` includes
  `oet-writing-claude` on the API slots and `oet-ai-worker`; `oet-ai-worker` is on `oet_agent_ctl`
  (queued grades run only in the worker). See [../ops/WRITING-AI-PROVIDERS.md](../ops/WRITING-AI-PROVIDERS.md) §9
  for the shared serial lane and the 300 s CLI timeout, and [../env/speaking.md](../env/speaking.md) for the keys.
- **Revert to the previous behaviour:** set `SPEAKING_GRADING_PINNED_PROVIDER=` (empty) in
  `/opt/oetwebapp/.env.production`, recreate the API slots and `oet-ai-worker`. Grading is then one
  unpinned gateway call on the default route, exactly as before the chain existed. (Deactivating the
  `writing-claude-sub` row has the same effect for grading, but every grade then records a refused
  L1 attempt first.)
- **Diagnostics:** each failed provider call writes one structured API log line with the HTTP status,
  failure class (`quota_exhausted`, `rate_limited`, `invalid_request`, `auth`, `overloaded`,
  `server_error`, `network`), the vendor error type/code, the request id and a redacted, capped
  provider message. Usage rows carry only the class-derived code, for example
  `provider_quota_exhausted`, never provider text. `GET /v1/admin/ai/usage?featureCode=speaking.grade&outcome=ProviderError`
  lists the failures.

## Provider env keys

See `docs/env/speaking.md`.

## OpenAI-compatible providers

Any vendor mirroring OpenAI Chat Completions works via `OpenAiCompatibleProvider`. Override `OPENAI__APIBASE` (e.g. NVIDIA NIM, Groq, Together, Mistral La Plateforme).

## Prompt caching

Anthropic ephemeral cache on persona system block + rulebook context block. Multi-turn role-plays hit cache from turn 2 — target ≥ 80% hit rate (SLA).

## Swap procedure

1. Add provider account row via admin UI.
2. Re-point feature route → new provider at 10% rollout.
3. Monitor latency + error rate (Grafana `speaking-quality` dashboard).
4. Promote / rollback.

## TTS + ASR

- TTS: `IConversationTtsProvider` (ElevenLabs default, Azure / OSS / Mock fallbacks via `ConversationTtsProviderSelector`).
- ASR: `WhisperPronunciationAsrProvider` is the default; `ISpeakingTranscriptionProvider` allows swap.

## Cost guardrails

- Per-key spend cap at the provider console.
- Daily admin cost dashboard with alert at 5× baseline.
- Batch authoring runs at low concurrency (1 at a time) with prompt cache to keep generation cost down.
