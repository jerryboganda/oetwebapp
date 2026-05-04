/**
 * Centralised currency formatting for the billing surfaces.
 *
 * All learner-facing money rendering must route through `formatMoney` so we
 * never concatenate a hard-coded `$` symbol with a number (which silently
 * lies for USD / GBP / EUR wallets) and so locale fallback behaviour is
 * uniform across pages.
 *
 * Returns a string formatted via `Intl.NumberFormat`. If the runtime ICU
 * data does not recognise the locale or currency, we fall back to a stable
 * `<CURRENCY> <amount>` representation rather than throwing.
 */

export interface FormatMoneyOptions {
  /** ISO 4217 currency code (e.g. "AUD", "USD"). Defaults to "AUD". */
  currency?: string;
  /** BCP-47 locale tag. Defaults to "en-AU". */
  locale?: string;
  /** Minimum fraction digits. Defaults to 2. */
  minimumFractionDigits?: number;
  /** Maximum fraction digits. Defaults to `minimumFractionDigits`. */
  maximumFractionDigits?: number;
}

const DEFAULT_LOCALE = 'en-AU';
const DEFAULT_CURRENCY = 'AUD';

function normaliseCurrency(currency: string | null | undefined): string {
  const trimmed = (currency ?? '').trim().toUpperCase();
  // ISO 4217 codes are exactly 3 alphabetic characters.
  return /^[A-Z]{3}$/.test(trimmed) ? trimmed : DEFAULT_CURRENCY;
}

function safeAmount(amount: number | null | undefined): number {
  return typeof amount === 'number' && Number.isFinite(amount) ? amount : 0;
}

export function formatMoney(
  amount: number | null | undefined,
  options: FormatMoneyOptions = {},
): string {
  const value = safeAmount(amount);
  const currency = normaliseCurrency(options.currency);
  const locale = options.locale ?? DEFAULT_LOCALE;
  const minimumFractionDigits = options.minimumFractionDigits ?? 2;
  const maximumFractionDigits =
    options.maximumFractionDigits ?? minimumFractionDigits;

  try {
    return new Intl.NumberFormat(locale, {
      style: 'currency',
      currency,
      minimumFractionDigits,
      maximumFractionDigits,
    }).format(value);
  } catch {
    // Bad locale tag → retry with the default locale before falling back
    // to a plain `<CCY> <amount>` representation.
    if (locale !== DEFAULT_LOCALE) {
      try {
        return new Intl.NumberFormat(DEFAULT_LOCALE, {
          style: 'currency',
          currency,
          minimumFractionDigits,
          maximumFractionDigits,
        }).format(value);
      } catch {
        /* fall through */
      }
    }
    return `${currency} ${value.toFixed(minimumFractionDigits)}`;
  }
}

/** Convenience wrapper for whole-number prices (no fraction digits). */
export function formatMoneyWhole(
  amount: number | null | undefined,
  options: Omit<FormatMoneyOptions, 'minimumFractionDigits' | 'maximumFractionDigits'> = {},
): string {
  return formatMoney(amount, {
    ...options,
    minimumFractionDigits: 0,
    maximumFractionDigits: 0,
  });
}
