# Rulebook / AI Gateway — Feature ↔ Provider Matrix

This file is the canonical reference for **which AI provider + model is wired
to which feature** in the OET Learner backend. Update it whenever a new
feature code is added to `AiFeatureCodes` (see
`Domain/AiEntities.cs`) or `SpeakingAiFeatureCodes` (see
`Services/Rulebook/AiFeatureRouteResolver.cs`), or when the default
provider/model for an existing code is changed in the seeders.

## Routing model

The runtime path resolves a provider for every AI call in this order:

1. **Explicit `request.ProviderCode`** — call site has overridden routing
   (rare; usually only in admin tools).
2. **DB override** — `AiFeatureRouteResolver` looks up
   `AiFeatureRoutes.FeatureCode = X AND IsActive = true` and uses
   `(ProviderCode, Model)` from that row. Admins edit this table via
   `/v1/admin/ai/feature-routes`.
3. **Static fallback** — for Speaking module features, if no DB row exists
   the resolver falls back to `SpeakingAiRouteDefaults.Defaults`. This
   guarantees that the Speaking module ships routable even on a fresh DB.
4. **Global default provider** — when none of the above match, the
   gateway falls through to the highest-priority active row in
   `AiProviders`.

## Speaking module routing (P1.3 — see plan §Phase 1.3)

| Feature code | Surface | Default provider | Default model | Prompt caching | Fallback |
| --- | --- | --- | --- | --- | --- |
| `speaking.score.v2` | Dual-grader AI assessment of finished role-plays | `anthropic` | `claude-sonnet-4-6` | yes (ephemeral, system prompt + rulebook block) | `openai` / `gpt-4o` |
| `speaking.patient.turn.v1` | Per-turn AI patient persona during AI role-play | `anthropic` | `claude-haiku-4-5` | yes (ephemeral, persona prompt) | `openai` / `gpt-4o-mini` |
| `card.draft.v1` | Admin AI-draft tool for new role-play cards | `anthropic` | `claude-sonnet-4-6` | yes (ephemeral, system prompt) | none |

Seeders live in `Services/Seeding/SpeakingAiRouteSeed.cs`. Static defaults
are pinned in `SpeakingAiRouteDefaults` so the resolver can route even
without a DB row (CI, fresh DBs, isolated unit tests).

## Other feature routes (live in `AiFeatureCodes`)

| Feature code | Surface | Default provider | Notes |
| --- | --- | --- | --- |
| `writing.grade` | Writing dual-grader (scoring-critical) | `anthropic` | Per-feature DB route required for production grading; falls back to global provider in dev. |
| `writing.sample_score` | Sample-letter scoring (scoring-critical) | `anthropic` | Same as above. |
| `writing.coach.suggest` | Inline writing-coach suggestions | platform default | BYOK-eligible. |
| `writing.coach.explain` | "Explain this fix" copilot | platform default | BYOK-eligible. |
| `speaking.grade` | Legacy Speaking grader | platform default | Use `speaking.score.v2` for new code paths. |
| `mock.full_grade` | Full mock scoring (all subtests) | `anthropic` | Scoring-critical. |
| `mock.remediation_draft` | 7-day remediation-plan personalisation | platform default | BYOK-eligible — non-scoring. |
| `conversation.opening` | Self-practice opening line | platform default | BYOK-eligible. |
| `conversation.reply` | Self-practice per-turn replies | platform default | BYOK-eligible. |
| `conversation.evaluation` | Self-practice evaluation | `anthropic` | Scoring-critical. |
| `pronunciation.score` | Pronunciation analysis | `anthropic` | Scoring-critical. |
| `pronunciation.feedback` | Per-phoneme feedback prose | platform default | Non-scoring. |
| `pronunciation.tip` | Inline phoneme tips | platform default | BYOK-eligible. |
| `summarise.passage` | Recall passage summariser | platform default | BYOK-eligible. |
| `vocabulary.gloss` | Recall vocabulary gloss | platform default | BYOK-eligible. |
| `recalls.mistake_explain` | Spaced-repetition mistake explainer | platform default | BYOK-eligible. |
| `recalls.revision_plan` | Spaced-repetition revision plan | platform default | BYOK-eligible. |
| `admin.content_generation` | Generic admin authoring tool | `anthropic` | Admin-only. |
| `admin.grammar_draft` / `admin.pronunciation_draft` / `admin.vocabulary_draft` / `admin.conversation_draft` / `admin.listening_draft` / `admin.reading_draft` / `admin.writing_draft` | Per-subtest admin drafting tools | `anthropic` | Admin-only. |
| `admin.listening.skill_tag` / `admin.listening.transcript_segment` | Reserved (manual today) | platform default | Reserved by PRD-LISTENING-V2.md §5.2/5.4. |
| `ai_assistant.admin` / `ai_assistant.expert` / `ai_assistant.learner` | Multi-role AI Assistant | Active text-chat provider default | Seeded by `AiAssistantFeatureRouteSeeder`. |

## Provider account env keys

Live API keys are stored encrypted in `AiProviders` (column
`EncryptedApiKey`, protected via `DataProtection`). For local development,
fall back keys are read from environment variables:

| Provider code | Primary env key | Secondary env key | Notes |
| --- | --- | --- | --- |
| `anthropic` | `Anthropic__ApiKey` | — | Used for all Anthropic models (Claude Sonnet 4.6, Claude Haiku 4.5). Prompt caching requires `anthropic-beta: prompt-caching-2024-07-31` (already set by `AnthropicProvider`). |
| `openai` | `OpenAi__ApiKey` | `OPENAI_API_KEY` | OpenAI Platform direct. |
| `cloudflare-workers-ai` | `Cloudflare__ApiToken` | — | Account id read from `Cloudflare__AccountId`. |
| `copilot` | — | — | GitHub Copilot OAuth flow — see `CopilotAiModelProvider`. |
| `digital-ocean` / `openrouter` / `groq` / `together` / `deepseek` / `gemini` | per-provider | — | OpenAI-compatible dialect, registered via `AiProviders` rows. |

Encrypted keys take priority. The env-var fallback exists so a developer
can boot the API with `dotnet run` without first seeding `AiProviders`.

## How to swap a provider

There are three ways to redirect a feature to a different provider/model,
in increasing order of permanence:

### 1. One-off override at the call site (debug / temporary)

Pass `ProviderCode` directly on the `AiProviderRequest` — bypasses the
resolver. Used by feature-flag rollouts and admin "test this prompt"
tools. Not durable across requests.

### 2. DB feature route (recommended for production swap)

Either edit the existing row at `/v1/admin/ai/feature-routes` (admin UI),
or `INSERT INTO "AiFeatureRoutes" (FeatureCode, ProviderCode, Model, IsActive, ...)`.
The resolver picks up the change on the next call — no restart required.
Set `IsActive = false` to disable a route and fall back to the static
default or global provider.

### 3. Static default (code change required)

Edit `SpeakingAiRouteDefaults.Defaults` (for Speaking codes) or the
corresponding seeder. This changes both the "no DB row" fallback and the
value the next seeder run would insert. Requires a deploy. Use when the
provider has been retired entirely (e.g. a model code-named for end-of-life).

When migrating between providers, prefer **route via DB**, watch the
gateway logs + `AiUsageRecorder` rows for outcomes, then update the
static default once the new provider is healthy.

## Prompt caching

Anthropic prompt caching is enabled by adding the
`cache_control: { type: "ephemeral" }` marker to the relevant
content block. For Speaking:

- `speaking.score.v2`: cache the **system prompt + rulebook context
  block** so each call within the 5-min TTL hits the cache.
- `speaking.patient.turn.v1`: cache the **persona system prompt + role-play
  card** so every turn in the same role-play reuses the same cache.
- `card.draft.v1`: cache the **system prompt + style examples** so a
  batch of card drafts in the same admin session shares one cache.

Cache hits are visible in the Anthropic dashboard (cache hit %) and on
`AiUsageRecord.CacheHit` (boolean column populated by the gateway when the
provider returns a cache-hit indicator).
