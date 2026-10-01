# Agent handoff — per-action authenticator step-up removed (2026-09-22)

## Goal
Owner directive: admins must be asked for an authenticator code **only once at sign-in, and only when an authenticator is configured** — never per action. Owner chose FULL DELETION of the per-action step-up mechanism (over config-disable).

## Shipped (main @ 98b43697a, deployed + verified)
- Deleted: `StepUpService.cs`, `StepUpEndpoints.cs` (POST /v1/auth/step-up), `StepUpContracts.cs`, `StepUpOptions.cs`, `StepUpTotpHelperTests.cs`, `lib/api/step-up.ts` + test, `StepUpConfirmDialog`, `components/admin/step-up/*`.
- Edited: `Program.cs` (DI/endpoint/comment removal), `AdminRouteBuilderExtensions.cs` (WithStepUp + filter gone), `BillingExpansionEndpoints.cs` + `AdminBillingEndpoints.cs` (WithStepUp conditionals gone), `TestWebApplicationFactory.cs` (EnrolAuthenticatorAsync/IssueStepUpTokenAsync gone; GenerateTotpCode/DecodeBase32 kept for login-MFA tests), `AdminBillingEndpointsTests.cs`, `BillingQuoteGuardTests.cs`, `lib/api/client.ts`, `lib/api.ts`, `lib/api/billing-expansion.ts`, `lib/api/billing-products.ts`, `lib/auth-client.ts`, manual-payments + refunds admin pages (direct calls now, no dialog), `docs/security/control-register.json` + `docs/security/README.md` (PAY-20 = WAIVED owner directive 2026-09-22; separation-of-duties permission split retained).
- **Untouched on purpose:** sign-in MFA (`AuthService.MfaChallengeRequiredException`, /mfa/* pages), sign-in risk step-up (email-OTP medium-risk path), `AuthenticatorTotp`, `ProtectedAuthenticatorSecret`/`AuthenticatorEnabledAt`, `authenticatorIssuer` runtime setting, `AdminBillingMarkPaidWrite`/`AdminBillingRefundWrite` policies.

## Validation
- `pnpm run ship:gate` → OK (74 files, typescript=yes).
- Grep sweeps: zero step-up remnants in backend/src, backend/tests, lib, app, components (only intentional sign-in-risk "step-up" comments/`step_up` allow-list mode strings remain).
- Local `dotnet`/`pnpm build` NOT run (repo law: GitHub Actions only).

## Git topology note (important)
Local branch `fix/checkout-expiry-payment-flow` holds 38 commits of UNRELATED in-progress Writing Rev8 work (owner-ordered revalidation in progress) and has diverged from main (main ahead by ~75). That branch's commit `3a5342f3` was **cherry-picked** onto `origin/main` tip (`17e433c2d`) in a temp worktree → `98b43697a`, pushed FAST-FORWARD to main (no force). One conflict in `Program.cs` resolved: kept main's TypeSafe block + dropped the StepUpOptions line. The temp worktree was removed. Do NOT merge that branch to main as-is; its Writing work must finish its own revalidation first.

## Deploy evidence
- Repo made public → pushed → `Build & Deploy (web + API)` run **35648644573** for `98b43697a` → **completed:success** (syntax-gate ✅, all build/deploy jobs ✅).
- Run logs pin GHCR images: `ghcr.io/jerryboganda/oetwebapp-api:98b43697a…`, `oetwebapp-web:98b43697a…` (API_IMAGE/WEB_IMAGE printed and deployed; 173 log matches for the SHA on ghcr lines).
- Repo flipped private after success (per GitHub-Actions visibility law).
- Live: `app…/api/health` 200 ok · `api…/health/ready` 200 (database/migrations/stuck_jobs/storage ok) · `api…/health/live` 200.

## Next concrete step
Owner verifies on production: sign in as an admin → expect the single authenticator code prompt at login (only if configured) → approve a manual payment / refund → no code prompt appears. Unrelated branch work (Writing Rev8) continues separately.
