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

## Code paths

| Concern | File |
| --- | --- |
| Encryption + projection allow-list + hint shape | `backend/src/OetLearner.Api/Endpoints/AiUsageAdminEndpoints.cs` (provider + account groups) |
| Connectivity probe + `RedactSecrets` helper | `backend/src/OetLearner.Api/Services/Rulebook/AiProviderConnectionTester.cs` |
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
