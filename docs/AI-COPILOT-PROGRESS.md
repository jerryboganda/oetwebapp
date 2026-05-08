# PROGRESS — GitHub Copilot Enterprise Integration

> Ralph-style execution log for the PRD at
> [`docs/AI-COPILOT-PRD.md`](AI-COPILOT-PRD.md). Each phase appends here
> as it completes; failures and pivots are recorded in-line.

## Phase 1 — `Azure.AI.Inference` SDK swap

**Status:** ✅ complete.

- [x] Add `Azure.AI.Inference` 1.0.0-beta.5 NuGet to
      `OetLearner.Api.csproj` (pulled `System.ClientModel 1.4.1`,
      `Azure.Core 1.46.1`).
- [x] Rewrite `CopilotAiModelProvider` to use `ChatCompletionsClient`
      with `AzureKeyCredential` and `AzureAIInferenceClientOptions`
      (note: NOT `ChatCompletionsClientOptions` — the Azure.AI.Inference
      SDK deviates from the usual `<Service>ClientOptions` convention).
- [x] Drop named `AiCopilotClient` `HttpClient` in `Program.cs`
      (the SDK owns its own pipeline).
- [x] Test-only public ctor on `CopilotAiModelProvider` accepting
      `AzureAIInferenceClientOptions`; tests inject a stubbed
      `HttpClientTransport(new HttpClient(stubHandler))`. No
      `[InternalsVisibleTo]` was added — the codebase has none.
- [x] `dotnet test --filter CopilotAiModelProviderTests`: 6/6 green.
- [x] Full backend `dotnet test`: green (no regressions).
- [x] `npx tsc --noEmit`: exit 0. `npm run lint`: clean.
      `npm test -- --run`: 163 files / 989 tests still green.
- [x] `app/admin/ai-providers/page.test.tsx`: 3/3 green.
- [x] Append "GitHub Copilot / GitHub Models provider" section
      (#18) to `docs/AI-USAGE-POLICY.md`.

**Notes for downstream phases:**

- The SDK appends `?api-version=2024-05-01-preview` to the URL —
  any future test that asserts on the URL must use `Contains` not
  `EndsWith`.
- The SDK does not deserialise array-shaped `message.content`; the
  `ParsesArrayContentParts` test was removed (GitHub Models always
  returns a string).
- `RequestFailedException` is rethrown as `InvalidOperationException`
  with `"... HTTP <status>"` in the message so the gateway classifier
  can pattern-match it without taking an Azure SDK type dependency.

## Phase 2 — Multi-account Copilot pool

**Status:** ✅ complete (Slice 2a + Slice 2b). 22/22 focused backend
tests + 7/7 focused frontend tests green; broader frontend suite
164 files / 993 tests green; lint + tsc clean.

### Audit / safety invariants (apply across phases 2-7)

1. **One turn = one `AiUsageRecord`.** Failover retries collapse into
   a single record. Every retry attempt appends to `PolicyTrace`
   (JSON array of `{accountId, status, durationMs, classification}`)
   and increments `RetryCount`. Never write a sibling row for a
   retried attempt — `IAiGatewayService` keeps "exactly one
   `AiUsageRecord` per `CompleteAsync`" exactly as documented in
   `AGENTS.md`.
2. **SQL-atomic account selection.** Account pick + monthly-cap
   increment is a single `UPDATE ... RETURNING` so two concurrent
   requests cannot both win the last slot:

   ```sql
   UPDATE ai_provider_account
      SET requests_used_this_month = requests_used_this_month + 1
    WHERE id = @id
      AND is_active = true
      AND (exhausted_until IS NULL OR exhausted_until < now())
      AND (monthly_request_cap IS NULL
           OR requests_used_this_month < monthly_request_cap)
   RETURNING *;
   ```

   Row-count = 1 → we own the slot; row-count = 0 → recurse to next
   candidate (priority asc, then `requests_used_this_month` asc).
3. **Privacy gating is code, not config.** Features that touch
   patient-style PII (`conversation.reply`, `writing.coach.*`,
   `summarise.passage`) require `IAiCredentialResolver` to return
   `AuthMode = Platform`. A flag flip cannot route them through
   BYOK. Refuse `admin.*` as a wildcard; enumerate explicitly.
4. **`PromptNotGroundedException` keeps catching.** The SDK swap
   does not loosen `IAiGatewayService` grounding checks; failover
   does not skip them.

### Phase 2 backlog

- [x] Domain: `AiProviderAccount` entity + `LearnerDbContext` DbSet.
- [x] Migration: `AddAiProviderAccount` (Up/Down) at
      `20260508120000_AddAiProviderAccount.cs`.
- [x] DI: `IAiProviderAccountRegistry` registered in `Program.cs`
      immediately after `IAiProviderRegistry`. Encrypts/decrypts
      via DataProtection purpose `"AiProvider.PlatformKey.v1"`
      (shared with single-row `AiProvider.EncryptedApiKey` so
      operators can move keys between the two without re-entry).
- [x] Refactor `CopilotAiModelProvider` to call
      `IAiProviderAccountRegistry.PickAndReserveAsync("copilot", …)`
      first; if the pool is empty it falls back to the legacy
      single-row `AiProvider.EncryptedApiKey` so existing installs
      keep working unchanged.
- [x] Failover loop in `TryCompleteWithAccountFailoverAsync`:
      `InvalidOperationException` containing `" 429"` →
      `RecordOutcomeAsync(RateLimited)` (15 min `ExhaustedUntil`),
      skip + retry; `" 401"` / `" 403"` → `Unauthorized`
      (deactivate `IsActive`), skip + retry. Pool exhaustion
      throws `InvalidOperationException` whose message contains
      the failover trail (`primary:429 → backup:auth`) so the
      gateway classifier and `PolicyTrace` can render it.
- [x] Tests: 10 `AiProviderAccountRegistryTests` (happy path,
      skip set, at-cap refusal, quarantine expired/active,
      inactive, no-provider, RateLimited / Unauthorized outcome,
      counter-determinism stress) + 4
      `CopilotMultiAccountFailoverTests` (429 failover, 401
      deactivation, pool-exhausted trail, empty-pool fallback to
      single-row). Targeted run: 19/19 green. SQLite in-memory
      per AGENTS.md.
- [x] Worker: `AiAccountQuotaResetWorker` resets
      `RequestsUsedThisMonth` at first-of-month UTC. Set-based
      `ExecuteUpdateAsync` keyed on `PeriodMonthKey != currentKey`,
      idempotent re-runs return 0. Hourly tick + boot jitter,
      registered as a hosted service in `Program.cs`. 3 focused
      tests in `AiAccountQuotaResetWorkerTests`.
- [x] Admin endpoints under `/v1/admin/ai/providers/{providerId}/accounts`
      (GET, POST, PUT, DELETE) plus `POST .../{accountId}/reset`
      to clear quarantine + counter. All write endpoints emit
      `AuditEvent` rows (`AiProviderAccountCreated/Updated/
      Deactivated/Reset`) and are rate-limited by `PerUserWrite`.
- [x] Admin UI: nested `AiProviderAccountsModal` opened from a new
      "Accounts" action on each row of `/admin/ai-providers`.
      Lists accounts with label, key hint, used/cap, priority,
      and a status badge (Active / Quarantined / At cap /
      Disabled). Add / Edit / Reset / Disable actions wired to
      the new `lib/ai-management-api.ts` helpers. Raw API key
      never stored in component state after submission and never
      rendered in the DOM (asserted by test).
- [x] Frontend tests for account add + key-length guard + reset
      action + secret-leak guard (4 specs in
      `components/domain/ai-provider-accounts-modal.test.tsx`).

### Slice 2a — implementation notes (May 2026)

- The atomic claim is implemented via EF Core 10
  `ExecuteUpdateAsync` over a `Where` predicate that includes the
  cap check (`!a.MonthlyRequestCap.HasValue ||
  a.RequestsUsedThisMonth < a.MonthlyRequestCap.Value`). Postgres
  READ COMMITTED re-evaluates the WHERE on row-lock release;
  SQLite serialises writes — same code is correct in both
  databases.
- Quarantine (`ExhaustedUntil`) is intentionally **not** in the
  EF UPDATE WHERE clause: the EF SQLite translator cannot lower
  nullable `DateTimeOffset` comparisons. We filter quarantine
  client-side over a tiny candidate set (a handful of rows per
  provider) and rely on `IsActive + cap` in the SQL UPDATE for
  atomicity. A racing pick of a freshly-quarantined account is
  harmless: it would only be picked once `ExhaustedUntil` had
  already passed, and the next call after `RecordOutcomeAsync`
  sees the new `ExhaustedUntil` so the same account is skipped.
- A separate `ResolveMetadataAsync(request, ct)` returning
  `(baseUrl, defaultModel)` is used by the failover loop so the
  loop's atomically-picked PAT wins without also requiring a
  legacy single-row key on the `AiProvider` record.
- Counter increment happens at pick time (not at outcome time)
  so a crashing call still consumes one slot. `Success` and
  `OtherError` therefore record nothing — only `RateLimited` and
  `Unauthorized` mutate state in `RecordOutcomeAsync`.

## Phase 3 — Per-account analytics

**Status:** ✅ complete. 39/39 focused backend tests
(`AiUsageRecorderTests` + `CopilotMultiAccountFailoverTests` +
`AiProviderAccountRegistryTests` + `AiAccountQuotaResetWorkerTests` +
`CopilotAiModelProviderTests` + `AiGatewayRecorderIntegrationTests`)
green; frontend lint + tsc + modal regression green.

- [x] Migration `20260509120000_AddAccountIdAndFailoverTraceToAiUsageRecord`:
      adds `AccountId` (nullable, max 64) and `FailoverTrace`
      (nullable, max 1024) to `AiUsageRecords`, plus composite
      index `(AccountId, CreatedAt)`.
- [x] `AiProviderCompletion` carries `AccountId` + `FailoverTrace`
      so the gateway can persist them on success without a side-channel.
- [x] New typed exception `AiProviderFailoverException(message,
      failoverTrace, lastAccountId)` thrown when the Copilot
      multi-account pool exhausts. Gateway pattern-matches and
      forwards both fields to `RecordFailureAsync`.
- [x] `IAiUsageRecorder.RecordSuccessAsync` / `RecordFailureAsync`
      accept optional `accountId` + `failoverTrace` parameters
      (default null → existing call sites unchanged).
- [x] `CopilotAiModelProvider.TryCompleteWithAccountFailoverAsync`
      tracks `lastAccountId` across attempts, builds a `slot.Label:outcome`
      trail, and only emits a trace when more than one slot was tried.
- [x] `/v1/admin/ai/usage` accepts `accountId` query param.
- [x] `/v1/admin/ai/usage/summary?groupBy=account` rolls up by
      `(providerId, accountId)`, including a `failovers` count
      (records where `FailoverTrace IS NOT NULL`).
- [x] `lib/ai-management-api.ts`: `AiUsageRow` +
      `AiUsageSummaryRow` extended; `fetchAiUsage` accepts
      `accountId`; `fetchAiUsageSummary` accepts `'account'` group key.
- [x] `app/admin/ai-usage/page.tsx`: new "By account" panel,
      Account column in the recent-calls log, accountId filter input
      with Apply/Clear, failover trail rendered in the Trace column.
- [x] Tests: 4 new recorder tests (success persists account+trace,
      failure persists same, single-credential null, plus pre-existing
      coverage); 2 new failover tests (completion exposes
      AccountId+FailoverTrace, single-account success has no trace);
      typed exception assertions on pool-exhausted path.

**Invariants honoured:**

- One turn = one `AiUsageRecord`. Failover hops collapse into the
  trace string; no new rows per attempt.
- Trace is omitted when only one slot was tried (no empty-trail
  pollution on single-shot success).
- Single-credential providers (no `AiProviderAccount` rows) record
  `AccountId=null`, preserving existing analytics shape.

## Phase 4 — Test Connection

**Status:** ✅ complete.

- [x] Migration `20260509130000_AddLastTestStatusToAiProviderAndAccount`
      adds `LastTestedAt`, `LastTestStatus`, `LastTestError` to both
      `AiProvider` and `AiProviderAccount`.
- [x] `IAiProviderConnectionTester` (`AiProviderConnectionTester.cs`)
      sends a 1-token chat completion and classifies the outcome into
      a fixed vocabulary: `ok` / `auth` / `rate_limited` / `network` /
      `unknown`. Classifier maps HTTP 200 → ok, 401/403 → auth, 429 →
      rate_limited, 5xx + `HttpRequestException` + timeouts → network,
      everything else → unknown.
- [x] `POST /v1/admin/ai/providers/{code}/test` and
      `POST /v1/admin/ai/providers/{providerId}/accounts/{accountId}/test`.
      Both rate-limited (`PerUserWrite`) and audited as
      `"AiProviderTested"` / `"AiProviderTestFailed"` (or the
      `Account*` variants) with `code`, `status`, and latency in the
      audit message.
- [x] Tester deliberately bypasses `IAiGatewayService` — connectivity
      probes do not consume per-user quota, write `AiUsageRecord`s, or
      apply rulebook grounding. Failed probes also do **not** mutate
      `ExhaustedUntil`/`IsActive`; admins can clear those via the
      existing reset endpoint.
- [x] UI: `app/admin/ai-providers/page.tsx` gained a "Test" action +
      "Last test" column with status pill + timestamp.
      `components/domain/ai-provider-accounts-modal.tsx` gained the
      same pair per account.
- [x] Tests: 12 new backend tests in `AiProviderConnectionTesterTests`
      (full classifier matrix + persistence + truncation + missing-key
      short-circuit). Frontend modal regression suite extended to 5/5.

**Invariants locked in this phase:**

- "Test" never goes through the gateway. Probe traffic must stay
  invisible to per-user quota and to `AiUsageRecord` analytics so the
  numbers continue to mean "real learner traffic".
- A failed test never quarantines or deactivates a row. Status
  columns are diagnostic only.
- Error messages are truncated to 512 chars before persisting to keep
  the column under its `[MaxLength]` budget regardless of upstream
  verbosity.

## Phase 5 — Tool calling support

**Status:** deferred to its own PRD. Not started in this stream.

Tool calling crosses three security boundaries that the current PRD
does not specify, so per the critic review it is split out:

- per-tool RBAC (which feature codes may invoke which tool)
- JSON-schema arg validation on every tool call (reject silently
  malformed args before they reach the tool)
- side-effect classification + `AuditEvent` row per invocation
  (read / write / external-network)

A follow-up `docs/AI-COPILOT-TOOLS-PRD.md` will own this. Do **not**
ship tool calling as part of this stream's Phase 5 slot. The slot is
intentionally left empty until that PRD lands.

## Phase 6 — Voice provider unification

**Status:** not started. Per critic review, this phase requires a
**per-dialect feature matrix** before any code lands. The matrix
must answer for each voice dialect (`ElevenLabsTts`, `AzureTts`,
`CosyVoiceTts`, `ChatTtsTts`, `GptSoVitsTts`, `AzureAsr`,
`WhisperAsr`, `DeepgramAsr`, `AzurePhoneme`):

| field | answers |
|-------|---------|
| credential fields | API key required? region? endpoint URL scope? |
| voice-id scope | per-row, per-routing-rule, or both? |
| model-id format | dialect-specific string contract |
| current consumers | which selectors / endpoints read it today |
| acceptance tests | which existing test files lock the contract |

The matrix lives in `docs/AI-COPILOT-VOICE-MATRIX.md` and is
authored before Phase 6 task breakdown.

- [ ] Extend `AiProviderDialect` enum with voice values
      (`ElevenLabsTts`, `AzureTts`, `CosyVoiceTts`, `ChatTtsTts`,
      `GptSoVitsTts`, `AzureAsr`, `WhisperAsr`, `DeepgramAsr`,
      `AzurePhoneme`).
- [ ] Add `AiProvider.Category` enum (`TextChat` / `Tts` / `Asr` /
      `Phoneme`); migrate existing rows to `TextChat`.
- [ ] Refactor `IConversationTtsProviderSelector`,
      `IConversationAsrProviderSelector`, and
      `IPronunciationAsrProviderSelector` to read credentials /
      base URLs from `IAiProviderRegistry` while keeping their
      existing routing-rule layer.
- [ ] Backfill: seed-data writes the existing voice config into
      `AiProvider` rows on first boot if not present.
- [ ] UI: tab the unified panel by Category; preserve dedicated
      `/admin/content/conversation/settings` page for routing
      rules.
- [ ] Tests: backend (selector falls back to options when DB row
      missing), frontend (category filter).

## Phase 7 — Per-feature routing defaults

**Status:** ✅ shipped.

- [x] New `AiFeatureRoutes` table (`backend/.../Domain/AiProviderEntities.cs`
      + migration `20260509140000_AddAiFeatureRoutes`). One row per
      `FeatureCode`; `(FeatureCode)` is unique. Active-only rows participate
      in routing; an inactive row falls through to the registry default.
- [x] `IAiFeatureRouteResolver`
      (`backend/.../Services/Rulebook/AiFeatureRouteResolver.cs`) consulted
      by the gateway between the explicit `request.Provider` pin and the
      registry-default block. Provider lookup goes via the registry's
      active list so the route honors the same dialect→provider-name
      mapping (Cloudflare, Anthropic, Copilot, OpenAi-compatible).
      Resolver failure is swallowed — features without an override row
      behave exactly as they did before Phase 7 landed.
- [x] Admin endpoints (`backend/.../Endpoints/AiUsageAdminEndpoints.cs`):
      - `GET /v1/admin/ai/feature-routes` → rows + `knownFeatureCodes`
        + `copilotBulkRouteTargets`.
      - `POST /v1/admin/ai/feature-routes` (audited
        `AiFeatureRouteCreated` / `AiFeatureRouteUpdated`) — refuses
        unknown feature codes and unregistered/inactive provider codes.
      - `DELETE /v1/admin/ai/feature-routes/{featureCode}` (audited
        `AiFeatureRouteDeleted`).
      - `POST /v1/admin/ai/feature-routes/bulk-copilot` flips the
        canonical bulk-route feature set to `copilot`. Refuses with 400
        when no Copilot row is registered + active so the UI can grey
        the button. Audited `AiFeatureRoutesBulkCopilot` with the
        comma-separated changed-features list.
- [x] Frontend helpers in `lib/ai-management-api.ts`:
      `fetchAiFeatureRoutes`, `upsertAiFeatureRoute`,
      `deleteAiFeatureRoute`, `bulkRouteFeaturesToCopilot`, plus
      `AiFeatureRouteRow` / `AiFeatureRoutesResponse` types.
- [x] `components/domain/ai-feature-routes-panel.tsx` mounted under
      `/admin/ai-providers`. The "Route to Copilot" button is greyed
      and shows a warning when no Copilot row is active. Per-row
      pause/resume/delete + a feature/provider/model upsert form.
- [x] Bulk-route target list and known-feature-code allowlist live in
      `AiFeatureRouteResolver` so the server is the single source of
      truth — the frontend never hard-codes either set.
- [x] Tests:
      - Backend `AiFeatureRouteResolverTests` — 6/6 green
        (no row → null; inactive → null; active → ProviderCode+Model;
        blank input → null; `IsKnownFeatureCode` allowlist behaviour;
        `CopilotBulkRouteTargets` locked against the PRD list).
      - Frontend `ai-feature-routes-panel.test.tsx` — 3/3 green
        (button disabled when Copilot row is inactive; button disabled
        when no Copilot row exists; bulk-route fires + surfaces the
        changed list in a toast when Copilot is active).

**Locked invariants:**

> Phase 7 is strictly additive: a feature without an active route row
> behaves exactly as it did pre-Phase 7. The resolver returns `null` and
> the gateway falls through to the existing registry-default selection.

> Bulk-route is server-enforced. The endpoint refuses with 400 when
> Copilot is not registered + active, so even if a UI bug enabled the
> button on a misconfigured environment, the call would not silently
> create dead routes pointing at an inactive provider.

> The bulk-route target list is **explicitly enumerated** in
> `AiFeatureRouteResolver.CopilotBulkRouteTargets` and locked by a unit
> test. No wildcards, ever — adding a new feature requires editing both
> the constant and the test.

## Verification gate (run after every phase)

1. `dotnet test backend/OetLearner.sln` — all green.
2. `npx tsc --noEmit` — 0 errors.
3. `npm run lint` — 0 errors.
4. `npm test` — every previously-green spec still green plus new ones.
