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
| L1 | `Speaking:Grading:PinnedProviderCode` / `PinnedModel` (default `writing-claude-sub` / `claude-opus-5-5`, the dedicated Claude Max sidecar; effort `high` from the sidecar's own env) | always first, when a provider is pinned, under one wall-clock budget (`PinnedTimeoutSeconds`, default 900 s) |
| L2 | the default route: the feature route row if one exists, else `anthropic` / `claude-sonnet-5` (maximum reasoning as before) | when L1 fails with a provider-side failure |

- **Fails over on:** any provider failure of L1 (HTTP error, timeout, open circuit, inactive or missing
  provider row), the L1 time budget running out, and a duplicate refusal of the pinned operation while
  that operation is `Indeterminate` (a dropped connection or timeout left its outcome unknown; the
  coordinator never re-runs it, but L2 is a different provider under a different operation, so without
  this one transient sidecar failure would block re-grading through both routes). **Never fails over on:**
  quota/budget/feature-policy refusals, other duplicate or conflicting AI operations (for example a
  `Completed` twin inside the replay window: the work already happened), an ungrounded prompt, or caller
  cancellation. If both levels fail the last error is rethrown, so the learner still sees the generic
  `409 speaking_ai_unavailable` (retryable, no charge).
- **L1 time budget:** `Speaking:Grading:PinnedTimeoutSeconds` (default `900`, `0` or less = no cap, above
  `1500` clamped) is one wall-clock budget over the whole pinned call: the sidecar's queue wait, the CLI
  run and the gateway's own retries. It exists because the Anthropic adapter's HttpClient allows 30
  minutes per attempt and the operation lease is 30 minutes while L2 needs about 12. When it expires the
  pinned call is cancelled (recorded as a cancelled, replayable operation) and L2 runs. The sidecar cannot
  see that the client gave up, so an abandoned request still runs to completion.
- **Independent of the Writing selector:** the pin goes straight to `writing-claude-sub`; the Writing mode
  selector and its weekly bands do not steer Speaking, but Speaking and Writing share that row's provider
  circuit and the sidecar's serial lane (details: [../ops/WRITING-AI-PROVIDERS.md](../ops/WRITING-AI-PROVIDERS.md) §9).
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
  for the shared serial lane, the 300 s CLI timeout and the L1 time budget, and [../env/speaking.md](../env/speaking.md) for the keys.
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
  lists the failures. The chain itself writes one warning per fallback (`Speaking grading via pinned provider
  <code> failed (<class>); falling back to the default route.`) where `<class>` is one of the failure classes
  above, `timeout` (the L1 time budget ran out, or an HTTP client timeout) or `operation_indeterminate` (the
  pinned operation is Indeterminate). A pinned call cut off by the time budget leaves a usage row with outcome
  `Cancelled` (error code `cancelled`) for the pinned provider: the gateway cannot tell the chain's own budget
  from a caller cancel, so look for `outcome=Cancelled` on `speaking.grade` to see L1 timeouts. Message phrases
  (credit balance, usage limits, ...) never decide the class of an OpenAI or Gemini HTTP 400, 413, 415 or 422,
  because those replies can echo request content.

## Speaking audio judge (owner decision 4 Oct 2026)

Intelligibility is judged from the candidate's audio by an OpenAI audio-chat model: provider row `openai-audio`
(seeded by `CoreAiProviderSeeder`; `OpenAiCompatible`, category `Asr` so it never joins text failover, model
`gpt-audio-1.5`, editable in the admin UI), feature `speaking.audio_assess` (scoring-critical, pinned, own circuit,
**never** on the grade chain above). Its key is the funded OpenAI key already used by live voice
(`LIVEVOICE__OPENAIAPIKEY`): `AiProviderRegistry.GetPlatformKeyAsync` falls back to it for this one provider code when
the row has no key of its own; a key pasted on the row overrides it. The payload builder sends the audio as
`input_audio` (mp3) parts on the first user message and uses `max_completion_tokens` for audio models; the
coordinator's request hash includes the audio bytes. Deactivating the row (or adding `speaking.audio_assess` to the
disabled-features list, or switching off the `speaking_audio_assessment` flag) stops the stage; grades continue from the
transcript, labelled as such. Details and the release probe: [scoring.md](./scoring.md#acoustic-evidence--intelligibility-is-judged-from-the-sound).

## Provider env keys

See `docs/env/speaking.md`.

## OpenAI-compatible providers

Any vendor mirroring OpenAI Chat Completions works via `OpenAiCompatibleProvider`. Override `OPENAI__APIBASE` (e.g. NVIDIA NIM, Groq, Together, Mistral La Plateforme).

## Prompt caching

Anthropic ephemeral cache on persona system block + rulebook context block. Multi-turn role-plays hit cache from turn 2 — target ≥ 80% hit rate (SLA).

## Swap procedure

1. Add provider account row via admin UI.
2. Re-point feature route → new provider at 10% rollout.
3. Monitor latency + error rate (admin AI usage pages and Sentry; the old Grafana `speaking-quality` dashboard queried metrics nothing emitted and was removed, see `ops/README.md`).
4. Promote / rollback.

## TTS + ASR

- TTS: `IConversationTtsProvider` (ElevenLabs default, Azure / OSS / Mock fallbacks via `ConversationTtsProviderSelector`).
- ASR: `WhisperPronunciationAsrProvider` is the default; `ISpeakingTranscriptionProvider` allows swap.

## Cost guardrails

- Per-key spend cap at the provider console.
- Daily admin cost dashboard with alert at 5× baseline.
- Batch authoring runs at low concurrency (1 at a time) with prompt cache to keep generation cost down.
