'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { ArrowRight, CheckCircle2, FileText } from 'lucide-react';
import { fetchPublicCatalog } from '@/lib/api';
import type { PublicCatalogPlanRow, PublicCatalogAddOnRow } from '@/lib/types/admin';

const PROFESSION_LABEL: Record<string, string> = {
  all: 'All disciplines',
  medicine: 'Medicine',
  nursing: 'Nursing',
  pharmacy: 'Pharmacy',
};

const CATEGORY_ORDER: Array<{ key: string; label: string }> = [
  { key: 'full_course', label: 'Full Recorded Courses' },
  { key: 'full_course_bundle', label: 'Full Course Bundles' },
  { key: 'crash_course', label: 'Crash Courses' },
  { key: 'crash_course_bundle', label: 'Crash Course Bundles' },
  { key: 'writing_crash', label: 'Writing Crash Courses' },
  { key: 'writing_crash_bundle', label: 'Writing Bundles' },
  { key: 'speaking_crash', label: 'Speaking Crash Course' },
  { key: 'speaking_session', label: 'Private Speaking Sessions' },
  { key: 'combo_double', label: 'Double Special: Writing + Speaking' },
  { key: 'combo_mega', label: 'Mega Special Package' },
  { key: 'foundation', label: 'Foundation' },
  { key: 'book', label: 'The Tutor Book' },
];

export default function CatalogPage() {
  const [plans, setPlans] = useState<PublicCatalogPlanRow[]>([]);
  const [addOns, setAddOns] = useState<PublicCatalogAddOnRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void (async () => {
      try {
        const response = await fetchPublicCatalog();
        setPlans(response.plans ?? []);
        setAddOns(response.addOns ?? []);
        setError(null);
      } catch (err) {
        console.error('Failed to load OET 2026 catalog', err);
        setError(err instanceof Error ? err.message : 'Could not load the catalogue.');
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const grouped = useMemo(() => {
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
      if (items && items.length > 0) ordered.push({ key, label, items: items.sort((a, b) => a.displayOrder - b.displayOrder) });
    }
    return ordered;
  }, [plans]);

  return (
    <div className="min-h-screen bg-background-light text-navy">
      {/* Hero */}
      <section className="relative overflow-hidden bg-navy px-4 pb-20 pt-24 text-white">
        <div className="mx-auto max-w-5xl text-center">
          <p className="mb-2 text-xs font-semibold uppercase tracking-[0.3em] text-[#D4A44F]">OET with Dr. Ahmed Hesham · 2026 Portfolio</p>
          <h1 className="text-4xl font-bold tracking-tight sm:text-5xl">The complete OET 2026 catalogue</h1>
          <p className="mx-auto mt-4 max-w-2xl text-lg text-white/80">
            Recorded courses, writing letter assessments, private speaking sessions and The Tutor Book. Every SKU from the
            2026 portfolio, with current and original pricing.
          </p>
          <div className="mt-6 flex flex-wrap items-center justify-center gap-2 text-sm">
            <FlagBadge label="W" tooltip="Writing letter assessment add-ons" />
            <FlagBadge label="S" tooltip="Extra private Speaking sessions" />
            <FlagBadge label="TB £32" tooltip="Discounted £32 Tutor Book add-on" />
          </div>
        </div>
      </section>

      {/* Matrix */}
      <section className="px-4 py-14">
        <div className="mx-auto max-w-6xl">
          <h2 className="text-2xl font-bold">Pricing matrix at a glance</h2>
          <p className="mt-1 text-sm text-muted">
            Gold dot indicates the add-on is offered on that product. Grey dash means the section is hidden entirely, with no card
            and no upsell.
          </p>

          {loading ? (
            <div className="mt-6 h-72 animate-pulse rounded-2xl border border-border bg-surface" />
          ) : error ? (
            <div className="mt-6 rounded-2xl border border-border bg-surface p-8 text-center text-muted">{error}</div>
          ) : plans.length === 0 ? (
            <div className="mt-6 rounded-2xl border border-border bg-surface p-8 text-center text-muted">
              No products are published right now. Please check back soon.
            </div>
          ) : (
            <div className="mt-6 overflow-hidden rounded-2xl border border-border bg-surface shadow-sm">
              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-border text-sm">
                  <thead className="bg-background-light text-left text-xs font-medium uppercase tracking-wide text-muted">
                    <tr>
                      <th className="px-4 py-3">Product</th>
                      <th className="px-4 py-3">Category</th>
                      <th className="px-4 py-3">Access</th>
                      <th className="px-4 py-3 text-center">W</th>
                      <th className="px-4 py-3 text-center">S</th>
                      <th className="px-4 py-3 text-center">TB £32</th>
                      <th className="px-4 py-3 text-right">Price</th>
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
                        <td className="px-4 py-3 text-muted">{labelForCategory(plan.productCategory)}</td>
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
          )}
        </div>
      </section>

      {/* Category-grouped cards */}
      <section className="border-t border-border bg-surface px-4 py-14">
        <div className="mx-auto max-w-6xl space-y-12">
          {grouped.map((group) => (
            <div key={group.key}>
              <h3 className="text-xl font-bold">{group.label}</h3>
              <div className="mt-4 grid gap-5 md:grid-cols-2 lg:grid-cols-3">
                {group.items.map((plan) => (
                  <CatalogCard key={plan.code} plan={plan} />
                ))}
              </div>
            </div>
          ))}
        </div>
      </section>

      {/* Add-ons reference */}
      {addOns.length > 0 && (
        <section className="px-4 py-14">
          <div className="mx-auto max-w-5xl">
            <h2 className="text-2xl font-bold">Add-ons reference</h2>
            <p className="mt-1 text-sm text-muted">
              Add-ons attach to an eligible parent course. Each card shows the eligibility flag the parent must have.
            </p>
            <div className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {addOns.map((addon) => (
                <div key={addon.code} className="rounded-2xl border border-border bg-surface p-5">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <h4 className="font-bold">{addon.name}</h4>
                      <p className="mt-1 text-[11px] uppercase tracking-wider text-muted">
                        requires <code>{addon.eligibilityFlag || 'n/a'}</code>
                      </p>
                    </div>
                    <PriceCell price={addon.price} originalPrice={addon.originalPrice ?? null} compact />
                  </div>
                  {addon.description && <p className="mt-3 text-sm text-muted">{addon.description}</p>}
                </div>
              ))}
            </div>
          </div>
        </section>
      )}

      {/* Footer CTA */}
      <section className="border-t border-border bg-surface px-4 py-16 text-center">
        <div className="mx-auto max-w-2xl">
          <h2 className="text-3xl font-bold">Ready to start your OET preparation?</h2>
          <p className="mt-3 text-muted">Create an account to purchase any package and unlock your dedicated dashboard.</p>
          <div className="mt-6 flex flex-wrap items-center justify-center gap-3">
            <Link
              href="/register"
              className="inline-flex min-h-11 items-center justify-center gap-2 rounded-lg bg-primary px-5 py-2.5 text-sm font-medium text-white shadow-sm transition-[color,background-color,transform] duration-200 hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
            >
              Get started <ArrowRight className="h-4 w-4" />
            </Link>
            <Link
              href="/marketplace/packages"
              className="inline-flex min-h-11 items-center justify-center gap-2 rounded-lg border border-border bg-surface px-5 py-2.5 text-sm font-medium text-navy transition-colors hover:bg-background-light"
            >
              <FileText className="h-4 w-4" /> Browse the marketplace
            </Link>
          </div>
        </div>
      </section>
    </div>
  );
}

function CatalogCard({ plan }: { plan: PublicCatalogPlanRow }) {
  return (
    <article className="flex h-full flex-col rounded-2xl border border-border bg-surface p-6 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <h4 className="text-lg font-bold leading-snug">{plan.name}</h4>
        <PriceCell price={plan.price} originalPrice={plan.originalPrice ?? null} />
      </div>
      <div className="mt-3 flex flex-wrap gap-1.5">
        <Tag label={PROFESSION_LABEL[plan.profession] ?? plan.profession} />
        <Tag label={formatAccess(plan.accessDurationDays)} />
        {plan.writingAddonsEnabled && <Tag label="W add-ons" gold />}
        {plan.speakingAddonsEnabled && <Tag label="S add-ons" gold />}
        {plan.tutorBookDiscountEnabled && <Tag label="TB £32" gold />}
      </div>
      {plan.description && <p className="mt-3 text-sm text-muted line-clamp-4">{plan.description}</p>}
      {(plan.bundledWritingAssessments > 0 ||
        plan.bundledSpeakingSessions > 0 ||
        plan.bundledAiCredits > 0 ||
        plan.bundledTutorBook ||
        plan.bundledBasicEnglish) && (
        <ul className="mt-4 space-y-1.5 text-sm">
          {plan.bundledWritingAssessments > 0 && (
            <li className="flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4 flex-none text-success" />
              {plan.bundledWritingAssessments} bundled writing assessment{plan.bundledWritingAssessments === 1 ? '' : 's'}
            </li>
          )}
          {plan.bundledSpeakingSessions > 0 && (
            <li className="flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4 flex-none text-success" />
              {plan.bundledSpeakingSessions} bundled private speaking session{plan.bundledSpeakingSessions === 1 ? '' : 's'}
            </li>
          )}
          {plan.bundledAiCredits > 0 && (
            <li className="flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4 flex-none text-success" />
              {plan.bundledAiCredits} AI practice credits
            </li>
          )}
          {plan.bundledTutorBook && (
            <li className="flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4 flex-none text-success" />
              The Tutor Book, First Edition 2026 (PDF + Telegram)
            </li>
          )}
          {plan.bundledBasicEnglish && (
            <li className="flex items-center gap-2">
              <CheckCircle2 className="h-4 w-4 flex-none text-success" />
              Basic English foundation course
            </li>
          )}
        </ul>
      )}
      <div className="mt-auto pt-5">
        <Link
          href={`/marketplace/packages/${encodeURIComponent(plan.code)}`}
          className="inline-flex w-full items-center justify-center gap-2 rounded-lg border border-border bg-background-light px-4 py-2 text-sm font-medium text-navy transition-colors hover:bg-surface"
        >
          View details <ArrowRight className="h-4 w-4" />
        </Link>
      </div>
    </article>
  );
}

function PriceCell({ price, originalPrice, compact = false }: { price: number; originalPrice: number | null; compact?: boolean }) {
  return (
    <div className={compact ? 'text-right' : 'text-right'}>
      <div className={`font-bold ${compact ? 'text-base' : 'text-xl'}`}>£{price.toFixed(0)}</div>
      {originalPrice !== null && originalPrice > price && (
        <div className="text-xs text-muted line-through">was £{originalPrice.toFixed(0)}</div>
      )}
    </div>
  );
}

function FlagDot({ enabled }: { enabled: boolean }) {
  return (
    <span
      aria-label={enabled ? 'enabled' : 'disabled'}
      className={`inline-block h-3 w-3 rounded-full ${enabled ? 'bg-[#D4A44F] ring-2 ring-[#D4A44F]/30' : 'bg-muted'}`}
    />
  );
}

function FlagBadge({ label, tooltip }: { label: string; tooltip: string }) {
  return (
    <span title={tooltip} className="inline-flex items-center gap-1.5 rounded-full bg-white/10 px-3 py-1 text-xs font-medium text-white">
      <span className="inline-block h-2 w-2 rounded-full bg-[#D4A44F]" /> {label}
    </span>
  );
}

function Tag({ label, gold = false }: { label: string; gold?: boolean }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider ${
        gold
          ? 'bg-[#D4A44F]/15 text-[#996F1F]'
          : 'bg-background-light text-muted'
      }`}
    >
      {label}
    </span>
  );
}

function labelForCategory(category: string): string {
  return (
    CATEGORY_ORDER.find((c) => c.key === category)?.label ?? category.replace(/_/g, ' ')
  );
}

function formatAccess(days: number): string {
  if (days >= 9000) return 'Permanent';
  if (days >= 365) return `${Math.round(days / 365)} year${days >= 730 ? 's' : ''}`;
  if (days >= 30) return `${Math.round(days / 30)} months`;
  return `${days} days`;
}
