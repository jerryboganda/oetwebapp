'use client';

import type { PublicCatalogAddOnRow } from '@/lib/types/admin';

import { PriceCell } from './PackageCard';

/**
 * Add-ons reference grid extracted from `app/catalog/page.tsx`. Each card
 * shows the eligibility flag the parent course must carry for the add-on
 * to attach (W, S, TB).
 */

export interface AddOnsGridProps {
  addOns: PublicCatalogAddOnRow[];
}

export function AddOnsGrid({ addOns }: AddOnsGridProps) {
  if (addOns.length === 0) return null;

  return (
    <section className="px-4 py-14">
      <div className="mx-auto max-w-5xl">
        <h2 className="text-2xl font-bold">Add-ons reference</h2>
        <p className="mt-1 text-sm text-muted">
          Add-ons attach to an eligible parent course. Each card shows the eligibility flag the
          parent must have.
        </p>
        <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {addOns.map((addon) => (
            <div key={addon.code} className="rounded-2xl border border-border bg-background p-5">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <h4 className="font-bold">{addon.name}</h4>
                  <p className="mt-1 text-[11px] uppercase tracking-wider text-muted">
                    requires <code>{addon.eligibilityFlag || 'n/a'}</code>
                  </p>
                </div>
                <PriceCell
                  price={addon.price}
                  originalPrice={addon.originalPrice ?? null}
                  compact
                />
              </div>
              {addon.description ? (
                <p className="mt-3 text-sm text-muted">{addon.description}</p>
              ) : null}
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
