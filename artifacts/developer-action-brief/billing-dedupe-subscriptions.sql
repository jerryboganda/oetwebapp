-- Billing duplicate-subscription triage (dry-run first; audited merge second).
-- Scope: Subscriptions (course entitlements). Renewals/repurchases (different quotes/payments) are NEVER touched.
-- Run each SELECT, review with Billing Ops, then run the merge in a transaction.

-- 1) Candidate duplicate groups: same user + same plan, >1 owned row.
SELECT "UserId", "PlanId", COUNT(*) AS rows,
       MIN("StartedAt") AS first_started, MAX("StartedAt") AS last_started,
       STRING_AGG("Id", ',' ORDER BY "StartedAt") AS subscription_ids
FROM "Subscriptions"
WHERE "Status" IN ('Active','Trial','FreezeRequested','Frozen','Pending')
GROUP BY "UserId", "PlanId"
HAVING COUNT(*) > 1
ORDER BY rows DESC, "UserId";

-- 2) Detail for one group (replace :user and :plan): same quote = same logical event.
SELECT s."Id", s."Status", s."FulfilmentStatus", s."PlanVersionId", s."PriceAmount", s."Currency",
       s."StartedAt", s."ExpiresAt", s."ChangedAt",
       q."Id" AS quote_id, q."Status" AS quote_status,
       t."GatewayTransactionId", t."Status" AS payment_status, t."Gateway"
FROM "Subscriptions" s
LEFT JOIN "BillingQuotes" q ON q."SubscriptionId" = s."Id"
LEFT JOIN "PaymentTransactions" t ON t."QuoteId" = q."Id" AND t."Status" = 'completed'
WHERE s."UserId" = :user AND s."PlanId" = :plan
ORDER BY s."StartedAt", s."Id";

-- 3) Same-quote SubscriptionItems replays (now blocked by unique index; lists leftovers).
SELECT "SubscriptionId", "ItemCode", "QuoteId", COUNT(*) AS rows,
       STRING_AGG("Id", ',' ORDER BY "CreatedAt") AS item_ids
FROM "SubscriptionItems"
WHERE "QuoteId" IS NOT NULL
GROUP BY "SubscriptionId", "ItemCode", "QuoteId"
HAVING COUNT(*) > 1;

-- 4) Merge (per group, after review): keep earliest StartedAt row as canonical,
--    cancel the rest, audit. Adjust :keep_id and :dup_ids per group.
-- BEGIN;
-- INSERT INTO "AuditEvents"("Id","OccurredAt","ActorId","ActorName","Action","ResourceType","ResourceId","Details")
-- VALUES ('audit-' || substr(md5(random()::text),1,16), NOW(), :admin_id, :admin_name,
--         'subscription.dedupe', 'Subscription', :keep_id,
--         'Merged defect duplicates ' || :dup_ids || ' into ' || :keep_id || ' (same quote/payment; progress/counters preserved on canonical)');
-- UPDATE "Subscriptions" SET "Status" = 'Cancelled', "ChangedAt" = NOW()
-- WHERE "Id" IN (:dup_ids);
-- COMMIT;
