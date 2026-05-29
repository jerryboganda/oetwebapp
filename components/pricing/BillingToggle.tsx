'use client';

/**
 * Monthly / Annual billing cadence toggle used on the public pricing
 * surface. Currently informational — the OET 2026 catalogue is priced
 * per access-window (180 / 365 / lifetime), but the toggle stays in the
 * UI so we can A/B-test recurring SKUs without re-shipping the page.
 */

import { useId } from 'react';

export type BillingCadence = 'monthly' | 'annual';

export interface BillingToggleProps {
  value: BillingCadence;
  onChange: (value: BillingCadence) => void;
  annualDiscountPct?: number | null;
  className?: string;
}

export function BillingToggle({
  value,
  onChange,
  annualDiscountPct = 20,
  className,
}: BillingToggleProps) {
  const groupId = useId();

  return (
    <div
      role="group"
      aria-labelledby={`${groupId}-label`}
      className={['inline-flex items-center gap-3', className].filter(Boolean).join(' ')}
    >
      <span id={`${groupId}-label`} className="sr-only">
        Billing cadence
      </span>
      <div className="inline-flex items-center rounded-full border border-border bg-surface p-1 text-sm">
        <button
          type="button"
          aria-pressed={value === 'monthly'}
          className={`rounded-full px-4 py-1.5 font-medium transition-colors ${
            value === 'monthly' ? 'bg-primary text-white dark:bg-violet-700 shadow-sm' : 'text-muted hover:text-navy'
          }`}
          onClick={() => onChange('monthly')}
        >
          Monthly
        </button>
        <button
          type="button"
          aria-pressed={value === 'annual'}
          className={`rounded-full px-4 py-1.5 font-medium transition-colors ${
            value === 'annual' ? 'bg-primary text-white dark:bg-violet-700 shadow-sm' : 'text-muted hover:text-navy'
          }`}
          onClick={() => onChange('annual')}
        >
          Annual
          {annualDiscountPct ? (
            <span className="ml-1.5 rounded-full bg-[#D4A44F]/20 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-[#996F1F]">
              -{annualDiscountPct}%
            </span>
          ) : null}
        </button>
      </div>
    </div>
  );
}
