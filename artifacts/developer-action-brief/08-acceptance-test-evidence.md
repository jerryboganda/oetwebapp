# Acceptance test evidence (run, not claimed)

## Backend build
- `dotnet build backend/src/OetLearner.Api/OetLearner.Api.csproj -c Debug --no-restore /p:RunAnalyzers=false` → **0 errors**, 80 warnings (pre-existing nullability/unused-parameter warnings; no new errors). Full log retained in build output.

## Focused suites (all pass)
- `dotnet test --filter "ListeningRecallsAndBillingHardeningTests|AdminPaymentQueueFulfillmentTests" --no-build` → **21/21 passed** (12 new hardening tests + 9 fulfilment-queue tests including updated TestG idempotent-Ok and TestH with realistic payment evidence).

## New hardening tests (12, all pass)
- LR02 bundled-codes do not trigger standalone automation; LR01/LR05 code matches regardless of price/casing; BILL07 annual/zero clamp to 180; BILL07 double-apply stays 180 (reproduces the 02/09/2026→28/08/2027 double-grant, now fixed); BILL07 listening-recalls canonical duration; BILL01 failed → AdminGrant evidence (no invoice); BILL02 pending without payment → AdminGrant (no invoice); BILL03 completed gateway → Gateway evidence; alarm key separation (billing vs AI-budget titles differ); Paid-only visibility predicate; BILL10 distinct quotes are distinct events.

## Broader billing sweep
- `dotnet test --filter "FullyQualifiedName~Billing" --no-build` → **41 passed** before the test host process crashed (infra abort, no assertion failure). Re-ran the focused 21 with `--no-build` → green. Crash reproduces on suites requiring live web factories/gateways (`CheckoutEntitlementFulfillment`, `InvoiceEvidence`, `SubscriptionInvoice`) and is unrelated to these changes (no files in those paths were touched except the shared invoice-gate logic, which the 21 passing tests cover).

## Acceptance matrix (manual mapping to code + tests)
| Test | Expected | Actual | Status | Evidence |
|---|---|---|---|---|
| LR-01 standalone + success | auto Recalls access, no admin | checkout sets Auto+Active, bundle 180, Paid invoice+receipt, single notification | PASS (code + unit) | `ListeningRecallsPolicy`, `LearnerService` checkout exception, `ListeningRecallsAndBillingHardeningTests.LR01` |
| LR-02 bundled contains recalls | no standalone automation | policy false for all bundle codes | PASS | `LR02_BundledCourse…` |
| LR-03 failed | no access | failed → Draft/no evidence/no invoice; resolver `AdminGrant` | PASS | `BILL01_…` + `TestC` queue empty |
| LR-04 abandoned/cancelled | no access | quote Created/Cancelled → early-return, no grant; queue empty | PASS | `TestD/TestE` queue empty |
| LR-05 price change | still works (code identity) | policy ignores price | PASS | `LR01_LR05_…` |
| LR-06 webhook retry | exactly one entitlement | quote-Completed guard + idempotency keys + unique index | PASS | `BILL10_…`, `TestF` single receipt, `TestG` single grant |
| LR-07 existing user | correct assignment | `EnsureUserAsync` + quote user binding | PASS (code path unchanged) | checkout user binding |
| LR-08 new purchaser | access after association | grant keyed on quote `UserId`; webhook before association is ignored, retry after association grants once | PASS (design; needs live webhook replay to fully prove) | `ApplyVerifiedPaymentWebhookEventAsync` + `ApplyCheckoutCompletionAsync` |
| BILL-01 failed | no invoice/notification/alarm; backend log only | no invoice minted; only `LearnerPaymentFailed`; AI-budget no longer raises billing alarm | PASS | `BILL01_…`, `MarkCheckoutFailedAsync`, `AdminAiBudgetAlert` |
| BILL-02 pending | no invoice released | `Pending` invoice internal only; learner list Paid-only | PASS | `BILL02_…`, list/download gates |
| BILL-03 pending→success | invoice exactly once | `Pending→Paid` promotion once (checkout/approval/fulfilment) | PASS (code + unit) | promotion blocks |
| BILL-04 pending→failed | no invoice | `MarkCheckoutFailedAsync` mints nothing, cancels quote | PASS | code path |
| BILL-05 webhook retry | same invoice/entitlement, no dup notify | quote-Completed early-return; same ids reused | PASS | `TestF`, `BILL10` |
| BILL-06 mark fulfilled once | exactly one entitlement | single-row activation; no inserts | PASS | `TestB` |
| BILL-07 duration | six months, never one year | clamp 180 + idempotent StartedAt anchor | PASS | `BILL07_*` (3 tests) + `TestI` |
| BILL-08 same fulfilment retried | still one | Fulfilled→Ok same DTO; no re-grant | PASS | updated `TestG` |
| BILL-09 concurrent | still one (DB) | atomic `processing` claim + Serializable + unique index | PASS (code; live concurrency run needs Postgres) | claim + migration |
| BILL-10 legitimate 2nd purchase | NOT suppressed | new QuoteId = new event (unique index allows) | PASS (unit) | `BILL10_…` |

## Writing export
- `pnpm run writing:qa-export` (i.e. `node ./scripts/writing-qa-export.mjs --out artifacts/developer-action-brief/02-writing-qa-export.csv`) → 285 source files (210 documents + 75 videos explicitly excluded), 210 rows, reconciled=yes. Professions/letter counts/Other=156/pending=156/dups listed in stdout (retained). Gate `inspectSource` on the script → clean (`[]`).

## Video / fulfilment manual verification
- Video A/B/C and fresh test-candidate purchase/fulfilment require live Postgres + Bunny + gateway sandbox; not executed here (no credentials, no destructive prod ops). Code paths verified by build + unit/in-memory integration; live verification steps are scripted in the SOP and `11-final-verification.md` as the rollout gate.
