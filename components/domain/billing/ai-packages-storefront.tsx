'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { CheckCircle2, ShoppingCart } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerSurfaceSectionHeader } from '@/components/domain';
import { fetchAiPackages, fetchBillingContent } from '@/lib/api';
import { makeBillingCopy } from '@/lib/billing-copy-defaults';
import type { AiPackage, AiPackagesResponse } from '@/lib/billing-types';
import { formatMoney } from '@/lib/money';

const AI_PACKAGE_SUBTEST_SECTIONS: Array<{
  key: 'listening' | 'reading' | 'writing' | 'speaking';
  copyKey: string;
  headerClass: string;
}> = [
  { key: 'listening', copyKey: 'billing.ai.section.listening', headerClass: 'bg-blue-700' },
  { key: 'reading', copyKey: 'billing.ai.section.reading', headerClass: 'bg-purple-700' },
  { key: 'writing', copyKey: 'billing.ai.section.writing', headerClass: 'bg-amber-600' },
  { key: 'speaking', copyKey: 'billing.ai.section.speaking', headerClass: 'bg-emerald-700' },
];

function aiPackageValidityLabel(validityDays: number): string {
  if (validityDays <= 0) return '';
  return validityDays >= 180 ? '6-month validity' : `${validityDays}-day validity`;
}

function aiPackageHeadline(pkg: AiPackage): string {
  if (pkg.group === 'mock') return `${pkg.mocks} full mock${pkg.mocks === 1 ? '' : 's'}`;
  if (pkg.credits > 0) return `${pkg.credits} AI credit${pkg.credits === 1 ? '' : 's'}`;
  return 'Practice access';
}

/**
 * AI grading packages storefront (Full / Separate / Mock), embeddable on the
 * Subscriptions & Packages page. Reads the same admin-editable billing copy keys
 * and the same /v1/billing/ai-packages data the catalogue is driven by; the Buy
 * button hands off to the standard checkout review flow.
 */
export function AiPackagesStorefront() {
  const router = useRouter();
  const [packages, setPackages] = useState<AiPackagesResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [view, setView] = useState<'full' | 'separate'>('full');

  const [billingCopy, setBillingCopy] = useState<Record<string, string> | null>(null);
  const copy = useMemo(() => makeBillingCopy(billingCopy), [billingCopy]);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    fetchAiPackages()
      .then((result) => {
        if (!cancelled) setPackages(result);
      })
      .catch(() => {
        if (!cancelled) setPackages(null);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    fetchBillingContent()
      .then((map) => {
        if (!cancelled) setBillingCopy(map);
      })
      .catch(() => {
        /* copy is optional; fall back to in-code defaults */
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const renderCard = (pkg: AiPackage) => {
    const validity = aiPackageValidityLabel(pkg.validityDays);
    return (
      <article
        key={pkg.code}
        className="flex flex-col rounded-2xl border border-border bg-surface p-5 shadow-sm"
      >
        <div className="flex items-start justify-between gap-3">
          <h3 className="text-xl font-semibold tracking-tight text-navy">{pkg.name}</h3>
          {pkg.priorityQueue ? (
            <span className="inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.08em] text-amber-800 dark:bg-amber-300/20 dark:text-amber-200">
              {copy('billing.ai.priority')}
            </span>
          ) : null}
        </div>
        <p className="mt-2 text-sm leading-6 text-muted">{pkg.description}</p>
        <div className="mt-4 rounded-xl border border-border/70 bg-background-light/60 p-4">
          <p className="text-2xl font-semibold tracking-tight text-navy">{formatMoney(pkg.price, { currency: pkg.currency })}</p>
          <p className="mt-1 text-sm text-muted">
            {aiPackageHeadline(pkg)}
            {validity ? ` · ${validity}` : ''}
          </p>
        </div>
        {pkg.features.length > 0 ? (
          <ul className="mt-4 flex-1 space-y-2 text-sm text-navy">
            {pkg.features.map((feature, index) => (
              <li key={index} className="flex items-start gap-2">
                <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
                <span>{feature}</span>
              </li>
            ))}
          </ul>
        ) : (
          <div className="flex-1" />
        )}
        <Button
          className="mt-5"
          fullWidth
          onClick={() => router.push(`/checkout/review?productType=addon_purchase&priceId=${encodeURIComponent(pkg.code)}&quantity=1`)}
        >
          <ShoppingCart className="h-4 w-4" />
          {copy('billing.ai.buyNow')}
        </Button>
      </article>
    );
  };

  return (
    <section className="mt-8">
      <LearnerSurfaceSectionHeader
        eyebrow={copy('billing.ai.eyebrow')}
        title={copy('billing.ai.title')}
        description={copy('billing.ai.description')}
        className="mb-4"
      />

      {loading ? (
        <div className="grid gap-4 lg:grid-cols-3">
          {[1, 2, 3].map((i) => (
            <Skeleton key={i} className="h-72 rounded-2xl" />
          ))}
        </div>
      ) : !packages ? (
        <div className="rounded-2xl border border-dashed border-border bg-background-light p-5 text-center text-sm text-muted">
          {copy('billing.ai.unavailable')}
        </div>
      ) : (
        <>
          <div className="mb-5 inline-flex rounded-xl border border-border bg-background-light p-1">
            {(
              [
                { id: 'full' as const, label: copy('billing.ai.toggle.full') },
                { id: 'separate' as const, label: copy('billing.ai.toggle.separate') },
              ]
            ).map((tab) => (
              <button
                key={tab.id}
                type="button"
                onClick={() => setView(tab.id)}
                className={`rounded-lg px-4 py-1.5 text-sm font-medium transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200 ${
                  view === tab.id ? 'bg-emerald-700 text-white shadow-sm' : 'text-navy hover:bg-surface'
                }`}
                aria-pressed={view === tab.id}
              >
                {tab.label}
              </button>
            ))}
          </div>

          <p className="mb-5 text-sm text-muted">
            {view === 'full' ? copy('billing.ai.fullIntro') : copy('billing.ai.separateIntro')}
          </p>

          {view === 'full' ? (
            packages.full.length > 0 ? (
              <div className="grid gap-4 lg:grid-cols-3">{packages.full.map(renderCard)}</div>
            ) : (
              <div className="rounded-2xl border border-dashed border-border bg-background-light p-5 text-center text-sm text-muted">
                {copy('billing.ai.fullEmpty')}
              </div>
            )
          ) : (
            <div className="space-y-6">
              {AI_PACKAGE_SUBTEST_SECTIONS.map((sectionDef) => {
                const sectionPackages = packages.separate[sectionDef.key];
                if (!sectionPackages || sectionPackages.length === 0) return null;
                return (
                  <section key={sectionDef.key}>
                    <div className={`mb-3 inline-block rounded-lg px-3 py-1.5 text-sm font-semibold text-white ${sectionDef.headerClass}`}>
                      {copy(sectionDef.copyKey)} {copy('billing.ai.sectionSuffix')}
                    </div>
                    <div className="grid gap-4 lg:grid-cols-3">{sectionPackages.map(renderCard)}</div>
                  </section>
                );
              })}
            </div>
          )}

          {packages.mock.length > 0 ? (
            <div className="mt-8">
              <LearnerSurfaceSectionHeader
                eyebrow={copy('billing.ai.mock.eyebrow')}
                title={copy('billing.ai.mock.title')}
                description={copy('billing.ai.mock.description')}
                className="mb-4"
              />
              <div className="grid gap-4 lg:grid-cols-3">{packages.mock.map(renderCard)}</div>
            </div>
          ) : null}
        </>
      )}
    </section>
  );
}
