# Billing Incident Runbook

> Audience: on-call engineer, billing slice owner, support escalation lead.
> Companion: [`docs/BILLING.md`](../BILLING.md), [`mission-critical-execution-ledger.md`](../../mission-critical-execution-ledger.md).

This runbook is the **first thing** an on-call engineer reads when a billing
alert fires. Every scenario below assumes:

* PostgreSQL 17 (production), accessed via the `psql` shell on the VPS.
* The `oetwebsite_oet_postgres_data` volume is the only authoritative source
  for billing state. **Never** drop or recreate this volume without a
  verified backup.
* Webhooks are signed; never disable signature verification as a workaround.

---

## 0. First-responder checklist

1. Acknowledge the page within 5 minutes; post `#oncall-billing` thread.
2. Capture the alert's Sentry issue link, request id, and time window.
3. Open Sentry filtered to `feature:billing` — confirm the spike isn't
   already caused by a deploy in flight.
4. Run the relevant **triage queries** (section 2) before mutating anything.
5. If unsure → escalate per section 5. **Do not invent state** in
   `Subscription`, `Invoice`, `Wallet`, or `WalletTransaction` to "fix" a
   symptom; the ledger must remain the source of truth.

---

## 1. Symptom → first action

| Symptom | First action | Section |
| ------- | ------------ | ------- |
| Sentry burst tagged `feature:billing` with `verification_status='failed'` | Triage webhook backlog. | 3.1 |
| Customer reports "I was charged twice" | Reconcile by `CheckoutSessionId`. | 3.4 |
| Refund stuck on `Processing` for > 1 hour | Inspect `PaymentWebhookEvent` for the refund event. | 3.2 |
| Coupon usage exceeds `UsageLimitTotal` | Lock coupon, reconcile redemptions. | 3.3 |
| Dispute / chargeback opened | Acknowledge in admin UI within 24h. | 3.5 |
| Wallet balance ≠ ledger sum | Freeze wallet writes, run drift query. | 3.6 |
| Webhook backlog growing (`ProcessingStatus='received'` count rising) | Scale worker; do not retry blindly. | 3.1 |

---

## 2. Triage queries

> All queries are PostgreSQL 17. Replace `:since`, `:user_id`, etc.
> `EXPLAIN (ANALYZE, BUFFERS)` first if the table is large.

### 2.1 Recent webhook health

```sql
SELECT processing_status,
       verification_status,
       count(*) AS n,
       max(received_at) AS most_recent
FROM   "PaymentWebhookEvents"
WHERE  received_at > now() - interval '1 hour'
GROUP BY 1, 2
ORDER BY 1, 2;
```

### 2.2 Stuck / unprocessed events

```sql
SELECT id, gateway, event_type, gateway_event_id,
       attempt_count, retry_count, last_attempted_at, error_message
FROM   "PaymentWebhookEvents"
WHERE  processing_status IN ('received', 'processing', 'failed')
  AND  received_at < now() - interval '15 minutes'
ORDER BY received_at ASC
LIMIT 200;
```

### 2.3 Wallet drift detection

```sql
WITH ledger AS (
  SELECT wallet_id, sum(amount) AS ledger_balance
  FROM   "WalletTransactions"
  GROUP BY wallet_id
)
SELECT w.id              AS wallet_id,
       w.user_id,
       w.credit_balance  AS cached_balance,
       l.ledger_balance,
       (w.credit_balance - l.ledger_balance) AS drift
FROM   "Wallets" w
LEFT JOIN ledger l ON l.wallet_id = w.id
WHERE  coalesce(l.ledger_balance, 0) <> w.credit_balance
ORDER BY abs(w.credit_balance - coalesce(l.ledger_balance, 0)) DESC
LIMIT 100;
```

### 2.4 Coupon over-redemption

```sql
SELECT c.code,
       c.usage_limit_total,
       c.redemption_count       AS counter_on_coupon,
       count(r.*)               AS confirmed_redemptions
FROM   "BillingCoupons" c
LEFT JOIN "BillingCouponRedemptions" r
       ON r.coupon_code = c.code
      AND r.status = 1            -- BillingRedemptionStatus.Confirmed
GROUP BY c.code, c.usage_limit_total, c.redemption_count
HAVING c.usage_limit_total IS NOT NULL
   AND count(r.*) > c.usage_limit_total
ORDER BY confirmed_redemptions DESC;
```

### 2.5 Double-charge investigation

```sql
SELECT pt.gateway_transaction_id,
       pt.learner_user_id,
       pt.quote_id,
       pt.status,
       pt.amount,
       pt.created_at,
       i.id AS invoice_id,
       i.status AS invoice_status
FROM   "PaymentTransactions" pt
LEFT JOIN "Invoices" i ON i.quote_id = pt.quote_id
WHERE  pt.learner_user_id = :user_id
  AND  pt.created_at > now() - interval '7 days'
ORDER BY pt.created_at DESC;
```

If two `PaymentTransaction` rows share the same `quote_id` and both are
`completed`, escalate to refund — see 3.4.

### 2.6 Dispute backlog

```sql
SELECT id, gateway, gateway_transaction_id, status, opened_at, evidence_due_at
FROM   "PaymentDisputes"
WHERE  status IN ('opened', 'evidence_submitted')
ORDER BY evidence_due_at ASC NULLS LAST;
```

---

## 3. Common scenarios

### 3.1 Webhook backlog

**Definition.** `PaymentWebhookEvents.processing_status = 'received'` count
exceeds 100, **or** any single event is older than 15 minutes without a
terminal status.

**Procedure.**

1. Run query 2.1 to confirm the spike is processing-side, not signature
   failures (different remediation).
2. Check application logs for `PaymentGatewayService` exceptions; share the
   stack trace in the on-call thread.
3. If the worker is wedged: restart the API container only
   (`docker compose restart api`). **Do not** `down -v` the stack.
4. Once processing resumes, monitor query 2.2 for 15 minutes; backlog
   should drain monotonically.
5. If a specific event keeps failing, capture its `id` and route to the billing owner.
   Never delete a `PaymentWebhookEvent` row to "unblock" the queue.

### 3.2 Refund stuck

**Definition.** An `OrderRefund` row sits in `Processing` for > 1 hour with no
provider webhook callback.

**Procedure.**

1. Locate the originating `PaymentTransaction` and confirm provider id.
2. Check the provider dashboard (Stripe / PayPal) for the refund's actual
   state. Provider state is authoritative.
3. If provider says `succeeded` but our row says `Processing`, the
   confirmation webhook was lost: locate it via query 2.2 filtered by
   `event_type LIKE '%refund%'` and retry from the admin webhook backlog.
4. If provider says `failed`, the admin must explicitly transition our row
   to `Failed` via the admin UI. Do not reverse the wallet ledger entry by
   hand.

### 3.3 Coupon over-redeemed

**Definition.** Query 2.4 returns one or more rows.

**Procedure.**

1. **Immediately** set the coupon `Status = 'paused'` via admin UI to stop
   further redemptions. This is reversible.
2. Snapshot the offending redemptions: `SELECT * FROM "BillingCouponRedemptions" WHERE coupon_code = :code ORDER BY redeemed_at;`.
3. Decide policy with the product owner: honour all, or refund the excess.
   Honouring is usually cheaper than chargeback risk.
4. If refunding excess, follow refund flow (3.2) per affected user.
5. File a defect against slice C: invariant **I6** failed.

### 3.4 Double-charge claim

**Procedure.**

1. Run query 2.5 with the customer's user id.
2. If two `completed` `PaymentTransaction` rows share the same `quote_id`,
   confirm via the provider dashboard that both charges actually settled.
3. Refund the **later** of the two via the admin payment-transaction refund action; cite the
   duplicate transaction id in the refund reason.
4. File a defect against slice D: invariant **I10** failed. Capture the
   webhook timeline (`PaymentWebhookEvents.received_at`) to determine
   whether the duplicate was provider-side or our-side.

### 3.5 Dispute escalation

**Procedure.**

1. Acknowledge in the admin disputes UI within 24 hours of the open event.
2. Pull the originating `Invoice` and `PaymentTransaction`; export PDF
   receipt + the learner's usage log for the billing period.
3. Upload evidence in the provider dashboard **and** attach a copy to the
   dispute row in admin (audit trail).
4. If the dispute is won, no further action — webhook will reconcile.
5. If lost, the wallet ledger must show a reversing entry **created
   automatically** by `RefundService` from the dispute event. Confirm via
   query 2.3 that drift is zero.

### 3.6 Wallet balance mismatch

**Procedure.**

1. Run query 2.3 to enumerate drifted wallets.
2. **Freeze writes** to the affected wallet by setting a feature flag (slice
   A is responsible for providing this; until then, escalate immediately
   instead of mutating data).
3. Determine ground truth: `sum(WalletTransaction.Amount)` is authoritative
   per invariant **I7**. Update `Wallet.CreditBalance` to match the ledger.
4. Find the missing or duplicate ledger entry by joining
   `WalletTransactions` to `PaymentTransactions` and `OrderRefunds` on
   `reference_id`.
5. File a defect against slice A. Invariant **I7** or **I9** failed.

---

## 4. Migration rollback

| Migration | Reversible? | Notes |
| --------- | ----------- | ----- |
| `*_HardenWallet*` (slice A) | Mostly. New columns are additive and nullable; new tables can be dropped. | If a backfill ran, the rollback must restore the old `Wallet.CreditBalance` from the ledger first. |
| `*_AddRefundDispute*` | Yes. Tables are net-new. | Forward-only data: dropping the table loses dispute evidence. Take a backup first. |
| `*_HardenCatalog*` (slice C) | Partially. New version-history tables are additive. | If new uniqueness constraints have been added on `BillingCoupon.Code`, rollback must verify no duplicates were inserted in the meantime. |
| `*_HardenCheckout*` (slice D) | Partially. New columns on `BillingQuote` / `Invoice` / `PaymentTransaction` are additive. | If a new FK was added (e.g. `Invoice.QuoteId` → `BillingQuotes.Id`), confirm no orphan invoices exist before rolling back. |

**Universal rule.** No billing migration may drop a column that ever held
production data without an explicit ledger snapshot taken in the same
session. If in doubt, do not roll back — fix forward.

Rollback command pattern (run on the VPS, only after a `pg_dump` of
`oetwebsite_oet_postgres_data`):

```bash
docker compose exec api dotnet ef database update <PreviousMigrationName> \
  --project /app/OetLearner.Api.csproj
```

---

## 5. Escalation

1. Primary on-call: see PagerDuty rotation `oet-billing-primary`
   *(placeholder — confirm rota in `mission-critical-execution-ledger.md`)*.
2. Slice owner escalation:
   * Wallet → slice A.
   * Payments / webhooks / refunds → billing owner.
   * Catalog → slice C.
   * Checkout / quote / subscription / invoice → slice D.
   * Entitlement / AI quota → slice E.
3. If learner-facing impact lasts > 30 minutes, post status to
   `status.oetwithdrhesham.co.uk` and notify product lead.
4. Append a runbook entry to
   [`mission-critical-execution-ledger.md`](../../mission-critical-execution-ledger.md)
   under the "Billing incidents" section with: timestamp, symptom, action
   taken, follow-up defect id.
