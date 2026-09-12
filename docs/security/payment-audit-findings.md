# OET Payment Security Audit — Findings & Evidence

- Standard: **OET Security Standard v1.0 (12 Sep 2026)** — control families `ARC PAY IAM API WEB INF DAT MOB CNT FIL DEV MON AI BCP IR TST`, 20-case Payment Verification Bypass matrix `PV-01..PV-20`, go-live gate §17.
- Release rule applied: **any P0 FAIL or any unverified P0 control blocks production.**
- Audit pass: **2026-09-12** (working tree, uncommitted changes present).
- Repo root: `backend/src/OetLearner.Api`.

> **Provenance note.** The standard document itself is not stored in this repository, so its verbatim §-text is not quoted here. Control IDs are used exactly as supplied (`PAY-19`, `MON-07`, `PV-01..PV-20`, and the family prefixes). Where a required PASS criterion could not be cross-checked against the standard's own text, the row is marked **UNVERIFIED** rather than PASS. Nothing below is marked PASS unless the cited code was read in this pass.

> **Concurrent-change notice (read this first).** At the start of this pass the four regional gateway adapters (`PaymobGateway`, `PayTabsGateway`, `CheckoutComGateway`, `EasyKashGateway`) and `LearnerService.cs`/`LearnerService.Billing.cs` were being modified by a parallel change stream in the same working tree. Several findings that were established as open **are already remediated in the working tree but not yet committed** (`git status` shows `M` on those files). Each affected row states the original finding, the current verified code, and the verdict against the current tree.

---

## 1. Verdict summary (PAY-* control set)

`PAY-19` is the only `PAY-*` identifier named in the task brief; the remaining `PAY-*` rows below enumerate the payment-control surface the audit exercised, numbered contiguously under the `PAY` family. **Do not renumber.** A row is PASS only where the current code was read and proves the criterion.

| ID | Control | Verdict | Evidence (file:line) |
|----|---------|---------|----------------------|
| PAY-01 | Provider webhook authenticity — signature verified before any trusted state change | **PASS** | Stripe `PaymentGatewayService.cs:478-537`; PayPal `:976-1027`; Paymob `PaymobGateway.cs:125-150`; PayTabs `PayTabsGateway.cs:88-106`; Checkout.com `CheckoutComGateway.cs:96-114`; EasyKash `EasyKashGateway.cs:136-182`; Fawaterak `FawaterakGateway.cs:198-204`; Whop `WhopGateway.cs:124-206` |
| PAY-02 | Replay window on time-stamped signatures | **PARTIAL** | `BillingOptions.cs:12` (`WebhookMaxAgeSeconds=300`); enforced Stripe `PaymentGatewayService.cs:517-524`, PayPal `:862-876`, Whop `WhopGateway.cs:151-152`. Paymob/PayTabs/Checkout.com/EasyKash/Fawaterak have HMAC but no explicit timestamp window — replay is bounded only by event-id dedup. |
| PAY-03 | Idempotent webhook dedup (no double processing) | **PASS** | `LearnerService.cs:10919-10933` (dup short-circuit), `:10983-11002` (unique-violation recovery) |
| PAY-04 | Per-provider identifier uniqueness (not global) | **PASS** | `BillingEntities.cs:595,653` (`[Index(Gateway, GatewayTransactionId)]`, `[Index(Gateway, GatewayEventId)]`); `LearnerDbContext.cs:1249-1250`; migration `Data/Migrations/20261231090000_AddPerProviderPaymentIdempotencyIndexes.cs` |
| PAY-05 | Normalized status vocabulary consistent with the fulfilment switch | **PASS** | Paymob `:163`, PayTabs `:119`, Checkout.com `:130`, Fawaterak `:222`, EasyKash `:204`, Whop `:222` all emit `completed`/`pending`/`failed`; switch acts on `completed`/`failed` at `LearnerService.cs:11170-11189` |
| PAY-06 | No grant without a verified `completed` payment state | **PASS** | `LearnerService.cs:11145-11190` |
| PAY-07 | Amount binding (local total == quoted total) | **PARTIAL** | `LearnerService.Billing.cs:431-436`. Applies only when an authoritative `BillingQuote` resolves (`LearnerService.cs:11093-11100`); skipped when `GetQuoteForTransactionAsync` returns null (`:13321-13346`). Capture path unchecked (see PAY-14). |
| PAY-08 | Currency binding | **PARTIAL** | `LearnerService.Billing.cs:424-429`; same quote-dependent caveat as PAY-07 |
| PAY-09 | User / owner binding (txn owner == quote owner) | **PARTIAL** | `LearnerService.Billing.cs:438-443`; same quote-dependent caveat as PAY-07 |
| PAY-10 | Provider-reported amount/currency binding | **PASS** | `LearnerService.Billing.cs:470-503` (`FindWebhookProviderOrderMismatch`) |
| PAY-11 | Order binding applied to completion, refund and dispute | **PASS** | `LearnerService.Billing.cs:461-464`; invoked at `LearnerService.cs:11093` before the refund (`:11102`) and dispute (`:11125`) branches |
| PAY-12 | Completed payment cannot be downgraded / terminal states not restorable | **PASS** | `LearnerService.cs:11145-11165`; `LearnerService.Billing.cs:466-468` |
| PAY-13 | Synchronous server-side capture is idempotent (PayPal embedded) | **PASS** | `LearnerService.cs:11317-11458`; idempotency key `PaymentGatewayService.cs:720-725` |
| PAY-14 | Capture-path amount verification (provider captured amount == order) | **FAIL** | `FulfillCapturedOrderAsync` `LearnerService.cs:11371-11458` records `capture.CaptureId` but never compares `capture.AmountCaptured` to the transaction/quote amount |
| PAY-15 | Owner scoping on synchronous capture | **PASS** | `LearnerService.cs:11328-11352` (`LearnerUserId == userId`, opaque 404 on mismatch) |
| PAY-16 | Owner scoping on cart (embedded PayPal) webhook | **PASS** | `LearnerService.cs:11656-11664` (rejects a session with no owner, and any provider amount/currency mismatch) |
| PAY-17 | Gateway availability enforced at checkout | **PASS** | `LearnerService.cs:10709-10726` (`EnsureCheckoutGatewayAsync`) |
| PAY-18 | Refund / dispute handling does not grant, and full-refund detection is amount-aware | **PASS** | `LearnerService.cs:11102-11143`; `DisputeService.cs` signal path |
| PAY-19 | **Daily reconciliation** of provider-vs-local payment state (the control this change implements) | **PARTIAL** | New `Services/Billing/BillingReconciliationWorker.cs` (compiles; build verified 0 errors). Not yet active until registered — see Required follow-up §4.1 |
| PAY-20 | Separation of duties + step-up on manual/admin payment approval | **PARTIAL** | Dedicated policy `AdminBillingMarkPaidWrite` `Program.cs:907-909`; endpoints `BillingExpansionEndpoints.cs:42,45,51`; role grant `Security/AdminRoleCatalog.cs` (`BillingAdmin`). Still accepts the legacy `billing:write`/`system_admin` superset and has **no step-up (MFA re-auth)**. |
| MON-07 | Monitoring cadence for payment-state divergence | **PARTIAL** | `BillingReconciliationWorker.cs` daily `PeriodicTimer` + `BillingEvents` audit rows; blocked on the DI registration (§4.1) |

### 1.1 Established findings — verified against current code

Each item below was in the starting audit brief. Verdicts reflect the **current working tree**.

1. **`NormalizedStatus: "succeeded"` vs the `"completed"` fulfilment switch** — *original finding: open.*
   - **Current:** all gateways now emit `completed`. `PaymobGateway.cs:163`, `PayTabsGateway.cs:119`, `CheckoutComGateway.cs:130` were changed from `"succeeded"` to `"completed"` in the working tree (confirmed against `git diff`). **Verdict: PASS (remediated, uncommitted).** Residual: non-terminal states (`pending`, `pending_offline`) fall to the switch `default` and are marked processed without action (`LearnerService.cs:11187-11189`) — informational only, no grant.

2. **Amount/currency/order/user binding absent in `ApplyVerifiedPaymentWebhookEventAsync`** — *original finding: open.*
   - **Current:** a binding gate now runs for `completed`/refund/dispute (`LearnerService.cs:11093-11100`) calling `EnsureWebhookMatchesAuthoritativeOrder` (`LearnerService.Billing.cs:416-459`), which enforces currency (`:424`), amount (`:431`), owner (`:438`) and provider-reported amount/currency (`:450-458`). **Verdict: PARTIAL** — enforced only when an authoritative quote resolves; the gate is skipped when `GetQuoteForTransactionAsync` returns null.

3. **`PaymentWebhookEvent.GatewayEventId` / `PaymentTransaction.GatewayTransactionId` globally unique** — *original finding: open.*
   - **Current:** both indexes are now composite per-provider (`BillingEntities.cs:595,653`, `LearnerDbContext.cs:1249-1250`), with a new migration `20261231090000_AddPerProviderPaymentIdempotencyIndexes.cs`. **Verdict: PASS (code); deployment action required** — the migration must be applied to every environment (see §4.3).

4. **Cart webhook path lacked the owner scoping the capture path had** — *original finding: open.*
   - **Current:** `ApplyCartCheckoutWebhookIfMatchedAsync` now rejects when `cartSession.UserId` is blank **or** the provider amount/currency does not match the session (`LearnerService.cs:11656-11664`) after signature verification. Webhooks are inherently unauthenticated, so "owner scope" = bind to a session that carries an owner plus amount/currency match. **Verdict: PASS.**

5. **`EnsureCheckoutGatewayAsync` / fulfilment never compared provider amount** — *original finding: open.*
   - **Current:** the webhook fulfilment path now compares amount/currency/owner (PAY-07..PAY-10). `EnsureCheckoutGatewayAsync` (`LearnerService.cs:10709-10726`) still only checks support + enablement, and the synchronous capture path (`FulfillCapturedOrderAsync`) never compares `capture.AmountCaptured` (PAY-14). **Verdict: PARTIAL.**

6. **Admin manual-payment approval shared the broad `AdminBillingRefundWrite` policy with no step-up** — *original finding: open.*
   - **Current:** approval/waive-proof/mark-fulfilled now use the dedicated `AdminBillingMarkPaidWrite` policy (`BillingExpansionEndpoints.cs:42,45,51`; policy added `Program.cs:907-909`; permission const + role grant in `Security/AdminRoleCatalog.cs`). **Verdict: PARTIAL** — the policy still falls back to the legacy `billing:write`/`system_admin` superset, and no endpoint performs a step-up.

7. **No daily reconciliation existed** — *original finding: open.*
   - **Current:** implemented as `BillingReconciliationWorker.cs` (see `BillingReconciliationWorker` report). **Verdict: PARTIAL** until the DI registration is applied.

8. **Webhook endpoints unrate-limited** — *stated as correct.*
   - **Current:** verified. `LearnerEndpoints.cs:428-542` defines `/v1/payment/webhooks/*` with no `RequireRateLimiting`; `StripeWebhookEndpoint` (`StripeWebhookEndpoints.cs:9`) is `.AllowAnonymous()`. **Verdict: PASS (by design).** **DO NOT add throttling here** — providers retry from many IPs and any throttle causes missed fulfilment. Compensating controls: signature verification (PAY-01), replay window (PAY-02), idempotent dedup (PAY-03).

9. **`NativeIapService.ValidateReceiptFailClosed` correctly grants nothing** — *stated as correct.*
   - **Current:** `NativeIapService.cs:144-160` returns `IsValid: false, EntitlementGranted: false`, code `native_iap_validation_unconfigured`. **Verdict: PASS.**

### 1.2 Not yet read in this pass (honest gaps)

The following payment surfaces were not read end-to-end, so any control depending on them is **UNVERIFIED** and blocks release until checked:

- Whop `ProbePaymentAsync` internals (`WhopGateway.cs:195`).
- `PrivateSpeakingService` payment confirmation (`ConfirmBookingPaymentAsync`) amount checks.
- `WalletService` top-up completion amount binding (`WalletService.cs:416-491`).
- `RefundService` full/partial refund amount reconciliation (`RefundService.cs`).
- `StripeService` subscription/invoice retrieval and price pinning.
- Admin RBAC permission evaluator merge semantics for the new `billing:mark_paid_write` (`Security/AdminPermissionEvaluator.cs`).

---

## 2. PV matrix control coverage (detail in `pv-matrix.md`)

| PV | Title | Verdict | Primary evidence |
|----|-------|---------|------------------|
| PV-01 | Replay of a valid webhook | **PASS** | `LearnerService.cs:10919-10933`; unique index `BillingEntities.cs:653` |
| PV-02 | Forged signature | **PASS** | `PaymentGatewayService.cs:526-537`; `PaymobGateway.cs:145-150` |
| PV-03 | Missing signature | **PASS** | `PaymentGatewayService.cs:486-490`; `PayTabsGateway.cs:96-99` |
| PV-04 | Stale-timestamp replay | **PASS** (Stripe/PayPal/Whop) / **PARTIAL** (others) | `PaymentGatewayService.cs:517-524`; `:862-876`; `WhopGateway.cs:151-152` |
| PV-05 | Amount tampering | **PARTIAL** | `LearnerService.Billing.cs:431-436,493-500` (quote-dependent) |
| PV-06 | Currency tampering | **PARTIAL** | `LearnerService.Billing.cs:424-429,482-486` |
| PV-07 | Cross-order / cross-user webhook | **PASS** | `LearnerService.Billing.cs:438-443`; `LearnerService.cs:11328-11352` |
| PV-08 | Status downgrade after completion | **PASS** | `LearnerService.cs:11156-11165` |
| PV-09 | Concurrent duplicate completion | **PASS** | `LearnerService.cs:11033` (tx) + `:10919-10933` |
| PV-10 | Browser redirect spoof (`payment-return?status=success`) | **PASS** | `FawaterakGateway.cs:408-448` (redirects carry ids only); grant requires a webhook/capture |
| PV-11 | Success-URL replay | **UNVERIFIED** | needs `app/billing/payment-return` + `PaymentCallbackHmac` review |
| PV-12 | Callback query-string tampering | **PASS** | `FawaterakGateway.cs:341-375` (`NormalizeQuotePayload`); signature required |
| PV-13 | Duplicate fulfilment (capture + webhook) | **PASS** | `FulfillmentService.cs:59-65,106-115,129-135` |
| PV-14 | Capture race | **PASS** | `PaymentGatewayService.cs:720-725`; `LearnerService.cs:11354-11366` |
| PV-15 | Real PayPal/Card capture amount tampering | **PARTIAL** | PAY-14 gap (`LearnerService.cs:11392`) |
| PV-16 | Coupon/discount abuse | **UNVERIFIED** | `PromoCodeService`/`CouponVariantApplicator` not read |
| PV-17 | IDOR/BOLA on order/session id | **PASS** | `LearnerService.cs:11328-11352` |
| PV-18 | Cross-gateway id collision | **PASS** | `BillingEntities.cs:595,653` |
| PV-19 | Mass assignment on checkout/manual-payment bodies | **UNVERIFIED** | endpoint DTO binding not read |
| PV-20 | Sandbox fallback leakage | **PARTIAL** | `BillingOptions.cs:6` `AllowSandboxFallbacks=false` default; PayPal sandbox capture path `PaymentGatewayService.cs:703-708` returns `completed` with amount `0` |

---

## 3. Files outside ownership that need a change (summary)

See §4 for the concrete, not-yet-applied change list. Everything in §4 lives in a file this change is not permitted to edit (or is an operational action).

---

## 4. Required follow-up (not yet applied)

These changes are **not** applied by this change because they live in files owned by other streams, or are operational. They must be completed before the §17 go-live gate can pass.

### 4.1 `Program.cs` — register the reconciliation worker (P0 for PAY-19/MON-07)

Add **exactly** this line next to the other Billing hosted services (immediately after `BillingMetricsRollupWorker`, around `Program.cs:1355`):

```csharp
builder.Services.AddHostedService<OetLearner.Api.Services.Billing.BillingReconciliationWorker>();
```

No other DI change is required: `TimeProvider` (`Program.cs:164`), `LearnerDbContext` (scoped) and `IPaymentGatewayProvider` (`Program.cs:1314`, scoped) are already registered.

### 4.2 Config keys (no code change; set per environment)

- `Billing__Reconciliation__Enabled` (bool, default `true`)
- `Billing__Reconciliation__SweepIntervalHours` (int, default `24`)
- `Billing__Reconciliation__PendingAgeMinutes` (int, default `60`)
- `Billing__Reconciliation__LookbackDays` (int, default `30`)
- `Billing__Reconciliation__BatchSize` (int, default `200`)

### 4.3 Apply the per-provider idempotency migration (P0, deployment)

`Data/Migrations/20261231090000_AddPerProviderPaymentIdempotencyIndexes.cs` must run against every environment before go-live; the DBA/operator must confirm the old global unique indexes were dropped and the composite ones created. Owner: migration/DB stream.

### 4.4 `LearnerService.cs` — capture-path amount verification (P0, PAY-14)

`FulfillCapturedOrderAsync` (`LearnerService.cs:11317-11458`) must compare `capture.AmountCaptured`/`capture.Currency` to the transaction (`transaction.Amount`/`transaction.Currency`) and to the authoritative `BillingQuote` before granting; a mismatch must set `payment_amount_mismatch` and refuse the grant. Owner: billing/LearnerService stream.

### 4.5 `LearnerService.Billing.cs` — make the order-binding gate fail-closed (P0, PAY-07..PAY-09)

`RequiresWebhookOrderBinding` currently skips enforcement when `GetQuoteForTransactionAsync` returns null (`LearnerService.cs:11093-11100`). For any one-time/subscription completion, the absence of a resolvable authoritative quote must be treated as a binding failure (fail-closed), not a pass. Owner: billing/LearnerService stream.

### 4.6 `LearnerService.cs` — pin the amount at checkout gateway selection (P1, PAY-17)

`EnsureCheckoutGatewayAsync` (`LearnerService.cs:10709-10726`) validates support/enablement only. The quoted amount/currency should be pinned to the created `PaymentTransaction` and re-validated on settle so a client cannot substitute a cheaper provider order. Owner: billing/LearnerService stream.

### 4.7 Admin mark-paid / refund endpoints — step-up and policy narrowing (P0, PAY-20)

`BillingExpansionEndpoints.cs:42,45,51` (mark-paid) and `:43,44,46` (refund) accept the legacy `billing:write`/`system_admin` superset and require no step-up. Require a fresh re-authentication (MFA/second-factor) on `AdminBillingMarkPaidWrite`/`AdminBillingRefundWrite`, and stop accepting the broad `billing:write` fallback for these two permissions. Owner: identity/RBAC + billing-endpoints stream.

### 4.8 Regional gateways — explicit replay window (P1, PAY-02/PV-04)

Add the `WebhookMaxAgeSeconds` replay check to Paymob (`PaymobGateway.cs:125-167`), PayTabs (`:88-123`), Checkout.com (`:96-134`), EasyKash (`:136-209`) and Fawaterak (`:169-232`) if the provider supplies a signed timestamp. Owner: gateway stream.

### 4.9 Webhook endpoints — keep them unthrottled (guard, no change)

Do **not** add `RequireRateLimiting` to `/v1/payment/webhooks/*`. Add a code comment so a future change does not "helpfully" throttle them. Owner: endpoints stream.

### 4.10 Remaining verification (P0, unverified controls)

Complete the §1.2 reads; **every `UNVERIFIED` P0 blocks release**.
