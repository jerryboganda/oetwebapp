'use client';

/**
 * Display-currency switcher used on the public pricing surface. The OET
 * 2026 catalogue is priced in GBP but presented with AUD / USD / EUR
 * comparison labels — the picker swaps the displayed conversion without
 * mutating the underlying price (the cart still bills the GBP amount).
 */

export const SUPPORTED_DISPLAY_CURRENCIES = ['GBP', 'AUD', 'USD', 'EUR'] as const;

export type DisplayCurrency = (typeof SUPPORTED_DISPLAY_CURRENCIES)[number];

export interface CurrencyPickerProps {
  value: DisplayCurrency;
  onChange: (currency: DisplayCurrency) => void;
  className?: string;
}

export function CurrencyPicker({ value, onChange, className }: CurrencyPickerProps) {
  return (
    <label
      className={['inline-flex items-center gap-2 text-sm', className].filter(Boolean).join(' ')}
    >
      <span className="text-muted">Currency</span>
      <select
        value={value}
        onChange={(event) => onChange(event.target.value as DisplayCurrency)}
        className="rounded-lg border border-border bg-surface px-2 py-1.5 text-sm font-medium text-navy shadow-sm focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
      >
        {SUPPORTED_DISPLAY_CURRENCIES.map((currency) => (
          <option key={currency} value={currency}>
            {currency}
          </option>
        ))}
      </select>
    </label>
  );
}
