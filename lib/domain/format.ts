/**
 * ============================================================================
 * Domain format helpers — pure, generic, UI- and transport-agnostic.
 * ============================================================================
 *
 * Extracted out of `lib/api.ts` so formatting / parsing helpers can be
 * consumed directly by UI components, mappers, and tests without pulling in
 * the entire API client. All helpers here are pure and deterministic.
 *
 * NOTE: Domain-scored grade thresholds (the OET 350/300 anchors) must stay in
 * `lib/scoring.ts`. Helpers here are for cosmetic formatting / light parsing.
 */

/**
 * Title-case a loosely-cased string: handles snake_case, kebab-case, and
 * space-separated inputs. Returns an empty string for null/undefined/empty.
 */
export function titleCase(value: string | null | undefined): string {
  if (!value) return '';
  return value
    .replace(/[_-]/g, ' ')
    .split(' ')
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

/** Render a minute duration as a compact label ("90 mins" / "3 hrs"). */
export function minutesToLabel(minutes: number): string {
  if (minutes >= 180) return `${Math.round(minutes / 60)} hrs`;
  return `${minutes} mins`;
}

/**
 * Human label for a billing interval / cadence. Maps the raw backend tokens
 * ("one_time", "month", "year", ...) to consumer-facing copy. Unknown values
 * fall back to title-case so nothing renders as a raw snake_case token.
 */
export function formatBillingInterval(interval: string | null | undefined): string {
  const key = String(interval ?? '').trim().toLowerCase();
  switch (key) {
    case '':
      return '';
    case 'one_time':
    case 'onetime':
    case 'one-time':
      return 'One-time';
    case 'day':
    case 'daily':
      return 'Daily';
    case 'week':
    case 'weekly':
      return 'Weekly';
    case 'month':
    case 'monthly':
      return 'Monthly';
    case 'quarter':
    case 'quarterly':
      return 'Quarterly';
    case 'year':
    case 'annual':
    case 'annually':
    case 'yearly':
      return 'Yearly';
    default:
      return titleCase(key);
  }
}

/**
 * Friendly label for a subscription status token coming off the billing summary
 * ("active", "freeze_requested", "frozen", "past_due", ...). Unknown values
 * fall back to title-case so the billing page never shows a raw token (and the
 * old literal "Unknown" can no longer leak through).
 */
export function formatSubscriptionStatus(status: string | null | undefined): string {
  const key = String(status ?? '').trim().toLowerCase();
  switch (key) {
    case '':
      return '—';
    case 'active':
      return 'Active';
    case 'trial':
      return 'Trial';
    case 'pending':
      return 'Pending';
    case 'past_due':
    case 'pastdue':
      return 'Past due';
    case 'suspended':
      return 'Suspended';
    case 'cancelled':
    case 'canceled':
      return 'Cancelled';
    case 'expired':
      return 'Expired';
    case 'paused':
      return 'Paused';
    case 'freeze_requested':
    case 'freezerequested':
      return 'Freeze requested';
    case 'frozen':
      return 'Frozen';
    default:
      return titleCase(key);
  }
}

/** Rewrite "3-4" / "3 - 4" as "3 to 4" for score-range display copy. */
export function scoreRangeDisplay(value: string | null | undefined): string {
  return (value ?? '').replace(/\s*-\s*/g, ' to ');
}

/**
 * Format a monetary amount. Defaults to AUD + en-AU locale to match the
 * platform's primary billing currency; pass an explicit currency code to
 * override. Non-finite / null / undefined inputs render as 0.00.
 */
export function formatCurrency(
  amount: number | string | null | undefined,
  currency = 'AUD',
): string {
  const numericAmount = Number(amount ?? 0);
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
  }).format(Number.isFinite(numericAmount) ? numericAmount : 0);
}

/**
 * Normalise an unknown waveform peaks payload into a clean number[] clamped
 * to the 6..100 rendering band. Drops non-finite entries. Guaranteed to
 * return an array (possibly empty).
 */
export function normalizeWaveformPeaks(value: unknown): number[] {
  if (!Array.isArray(value)) return [];
  return value
    .map((entry) => Number(entry))
    .filter((entry) => Number.isFinite(entry))
    .map((entry) => Math.max(6, Math.min(100, Math.round(entry))));
}

/**
 * Parse a criterion score from a "3", "3-4", or "3 - 4" string into its
 * rounded midpoint on the 0–6 scale. Returns 0 for unparseable input.
 *
 * This is NOT the OET scaled-score parser — use `lib/scoring.ts` for that.
 */
export function parseCriterionScore(scoreRange: string | null | undefined): number {
  if (!scoreRange) return 0;
  const match = scoreRange.match(/(\d+)(?:-(\d+))?/);
  if (!match) return 0;
  const first = Number(match[1]);
  const second = match[2] ? Number(match[2]) : first;
  return Math.round((first + second) / 2);
}

/**
 * Coarse rubric-grade letter for a 0–6 criterion score.
 *
 * NOTE: This is the per-criterion advisory grade used in writing/speaking
 * feedback surfaces. It is NOT a substitute for the canonical 0–500 OET
 * scaled-score grade — that logic lives in `lib/scoring.ts` / `OetScoring`.
 * Never use this for pass/fail display on Statement-of-Results surfaces.
 */
export function scoreToGrade(score: number): string {
  if (score >= 5) return 'B+';
  if (score >= 4) return 'B';
  if (score >= 3) return 'C+';
  if (score >= 2) return 'C';
  return 'D';
}
