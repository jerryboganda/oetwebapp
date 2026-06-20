'use client';

import { useState } from 'react';
import { ChevronDown, ChevronUp } from 'lucide-react';
import { cn } from '@/lib/utils';
import type { PublicCatalogPlanRow } from '@/lib/types/admin';
import {
  type CatalogStorefrontConfig,
  professionLabel,
  categoryLabel,
  formatAccessDuration,
  formatPrice,
} from '@/lib/catalog-presentation';

function FlagDot({ enabled }: { enabled: boolean }) {
  return (
    <span
      aria-label={enabled ? 'Offered' : 'Not offered'}
      className={cn('inline-block h-2.5 w-2.5 rounded-full', enabled ? 'bg-primary ring-2 ring-primary/25' : 'bg-muted')}
    />
  );
}

export interface CatalogCompareMatrixProps {
  plans: PublicCatalogPlanRow[];
  config: CatalogStorefrontConfig;
}

export function CatalogCompareMatrix({ plans, config }: CatalogCompareMatrixProps) {
  const [open, setOpen] = useState(false);
  if (plans.length === 0) return null;

  return (
    <section className="rounded-2xl border border-border bg-surface shadow-sm">
      <button
        type="button"
        onClick={() => setOpen((value) => !value)}
        className="flex w-full items-center justify-between gap-3 px-5 py-4 text-left"
        aria-expanded={open}
      >
        <div>
          <h2 className="text-base font-bold text-navy">Compare all packages</h2>
          <p className="text-sm text-muted">A side-by-side view of access, add-ons and pricing.</p>
        </div>
        {open ? <ChevronUp className="h-5 w-5 text-muted" /> : <ChevronDown className="h-5 w-5 text-muted" />}
      </button>
      {open ? (
        <div className="overflow-x-auto border-t border-border">
          <table className="min-w-full divide-y divide-border text-sm">
            <thead className="bg-background-light text-left text-xs font-semibold uppercase tracking-wide text-muted">
              <tr>
                <th className="px-4 py-3">Product</th>
                <th className="px-4 py-3">Category</th>
                <th className="px-4 py-3">Access</th>
                <th className="px-4 py-3 text-center">W</th>
                <th className="px-4 py-3 text-center">S</th>
                <th className="px-4 py-3 text-center">TB</th>
                <th className="px-4 py-3 text-right">Price</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {plans.map((plan) => (
                <tr key={plan.code} className="hover:bg-background-light/60">
                  <td className="px-4 py-3">
                    <div className="font-semibold text-navy">{plan.name}</div>
                    <div className="text-[11px] uppercase tracking-wider text-muted">{professionLabel(config, plan.profession)}</div>
                  </td>
                  <td className="px-4 py-3 text-muted">{categoryLabel(config, plan.productCategory)}</td>
                  <td className="px-4 py-3 text-muted">{formatAccessDuration(plan.accessDurationDays)}</td>
                  <td className="px-4 py-3 text-center"><FlagDot enabled={plan.writingAddonsEnabled} /></td>
                  <td className="px-4 py-3 text-center"><FlagDot enabled={plan.speakingAddonsEnabled} /></td>
                  <td className="px-4 py-3 text-center"><FlagDot enabled={plan.tutorBookDiscountEnabled} /></td>
                  <td className="px-4 py-3 text-right font-semibold text-navy">{formatPrice(plan.price, plan.currency)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
