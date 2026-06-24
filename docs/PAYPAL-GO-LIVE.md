# PayPal — Go-Live Runbook

> Operational checklist for enabling PayPal (embedded "Expanded" checkout) in
> production. For the billing architecture and invariants see
> [`BILLING.md`](./BILLING.md). PayPal is **credential-gated**: with no
> credentials it stays dormant and the gateway picker simply hides it — Stripe
> is unaffected.

PayPal credentials are **not** env vars in `docker-compose.production.yml`. They
are configured at runtime in **Admin → Runtime Settings → Billing (PayPal)**,
stored encrypted, and applied within ~30 s without a redeploy. This is the same
DB-over-env pattern Stripe uses for rotation.

---

## 1. Create the PayPal app

1. Sign in to the [PayPal Developer Dashboard](https://developer.paypal.com/dashboard/).
2. Switch to **Sandbox** first (go live only after sandbox verification below).
3. **Apps & Credentials → Create App** (Merchant). Note the **Client ID** and **Secret**.
4. In the app's features, enable **Accept payments** and, for on-page card fields,
   **Advanced Credit and Debit Card Payments** (a.k.a. Advanced Card Payments).
   Your account/region must be eligible — if it is not, embedded card fields are
   hidden automatically and only the PayPal/Venmo/Pay Later buttons render.

## 2. Register the webhook

1. In the app, **Add Webhook**.
2. **URL:** `https://api.oetwithdrhesham.co.uk/v1/payment/webhooks/paypal`
   (this is the only PayPal webhook route; do not register `/v1/webhooks/*`).
3. **Subscribe to these events** (minimum):
   - `PAYMENT.CAPTURE.COMPLETED`
   - `PAYMENT.CAPTURE.DENIED`
   - `PAYMENT.CAPTURE.REFUNDED`
   - `CHECKOUT.ORDER.APPROVED`
   - `CUSTOMER.DISPUTE.CREATED`, `CUSTOMER.DISPUTE.UPDATED`, `CUSTOMER.DISPUTE.RESOLVED`
4. Copy the generated **Webhook ID**.

> **The Webhook ID is required.** Embedded checkout captures and fulfils
> synchronously, but refunds, disputes, and the redirect (hosted) fallback all
> depend on verified webhooks. Without a Webhook ID, `VerifyWebhookAsync` cannot
> verify signatures and (with `AllowSandboxFallbacks=false`, the prod default)
> every webhook is rejected with HTTP 400.

## 3. Configure Admin → Runtime Settings → Billing (PayPal)

| Setting | Value |
| --- | --- |
| **PayPal Client ID** | from step 1 (public; surfaced to the browser SDK) |
| **PayPal Client Secret** | from step 1 (stored encrypted) |
| **PayPal Webhook ID** | from step 2 (stored encrypted) |
| **PayPal Success URL** | `https://app.oetwithdrhesham.co.uk/billing/payment-return` (or your return page) |
| **PayPal Cancel URL** | `https://app.oetwithdrhesham.co.uk/checkout/cancel` |
| **Use PayPal Sandbox** | **checked** for sandbox creds, **unchecked** for live creds |
| **PayPal Advanced Cards** | on (default) to show embedded card fields; off for buttons-only |

> ⚠️ **The single most important toggle is "Use PayPal Sandbox".** The gateway
> selects its API host (`api-m.sandbox.paypal.com` vs `api-m.paypal.com`) from
> this flag. **Live credentials with the box still checked → `401 invalid_client`
> on every call.** When you swap sandbox creds for live creds, you MUST also
> uncheck this box. (Locked in by `PayPalCheckout_RuntimeLiveToggle_CallsLiveHost`.)

## 4. Verify it is live

After saving, confirm the wiring without spending money:

1. `GET /v1/billing/paypal/client-config` → `enabled: true`, and `environment`
   matches your toggle (`"sandbox"` or `"live"`).
2. `GET /v1/billing/payment-gateways` → the `methods[]` array includes
   `{ name: "paypal", mode: "embedded" }`.
3. On `/checkout/review`, PayPal appears in the picker and the buttons render.

**Gated live gateway test.** `PayPalSandboxLiveTests` drives the real gateway code path
(effective-settings → OAuth → order create, plus the sandbox/live host switch) against the
live sandbox REST API. It no-ops unless creds are supplied via env, so it never blocks CI:

```
PAYPAL_SANDBOX_CLIENT_ID=... PAYPAL_SANDBOX_SECRET=... \
  dotnet test --filter FullyQualifiedName~PayPalSandboxLiveTests
```

### Sandbox end-to-end (do this before flipping to live)

Use sandbox buyer accounts + [test card numbers](https://developer.paypal.com/tools/sandbox/card-testing/):

1. **Plan/add-on:** buy via embedded PayPal → on-approve capture → entitlement granted once.
2. **Wallet top-up**, **cart**, and **paid speaking booking** (each embedded surface).
3. **Idempotency:** from the dashboard **Webhooks → Simulator**, replay
   `PAYMENT.CAPTURE.COMPLETED` for a completed order → confirm **no double-grant**
   (the transaction is already `completed`; the webhook dedupes on event id).
4. **Refund:** issue a sandbox refund → confirm the `PAYMENT.CAPTURE.REFUNDED`
   webhook is verified and recorded.

## 5. Flip to live

1. Repeat steps 1–2 in the **Live** dashboard; get live Client ID / Secret / Webhook ID.
2. In Admin → Runtime Settings, replace the three values **and uncheck "Use PayPal Sandbox"**.
3. Re-run the §4 checks (`client-config` must report `environment: "live"`).
4. Do one small real purchase, then refund it, to confirm the live path end-to-end.

## 6. Rollback

To disable PayPal instantly: clear **PayPal Client ID** (or Client Secret) in
Admin → Runtime Settings. The availability endpoint stops advertising PayPal, the
picker hides it, and Stripe continues unaffected. No redeploy required.

---

## Notes / invariants

- Keep `Billing__AllowSandboxFallbacks=false` in production (the default). It
  ensures missing/invalid PayPal credentials fail loudly instead of returning
  faked "completed" responses.
- Capture and the webhook converge on the **same** idempotent fulfilment, so a
  capture plus a later `PAYMENT.CAPTURE.COMPLETED` webhook never double-grant.
- Currency follows `Billing__DefaultCurrency` (GBP) unless a region override
  applies; the client-config currency must match the order currency.
