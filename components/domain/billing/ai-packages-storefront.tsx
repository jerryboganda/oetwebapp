'use client';

import { useEffect, useMemo, useState } from 'react';
import { CheckCircle2, ShoppingCart } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerSurfaceSectionHeader } from '@/components/domain';
import { fetchAiPackages, fetchBillingContent } from '@/lib/api';
import { makeBillingCopy } from '@/lib/billing-copy-defaults';
import type { AiPackage, AiPackagesResponse } from '@/lib/billing-types';
import { formatMoney } from '@/lib/money';
import { useAddToCart } from '@/lib/cart/use-add-to-cart';
import {
  resolveWebsitePackageByCode,
  type WebsitePackage,
} from '@/lib/catalog-website-packages';

const AI_PACKAGE_SUBTEST_SECTIONS: Array<{
  key: 'listening' | 'reading' | 'writing' | 'speaking';
  title: string;
  headerClass: string;
}> = [
  { key: 'listening', title: 'Separate Listening Packages', headerClass: 'bg-blue-700' },
  { key: 'reading', title: 'Separate Reading Packages', headerClass: 'bg-purple-700' },
  { key: 'writing', title: 'Separate Writing Packages', headerClass: 'bg-amber-600' },
  { key: 'speaking', title: 'Separate Speaking Packages', headerClass: 'bg-emerald-700' },
];

interface CanonicalAiPackage {
  live: AiPackage;
  website: WebsitePackage;
}

function canonicalAiRows(rows: AiPackage[]): CanonicalAiPackage[] {
  return rows
    .flatMap((live) => {
      const website = resolveWebsitePackageByCode(live.code);
      return website && website.packageNo >= 30 && website.packageNo <= 47
        ? [{ live, website }]
        : [];
    })
    .sort((left, right) => left.website.packageNo - right.website.packageNo);
}

/**
 * AI grading packages storefront (Full / Separate / Mock), embeddable on the
 * Subscriptions & Packages page. Reads the same admin-editable billing copy keys
 * and the same /v1/billing/ai-packages data the catalogue is driven by; the Buy
 * button hands off to the standard checkout review flow.
 */
export function AiPackagesStorefront() {
  const { addToCart } = useAddToCart();
  const [packages, setPackages] = useState<AiPackagesResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [view, setView] = useState<'full' | 'mock' | 'separate'>('full');

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

  const canonicalPackages = useMemo(() => {
    if (!packages) return null;
    return {
      full: canonicalAiRows(packages.full),
      mock: canonicalAiRows(packages.mock),
      separate: {
        listening: canonicalAiRows(packages.separate.listening),
        reading: canonicalAiRows(packages.separate.reading),
        writing: canonicalAiRows(packages.separate.writing),
        speaking: canonicalAiRows(packages.separate.speaking),
      },
    };
  }, [packages]);

  const renderCard = ({ live, website }: CanonicalAiPackage) => {
    return (
      <article
        key={website.code}
        className="flex flex-col rounded-2xl border border-border bg-surface p-5 shadow-sm"
      >
        <div className="flex items-start justify-between gap-3">
          <div>
            <p className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted">
              Package {website.packageNo}
            </p>
            <h3 className="mt-1 text-xl font-semibold tracking-tight text-navy">{website.name}</h3>
          </div>
          <div className="flex flex-wrap justify-end gap-1.5">
            {website.badges.map((badge) => (
              <span
                key={badge}
                className="inline-flex items-center rounded-full bg-primary/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.08em] text-primary"
              >
                {badge}
              </span>
            ))}
          </div>
        </div>
        <div className="mt-3 flex flex-wrap gap-1.5">
          {website.metaChips.map((chip) => (
            <span
              key={chip}
              className="inline-flex items-center rounded-full bg-background-light px-2.5 py-0.5 text-[11px] font-semibold text-muted"
            >
              {chip}
            </span>
          ))}
        </div>
        <p className="mt-2 text-xs text-muted">
          <span className="font-semibold text-navy">Category:</span> {website.category}
        </p>
        <p className="mt-3 text-sm leading-6 text-muted">{website.description}</p>
        <p className="mt-3 text-sm text-muted">
          <span className="font-semibold text-navy">Format:</span> {website.formatLine}
        </p>
        <div className="mt-4 rounded-xl border border-border/70 bg-background-light/60 p-4">
          <p className="text-2xl font-semibold tracking-tight text-navy">
            {formatMoney(live.price, { currency: live.currency })}
          </p>
        </div>
        {website.features.length > 0 ? (
          <ul className="mt-4 flex-1 space-y-2 text-sm text-navy">
            {website.features.map((feature) => (
              <li key={feature} className="flex items-start gap-2">
                <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
                <span>{feature}</span>
              </li>
            ))}
          </ul>
        ) : (
          <div className="flex-1" />
        )}
        <p className="mt-4 rounded-xl border border-border bg-background-light px-3 py-2 text-sm text-navy">
          <span className="font-bold">Best for:</span> {website.bestFor}
        </p>
        <Button
          className="mt-5"
          fullWidth
          onClick={() =>
            addToCart({
              code: live.code,
              kind: 'addon',
              name: website.name,
              price: live.price,
              currency: live.currency,
            })
          }
        >
          <ShoppingCart className="h-4 w-4" />
          Add to cart
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
      ) : !canonicalPackages ? (
        <div className="rounded-2xl border border-dashed border-border bg-background-light p-5 text-center text-sm text-muted">
          {copy('billing.ai.unavailable')}
        </div>
      ) : (
        <>
          <div className="mb-5 inline-flex rounded-xl border border-border bg-background-light p-1">
            {(
              [
                { id: 'full' as const, label: 'AI Grading Packages' },
                { id: 'mock' as const, label: 'Full Mock Exam Packages' },
                { id: 'separate' as const, label: 'Separate Packages' },
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
            {view === 'full'
              ? copy('billing.ai.fullIntro')
              : view === 'mock'
                ? 'Full OET mock exam packages covering Listening, Reading, Writing, and Speaking in exam-style runs.'
                : copy('billing.ai.separateIntro')}
          </p>

          {view === 'full' ? (
            canonicalPackages.full.length > 0 ? (
              <div className="grid gap-4 lg:grid-cols-3">{canonicalPackages.full.map(renderCard)}</div>
            ) : (
              <div className="rounded-2xl border border-dashed border-border bg-background-light p-5 text-center text-sm text-muted">
                {copy('billing.ai.fullEmpty')}
              </div>
            )
          ) : view === 'mock' ? (
            canonicalPackages.mock.length > 0 ? (
              <div className="grid gap-4 lg:grid-cols-3">{canonicalPackages.mock.map(renderCard)}</div>
            ) : (
              <div className="rounded-2xl border border-dashed border-border bg-background-light p-5 text-center text-sm text-muted">
                {copy('billing.ai.fullEmpty')}
              </div>
            )
          ) : (
            <div className="space-y-6">
              {AI_PACKAGE_SUBTEST_SECTIONS.map((sectionDef) => {
                const sectionPackages = canonicalPackages.separate[sectionDef.key];
                if (!sectionPackages || sectionPackages.length === 0) return null;
                return (
                  <section key={sectionDef.key}>
                    <div className={`mb-3 inline-block rounded-lg px-3 py-1.5 text-sm font-semibold text-white ${sectionDef.headerClass}`}>
                      {sectionDef.title}
                    </div>
                    <div className="grid gap-4 lg:grid-cols-3">{sectionPackages.map(renderCard)}</div>
                  </section>
                );
              })}
            </div>
          )}
        </>
      )}
    </section>
  );
}
