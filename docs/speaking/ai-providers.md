# Speaking Module — AI Provider Matrix

Source-of-truth registration in `backend/src/OetWithDrHesham.Api/Services/Seeding/SpeakingAiRouteSeed.cs`. Resolver in `AiFeatureRouteResolver.cs`. Provider registry in `AiProviderRegistry.cs` (AnthropicProvider, OpenAiCompatibleProvider, CloudflareWorkersAiProvider).

| Feature route | Default provider | Default model | Caching | Fallback |
|---------------|------------------|---------------|---------|----------|
| `speaking.score.v2` | Anthropic | `claude-sonnet-4-6` | ephemeral | OpenAI `gpt-4o` |
| `speaking.patient.turn.v1` | Anthropic | `claude-haiku-4-5` | ephemeral | OpenAI `gpt-4o-mini` |
| `card.draft.v1` | Anthropic | `claude-sonnet-4-6` | ephemeral | OpenAI `gpt-4o` |
| `drill.draft.v1` | Anthropic | `claude-sonnet-4-6` | ephemeral | OpenAI `gpt-4o` |
| `speaking.drill.score.v1` | Anthropic | `claude-haiku-4-5` | ephemeral | OpenAI `gpt-4o-mini` |

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
