-- W11 owner-reviewable ledger vs package-credit discrepancy report.
-- Read-only. Does not UPDATE/DELETE any balance.

SELECT
    COALESCE(ledger."UserId", pkg."UserId") AS "UserId",
    COALESCE(ledger."LedgerTokenBalance", 0) AS "LedgerTokenBalance",
    COALESCE(pkg."PackageCreditBalance", 0) AS "PackageCreditBalance",
    COALESCE(ledger."LedgerTokenBalance", 0) - COALESCE(pkg."PackageCreditBalance", 0) AS "Delta",
    CASE
        WHEN ledger."MissingReferenceCount" > 0 THEN 'ledger_rows_missing_source_reference'
        ELSE 'ledger_vs_package_mismatch'
    END AS "Note"
FROM (
    SELECT "UserId",
           SUM("TokensDelta")::int AS "LedgerTokenBalance",
           COUNT(*) FILTER (WHERE "ReferenceId" IS NULL OR "ReferenceId" = '')::int AS "MissingReferenceCount"
    FROM "AiCreditLedger"
    GROUP BY "UserId"
) ledger
FULL OUTER JOIN (
    SELECT "UserId",
           ("SharedCredits" + "FlexibleCredits" + "WritingOnlyCredits" + "SpeakingOnlyCredits") AS "PackageCreditBalance"
    FROM "AiPackageCreditAccounts"
) pkg ON pkg."UserId" = ledger."UserId"
WHERE COALESCE(ledger."LedgerTokenBalance", 0) <> COALESCE(pkg."PackageCreditBalance", 0)
   OR COALESCE(ledger."MissingReferenceCount", 0) > 0
ORDER BY "UserId";
