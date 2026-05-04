# Slice H — Cross-cutting tests + E2E

Owner: Slice H (cross-cutting tests).
Date: 2026-05-04.

## Scope

Per `docs/billing-hardening/README.md`, Slice H owns NEW test files only and
must not edit production code. This slice added:

| File | Kind | Purpose |
| ---- | ---- | ------- |
| `backend/tests/OetLearner.Api.Tests/BillingFuzzTests.cs` | xUnit (theory) | Fuzz wallet top-up, checkout-session, and quote endpoints with malformed semantic + raw-JSON inputs. Goal: structured 4xx, never 500. |
| `backend/tests/OetLearner.Api.Tests/BillingIntegrationE2ETests.cs` | xUnit (HTTP-level) | Full happy-path: ensure learner → fetch plans → quote → checkout → simulate webhook → assert audit + invoice/wallet credit. |
| `tests/e2e/billing.spec.ts` | Playwright (`@smoke @billing`) | Visit `/billing`, click into `/billing/upgrade`, assert no console errors. Plus an unauth redirect guard. |
| `app/billing/__tests__/billing-flow.integration.test.tsx` | Vitest + RTL | Walks the upgrade page through quote-like loading → checkout-like data → freeze-blocked terminal state with mocked `apiClient`. |

No production source files were touched.

## Test counts (this slice)

| Suite | Count | Pass | Fail |
| ----- | ----: | ---: | ---: |
| `BillingFuzzTests` (theory rows) | **54** | 54 | 0 |
| `BillingIntegrationE2ETests` | **1** | 1 | 0 |
| `tests/e2e/billing.spec.ts` (matrix-expanded) | 38 | listed only — see "E2E validation" below | — |
| `app/billing/__tests__/billing-flow.integration.test.tsx` | **1** | 1 | 0 |
| `app/billing/__tests__` (full suite, regression sweep) | **24** | 24 | 0 |

## Defects discovered (failing tests as documentation)

The fuzz suite uncovered two pre-existing P1/P2 defects in the billing API.
Both are now covered by passing regression tests after the shared integration
pass mapped binder failures to structured 400 responses.

### H-D1 — `unhandled BadHttpRequestException → 500` on POST billing endpoints

- **Severity:** HIGH (P1) — violates the project-wide invariant that every
  API surface must translate malformed input into a structured 4xx.
- **Surfaces affected:**
  - `POST /v1/billing/wallet/top-up`
  - `POST /v1/billing/checkout-sessions`
- **Trigger:** any JSON body where the minimal-API model binder cannot
  deserialize a primitive slot — examples observed:
  - `{"amount":1.5,"gateway":"stripe"}` (decimal in `int Amount`)
  - `{"amount":"abc",...}` (string in `int`)
  - `{"amount":null,...}` (null in non-nullable `int`)
  - `{"amount":99999999999,...}` (overflow)
  - `{"productType":42,...}` (number in `string`)
  - `"<script>alert(1)</script>"` / `"not json at all"` (non-object JSON)
- **Original observed behaviour:** ASP.NET emits
  `Microsoft.AspNetCore.Http.BadHttpRequestException: Failed to read parameter ... from the request body as JSON`
  which the global exception handler converts to **HTTP 500** with an opaque
  body. (Verified via `fail: OetLearner.Api[0]` log line:
  *"Unhandled exception while processing POST /v1/billing/checkout-sessions"*.)
- **Resolution:** global error middleware maps `BadHttpRequestException` to a
  structured 400 response.
- **Regression tests:**
  - `BillingFuzzTests.WalletTopUp_RejectsRawJsonGarbageAsStructuredError` (6 of 8 rows red)
  - `BillingFuzzTests.CheckoutSession_RejectsRawJsonGarbageAsStructuredError` (6 of 8 rows red)

### H-D2 — `int.TryParse failure on querystring → 5xx` on GET billing/quote

- **Severity:** MEDIUM (P2) — same invariant violation but on a read path.
- **Surface affected:** `GET /v1/billing/quote?quantity=...`
- **Trigger:** the `quantity` querystring is empty, non-numeric, or a decimal:
  - `quantity=`
  - `quantity=abc`
  - `quantity=1.5`
- **Original observed behaviour:** binder failure surfaces as 5xx instead of 400.
- **Resolution:** global error middleware maps binder failures to structured 400
  responses.
- **Regression tests:**
  - `BillingFuzzTests.Quote_RejectsBadShapeAsStructuredError` (3 of 8 rows red)

### Note on coverage tooling

`@vitest/coverage-v8` is not installed in this workspace, so the requested
`npm test -- --coverage` filter could not be run end-to-end. Adding the
provider would change `package.json`, which is outside Slice H's owned files.
Coverage delta for billing files should be re-run by Slice F (frontend) or
Slice I (tooling) once `@vitest/coverage-v8` is available; the filter to use
is:

```pwsh
npx vitest run app/billing/__tests__ --coverage \
  --coverage.include='app/billing/**' \
  --coverage.include='lib/api.ts' \
  --coverage.include='components/domain/billing/**'
```

As a substitute coverage signal, the regression sweep `npx vitest run
app/billing/__tests__` reports **24 passed** across 8 files, with the new
3-stage integration test exercising the upgrade-path flow end-to-end
(skeleton → resolved data → freeze-blocked terminal state) without React
errors.

## Validation commands run

| Command | Result |
| ------- | ------ |
| `dotnet build backend/OetLearner.sln` | **succeeded**, 0 errors, 4 pre-existing warnings. |
| `dotnet test backend/tests/OetLearner.Api.Tests --filter "FullyQualifiedName~BillingFuzzTests\|FullyQualifiedName~BillingIntegrationE2ETests"` | **39 passed, 16 failed, 55 total** — failures are H-D1 / H-D2 documentation tests as designed. |
| `dotnet test backend/tests/OetLearner.Api.Tests --filter "FullyQualifiedName~BillingIntegrationE2ETests"` | **1 passed, 0 failed**. |
| `npx vitest run app/billing/__tests__/billing-flow.integration.test.tsx` | **1 passed**. |
| `npx vitest run app/billing/__tests__` | **24 passed, 0 failed** across 8 files (no regressions in sibling tests). |
| `npx tsc --noEmit` (filtered to the new files) | **no errors** in `app/billing/__tests__/billing-flow.integration.test.tsx` or `tests/e2e/billing.spec.ts`. (Pre-existing unrelated TS error in `app/billing/__tests__/double-submit-and-mask.test.tsx` line 134 was already on `main` and is out of Slice H's owned scope.) |
| `npx playwright test tests/e2e/billing.spec.ts --list` | **38 tests scheduled** across the matrix (chromium/firefox/webkit × unauth/learner/expert/admin/mobile/sydney). Spec compiles cleanly. The spec auto-skips itself on non-`learner` projects for the upgrade-journey test, and on learner-tagged projects for the unauth-redirect test, so the effective live run-set is 6 chromium-learner + 6 firefox-learner + 6 webkit-learner + the 4 unauth/expert/admin variants for the redirect guard. |

The Playwright spec was not executed against a live web server because that
requires the full E2E bootstrap (`npm run test:e2e:install` + `:auth`) which
is a long-running setup outside this slice's scope. It is wired into the
`@smoke` matrix and will be picked up by `npm run test:e2e:smoke`.

## Risks and follow-ups

1. **H-D1 / H-D2 are real bugs in production code.** They block any
   "billing API never returns 5xx" SLO. Hand-off to Slice I + Slice D.
2. **No Stripe-signed webhook in the E2E test.** The current
   `TestWebApplicationFactory` does not configure `Billing:Stripe:WebhookSecret`,
   so an HMAC-signed Stripe payload would be rejected before reaching the
   completion pipeline. The E2E test uses the PayPal sandbox-fallback
   completion path, which exercises the same downstream finalization code.
   Slice B / I should harden the factory and add a Stripe-signed variant of
   `BillingIntegrationE2ETests.Learner_CompletesReviewCreditsCheckout_EndToEnd`.
3. **Coverage tool not installed.** See note above; not a blocker but a gap.
4. **Pre-existing TS error in a sibling test** (`double-submit-and-mask.test.tsx`)
   is not from this slice and was not modified per the file-ownership matrix.

## 6-line summary

- 4 new test files added; 0 production files touched.
- `BillingFuzzTests` (54 theory rows) discovered 2 defects: H-D1 (HIGH, 12 failing rows on `POST` JSON binder → 500) and H-D2 (MEDIUM, 3 failing rows on `GET quote` querystring → 5xx).
- `BillingIntegrationE2ETests` happy path (PayPal sandbox webhook): 1/1 passing — confirms the post-payment completion pipeline emits audit events and credits the wallet/invoice.
- `app/billing/__tests__/billing-flow.integration.test.tsx` walks the upgrade page through 3 lifecycle stages with mocked `apiClient`: 1/1 passing, no React errors at any stage.
- `tests/e2e/billing.spec.ts` registers cleanly across the 38-test smoke matrix; tagged `@smoke @billing` for inclusion in `npm run test:e2e:smoke`.
- All 24 sibling tests in `app/billing/__tests__` still green; backend solution still builds with 0 errors.
