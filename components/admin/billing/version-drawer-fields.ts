/**
 * Catalog version drawer field allowlist.
 *
 * The catalog version history drawer in `app/admin/billing/page.tsx` shows a
 * `summary` map per version. The map is `Record<string, unknown>` server-side
 * which means a future API change could leak operational data such as
 * redemption counts, invoices, checkout sessions, payment transaction IDs, or
 * subscriber counts into a surface that is meant to display **catalog
 * metadata only**.
 *
 * `filterCatalogVersionSummary()` enforces that contract on the client:
 *   1. Hard-deny any key matching the operational-data deny list.
 *   2. Allow only keys that match the catalog-metadata allow list.
 *
 * Both lists are intentionally case-insensitive and substring-based to catch
 * common naming variants (`redemptionCount`, `redemptions_total`, etc.).
 */

const FORBIDDEN_KEY_PATTERNS = [
  /redempt/i,
  /invoice/i,
  /checkout/i,
  /payment/i,
  /transaction/i,
  /subscriber/i,
  /subscription/i,
  /webhook/i,
  /refund/i,
  /dispute/i,
  /wallet/i,
  /quote/i,
  /chargeback/i,
] as const;

const ALLOWED_KEYS = new Set<string>([
  // shared
  'price',
  'currency',
  'interval',
  'status',
  'displayOrder',
  'description',
  'name',
  'code',
  'notes',
  // plan
  'durationMonths',
  'includedCredits',
  'isVisible',
  'isRenewable',
  'trialDays',
  'diagnosticMockEntitlement',
  'includedSubtests',
  'entitlements',
  // add-on
  'durationDays',
  'grantCredits',
  'isRecurring',
  'appliesToAllPlans',
  'isStackable',
  'quantityStep',
  'maxQuantity',
  'compatiblePlanCodes',
  'grantEntitlements',
  // coupon
  'discountType',
  'discountValue',
  'startsAt',
  'endsAt',
  'usageLimitTotal',
  'usageLimitPerUser',
  'minimumSubtotal',
  'applicablePlanCodes',
  'applicableAddOnCodes',
]);

export function isForbiddenSummaryKey(key: string): boolean {
  return FORBIDDEN_KEY_PATTERNS.some((pattern) => pattern.test(key));
}

export function isAllowedSummaryKey(key: string): boolean {
  if (isForbiddenSummaryKey(key)) return false;
  return ALLOWED_KEYS.has(key);
}

/**
 * Strip any keys that look like operational data and keep only the curated
 * catalog metadata fields. Returns a new object — never mutates input.
 */
export function filterCatalogVersionSummary(
  summary: Record<string, unknown> | null | undefined,
): Record<string, unknown> {
  if (!summary) return {};
  const out: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(summary)) {
    if (isAllowedSummaryKey(key)) {
      out[key] = value;
    }
  }
  return out;
}
