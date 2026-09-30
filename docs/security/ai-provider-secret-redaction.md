# Admin AI Provider Secret Redaction Policy

> Authoritative invariants for **RW-019** — admin-managed provider
> credentials must never become a plaintext secret store. This file
> documents the contract that the backend code + tests enforce, so a
> future change cannot silently weaken it.

## Scope

Applies to every admin-facing surface that stores or returns AI
provider credentials:

- `AiProvider` rows (platform key) — managed via
  `POST/PUT/GET/DELETE /v1/admin/ai/providers[/{id}]`
- `AiProviderAccount` rows (multi-account pool PATs) — managed via
  `POST/PUT/GET/DELETE /v1/admin/ai/providers/{providerId}/accounts[/{accountId}]`
- The connectivity probe `POST /v1/admin/ai/providers/{code}/test` and
  `POST /v1/admin/ai/providers/{providerId}/accounts/{accountId}/test`,
  whose result is persisted into `LastTestStatus` / `LastTestError`.

## Invariants

1. **Secret entry is server-side only.** The admin UI POSTs the new
   key once; the server encrypts it via ASP.NET Data Protection
   (`AiProvider.PlatformKey.v1` purpose) and stores it in
   `EncryptedApiKey`.
2. **Responses never include the raw or encrypted key.** The only
   admin-visible material is `apiKeyHint` (last 4 characters with a
   leading ellipsis, e.g. `…ab12`).
3. **List/read projections are explicit allow-lists.** The
   `GET /v1/admin/ai/providers` and
   `GET /v1/admin/ai/providers/{providerId}/accounts` projections in
   `AiUsageAdminEndpoints.cs` enumerate every column they return; new
   secret-bearing columns will not leak unless someone explicitly adds
   them to that projection.
4. **Audit detail strings only contain non-secret metadata.** Audit
   rows for `AiProviderCreated/Updated/Deactivated/Tested[Failed]` and
   the equivalent account events use the provider `code` / account
   `label` only — never the key itself or the encrypted blob.
5. **Connectivity-probe error messages are redacted before
   persistence.** Providers commonly echo the offending Authorization
   header back in error bodies. `AiProviderConnectionTester` strips:
   - the live decrypted key for the probe (exact-string match), and
   - the documented PAT/key prefixes for the providers we currently
     support plus common third-party prefixes:
     `github_pat_…`, `ghp_…`, `gho_…`, `ghu_…`, `ghs_…`, `ghr_…`,
     `sk-ant-…`, `sk-proj-…`, `sk-…`, `AIza…`, and `xox[baprs]-…`.
   The redaction runs against both the JSON-extracted error message and
   the raw exception message before truncation, so neither
   `LastTestError` (database column) nor the JSON returned by the
   `…/test` endpoint can contain the secret.
6. **Runtime provider errors keep provider text in one log line per call
   path, never anywhere else.**
   When a live provider call returns a non-success status,
   `AiProviderErrorParser` reduces the body to a failure class, the HTTP
   status, allow-listed `type` / `code` / request-id tokens and a
   single-line message of at most 300 characters. Sanitising redacts the
   WHOLE raw text first, and only then applies the 2000-character raw cap,
   the head-only cut (below), control-character removal and the 300-character
   final cap, so a secret that straddles a cut can never survive as a
   fragment; a redaction that cannot finish (regex timeout) yields no text.
   That message is written to exactly one structured server log line per
   failed call: `AI provider call failed: ...` in `AiGatewayService` for
   gateway calls (attached to `AiProviderHttpException.ProviderError`), and
   `Live voice {Provider} session creation failed: ...` in `LiveVoiceService`
   for the realtime voice create calls, which never go through the gateway.
   It never enters `Exception.Message`, an `AiUsageRecord` row (only
   class-derived codes such as `provider_quota_exhausted` and an
   allow-listed message), or a client response (live voice answers a generic
   503). For non-first-party hosts (the subscription sidecars) only the head
   of the message before the first colon, at most 60 characters, is kept,
   because a sidecar can echo CLI output. Live voice keeps provider text only
   for HTTP 429 and 5xx answers from `api.openai.com` and
   `generativelanguage.googleapis.com` (an allow-list: a 400, 413, 415 or 422
   can echo the request, a 401 or 403 a masked key, a 404 the URL, and the
   request carries the hidden card instructions and the learner's SDP); every
   other status logs class, status, type, code and request id only, and the
   live voice catalog probe never keeps provider text. Message phrases
   ("credit balance", "usage limits", ...) do not decide the class of an
   OpenAI or Gemini 400, 413, 415 or 422 either, so echoed request content
   cannot open a shared circuit breaker.
7. **The `subscription-sidecar` marker is not a secret.** The keyless
   subscription sidecar rows store this literal in `EncryptedApiKey`; the
   registry returns it as the key (the sidecars ignore it) only while the
   row's `BaseUrl` host is on `OET_INTERNAL_AI_HOSTS` (a row re-pointed at a
   public URL stops looking credentialed and fails as "Platform API key
   missing"), and the admin probe treats such a row as credentialed, probing
   the sidecar's `GET /healthz` rather than running a completion. A stored
   value that is neither the marker nor decryptable makes the probe return
   status `auth` with a clear message instead of an unhandled error.

## Code paths

| Concern | File |
| --- | --- |
| Encryption + projection allow-list + hint shape | `backend/src/OetLearner.Api/Endpoints/AiUsageAdminEndpoints.cs` (provider + account groups) |
| Connectivity probe + `RedactSecrets` helper | `backend/src/OetLearner.Api/Services/Rulebook/AiProviderConnectionTester.cs` |
| Runtime provider-error parsing + sanitising | `backend/src/OetLearner.Api/Services/Rulebook/AiProviderError.cs` (tests: `AiProviderErrorParserTests`, `AiProviderHttpExceptionTests`) |
| The single provider-error log line + usage-row message | `backend/src/OetLearner.Api/Services/Rulebook/AiGatewayService.cs` (`LogProviderFailure`, `SanitiseProviderErrorMessage`) |
| The realtime voice provider-error log line (allow-listed hosts and statuses) | `backend/src/OetLearner.Api/Services/Speaking/LiveVoiceService.cs` (`RetainsProviderText`, `FailProviderAsync`) |
| Encrypted column declaration | `backend/src/OetLearner.Api/Domain/AiProviderEntities.cs` |

## Test evidence

The contract above is locked down by xUnit tests in
`backend/tests/OetLearner.Api.Tests/`:

- `AiProviderConnectionTesterTests`
  - `ProviderProbe_RedactsLiveApiKeyEchoedInErrorBody` — provider
    rejection echoes the live PAT in its JSON error; tester strips it
    from both the returned `ErrorMessage` and the persisted
    `LastTestError`.
  - `ProviderProbe_RedactsForeignPatPatternsEvenWithoutLiveKeyMatch` —
    redaction covers other vendors' prefixes (e.g. `sk-ant-…`) even
    when the active provider's own key is not the leaked value.
  - `NetworkException_RedactsLiveApiKeyEchoedInExceptionMessage` —
    `HttpRequestException` paths (DNS / TCP / TLS errors) are
    redacted before persistence.
  - `AccountProbe_RedactsLiveAccountKeyEchoedInErrorBody` — same
    contract for the per-account probe path.
- `AdminAiProviderSecretsTests`
  - `PostProvider_DoesNotReturnRawOrEncryptedKey` — `POST` response
    only contains `id`, `code`, and an `apiKeyHint` that is exactly
    `…` + the last 4 characters of the submitted key.
  - `GetProviders_DoesNotIncludeRawOrEncryptedKey` — list response is
    schema-asserted against `apiKey` / `encryptedApiKey` keys and the
    hint is again checked to equal `…` + last 4 characters.
  - `PostProvider_PersistsEncryptedKeyOnly_AndAuditDoesNotLeakIt` —
    DB `EncryptedApiKey` is non-empty, ≠ raw key, ≠ contained in
    audit `Details`; `ApiKeyHint` is exactly the `…XXXX` shape.
  - `PutProvider_RotatedKey_DoesNotLeakInResponseOrPersistence` —
    rotation via `PUT` re-encrypts the key, response only carries the
    new hint, the persisted column never contains either the old or
    the new raw key.
  - `PostProviderAccount_DoesNotReturnRawOrEncryptedKey_AndPersistsEncryptedOnly`
    — same end-to-end proof for the account pool.
  - `PutProviderAccount_RotatedKey_DoesNotLeakInResponseOrPersistence`
    — rotation contract for accounts.

## Out of scope (handled elsewhere)

- BYOK learner credentials (`UserAiCredential`) — covered by the
  AI-usage policy doc and learner-side gateway tests.
- Provider SDK transport-level logging — providers are accessed via
  `IHttpClientFactory`; we do not add request/response logging
  middleware in production builds.
- Secret rotation cadence and human KMS handling — operational, not
  enforced by code.
