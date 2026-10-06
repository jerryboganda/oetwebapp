'use client';

import { Fragment, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { CheckCircle2, ShoppingCart, Sparkles } from 'lucide-react';
import { BuyTutorBookButton } from '@/components/billing/buy-tutor-book-button';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { Button, buttonClassName } from '@/components/ui/button';
import { cardClassName } from '@/components/ui/card';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { CatalogEntitlementSummary } from './catalog-sections';
import { PromoHeroSlider } from './promo-hero-slider';
import { AppDownloadPromo } from '@/components/marketing/app-download-promo';
import { useAuth } from '@/contexts/auth-context';
import { fetchPublicCatalog, fetchAiPackages } from '@/lib/api';
import { useEntitlementSnapshot } from '@/lib/query/hooks';
import type { PublicCatalogPlanRow, PublicCatalogResponse } from '@/lib/types/admin';
import type { AiPackagesResponse } from '@/lib/billing-types';
import { formatPrice, type PublicCatalogResponseWithPresentation } from '@/lib/catalog-presentation';
import {
  WEBSITE_SECTIONS,
  WEBSITE_PACKAGES,
  SEPARATE_AI_PACKAGES_GROUP,
  resolveWebsitePackageBySlug,
  resolveWebsitePackageByCode,
  applyWebsitePackageOverlay,
  overlayForPackage,
  resolveWebsiteSections,
  buildCustomWebsitePackages,
  websitePackageNumber,
  type WebsitePackage,
  type WebsiteSectionKey,
} from '@/lib/catalog-website-packages';
import { cn } from '@/lib/utils';
import { useAddToCart } from '@/lib/cart/use-add-to-cart';
import { useRevalidateOnResume } from '@/hooks/use-revalidate-on-resume';

// Live billing values (price is the source of truth for what the learner is charged).
interface LivePrice {
  code: string;
  price: number;
  originalPrice: number | null;
  currency: string;
  profession?: string;
}

const PROFESSION_ORDER = [
  'all',
  'medicine',
  'nursing',
  'pharmacy',
  'physiotherapy',
  'other-allied-health',
  'radiography',
  'allied_health',
];
const PROFESSION_LABEL: Record<string, string> = {
  all: 'All disciplines',
  medicine: 'Medicine',
  nursing: 'Nursing',
  pharmacy: 'Pharmacy',
  physiotherapy: 'Physiotherapy',
  'other-allied-health': 'Modified Allied Health Profession',
  'modified-allied-health': 'Modified Allied Health Profession',
  allied_health: 'Modified Allied Health Profession',
  'allied-health': 'Modified Allied Health Profession',
  'Allied Health Profession': 'Modified Allied Health Profession',
  'Modified Allied Health Profession': 'Modified Allied Health Profession',
  radiography: 'Radiography',
};

function buildPriceMap(
  catalog: PublicCatalogResponse | null,
  ai: AiPackagesResponse | null,
): Map<string, LivePrice> {
  const map = new Map<string, LivePrice>();
  const put = (code: string, price: number, originalPrice: number | null, currency: string, profession?: string) => {
    const live = { code, price, originalPrice, currency: currency || 'GBP', profession };
    map.set(code, live);
    const canonicalCode = resolveWebsitePackageByCode(code)?.code;
    if (canonicalCode) map.set(canonicalCode, live);
  };

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

function isConditionalPackageVisible(pkg: WebsitePackage, ownedPlan: PublicCatalogPlanRow | null): boolean {
  if (pkg.code === 'tutor-book-addon') return ownedPlan?.tutorBookDiscountEnabled === true;
  if (pkg.code.startsWith('addon-') && pkg.code.endsWith('-letters')) {
    return ownedPlan?.writingAddonsEnabled === true;
  }
  return true;
}

function SubscriptionPackageCard({
  pkg,
  live,
  owned,
  highlighted,
}: {
  pkg: WebsitePackage;
  live: LivePrice;
  owned: boolean;
  highlighted: boolean;
}) {
  const { addToCart } = useAddToCart();
  const currency = live.currency;
  const price = live.price;
  const hasDiscount = live.originalPrice != null && live.originalPrice > price;
  const packageNo = websitePackageNumber(pkg);

  const onAddToCart = () => {
    addToCart({
      code: live.code,
      kind: pkg.productType === 'plan_purchase' ? 'plan' : 'addon',
      name: pkg.name,
      price,
      currency,
    });
  };

  return (
    <article
      id={`pkg-${pkg.code}`}
      className={cn(
        cardClassName({ padding: 'none' }),
        'flex h-full scroll-mt-24 flex-col overflow-hidden transition-shadow',
        pkg.featured && 'ring-2 ring-primary/40',
        highlighted && 'ring-2 ring-primary shadow-clinical',
      )}
    >
      {pkg.featured ? (
        <div className="flex items-center justify-center gap-1.5 bg-primary px-3 py-1.5 eyebrow text-white dark:bg-primary-700">
          <Sparkles className="h-3.5 w-3.5" aria-hidden="true" /> {pkg.badges.includes('Recommended') ? 'Recommended' : pkg.badges.includes('Best value') ? 'Best value' : 'Most popular'}
        </div>
      ) : null}

      <div className="flex h-full flex-col gap-4 p-4 sm:p-5">
        <div className="flex items-start justify-between gap-3">
          {packageNo != null ? <span className="eyebrow text-muted">Package {packageNo}</span> : null}
          <div className="ms-auto text-end">
            <div className="text-2xl font-bold tabular-nums text-navy">{formatPrice(price, currency)}</div>
            {hasDiscount ? (
              <div className="text-xs tabular-nums text-muted line-through">was {formatPrice(live!.originalPrice as number, currency)}</div>
            ) : null}
          </div>
        </div>

        <div>
          <h3 className="text-lg font-bold leading-snug text-navy">{pkg.name}</h3>
          {pkg.metaChips.length > 0 ? (
            <div className="mt-2 flex flex-wrap gap-1.5">
              {pkg.metaChips.map((chip, chipIndex) => (
                <span
                  key={`${chipIndex}-${chip}`}
                  className="inline-flex items-center rounded-full bg-background-light px-2.5 py-0.5 text-2xs font-semibold text-muted"
                >
                  {chip}
                </span>
              ))}
            </div>
          ) : null}
          {pkg.category ? (
            <p className="mt-2 text-xs text-muted">
              <span className="font-semibold text-navy">Category:</span> {pkg.category}
            </p>
          ) : null}
        </div>

        {pkg.formatLine ? (
          <p className="text-sm text-muted">
            <span className="font-semibold text-navy">Format:</span> {pkg.formatLine}
          </p>
        ) : null}

        {pkg.description ? <p className="whitespace-pre-line text-sm leading-relaxed text-muted">{pkg.description}</p> : null}

        {pkg.badges.length > 0 ? (
          <div className="flex flex-wrap gap-1.5">
            {pkg.badges.map((badge, badgeIndex) => (
              <span
                key={`${badgeIndex}-${badge}`}
                className="inline-flex items-center rounded-full bg-primary/10 px-2.5 py-0.5 text-2xs font-semibold text-primary"
              >
                {badge}
              </span>
            ))}
          </div>
        ) : null}

        {pkg.features.length > 0 ? (
          <ul className="space-y-1.5 text-sm text-navy">
            {pkg.features.map((feature, featureIndex) => (
              <li key={`${featureIndex}-${feature}`} className="flex items-start gap-2">
                <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success-strong" aria-hidden="true" />
                <span>{feature}</span>
              </li>
            ))}
          </ul>
        ) : null}

        {pkg.bestFor ? (
          <p className="whitespace-pre-line rounded-xl bg-background-light px-3 py-2 text-sm text-navy">
            <span className="font-bold">Best for:</span> {pkg.bestFor}
          </p>
        ) : null}

        <div className="mt-auto pt-1">
          {owned ? (
            <span className="inline-flex min-h-11 w-full items-center justify-center gap-2 rounded-control bg-success/10 px-4 py-2.5 text-sm font-semibold text-success-strong">
              <CheckCircle2 className="h-4 w-4" aria-hidden="true" /> Active on your account
            </span>
          ) : pkg.section === 'tutorbook' ? (
            <BuyTutorBookButton className={buttonClassName({ fullWidth: true, className: 'pressable' })}>
              <ShoppingCart className="h-4 w-4" aria-hidden="true" /> Buy The Tutor Book
            </BuyTutorBookButton>
          ) : (
            <Button fullWidth onClick={onAddToCart}>
              <ShoppingCart className="h-4 w-4" aria-hidden="true" /> Add to cart
            </Button>
          )}
        </div>
      </div>
    </article>
  );
}

export function SubscriptionsCatalog() {
  const searchParams = useSearchParams();
  const [catalog, setCatalog] = useState<PublicCatalogResponseWithPresentation | null>(null);
  const [ai, setAi] = useState<AiPackagesResponse | null>(null);
  const { user } = useAuth();
  const userId = user?.userId ?? '';
  // Owned-plan context for the page. The shared entitlement query (cached, deduped with the
  // shell and dashboard), not a private fetch; no request at all for a signed-out visitor.
  const entitlement = useEntitlementSnapshot(userId, { enabled: Boolean(userId) }).data ?? null;
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeProfession, setActiveProfession] = useState('all');
  const [highlightCode, setHighlightCode] = useState<string | null>(null);

  const requestIdRef = useRef(0);
  const hasDataRef = useRef(false);

  // Used for the first load and for the silent refresh on resume: `loading` is never
  // set back to true, and a failed refresh keeps whatever was already on screen.
  const loadPackages = useCallback(async () => {
    requestIdRef.current += 1;
    const requestId = requestIdRef.current;
    try {
      const [catalogResult, aiResult] = await Promise.allSettled([fetchPublicCatalog(), fetchAiPackages()]);
      if (requestId !== requestIdRef.current) return;
      if (catalogResult.status === 'fulfilled') setCatalog(catalogResult.value);
      if (aiResult.status === 'fulfilled') setAi(aiResult.value);
      if (catalogResult.status === 'fulfilled' || aiResult.status === 'fulfilled') {
        hasDataRef.current = true;
        setError(null);
      } else if (!hasDataRef.current) {
        setError('Could not load the packages right now. Please refresh to try again.');
      }
    } finally {
      if (requestId === requestIdRef.current) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadPackages();
    return () => {
      // Drop the reply of any request still in flight once this page is gone.
      requestIdRef.current += 1;
    };
  }, [loadPackages]);

  useRevalidateOnResume(() => {
    void loadPackages();
  });

  const priceMap = useMemo(() => buildPriceMap(catalog, ai), [catalog, ai]);
  const websitePackages = catalog?.presentation?.websitePackages;
  // Section headings and the quick-jump labels follow the admin's section overrides.
  const sections = useMemo(() => resolveWebsiteSections(websitePackages?.sections), [websitePackages]);
  // Plans created in Billing > Pricing that have no static definition but whose package
  // record was given a section in the admin editor. Their overlay is already applied.
  const customPackages = useMemo(
    () => buildCustomWebsitePackages(catalog?.plans ?? [], catalog?.presentation),
    [catalog],
  );
  const ownedPlanCode = entitlement?.planCode ?? null;
  const canonicalOwnedPlanCode = ownedPlanCode
    ? resolveWebsitePackageByCode(ownedPlanCode)?.code ?? ownedPlanCode
    : null;
  const ownedPlan = useMemo(
    () =>
      catalog?.plans.find((plan) => {
        const canonicalCode = resolveWebsitePackageByCode(plan.code)?.code ?? plan.code;
        return canonicalOwnedPlanCode != null && canonicalCode === canonicalOwnedPlanCode;
      }) ?? null,
    [canonicalOwnedPlanCode, catalog?.plans],
  );

  // Always show the six candidate-facing discipline chips in this order,
  // including Modified Allied Health Profession after Physiotherapy.
  const professions = [
    'all',
    'medicine',
    'nursing',
    'pharmacy',
    'physiotherapy',
    'other-allied-health',
  ] as const;

  const packagesBySection = useMemo(() => {
    const grouped = new Map<WebsiteSectionKey, WebsitePackage[]>();
    for (const section of WEBSITE_SECTIONS) grouped.set(section.key, []);
    const customCodes = new Set(customPackages.map((pkg) => pkg.code));
    // Filtering and ordering run on the static package; the overlay is applied afterwards.
    for (const pkg of [...WEBSITE_PACKAGES, ...customPackages]) {
      if (!priceMap.has(pkg.code) || !isConditionalPackageVisible(pkg, ownedPlan)) continue;
      // Full Recorded courses respect the active discipline filter; every other
      // section is discipline-agnostic (all "All disciplines") and always shown.
      if (pkg.section === 'full-recorded' && activeProfession !== 'all') {
        const prof = priceMap.get(pkg.code)?.profession;
        const matchesProfession =
          prof === activeProfession ||
          (activeProfession === 'other-allied-health' &&
            (prof === 'other-allied-health' || prof === 'allied_health' || pkg.code === 'full-allied-health')) ||
          (activeProfession === 'physiotherapy' &&
            (prof === 'physiotherapy' || pkg.code === 'full-physiotherapy')) ||
          (activeProfession === 'pharmacy' &&
            (prof === 'pharmacy' || pkg.code === 'full-pharmacy')) ||
          (activeProfession === 'nursing' &&
            (prof === 'nursing' || pkg.code === 'full-nursing')) ||
          (activeProfession === 'medicine' &&
            (prof === 'medicine' ||
              pkg.code === 'full-condensed-medicine' ||
              pkg.code === 'full-condensed-medicine-tbook' ||
              pkg.code === 'full-recorded-intensive' ||
              pkg.code === 'full-recorded-vip' ||
              pkg.code === 'full-recorded-pass-guarantee' ||
              pkg.code === 'full-condensed-medicine-crash' ||
              pkg.code === 'basic-english'));
        if (prof && prof !== 'all' && !matchesProfession) continue;
      }
      const merged = customCodes.has(pkg.code)
        ? pkg
        : applyWebsitePackageOverlay(pkg, overlayForPackage(websitePackages?.byCode, pkg));
      grouped.get(pkg.section)?.push(merged);
    }
    return grouped;
  }, [activeProfession, customPackages, ownedPlan, priceMap, websitePackages]);

  // First visible section of the "Separate AI Packages" group — the parent
  // group heading renders immediately before it.
  const firstSeparateAiSectionKey = useMemo(
    () =>
      SEPARATE_AI_PACKAGES_GROUP.sectionKeys.find(
        (key) => (packagesBySection.get(key) ?? []).length > 0,
      ) ?? null,
    [packagesBySection],
  );

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

  const sectionTitle = (key: WebsiteSectionKey) => sections.find((section) => section.key === key)?.title ?? key;
  // Ids stay fixed (they are scroll targets); labels follow the resolved section titles.
  const catalogShortcuts = [
    { id: 'section-full-recorded', label: sectionTitle('full-recorded') },
    { id: 'section-separate', label: sectionTitle('separate') },
    { id: 'section-ai', label: sectionTitle('ai') },
    { id: 'section-separate-ai', label: SEPARATE_AI_PACKAGES_GROUP.title },
    { id: 'section-listening-recalls', label: sectionTitle('listening-recalls') },
    { id: 'section-tutorbook', label: sectionTitle('tutorbook') },
    { id: 'section-mock', label: sectionTitle('mock') },
  ];

  const handleShortcutClick = (sectionId: string) => {
    const el = document.getElementById(sectionId);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  };

  return (
    <>
      <LearnerPageHero
        eyebrow="OET with Dr Ahmed Hesham · 2026 portfolio"
        title="Subscriptions & Packages"
        description="Every 2026 package from the website pricing page — full recorded courses, focused Writing & Speaking bundles, AI practice, mock exams and TutorBook — with current pricing."
        icon={Sparkles}
        accent="primary"
      />

      <PromoHeroSlider />

      {entitlement ? <CatalogEntitlementSummary snapshot={entitlement} /> : null}

      {/* Horizontally scrollable mobile shortcut buttons (in normal page flow, non-sticky) */}
      <nav
        aria-label="Catalogue quick jump navigation"
        className={cn(cardClassName({ padding: 'none' }), 'relative w-full p-1.5 sm:p-2')}
      >
        <div className="flex w-full items-center gap-2 overflow-x-auto overscroll-x-contain px-1 py-1 [-webkit-overflow-scrolling:touch] touch-pan-x [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
          <span className="shrink-0 select-none ps-1.5 eyebrow text-muted">
            Quick jump:
          </span>
          {catalogShortcuts.map((shortcut) => (
            <button
              key={shortcut.id}
              type="button"
              onClick={() => handleShortcutClick(shortcut.id)}
              className="pressable hover-primary min-h-11 shrink-0 touch-manipulation select-none rounded-control border border-border bg-background-light px-3.5 text-xs font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              {shortcut.label}
            </button>
          ))}
          {/* Trailing spacer ensures final button is 100% visible and unclipped on narrow 320-430px screens */}
          <div className="w-3 shrink-0" aria-hidden="true" />
        </div>
      </nav>

      {error ? (
        <ErrorState message={error} />
      ) : loading ? (
        <div className="grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-3">
          {[0, 1, 2, 3, 4, 5].map((i) => (
            <Skeleton key={i} className="h-80 rounded-2xl" />
          ))}
        </div>
      ) : (
        <>
          {sections.map((section) => {
            const packages = packagesBySection.get(section.key) ?? [];
            // A filtered-out Full Recorded section keeps its discipline chips, so the
            // learner can switch the filter back instead of losing the whole section.
            const keepFilteredSection = section.key === 'full-recorded' && activeProfession !== 'all';
            if (packages.length === 0 && !keepFilteredSection) return null;
            return (
              <Fragment key={section.key}>
                {section.key === firstSeparateAiSectionKey ? (
                  <div id="section-separate-ai" className="scroll-mt-16 border-t border-border pt-8">
                    <LearnerSurfaceSectionHeader
                      title={SEPARATE_AI_PACKAGES_GROUP.title}
                      description={SEPARATE_AI_PACKAGES_GROUP.description}
                    />
                  </div>
                ) : null}
                <MotionSection id={`section-${section.key}`} className="scroll-mt-16 space-y-4">
                  <LearnerSurfaceSectionHeader
                    title={section.title}
                    description={section.description}
                  />

                  {section.key === 'full-recorded' ? (
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
                              'pressable min-h-11 rounded-control border px-3.5 text-sm font-semibold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
                              active ? 'border-primary/30 bg-primary/10 text-primary' : 'hover-primary border-border bg-surface text-muted',
                            )}
                          >
                            {PROFESSION_LABEL[profession] ?? profession}
                          </button>
                        );
                      })}
                    </div>
                  ) : null}

                  {packages.length === 0 ? (
                    <EmptyState
                      icon={<ShoppingCart className="h-8 w-8" />}
                      title={`No ${PROFESSION_LABEL[activeProfession] ?? activeProfession} packages`}
                      action={{ label: PROFESSION_LABEL.all, onClick: () => setActiveProfession('all') }}
                    />
                  ) : (
                    <div className="grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-3">
                      {packages.map((pkg, index) => (
                        <MotionItem key={pkg.code} delayIndex={Math.min(index, 5)}>
                          <SubscriptionPackageCard
                            pkg={pkg}
                            live={priceMap.get(pkg.code)!}
                            owned={canonicalOwnedPlanCode != null && canonicalOwnedPlanCode === pkg.code}
                            highlighted={highlightCode === pkg.code}
                          />
                        </MotionItem>
                      ))}
                    </div>
                  )}
                </MotionSection>
              </Fragment>
            );
          })}

          {/* Official Candidate Apps Download Section */}
          <AppDownloadPromo variant="card" />
        </>
      )}
    </>
  );
}
