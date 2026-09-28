import { formatMoney, type FormatMoneyOptions } from '@/lib/money';

interface PriceProps extends FormatMoneyOptions {
  amount: number | null | undefined;
  className?: string;
  /** Optional region badge appended after the formatted amount, e.g. "UK", "GULF". */
  regionLabel?: string;
}

/**
 * Display-only money component. Routes every learner-facing price through
 * formatMoney so locale + currency fraction-digit rules stay consistent
 * (e.g. KWD/BHD/OMR render with 3 decimals; JPY renders with 0).
 *
 * Pass the amount and currency exactly as the billing API returns them for
 * this learner (never a hardcoded or locally derived price).
 */
export function Price({ amount, currency, locale, minimumFractionDigits, maximumFractionDigits, className, regionLabel }: PriceProps) {
  const text = formatMoney(amount, { currency, locale, minimumFractionDigits, maximumFractionDigits });
  return (
    <span className={className} data-testid="price">
      {text}
      {regionLabel ? <span className="ml-1 text-xs text-muted-foreground">({regionLabel})</span> : null}
    </span>
  );
}
