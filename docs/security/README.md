# Security Evidence Pack — OET With Dr Hesham

Release-gate evidence for **OET Security Standard v1.0 (12 Sep 2026)**.

## Verdict

> **BLOCKED.** The §17 go-live gate does **not** pass. At least one P0 control is FAIL (`PAY-14` capture-path amount verification) and several P0 controls are UNVERIFIED or only PARTIAL (see `payment-audit-findings.md`). Per the release rule, *any P0 FAIL or unverified control blocks production.*

Evidence pass: **2026-09-12** (working tree; uncommitted remediations from a parallel change stream are present — see the notice in `payment-audit-findings.md`).

## Documents in this pack

| File | Purpose |
|------|---------|
| `control-register.json` | Machine-readable register of all controls (ARC-01): IDs, owners, evidence, status; source for the generated evidence matrix. **Needs reconciliation with the standard attachment (exact wording/priorities).** |
| `payment-audit-findings.md` | Control-by-control `PAY-*` + `PV-*` audit with verdicts and file:line evidence, plus the "Required follow-up (not yet applied)" list |
| `pv-matrix.md` | `PV-01..PV-20` execution matrix: PASS criterion, harness step, per-gateway verification columns, repeat-per-purchase-type rule, release gate |
| `threat-model-outline.md` | Text trust-boundary diagram (browser → BFF → API → DB/storage → mail/push → gateways → AI → native shells) and the per-flow threat list |
| `pentest-scope.md` | Scoped targets, mandated tests, rules of engagement, retest requirement |

Related pre-existing material (not part of this pack): `docs/security/speaking/**`, `docs/SECURITY-*.md`.

## Ordered fix list (do these in order)

1. ~~**PAY-14 — capture-path amount verification (P0 FAIL).**~~ **APPLIED, hardened this pass** — `FindCaptureOrderMismatch` is now unconditional: no authoritative order, zero/absent capture amount, or blank currency all refuse the grant (sandbox zero-amount capture is also closed — PV-20). Speaking bookings bind to `PriceMinorUnits`. CI pending.
2. ~~**PAY-07/08/09 — make the order-binding gate fail-closed (P0).**~~ **APPLIED this pass** — quote-less completion events now park as `failed` (`payment_order_unresolvable`) and grant nothing; wallet top-ups (server-validated tier rows, no BillingQuote) bind to the transaction row and reject provider-reported contradictions. Regression tests: `WebhookOrderBindingGateTests`. CI pending.
3. ~~**PAY-19/MON-07 — register the reconciliation worker (P0).**~~ **APPLIED** — `BillingReconciliationWorker` registered in `Program.cs`; config under `Billing:Reconciliation:*`.
4. **PAY-04 — apply the per-provider idempotency migration (P0, deployment).** `20261231090000_AddPerProviderPaymentIdempotencyIndexes.cs` (`payment-audit-findings.md` §4.3). Owner: DB/migration stream; runs with deploy, must be confirmed per environment.
5. ~~**PAY-20 — step-up on admin mark-paid/refund (P0).**~~ **APPLIED** — `WithStepUp("billing.mark_paid")` on approve/waive/mark-fulfilled, `WithStepUp("billing.refund")` on refunds (`StepUpService` TOTP proof, 300s single-scope tokens); policies `AdminBillingMarkPaidWrite`/`AdminBillingRefundWrite` narrowed to the granular permission only (SystemAdmin retains access via `AdminPermissions.All`). CI pending.
6. ~~**PAY-17 — pin amount at checkout gateway selection (P1).**~~ **CLOSED this pass** — the pin happens at intent creation (server-calculated `BillingQuote` total/currency minted into every checkout intent, `quote_id` in metadata) and settlement re-validates provider-vs-order on both the webhook and capture paths (fail-closed). PV-15 live exercise per gateway still required at verification time.
7. ~~**PAY-02/PV-04 — replay window on the regional gateways (P1).**~~ **APPLIED for Paymob/PayTabs/Checkout.com/Whop** (working tree). EasyKash/Fawaterak callbacks carry no provider-signed timestamp — bounded by HMAC + event-id dedup; documented as residual.
8. **Complete the §1.2 unverified reads (P0).** Every UNVERIFIED P0 blocks release until checked.
9. **Independent pentest + retest** for all Critical/High before sign-off (`pentest-scope.md`).

This pass additionally applied: **IAM-01** (PBKDF2-HMAC-SHA512 ≥220k custom `IPasswordHasher` in Identity-v3 blob format + `SuccessRehashNeeded` rehash-on-login + CI policy tests — the stock hasher has no PRF selector, verified against the .NET 10 API), **IAM-05** (E2E token-persistence path dead-code-gated in production bundles, `lib/auth-storage.ts`), and the **§4.9 no-throttle guard comment** on the webhook group. All statuses remain **blocked/unverified until a GitHub Actions run on the exact commit passes**.

## What is already good (verified this pass)

- Webhook signature verification for all eight gateways (PAY-01) and idempotent event dedup (PAY-03).
- Per-provider uniqueness (PAY-04, code + migration).
- `NormalizedStatus` vocabulary now consistent with the fulfilment switch (PAY-05).
- Webhook endpoints are deliberately unthrottled and **must stay that way** (PASS by design).
- `NativeIapService.ValidateReceiptFailClosed` grants nothing (PASS).
- Concurrent remediations already present in the working tree: gateway status fix, amount/currency/order binding, cart owner scoping, per-provider indexes, `AdminBillingMarkPaidWrite` policy.

## Sign-off

| Role | Name | Date | Commit/build under test |
|------|------|------|-------------------------|
| Release owner | | | |
| Independent tester | | | |
| Security lead | | | |
