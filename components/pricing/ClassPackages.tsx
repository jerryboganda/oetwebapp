'use client';

import type { PublicCatalogPlanRow } from '@/lib/types/admin';

import { PackageCard } from './PackageCard';

/**
 * Category-grouped grid of recorded courses, crash courses and class
 * bundles. Each group renders a heading with up to N cards on the OET
 * 2026 plan list. Wraps `PackageCard` so consumers compose once.
 */

const CATEGORY_ORDER: Array<{ key: string; label: string }> = [
  { key: 'full_course', label: 'Full Recorded Courses' },
  { key: 'full_course_bundle', label: 'Full Course Bundles' },
  { key: 'crash_course', label: 'Crash Courses' },
  { key: 'crash_course_bundle', label: 'Crash Course Bundles' },
  { key: 'writing_crash', label: 'Writing Crash Courses' },
  { key: 'writing_crash_bundle', label: 'Writing Bundles' },
  { key: 'speaking_crash', label: 'Speaking Crash Course' },
  { key: 'speaking_session', label: 'Private Speaking Sessions' },
  { key: 'combo_double', label: 'Double Special - Writing + Speaking' },
  { key: 'combo_mega', label: 'Mega Special Package' },
  { key: 'foundation', label: 'Foundation' },
  { key: 'book', label: 'The Tutor Book' },
];

export interface ClassPackagesProps {
  plans: PublicCatalogPlanRow[];
}

export function ClassPackages({ plans }: ClassPackagesProps) {
  const grouped = groupByCategory(plans);

  if (grouped.length === 0) return null;

  return (
    <section className="border-t border-border bg-surface px-4 py-14">
      <div className="mx-auto max-w-6xl space-y-12">
        {grouped.map((group) => (
          <div key={group.key}>
            <h3 className="text-xl font-bold">{group.label}</h3>
            <div className="mt-4 grid gap-5 md:grid-cols-2 lg:grid-cols-3">
              {group.items.map((plan) => (
                <PackageCard key={plan.code} plan={plan} />
              ))}
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

function groupByCategory(
  plans: PublicCatalogPlanRow[],
): Array<{ key: string; label: string; items: PublicCatalogPlanRow[] }> {
  const map = new Map<string, PublicCatalogPlanRow[]>();
  for (const plan of plans) {
    const key = plan.productCategory || 'standalone';
    const bucket = map.get(key) ?? [];
    bucket.push(plan);
    map.set(key, bucket);
  }
  const ordered: Array<{ key: string; label: string; items: PublicCatalogPlanRow[] }> = [];
  for (const { key, label } of CATEGORY_ORDER) {
    const items = map.get(key);
    if (items && items.length > 0)
      ordered.push({ key, label, items: items.sort((a, b) => a.displayOrder - b.displayOrder) });
  }
  return ordered;
}
