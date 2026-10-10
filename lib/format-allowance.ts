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

/**
 * One Writing letter / Speaking card costs this many credits from ANY pool
 * (mirrors `AiGradingCreditCost.CreditsPerWritingOrSpeakingActivity` on the API;
 * Reading/Listening cost 1 per paper). Packages are SOLD as letters/cards but
 * GRANTED as credits, so 6 Writing credits = 3 letters.
 */
export const CREDITS_PER_WRITING_OR_SPEAKING_ACTIVITY = 2;

const ACTIVITY_NOUN: Record<string, string> = {
  writing: 'letter',
  speaking: 'card',
  flexible_ws: 'attempt',
};

/**
 * What a credit balance buys, e.g. `6` Writing credits -> "3 letters". Null for
 * pools that are 1 credit per item (Reading/Listening/Mock) or an empty balance.
 * A lone spare credit is surfaced so an odd balance never looks like a rounding error.
 */
export function describeCreditsAsActivities(bucketKey: string, credits: number): string | null {
  const noun = ACTIVITY_NOUN[bucketKey];
  if (!noun || !Number.isFinite(credits) || credits <= 0) return null;
  const whole = Math.floor(credits / CREDITS_PER_WRITING_OR_SPEAKING_ACTIVITY);
  const spare = credits % CREDITS_PER_WRITING_OR_SPEAKING_ACTIVITY;
  const spareLabel = `${spare} spare credit${spare === 1 ? '' : 's'}`;
  // Flexible/Shared credits can top a spare credit up, so state what is missing
  // rather than claiming it can never be used.
  if (whole === 0) {
    const missing = CREDITS_PER_WRITING_OR_SPEAKING_ACTIVITY - spare;
    const article = /^[aeiou]/i.test(noun) ? 'an' : 'a';
    return `${spareLabel} — ${missing} more makes ${article} ${noun}`;
  }
  const wholeLabel = `${whole} ${noun}${whole === 1 ? '' : 's'}`;
  return spare > 0 ? `${wholeLabel} + ${spareLabel}` : wholeLabel;
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
