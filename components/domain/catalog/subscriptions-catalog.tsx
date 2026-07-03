'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { ArrowRight, CheckCircle2, Sparkles } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { CatalogEntitlementSummary } from './catalog-sections';
import { PromoHeroSlider } from './promo-hero-slider';
import {
  fetchPublicCatalog,
  fetchAiPackages,
  fetchMyEntitlementSnapshot,
  type MyEntitlementSnapshot,
} from '@/lib/api';
import type { PublicCatalogResponse } from '@/lib/types/admin';
import type { AiPackagesResponse } from '@/lib/billing-types';
import { formatPrice } from '@/lib/catalog-presentation';
import {
  WEBSITE_SECTIONS,
  WEBSITE_PACKAGES,
  resolveWebsitePackageBySlug,
  resolveWebsitePackageByCode,
  type WebsitePackage,
  type WebsiteSectionKey,
} from '@/lib/catalog-website-packages';
import { cn } from '@/lib/utils';

// Live billing values (price is the source of truth for what the learner is charged).
interface LivePrice {
  price: number;
  originalPrice: number | null;
  currency: string;
  profession?: string;
}

const PROFESSION_ORDER = ['all', 'medicine', 'nursing', 'pharmacy', 'physiotherapy', 'allied_health'];
const PROFESSION_LABEL: Record<string, string> = {
  all: 'All disciplines',
  medicine: 'Medicine',
  nursing: 'Nursing',
  pharmacy: 'Pharmacy',
  physiotherapy: 'Physiotherapy',
  allied_health: 'Allied health',
};

function buildPriceMap(
  catalog: PublicCatalogResponse | null,
  ai: AiPackagesResponse | null,
): Map<string, LivePrice> {
  const map = new Map<string, LivePrice>();
  const put = (code: string, price: number, originalPrice: number | null, currency: string, profession?: string) =>
    map.set(code, { price, originalPrice, currency: currency || 'GBP', profession });

  for (const p of catalog?.plans ?? []) put(p.code, p.price, p.originalPrice ?? null, p.currency, p.profession);
  for (const a of catalog?.addOns ?? []) put(a.code, a.price, a.originalPrice ?? null, a.currency);
  if (ai) {
    const flat = [
      ...ai.full,
      ...ai.separate.listening,
      ...ai.separate.reading,
      ...ai.separate.writing,
      ...ai.separate.speaking,
      ...ai.mock,
    ];
    for (const x of flat) put(x.code, x.price, null, x.currency);
  }
  return map;
}

function checkoutHref(pkg: WebsitePackage): string {
  return `/checkout/review?productType=${pkg.productType}&priceId=${encodeURIComponent(pkg.code)}&quantity=1`;
}

function SubscriptionPackageCard({
  pkg,
  live,
  owned,
  highlighted,
}: {
  pkg: WebsitePackage;
  live: LivePrice | undefined;
  owned: boolean;
  highlighted: boolean;
}) {
  const currency = live?.currency ?? 'GBP';
  const price = live?.price;
  const hasDiscount = live?.originalPrice != null && price != null && live.originalPrice > price;

  return (
    <article
      id={`pkg-${pkg.code}`}
      className={cn(
        'flex h-full scroll-mt-24 flex-col overflow-hidden rounded-2xl border border-border bg-surface shadow-sm transition-shadow',
        pkg.featured && 'ring-2 ring-primary/40',
        highlighted && 'ring-2 ring-primary shadow-clinical',
      )}
    >
      {pkg.featured ? (
        <div className="flex items-center justify-center gap-1.5 bg-primary px-3 py-1.5 text-xs font-bold uppercase tracking-wider text-white">
          <Sparkles className="h-3.5 w-3.5" /> {pkg.badges.includes('Recommended') ? 'Recommended' : pkg.badges.includes('Best value') ? 'Best value' : 'Most popular'}
        </div>
      ) : null}

      <div className="flex h-full flex-col gap-4 p-5">
        <div className="flex items-start justify-between gap-3">
          <span className="text-[11px] font-bold uppercase tracking-[0.14em] text-muted">Package {pkg.packageNo}</span>
          <div className="text-right">
            <div className="text-2xl font-bold text-navy">{price != null ? formatPrice(price, currency) : '—'}</div>
            {hasDiscount ? (
              <div className="text-xs text-muted line-through">was {formatPrice(live!.originalPrice as number, currency)}</div>
            ) : null}
          </div>
        </div>

        <div>
          <h3 className="text-lg font-bold leading-snug text-navy">{pkg.name}</h3>
          {pkg.metaChips.length > 0 ? (
            <div className="mt-2 flex flex-wrap gap-1.5">
              {pkg.metaChips.map((chip) => (
                <span
                  key={chip}
                  className="inline-flex items-center rounded-full bg-background-light px-2.5 py-0.5 text-[11px] font-semibold text-muted"
                >
                  {chip}
                </span>
              ))}
            </div>
          ) : null}
        </div>

        {pkg.formatLine ? (
          <p className="text-sm text-muted">
            <span className="font-semibold text-navy">Format:</span> {pkg.formatLine}
          </p>
        ) : null}

        {pkg.description ? <p className="text-sm leading-relaxed text-muted">{pkg.description}</p> : null}

        {pkg.badges.length > 0 ? (
          <div className="flex flex-wrap gap-1.5">
            {pkg.badges.map((badge) => (
              <span
                key={badge}
                className="inline-flex items-center rounded-full bg-primary/10 px-2.5 py-0.5 text-[11px] font-semibold text-primary"
              >
                {badge}
              </span>
            ))}
          </div>
        ) : null}

        {pkg.features.length > 0 ? (
          <ul className="space-y-1.5 text-sm text-navy">
            {pkg.features.map((feature) => (
              <li key={feature} className="flex items-start gap-2">
                <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
                <span>{feature}</span>
              </li>
            ))}
          </ul>
        ) : null}

        {pkg.bestFor ? (
          <p className="rounded-xl border border-border bg-background-light px-3 py-2 text-sm text-navy">
            <span className="font-bold">Best for:</span> {pkg.bestFor}
          </p>
        ) : null}

        <div className="mt-auto pt-1">
          {owned ? (
            <span className="inline-flex w-full items-center justify-center gap-2 rounded-lg bg-success/10 px-4 py-2.5 text-sm font-semibold text-success">
              <CheckCircle2 className="h-4 w-4" /> Active on your account
            </span>
          ) : (
            <Link
              href={checkoutHref(pkg)}
              className="inline-flex w-full items-center justify-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-semibold text-white transition-[background-color,transform] duration-200 hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
            >
              Get this package <ArrowRight className="h-4 w-4" />
            </Link>
          )}
        </div>
      </div>
    </article>
  );
}

export function SubscriptionsCatalog() {
  const searchParams = useSearchParams();
  const [catalog, setCatalog] = useState<PublicCatalogResponse | null>(null);
  const [ai, setAi] = useState<AiPackagesResponse | null>(null);
  const [entitlement, setEntitlement] = useState<MyEntitlementSnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeProfession, setActiveProfession] = useState('all');
  const [highlightCode, setHighlightCode] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const [catalogResult, aiResult] = await Promise.allSettled([fetchPublicCatalog(), fetchAiPackages()]);
        if (cancelled) return;
        if (catalogResult.status === 'fulfilled') setCatalog(catalogResult.value as PublicCatalogResponse);
        if (aiResult.status === 'fulfilled') setAi(aiResult.value);
        if (catalogResult.status === 'rejected' && aiResult.status === 'rejected') {
          setError('Could not load the packages right now. Please refresh to try again.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const snapshot = await fetchMyEntitlementSnapshot();
        if (!cancelled) setEntitlement(snapshot);
      } catch {
        // Entitlement context is optional; ignore failures.
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const priceMap = useMemo(() => buildPriceMap(catalog, ai), [catalog, ai]);
  const ownedPlanCode = entitlement?.planCode ?? null;

  // Discipline tabs are derived from the profession of the Full Recorded courses.
  const professions = useMemo(() => {
    const present = new Set<string>();
    for (const pkg of WEBSITE_PACKAGES) {
      if (pkg.section !== 'full-recorded') continue;
      const prof = priceMap.get(pkg.code)?.profession;
      if (prof) present.add(prof);
    }
    return PROFESSION_ORDER.filter((p) => p === 'all' || present.has(p));
  }, [priceMap]);

  const packagesBySection = useMemo(() => {
    const grouped = new Map<WebsiteSectionKey, WebsitePackage[]>();
    for (const section of WEBSITE_SECTIONS) grouped.set(section.key, []);
    for (const pkg of WEBSITE_PACKAGES) {
      // Full Recorded courses respect the active discipline filter; every other
      // section is discipline-agnostic (all "All disciplines") and always shown.
      if (pkg.section === 'full-recorded' && activeProfession !== 'all') {
        const prof = priceMap.get(pkg.code)?.profession;
        if (prof && prof !== 'all' && prof !== activeProfession) continue;
      }
      grouped.get(pkg.section)?.push(pkg);
    }
    return grouped;
  }, [activeProfession, priceMap]);

  // Deep-link: /subscriptions?package=<slug|code> from the website CTAs — scroll to
  // and briefly highlight the requested package once the catalogue has loaded.
  useEffect(() => {
    if (loading) return;
    const requested = searchParams.get('package');
    if (!requested) return;
    const match = resolveWebsitePackageBySlug(requested) ?? resolveWebsitePackageByCode(requested);
    if (!match) return;
    // Highlight + scroll are a genuine sync-to-URL/DOM side effect that auto-clears on a timer.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setHighlightCode(match.code);
    const el = document.getElementById(`pkg-${match.code}`);
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    const timer = window.setTimeout(() => setHighlightCode(null), 3200);
    return () => window.clearTimeout(timer);
  }, [loading, searchParams]);

  return (
    <div className="space-y-8">
      <LearnerPageHero
        eyebrow="OET with Dr Ahmed Hesham · 2026 portfolio"
        title="Subscriptions & Packages"
        description="Every 2026 package from the website pricing page — full recorded courses, focused Writing & Speaking bundles, AI practice, mock exams and TutorBook — with current pricing."
        icon={Sparkles}
        accent="primary"
      />

      <PromoHeroSlider />

      {entitlement ? <CatalogEntitlementSummary snapshot={entitlement} /> : null}

      {error ? (
        <div className="rounded-2xl border border-border bg-surface p-8 text-center text-muted">{error}</div>
      ) : loading ? (
        <div className="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
          {[0, 1, 2, 3, 4, 5].map((i) => (
            <div key={i} className="h-80 animate-pulse rounded-2xl border border-border bg-surface" />
          ))}
        </div>
      ) : (
        WEBSITE_SECTIONS.map((section) => {
          const packages = packagesBySection.get(section.key) ?? [];
          if (packages.length === 0) return null;
          return (
            <section key={section.key} id={`section-${section.key}`} className="space-y-4">
              <LearnerSurfaceSectionHeader title={section.title} description={section.description} />

              {section.key === 'full-recorded' && professions.length > 1 ? (
                <div className="flex flex-wrap gap-1.5">
                  {professions.map((profession) => {
                    const active = profession === activeProfession;
                    return (
                      <button
                        key={profession}
                        type="button"
                        onClick={() => setActiveProfession(profession)}
                        aria-pressed={active}
                        className={cn(
                          'rounded-full px-3.5 py-1.5 text-sm font-semibold transition-colors',
                          active ? 'bg-primary text-white' : 'border border-border bg-surface text-muted hover:text-navy',
                        )}
                      >
                        {PROFESSION_LABEL[profession] ?? profession}
                      </button>
                    );
                  })}
                </div>
              ) : null}

              <div className="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
                {packages.map((pkg) => (
                  <SubscriptionPackageCard
                    key={pkg.code}
                    pkg={pkg}
                    live={priceMap.get(pkg.code)}
                    owned={ownedPlanCode != null && ownedPlanCode === pkg.code}
                    highlighted={highlightCode === pkg.code}
                  />
                ))}
              </div>
            </section>
          );
        })
      )}
    </div>
  );
}
