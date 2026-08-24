export type AllowanceValue = number | null | undefined;

/**
 * Candidate-facing balances use Credits / Attempts / Unlimited only
 * (never raw provider tokens). A null/undefined pool means Unlimited for
 * the package validity window.
 */
export function formatAllowance(
  value: AllowanceValue,
  noun: string,
  opts: { unlimitedLabel?: string; zeroLabel?: string } = {},
): string {
  const { unlimitedLabel = `Unlimited ${noun}`, zeroLabel = `0 ${noun}` } = opts;
  if (value === null || value === undefined) return unlimitedLabel;
  if (value <= 0) return zeroLabel;
  return `${value} ${noun}`;
}

/** Renders a bucket remaining value with the literal "Unlimited". */
export function formatBucketRemaining(unlimited: boolean, remaining: number): string {
  return unlimited ? 'Unlimited' : String(Math.max(0, remaining));
}

const MS_PER_DAY = 86_400_000;

/** Whole days between now and the expiry instant; null when no expiry. */
export function calculateDaysLeft(expiryDate?: string | Date | null, from: Date = new Date()): number | null {
  if (!expiryDate) return null;
  const expiry = typeof expiryDate === 'string' ? new Date(expiryDate) : expiryDate;
  if (Number.isNaN(expiry.getTime())) return null;
  return Math.max(0, Math.ceil((expiry.getTime() - from.getTime()) / MS_PER_DAY));
}

export function formatValidityWindow(validFrom?: string | null, expiresAt?: string | null): string {
  const start = validFrom ? new Date(validFrom) : null;
  const end = expiresAt ? new Date(expiresAt) : null;
  const startLabel = start && !Number.isNaN(start.getTime())
    ? start.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' })
    : null;
  if (!end || Number.isNaN(end.getTime())) {
    return startLabel ? `${startLabel} — no expiry` : 'No active expiry';
  }
  const endLabel = end.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
  const days = calculateDaysLeft(end);
  const suffix = days === null ? '' : days > 0 ? ` · ${days} day${days === 1 ? '' : 's'} left` : ' · expired';
  return startLabel ? `${startLabel} → ${endLabel}${suffix}` : `${endLabel}${suffix}`;
}
