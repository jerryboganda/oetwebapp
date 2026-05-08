# PRD — GitHub Copilot / GitHub Models Enterprise Integration

> **Version:** 2 (May 7, 2026) — supersedes the Phase-1-only design in
> [`docs/AI-COPILOT-SDK-INTEGRATION.md`](AI-COPILOT-SDK-INTEGRATION.md).
> Drafted from the user's explicit answers in chat (May 7).

## Goals

1. Plug **GitHub Copilot / GitHub Models** into the existing grounded
   `IAiGatewayService` so non-voice AI features can be routed to it from
   `/admin/ai-config` without bypassing rulebook grounding, quota, or
   audit.
2. Support **multiple GitHub Copilot Enterprise accounts** (PATs)
   pooled under one provider with **automatic failover** when an
   account hits 100 % of its monthly entitlement or returns 429.
3. Surface **per-account + overall analytics** (premium requests,
   tokens in / out, cost, success rate) in `/admin/ai-usage`.
4. Add **per-row Test Connection** in `/admin/ai-providers`.
5. Unify **voice providers** (ElevenLabs / Azure Speech / Whisper /
   Deepgram / CosyVoice / ChatTTS / GPT-SoVITS) into the same
   `/admin/ai-providers` panel. Existing voice settings pages stay as
   feature-routing surfaces; the credential / quota / failover plane
   moves to the unified registry.
6. Support **tool calling** for the new provider where the gateway
   can preserve audit + quota integrity (multi-turn invocations
   produce one parent `AiUsageRecord` + N child invocation records).

## Non-goals

- Streaming responses (deferred — single-record-per-call invariant
  preserved).
- Per-learner GitHub OAuth (rejected: ToS / age-of-consent risk and
  cross-contaminates `AiUsageRecord.UserId`).
- Re-routing scoring features (`writing.grade`, `speaking.grade`,
  `mock.full_grade`, `pronunciation.score`, `conversation.evaluation`,
  `writing.sample_score`) — these stay on the privacy-reviewed default
  provider and require a separate regression test pack before any
  re-route.
- Replacing or modifying the rulebook grounding contract.

## User decisions (verbatim, May 7 2026)

| Question | Choice |
|---|---|
| Client library | **B. `Azure.AI.Inference` typed SDK** |
| Auth model | **Platform PAT + admin BYOK + multi-account pool with auto-failover and per-account analytics** |
| Default model | **Empty — set per-feature in `/admin/ai-config`** |
| Streaming | **No — Phase 1 stays non-streaming** |
| Tool calling | **Yes — fully** |
| Voice surface | **Unified into `/admin/ai-providers`** |
| Feature routing | `vocabulary.gloss`, `recalls.mistake_explain`, `recalls.revision_plan`, `conversation.opening`, `conversation.reply`, `writing.coach.suggest`, `writing.coach.explain`, `summarise.passage`, `admin.*` |
| Privacy gate | **Manual — admin flips `IsActive` after privacy review (no code gate)** |
| Test Connection | **Yes — `POST /v1/admin/ai/providers/{code}/test`** |
| Scope | **Phases 1 + 2 + 3 + voice unification** |

## Domain model — additions

### `AiProviderAccount` (NEW)

| Column | Type | Notes |
|---|---|---|
| `Id` | string(32) | Guid `"N"` |
| `ProviderId` | FK → `AiProvider.Id` | cascade on delete |
| `Label` | string(120) | e.g. *"Acme Corp Enterprise #1"* |
| `EncryptedApiKey` | string | DataProtection purpose `"AiProvider.PlatformKey.v1"` |
| `ApiKeyHint` | string(8) | last 4 chars |
| `MonthlyRequestCap` | int? | nullable; `null` = unmetered |
| `RequestsUsedThisMonth` | int | reset by `AiAccountQuotaResetWorker` |
| `TokensInThisMonth` | long | sum from `AiUsageRecord` |
| `TokensOutThisMonth` | long | sum from `AiUsageRecord` |
| `LastUsedAt` | datetimeoffset? | |
| `LastResetAt` | datetimeoffset | first-of-month UTC |
| `LastTestedAt` | datetimeoffset? | populated by Test Connection |
| `LastTestStatus` | string(32)? | `ok` / `auth_failed` / `rate_limited` / `error` |
| `ExhaustedUntil` | datetimeoffset? | set when 429 received; cleared at next reset |
| `Priority` | int | lower wins |
| `IsActive` | bool | |
| `CreatedAt` / `UpdatedAt` / `UpdatedByAdminId` | | standard |

### `AiProvider` extensions

- `LastTestedAt` (datetimeoffset?) — for providers without accounts
  (Cloudflare, DigitalOcean, voice).
- `LastTestStatus` (string(32)?).
- `Category` (enum) — `TextChat` / `Tts` / `Asr` / `Phoneme` — used by
  the unified panel to filter rows.

### `AiUsageRecord` extension

- `AccountId` (string?, FK → `AiProviderAccount.Id`) — populated when
  the call resolves to a multi-account provider.
- `ToolCallCount` (int, default 0) — number of child tool invocations
  for this turn.

### `AiToolInvocation` (NEW — Phase 5 only)

| Column | Type | Notes |
|---|---|---|
| `Id` | string(32) | |
| `ParentUsageRecordId` | FK → `AiUsageRecord.Id` | |
| `ToolName` | string(120) | |
| `ArgumentsJson` | text | sanitised — never includes credentials |
| `ResultStatus` | string(32) | `success` / `error` / `refused` |
| `LatencyMs` | int | |
| `CreatedAt` | datetimeoffset | |

## Failover algorithm

When the gateway resolves the active provider for a request:

```pseudo
accounts = AiProviderAccount.where(ProviderId == provider.Id, IsActive == true)
                            .where(ExhaustedUntil is null OR ExhaustedUntil < now)
                            .where(MonthlyRequestCap is null
                                   OR RequestsUsedThisMonth < MonthlyRequestCap)
                            .orderBy(Priority asc, LastUsedAt asc nulls first)
if accounts is empty: raise AiAccountPoolExhaustedException
account = accounts.first
try:
    response = client(account).complete(...)
    increment account.RequestsUsedThisMonth atomically
    set account.LastUsedAt = now
    write AiUsageRecord with AccountId = account.Id
except 429:
    set account.ExhaustedUntil = now + min(retry-after, end-of-month)
    increment retry counter; recurse on next account in pool
except 401/403:
    set account.IsActive = false; alert admin via AuditEvent
    increment retry counter; recurse on next account in pool
```

## Tool calling — audit invariant

The current invariant *"exactly one `AiUsageRecord` per
`CompleteAsync`"* is preserved. Tool calling is implemented inside
`AiGatewayService.CompleteAsync`:

1. Outer call records ONE parent `AiUsageRecord`.
2. Each model→tool→model round-trip writes one `AiToolInvocation`
   row pointing at the parent.
3. Token counts on the parent are the **sum** across all turns.
4. Quota is checked once at start; mid-call the gateway will allow
   completing the tool round-trip even if mid-call the quota would
   be exhausted (otherwise tool chains become non-deterministic).
5. Tools are declared per feature code in
   `AiToolRegistry` (TBD in Phase 5 design doc); only whitelisted
   tools can be called.

## Voice provider unification

Existing voice configuration lives in `appsettings` (`Conversation:`,
`Pronunciation:`) plus admin pages
[`/admin/content/conversation/settings`](../app/admin/content/conversation/settings/page.tsx)
and
[`/admin/content/pronunciation/settings`](../app/admin/content/pronunciation/settings/page.tsx).

Phase 6 migrates the **credential / endpoint / quota** plane into
`AiProvider` rows with new dialects:

- `ElevenLabsTts`, `AzureTts`, `CosyVoiceTts`, `ChatTtsTts`, `GptSoVitsTts`
- `AzureAsr`, `WhisperAsr`, `DeepgramAsr`
- `AzurePhoneme` (pronunciation phoneme scoring)

The two voice settings pages keep their **routing** (which provider is
active for conversation-tts vs pronunciation-asr etc.) but read keys
and base URLs from the unified `AiProvider` registry. ElevenLabs voice
ids and Azure regions remain in their own per-provider settings tables
(out of `AiProvider`).

## Rollout / rollback

- Each phase ships behind a feature flag where the schema change is
  visible (Phase 2 adds `AccountId` nullable; Phase 5 adds
  `AiToolInvocation`). Backfill is implicit (NULL = legacy).
- Disable order: flip `IsActive=false` on all Copilot accounts → flip
  `IsActive=false` on the Copilot provider row → restart pods.
- Schema rollback: each migration's `Down()` drops only the new
  columns/tables; no data loss for legacy paths.

## Acceptance criteria

- All existing tests pass (currently 989 Vitest + the backend xUnit
  suite).
- New backend tests cover: SDK happy path, 401, 429, multi-account
  failover, exhaustion, tool round-trip audit shape.
- New Vitest specs cover: account list / add / remove, Test
  Connection wiring, voice unification panel filter.
- No new `// @ts-ignore`. `npx tsc --noEmit` returns 0.
- `npm run lint` returns 0.
- A manual smoke test (per phase): admin adds two PATs, sends a
  `vocabulary.gloss` call through `/admin/ai-config` test
  console — call succeeds, account 1's `RequestsUsedThisMonth`
  increments by 1, `AiUsageRecord` shows `accountId`.
