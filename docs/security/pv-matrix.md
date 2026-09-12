# Payment Verification Bypass Matrix — PV-01..PV-20 (execution harness)

- Standard: **OET Security Standard v1.0 (12 Sep 2026)**, §16 Payment Verification Bypass matrix.
- Release gate (also recorded in `README.md`): **any grant of an entitlement, credit, subscription or content access without independently verified payment is a P0 FAIL and blocks the §17 go-live gate.**

> **Provenance note.** The standard's §16 text is not stored in this repository; the PASS criterion for each case below is written as the concrete, testable acceptance condition and is to be reconciled one-to-one with §16 before the tester starts. Case identifiers `PV-01..PV-20` and control IDs are used exactly as supplied and must not be renumbered.

Every case must be executed **for every purchase type** on the target environment:

| Purchase type | What it exercises | Notes |
|---------------|-------------------|-------|
| One-time | `PaymentTransaction.TransactionType = one_time_purchase` | default purchase |
| Subscription | recurring line → `CustomerSubscription` upsert + first-period grant | verify renewal path too |
| Credits | wallet top-up (`wallet_top_up`) and AI-package credits | `MetadataJson` credit totals |
| Upgrade | plan-change / add-on attach on an active subscription | proration / `SubscriptionItem` |
| Discount | coupon / promo redemption (`BillingCouponRedemption`) | verify redemption consumed once |

**Repeat rule:** if any purchase type cannot be exercised on a gateway, record that gateway × type cell as **UNTESTED**, not PASS.

Base URLs: API `/v1/*`; webhook ingress `/v1/payment/webhooks/{gateway}`; learner return `/billing/payment-return`; capture `POST /v1/billing/paypal/orders/{orderId}/capture` (see `LearnerEndpoints.cs:428-542`, `BillingCheckoutEndpoints.cs`).

---

## 1. Case table

| PV | Title | Verbatim PASS criterion | Harness step (sandbox) | Control |
|----|-------|-------------------------|------------------------|---------|
| PV-01 | Replayed valid webhook | Accepting the same signed event twice produces exactly one fulfilment and no second grant. | Capture a real provider event; `POST` the identical raw body + headers to `/v1/payment/webhooks/{gateway}` twice. Assert one `CheckoutSession.Status="fulfilled"` and one credit grant. | PAY-03 |
| PV-02 | Forged signature | A body whose signature does not match the configured secret is rejected with HTTP 400 and changes no state. | Flip one hex char of the signature header; replay. Assert `400`, `state="rejected"`, no row change. | PAY-01 |
| PV-03 | Missing signature | A request with no signature header is rejected with HTTP 400 and changes no state. | Strip the signature header; replay. Assert `400`. | PAY-01 |
| PV-04 | Stale-timestamp replay | A correctly-signed event whose embedded timestamp is outside the configured window (`WebhookMaxAgeSeconds`, default 300s) is rejected. | Re-sign with `t = now - 3600s`. Assert rejection (Stripe/PayPal/Whop). For gateways with no window, assert idempotent no-op instead. | PAY-02 |
| PV-05 | Amount tampering | A signed event reporting a paid amount different from the quoted total does not grant and records `payment_amount_mismatch`. | Create a quote, settle the provider for a smaller/larger amount, deliver the event. Assert no grant + conflict finding. | PAY-07, PAY-10 |
| PV-06 | Currency tampering | A signed event reporting a currency different from the quoted currency does not grant and records `payment_currency_mismatch`. | Settle the provider in a different currency; deliver. Assert no grant + conflict. | PAY-08, PAY-10 |
| PV-07 | Cross-user / cross-order id | An event whose transaction id belongs to another learner's order does not grant to the test user. | Deliver learner A's event id on learner B's session. Assert no grant to either, opaque 404/ignore. | PAY-09, PAY-15 |
| PV-08 | Status downgrade | A `failed`/`pending` event arriving after a `completed` one neither revokes access nor re-grants. | Complete an order, then deliver a `failed` event. Assert status unchanged and no second grant. | PAY-12 |
| PV-09 | Concurrent duplicate completion | Two `completed` events delivered in parallel produce exactly one grant. | Fire the same event concurrently (`xargs -P`); assert one grant. | PAY-03 |
| PV-10 | Browser redirect spoof | Navigating to `/billing/payment-return?status=success&session=...` without a provider webhook/capture grants nothing. | Request the return URL with a fabricated session id. Assert no grant; invoice stays unpaid. | PAY-06 |
| PV-11 | Success-URL replay | Replaying a previously-used success redirect (with its real ids) grants nothing new. | Replay a captured success URL. Assert idempotent no-op. | PAY-06 |
| PV-12 | Callback query-string tampering | Mutating `quote`/`session`/`gateway` query params on a field callback (Fawaterak/EasyKash) is rejected by signature verification. | Change the `quote` param, keep the payload; deliver. Assert signature failure. | PAY-01, PAY-12 |
| PV-13 | Duplicate fulfilment (capture + webhook) | For embedded PayPal, capture and the subsequent `PAYMENT.CAPTURE.COMPLETED` webhook produce exactly one grant. | Capture via API, then deliver the webhook. Assert one grant. | PAY-13, FulfillmentService idempotency |
| PV-14 | Capture race | Two concurrent capture calls for the same order grant once and never double-charge. | Fire `POST capture` twice (`-P 2`). Assert one grant, one capture id. | PAY-13 |
| PV-15 | Real captured-amount tampering | Capturing an order whose real captured amount differs from the order grants nothing. | (Requires a modified sandbox order) — assert `capture.AmountCaptured` is compared. **Currently FAIL (PAY-14).** | PAY-14 |
| PV-16 | Coupon / discount abuse | Redeeming a coupon after payment, or reusing a redeemed coupon, is rejected and cannot reduce an already-quoted total. | Apply a coupon post-quote; reuse a coupon. Assert `BillingCouponRedemption` is single-use and totals recomputed. | pricing controls |
| PV-17 | IDOR / BOLA on order id | Guessing another learner's order/session id on capture or status returns an opaque 404 and grants nothing. | Use a foreign `orderId` in `POST capture`. Assert 404. | PAY-15 |
| PV-18 | Cross-gateway id collision | The same id string presented on two different providers cannot hijack a fulfilment. | Insert a row on gateway A reusing gateway B's id; deliver. Assert no cross-gateway match. | PAY-04 |
| PV-19 | Mass assignment | Extra JSON fields (`status`, `userId`, `amount`, `entitlements`) in checkout/manual-payment bodies are ignored. | POST bodies with injected fields; assert server-derived values win. | API/ARC |
| PV-20 | Sandbox fallback leakage | With `AllowSandboxFallbacks=false` (production default), a sandbox/synthetic order cannot be fulfilled as paid. | Present a `*_sandbox_*` / `cs_local_*` / `PAYPAL-*` id. Assert it cannot be captured/fulfilled. | PAY-06, DEV |

---

## 2. Per-gateway verification column set

For each gateway, confirm the four verification mechanisms independently. `replay window` = explicit signed-timestamp tolerance; `s2s lookup` = server-to-server status query available to reconciliation.

| Mechanism | Stripe | PayPal | Whop | Fawaterak | Paymob | EasyKash | PayTabs | Checkout.com |
|-----------|--------|--------|------|-----------|--------|----------|---------|--------------|
| Signature/HMAC verified | HMAC-SHA256 (`PaymentGatewayService.cs:478-537`) | s2s `verify-webhook-signature` (`:976-1027`) | HMAC + API probe (`WhopGateway.cs:124-206`) | HASH (`FawaterakGateway.cs:198-204`) | HMAC-SHA512 (`PaymobGateway.cs:125-150`) | HMAC-SHA512 (`EasyKashGateway.cs:136-182`) | HMAC-SHA256 (`PayTabsGateway.cs:88-106`) | HMAC-SHA256 (`CheckoutComGateway.cs:96-114`) |
| Replay window | yes (`:517-524`) | yes (`:862-876`) | yes (`WhopGateway.cs:151-152`) | no | no | no | no | no |
| s2s status lookup | no (shared contract) | no (shared contract) | no (uses API probe) | `GetInvoiceStatusAsync` | `GetTransactionConfirmationAsync` | none (returns null by design) | `GetTransactionConfirmationAsync` | `GetTransactionConfirmationAsync` |
| Idempotent event dedup | yes | yes | yes | yes | yes | yes | yes | yes |

`no` in the replay-window row is a P1 gap (`payment-audit-findings.md` §4.8). `no` in the s2s-lookup row means the reconciliation worker can only report "unknown" for that gateway (`BillingReconciliationWorker.cs`, `QueryProviderStatusAsync`).

---

## 3. Harness prerequisites

- Sandbox credentials for all eight gateways in runtime settings; `AllowSandboxFallbacks=false`.
- A capture of the raw provider callback bodies + headers (curl artefacts) per gateway.
- The reconciliation finding sink is `BillingEvents` (`EventType = reconciliation.mismatch`); assert findings appear there after `BillingReconciliationWorker` runs.
- For PV-09/PV-14, a load driver able to issue genuinely concurrent requests.

---

## 4. Release decision

- Each PV case × each purchase type must be **PASS**.
- Any **P0 FAIL** (esp. PV-05, PV-06, PV-15, PV-18, PV-20 and any case producing a grant) or any **UNTESTED/UNVERIFIED** P0 blocks production per the standard's §17 go-live gate.
- Retest is mandatory for every Critical/High finding (see `pentest-scope.md`).
