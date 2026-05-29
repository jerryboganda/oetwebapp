'use client';

import type { PublicCatalogPlanRow } from '@/lib/types/admin';

import { PriceCell } from './PackageCard';

/**
 * Renders the "Pricing matrix at a glance" table shown on `/catalog` and
 * `/pricing`. Each row corresponds to one OET 2026 plan and shows which
 * add-on flags it supports.
 */

const PROFESSION_LABEL: Record<string, string> = {
  all: 'All disciplines',
  medicine: 'Medicine',
  nursing: 'Nursing',
  pharmacy: 'Pharmacy',
};

const CATEGORY_LABELS: Record<string, string> = {
  full_course: 'Full Recorded Courses',
  full_course_bundle: 'Full Course Bundles',
  crash_course: 'Crash Courses',
  crash_course_bundle: 'Crash Course Bundles',
  writing_crash: 'Writing Crash Courses',
  writing_crash_bundle: 'Writing Bundles',
  speaking_crash: 'Speaking Crash Course',
  speaking_session: 'Private Speaking Sessions',
  combo_double: 'Double Special - Writing + Speaking',
  combo_mega: 'Mega Special Package',
  foundation: 'Foundation',
  book: 'The Tutor Book',
};

export interface FeatureMatrixProps {
  plans: PublicCatalogPlanRow[];
  loading?: boolean;
  error?: string | null;
  emptyMessage?: string;
}

export function FeatureMatrix({ plans, loading, error, emptyMessage }: FeatureMatrixProps) {
  if (loading) {
    return <div className="mt-6 h-72 animate-pulse rounded-2xl border border-border bg-surface" />;
  }
  if (error) {
    return (
      <div className="mt-6 rounded-2xl border border-border bg-surface p-8 text-center text-muted">
        {error}
      </div>
    );
  }
  if (plans.length === 0) {
    return (
      <div className="mt-6 rounded-2xl border border-border bg-surface p-8 text-center text-muted">
        {emptyMessage ?? 'No products are published right now. Please check back soon.'}
      </div>
    );
  }

  return (
    <div className="mt-6 overflow-hidden rounded-2xl border border-border bg-surface shadow-sm">
      <div className="overflow-x-auto">
        <table className="min-w-full divide-y divide-border text-sm">
          <thead className="bg-background-light text-left text-xs font-medium uppercase tracking-wide text-muted">
            <tr>
              <th scope="col" className="px-4 py-3">Product</th>
              <th scope="col" className="px-4 py-3">Category</th>
              <th scope="col" className="px-4 py-3">Access</th>
              <th scope="col" className="px-4 py-3 text-center">W</th>
              <th scope="col" className="px-4 py-3 text-center">S</th>
              <th scope="col" className="px-4 py-3 text-center">TB GBP32</th>
              <th scope="col" className="px-4 py-3 text-right">Price</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {plans.map((plan) => (
              <tr key={plan.code} className="hover:bg-background-light/50">
                <td className="px-4 py-3">
                  <div className="font-medium">{plan.name}</div>
                  <div className="text-[11px] uppercase tracking-wider text-muted">
                    {PROFESSION_LABEL[plan.profession] ?? plan.profession}
                  </div>
                </td>
                <td className="px-4 py-3 text-muted">
                  {CATEGORY_LABELS[plan.productCategory] ?? plan.productCategory.replace(/_/g, ' ')}
                </td>
                <td className="px-4 py-3 text-muted">{formatAccess(plan.accessDurationDays)}</td>
                <td className="px-4 py-3 text-center">
                  <FlagDot enabled={plan.writingAddonsEnabled} />
                </td>
                <td className="px-4 py-3 text-center">
                  <FlagDot enabled={plan.speakingAddonsEnabled} />
                </td>
                <td className="px-4 py-3 text-center">
                  <FlagDot enabled={plan.tutorBookDiscountEnabled} />
                </td>
                <td className="px-4 py-3 text-right">
                  <PriceCell price={plan.price} originalPrice={plan.originalPrice ?? null} />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function FlagDot({ enabled }: { enabled: boolean }) {
  return (
    <span
      aria-label={enabled ? 'enabled' : 'disabled'}
      className={`inline-block h-3 w-3 rounded-full ${
        enabled ? 'bg-[#D4A44F] ring-2 ring-[#D4A44F]/30' : 'bg-muted'
      }`}
    />
  );
}

function formatAccess(days: number): string {
  if (days >= 9000) return 'Permanent';
  if (days >= 365) return `${Math.round(days / 365)} year${days >= 730 ? 's' : ''}`;
  if (days >= 30) return `${Math.round(days / 30)} months`;
  return `${days} days`;
}
