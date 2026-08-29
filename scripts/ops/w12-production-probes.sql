-- W12 scaled-down production probes. Green thresholds in comments.
-- Run read-only against production after deploy. Do not UPDATE.

-- 1. AiOperations queued/leased/retry older than 24h => 0
SELECT COUNT(*) AS stale_inflight
FROM "AiOperations"
WHERE "State" IN (0, 1, 4)
  AND "UpdatedAt" < NOW() - INTERVAL '24 hours';

-- 2. unclassified usage in production => 0
SELECT COUNT(*) AS unclassified_usage
FROM "AiUsageRecords"
WHERE "FeatureCode" = 'unclassified';

-- 3. duplicate speaking assessments per identity still unmarked => 0
SELECT "IdentityHash", COUNT(*) AS dupes
FROM "SpeakingAiAssessments"
WHERE "IdentityHash" IS NOT NULL AND "IsDuplicate" = FALSE
GROUP BY "IdentityHash"
HAVING COUNT(*) > 1;

-- 4. duplicate credit reservations per business reference => 0
SELECT "BusinessReference", COUNT(*) AS dupes
FROM "AiCreditReservations"
WHERE "BusinessReference" IS NOT NULL AND "BusinessReference" <> ''
GROUP BY "BusinessReference"
HAVING COUNT(*) > 1;
