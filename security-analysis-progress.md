# Security Analysis Progress

Project: `C:\Users\Dr Faisal Maqsood PC\Desktop\New OET Web App`
Analysis date: 2026-04-24
Scope: repository-level security review of frontend, backend API, auth flows, storage, upload/media paths, deployment configuration, and package vulnerability metadata.

## Phase Status

- [x] Phase 1 - Discovery: attack surface and technology stack
- [x] Phase 2 - Authentication and authorization audit
- [x] Phase 3 - Data protection review
- [x] Phase 4 - Vulnerability assessment
- [x] Phase 5 - Risk analysis and remediation plan

## Remediation Update - 2026-04-24

Status: critical P1 remediation implemented and verified.

### Review Findings Closed

- [x] P1 - Refresh tokens persisted in web storage.
  - Web storage now persists only a sanitized session snapshot without access or refresh tokens.
  - Browser refresh uses the `oet_rt` HttpOnly cookie path. Web responses no longer expose refresh tokens.
  - Native/desktop token body flow remains available only when clients explicitly identify as `capacitor`, `desktop`, or `native`.
  - Sign-out clears web storage, platform secure storage, the indicator cookie, and revokes the current refresh-token family.

- [x] P1 - Media download lacks entitlement checks.
  - Media download, metadata access, and signed media URL generation now enforce admin/owner access, published free-preview access, learner-visible paper asset roles, paper publication, profession match, and the same plan-scoped content entitlement gate used by practice/reading flows.
  - Content entitlement checks now resolve eligibility through `EffectiveEntitlementResolver`, honor `PlanVersionId` snapshots when present, fail closed on broken version anchors or missing live plans, and treat explicit `entitlementsJson.content` scopes as authoritative over legacy `IncludedSubtestsJson`.
  - Paper list/detail, Reading structure/start, and Listening home/session/start endpoints now apply stored active-profession visibility before returning learner stimuli, questions, asset metadata, or attempts.
  - Unauthorized media access returns not found, avoiding asset-existence disclosure.

- [x] P1 - Direct media uploads bypass scanner validation.
  - Direct media upload now runs magic-byte validation and upload scanning before persistence.
  - The endpoint stores validated MIME/extension data and no longer returns internal storage paths.

- [x] P1 - Chunked upload size limits can be bypassed.
  - Chunked upload start rejects invalid declared sizes and chunk sizes.
  - Part upload rejects unexpected part numbers, duplicate parts, empty parts, over-large parts, and writes that exceed declared remaining bytes.
  - Completion requires all declared parts and exact declared byte count before assembly.

- [x] P1 - Webhook verification can downgrade on missing secrets.
  - Billing sandbox fallback now defaults to disabled.
  - Stripe webhook verification fails closed when the webhook secret is missing.
  - Production startup now fails fast if sandbox fallback is enabled or required Stripe secrets/URLs are missing.

### Additional Hardening Completed

- [x] Bulk ZIP import now enforces compressed write limits, safe normalized paths, symlink rejection, entry count, per-entry size, total uncompressed size, and compression-ratio caps.
- [x] npm dependency audit remediated by upgrading vulnerable packages; `npm audit --audit-level=moderate` now reports zero vulnerabilities.
- [x] Android buildscript dependency vulnerabilities remediated.
  - Android Gradle Plugin upgraded from `8.2.1` to `8.13.2`.
  - Gradle wrapper upgraded from `8.2.1` to `8.13`, matching the AGP 8.13 compatibility baseline.
  - Google Services Gradle plugin upgraded from `4.4.0` to `4.4.4`.
  - Buildscript-only resolution pins now force fixed versions for Commons Compress, Commons Lang, Bouncy Castle, Netty, jose4j, and jdom2.
- [x] Production deployment examples now explicitly set `Billing__AllowSandboxFallbacks=false`.
- [x] API proxy CSRF protection moved into the proxy route/helper path; unsafe refresh-cookie proxy requests now require trusted origin and double-submit CSRF validation.
- [x] OTP email verification now returns generic responses for unknown accounts and enforces a per-challenge attempt cap; password reset OTPs also enforce the cap.
- [x] Learner content APIs, rulebook utility APIs, and AI completion now use explicit role policies rather than generic authentication.
- [x] Sanitizer regression tests cover dangerous HTML rendered later through `dangerouslySetInnerHTML` reading surfaces.
- [x] Zoom provider error-body logging now redacts likely secrets and truncates logged payloads.
- [x] Production compose now requires an explicit `API_ALLOWED_HOSTS` value to override broad default host acceptance.

### Verification Completed

- `dotnet test backend/OetLearner.sln` - 601 passed, 0 failed.
- `cmd /c npx tsc --noEmit` - passed.
- `cmd /c npm run lint` - passed.
- `cmd /c npm test` - 112 test files passed, 668 tests passed.
- `cmd /c npm run build` - passed with one existing Sentry/Prisma OpenTelemetry dynamic dependency warning.
- `cmd /c npm run check:encoding` - passed.
- `cmd /c npm audit --audit-level=moderate` - zero vulnerabilities.
- `dotnet list backend\OetLearner.sln package --vulnerable --include-transitive` - no vulnerable packages.
- Android Gradle release runtime and buildscript graphs scanned against OSV - 209 Maven coordinates scanned, zero vulnerable packages.
- `android\gradlew.bat :app:assembleDebug` - passed.
- `android\gradlew.bat :app:assembleRelease` - passed.

### Backlog Disposition

- A2 is accepted as a UX-only indicator cookie. Server data access remains enforced by API authorization; if future server-rendered protected pages include sensitive data, this should be replaced with server-side session validation.
- A3, A4, A5, D5, D6, and V4 are remediated in this pass.

## Phase 1 - Discovery: Attack Surface and Technology Stack

### Technology Stack

- Frontend: Next.js App Router, React 19, TypeScript, Electron, Capacitor.
- Backend: ASP.NET Core minimal API targeting `net10.0`, Entity Framework Core, PostgreSQL via Npgsql.
- Authentication: first-party JWT access tokens, opaque refresh tokens hashed in the database, MFA/TOTP, email OTP flows, role and permission policies.
- Storage and uploads: local file storage, chunked upload service, media asset service, ClamAV scanner integration, HTML sanitizer for content ingestion.
- Observability: Sentry in frontend and backend with explicit PII scrubbing.
- Deployment: Docker Compose production stack with Next.js web, API, PostgreSQL, ClamAV, reverse proxy network, and backup sidecar.

### Main Attack Surface

- Public frontend routes and protected role dashboards under learner, expert, admin, and sponsor paths.
- Backend API endpoints under `/v1/*`, usually reached directly or through the Next.js proxy route `/api/backend/[...path]`.
- Auth endpoints under `/v1/auth/*`: register, sign-in, refresh, sign-out, OTP, MFA, password reset, session management, account deletion.
- Upload and content endpoints: media upload/download, chunked admin uploads, bulk ZIP import, content hierarchy APIs.
- AI and rulebook endpoints: rulebooks, writing linter, speaking auditor, AI completion.
- WebSocket/SignalR notification and conversation hubs that accept access tokens through the query string only on approved hub paths.
- Payment webhooks and subscription checkout APIs.

### Positive Baseline Controls

- Production auth tokens fail fast if issuer, audience, access signing key, refresh signing key, authenticator issuer, or lifetimes are unsafe.
- JWT validation checks issuer, audience, lifetime, signing key, account status, learner suspension, expert activation, and deleted account state.
- Higher-privilege role policies require verified email for expert, admin, and sponsor access.
- Admin authorization uses granular permission policies.
- Production blocks `NoOpUploadScanner` and expects ClamAV-backed scanning.
- Security headers include `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, and a restrictive `Permissions-Policy`.
- Docker production images run as non-root users.
- Sentry disables default PII and scrubs authorization, cookies, CSRF values, IP address fields, request bodies, and query strings.

## Phase 2 - Authentication and Authorization Audit

### Findings

#### A1 - High - Web auth tokens are persisted in JavaScript-readable storage

Evidence:
- `lib/auth-storage.ts` stores the full auth session, including access token and refresh token, in `localStorage` or `sessionStorage`.
- `lib/auth-client.ts` reads the stored access token to build Bearer headers and sends refresh tokens in request bodies.
- `backend/src/OetLearner.Api/Services/AuthService.cs` already supports the `oet_rt` HttpOnly refresh cookie but still returns the refresh token in response bodies.

Impact:
- Any frontend XSS can steal long-lived refresh tokens, mint new sessions, and bypass the intended HttpOnly cookie protection.
- Native/mobile storage currently mirrors auth state into web storage via Capacitor Preferences instead of using the existing secure-storage wrapper.

Recommendation:
- Complete the migration to HttpOnly refresh cookies: stop returning refresh tokens to web clients, stop accepting refresh tokens in web request bodies, keep access tokens in memory only, and refresh through the cookie-bound endpoint.
- Use platform secure storage for native/mobile auth state instead of Preferences/localStorage.
- Add tests that assert refresh tokens are absent from web responses and storage payloads.

#### A2 - Medium - Next middleware route protection is a spoofable UX gate, not a server auth decision

Evidence:
- `middleware.ts` uses the client-set `oet_auth` cookie as the protected-route indicator.
- `lib/auth-storage.ts` writes that cookie from JavaScript and it is not HttpOnly.

Impact:
- A user can set `oet_auth=1` and bypass middleware redirects for protected pages. API calls still require real Bearer tokens, so this is mostly route privacy and UX integrity risk, not direct API authorization bypass.

Recommendation:
- Treat the middleware cookie only as a display hint.
- If protected pages contain sensitive server-rendered data in the future, use server-side session validation or a signed HttpOnly session indicator.

#### A3 - Medium - Intended CSRF middleware does not execute for API proxy routes

Evidence:
- `middleware.ts` contains CSRF logic for `/api/backend/*` mutation methods.
- The matcher excludes `api`, so `/api/backend/*` never enters this middleware.
- `lib/backend-proxy.ts` performs origin checks, but accepts missing `Origin` and `Referer` headers for unsafe methods.

Impact:
- The code suggests there is a CSRF gate, but it is currently unreachable for the backend proxy route.
- Risk increases materially once refresh/auth moves to cookie-based browser flows.

Recommendation:
- Move CSRF validation into `app/api/backend/[...path]/route.ts` or include API proxy paths in the middleware matcher.
- Require configured trusted origins for unsafe methods and remove hard-coded production origins.
- Pair cookie-based auth with a real double-submit or synchronizer-token CSRF control.

#### A4 - Medium - OTP protections are incomplete

Evidence:
- `EmailOtpService` increments OTP attempt counts but does not enforce a per-challenge max-attempt lockout.
- OTP send rate limiting appears to rely on `httpContext.Items["otp_email"]` being populated inside handlers, but endpoint rate limiting normally runs before handler bodies.
- `/v1/auth/email/send-verification-otp` can reveal whether an email account exists because nonexistent accounts return an account-not-found path.

Impact:
- Attackers may brute-force individual OTP challenges within the broader IP limiter.
- Email-scoped OTP throttling may silently fall back to IP-based throttling.
- Account enumeration is possible on the verification OTP endpoint.

Recommendation:
- Enforce max attempts per OTP challenge in the database-backed verification path.
- Partition OTP send rate limits using request data available before handler execution, or implement explicit throttling inside the service.
- Return generic responses for anonymous email verification OTP send attempts.

#### A5 - Medium - Some learner and AI endpoints use generic authentication instead of role-specific policies

Evidence:
- `ReadingLearnerEndpoints.cs` uses `.RequireAuthorization()` for learner reading-paper workflows.
- `ContentHierarchyEndpoints.cs` exposes learner content browser APIs with generic authentication.
- `RulebookEndpoints.cs` uses generic authentication for AI/rulebook endpoints, including AI completion.
- Sponsor policy exists, but the central `ApplicationUserRoles` constants only include learner, expert, and admin.

Impact:
- Non-learner roles may be able to create or access learner-scoped content records under their account IDs.
- AI completion may be available to any authenticated role unless downstream quota checks fully constrain it.
- Role taxonomy drift can cause authorization gaps as sponsor features grow.

Recommendation:
- Add and enforce explicit role policies for learner-only APIs and AI-cost-bearing APIs.
- Centralize the sponsor role constant and add role-matrix tests for learner, expert, admin, sponsor, suspended, deleted, and unverified accounts.

## Phase 3 - Data Protection Review

### Findings

#### D1 - High - Media asset download lacks per-asset authorization

Status: remediated in the P1 hardening pass. Media download, metadata, and signed URL endpoints now use shared media authorization based on admin/owner access, published free previews, learner-visible paper asset roles, paper publication/profession checks, and plan-scoped content entitlement resolution.

Evidence:
- `MediaEndpoints.cs` protects the media group with generic authentication.
- `HandleDownloadAsync` streams a media asset by ID when status is ready and storage path exists.
- The comment says access is authorized by containing content paper status/profession, but the code does not enforce owner, role, entitlement, or publication checks.

Impact:
- Any authenticated user who can obtain or guess a media asset ID may download ready media regardless of content entitlement or ownership.

Recommendation:
- Before streaming media, resolve the owning content item and enforce publication, profession, role, subscription, and ownership checks.
- Add negative tests proving learner, expert, sponsor, and unrelated users cannot fetch unauthorized assets.

#### D2 - High - Media upload path bypasses hardened content validation and scanning

Evidence:
- `MediaEndpoints.cs` validates upload using only declared content type and file extension.
- `IUploadContentValidator` and `IUploadScanner` are used in chunked content upload paths but not in the direct media upload endpoint.

Impact:
- Authenticated users can upload mislabeled or malicious bytes under allowed media extensions and MIME types.
- Production scanner coverage is inconsistent.

Recommendation:
- Apply magic-byte validation and scanner checks to every upload path, including direct media uploads.
- Store only validated assets and quarantine/reject failed scans consistently.

#### D3 - High - Chunked uploads can exceed declared and role-based size limits

Evidence:
- `ChunkedUploadService.StartAsync` validates the declared upload size.
- `UploadPartAsync` accepts part uploads without enforcing `partNumber <= TotalParts`, per-part size, cumulative bytes <= declared size, or cumulative bytes <= role limit.

Impact:
- A compromised or malicious admin session can write more data than declared and exhaust local disk storage.

Recommendation:
- Reject unexpected part numbers.
- Enforce per-part and cumulative byte limits before and during writes.
- Verify final assembled size exactly matches declared size before scan and promotion.

#### D4 - High - Bulk ZIP import lacks decompression-bomb controls

Evidence:
- `ContentBulkImportService` writes the uploaded ZIP before checking total ZIP size.
- Extraction does not enforce total uncompressed bytes, entry count, per-entry size, or compression ratio.

Impact:
- A malicious ZIP can exhaust disk or CPU during staging/extraction.

Recommendation:
- Enforce compressed size before writing.
- Limit entry count, per-entry uncompressed size, total extracted bytes, and compression ratio.
- Reject symlinks and special file types explicitly.

#### D5 - Medium - HTML rendering surfaces need end-to-end sanitizer coverage tests

Evidence:
- Frontend uses `dangerouslySetInnerHTML` for reading paper body HTML and pronunciation tips HTML.
- Backend has a strong `HtmlSanitizerService`, but each ingestion path must be verified to use it.

Impact:
- Any unsanitized admin-authored or AI-generated HTML reaching those fields can become stored XSS.

Recommendation:
- Add tests for every field rendered with `dangerouslySetInnerHTML`.
- Sanitize AI-generated HTML server-side before persistence and again before publication if editors can modify it.

#### D6 - Low - External provider error body logging should be sanitized

Evidence:
- `ZoomMeetingService.cs` logs Zoom API and token endpoint error response bodies.

Impact:
- Provider error bodies are usually not secret, but can include request context or operational details that do not belong in long-term logs.

Recommendation:
- Truncate and sanitize third-party error bodies before logging.
- Keep correlation IDs/status codes rather than raw response payloads.

## Phase 4 - Vulnerability Assessment

### Dependency Findings

#### V1 - High - npm audit reports active critical and high vulnerabilities

Command run:

```powershell
cmd /c npm audit --audit-level=moderate --json
```

Result:
- 12 total npm vulnerabilities: 1 critical, 8 high, 3 moderate.
- Direct high-risk packages include `next`, `@sentry/nextjs`, and `@capacitor/cli`.
- Notable transitive advisories include `protobufjs` arbitrary code execution, `tar` path traversal/hardlink/symlink issues, `rollup` arbitrary file write, `vite` dev-server file read/path traversal, `@xmldom/xmldom` DoS/XML injection, and `lodash` code injection/prototype pollution.

Recommendation:
- Upgrade Next.js to a patched release beyond the vulnerable range.
- Upgrade Sentry Next.js packages and Capacitor CLI, accepting semver-major upgrades where required after testing.
- Run the full project verification matrix after upgrades: TypeScript, lint, tests, backend tests, build, and encoding check if available.

#### V2 - No current NuGet package vulnerabilities reported

Commands run:

```powershell
dotnet list backend\src\OetLearner.Api\OetLearner.Api.csproj package --vulnerable --include-transitive
dotnet list backend\tests\OetLearner.Api.Tests\OetLearner.Api.Tests.csproj package --vulnerable --include-transitive
```

Result:
- No vulnerable packages were reported by the configured NuGet sources for the API project or API test project.

### Configuration Findings

#### V3 - High - Billing webhook sandbox fallback is enabled by default

Evidence:
- `BillingOptions.AllowSandboxFallbacks` defaults to `true`.
- `PaymentGatewayService` accepts fallback webhook verification when a webhook secret is missing and sandbox fallback is enabled.
- Production compose passes billing secrets from environment but does not explicitly set `Billing__AllowSandboxFallbacks=false`.

Impact:
- A production secret misconfiguration can silently downgrade webhook verification behavior.
- Payment/subscription state is security-sensitive because it gates entitlements.

Recommendation:
- Default `AllowSandboxFallbacks` to `false`.
- Enable sandbox fallback only in development or explicit test environments.
- Fail fast in production if a configured payment provider lacks webhook verification secrets.

#### V4 - Low - Production host allow-list is broad

Evidence:
- `appsettings.json` sets `AllowedHosts` to `*`.

Impact:
- Reverse proxy controls may compensate, but unrestricted host acceptance weakens host-header hardening.

Recommendation:
- Set production allowed hosts to the real application domains.

## Phase 5 - Risk Analysis and Remediation Plan

### Priority 0 - Immediate Production Hardening

1. Patch npm vulnerabilities, especially Next.js and `protobufjs` transitive exposure.
2. Set `Billing__AllowSandboxFallbacks=false` in production and add production startup validation for webhook secrets.
3. Add authorization checks to media download and metadata endpoints.
4. Apply scanner and magic-byte validation to direct media uploads.

### Priority 1 - Auth Session Migration

1. Stop storing refresh tokens in localStorage/sessionStorage.
2. Stop returning refresh tokens to web clients.
3. Use HttpOnly Secure SameSite Strict refresh cookies for web refresh only.
4. Store mobile tokens in platform secure storage, not Capacitor Preferences.
5. Add regression tests that inspect persisted auth state and refresh responses.

### Priority 2 - Upload and Storage Abuse Controls

1. Enforce chunked upload part count, part size, declared size, and role size limits before writes.
2. Add ZIP decompression controls: max entries, total extracted bytes, per-entry size, compression ratio, and file type restrictions.
3. Add storage quota monitoring and alerting for upload staging directories.

### Priority 3 - Authorization Matrix Cleanup

1. Convert learner-facing endpoints from generic auth to `LearnerOnly` where intended.
2. Gate AI-cost-bearing endpoints with explicit role and quota policies.
3. Add a centralized sponsor role constant.
4. Add integration tests for role access across learner, expert, admin, sponsor, unverified, suspended, and deleted states.

### Priority 4 - Defense-in-Depth Cleanup

1. Move CSRF validation into the API proxy route or include API proxy routes in middleware matching.
2. Remove hard-coded origin allow-list values from proxy validation and use configuration.
3. Enforce OTP max attempts and generic anonymous OTP responses.
4. Add sanitizer tests for every `dangerouslySetInnerHTML` surface.
5. Restrict production `AllowedHosts`.
6. Sanitize or truncate third-party provider error bodies before logging.

## Verification Notes

- This was a static repository audit plus package advisory check. I did not run dynamic penetration tests or fuzzers.
- `rg` was not usable in this environment due to access-denied errors, so file discovery used PowerShell `Get-ChildItem` and `Select-String`.
- Dependency advisory checks were run for npm and NuGet as listed above.
