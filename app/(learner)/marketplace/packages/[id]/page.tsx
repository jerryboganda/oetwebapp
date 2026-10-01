'use client';

import { useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, ArrowRight, CheckCircle2, Clock, Package, Sparkles, Tag as TagIcon } from 'lucide-react';
import { fetchPublicCatalog } from '@/lib/api';
import type { PublicCatalogPlanRow, PublicCatalogAddOnRow } from '@/lib/types/admin';
import { AddonPurchaseModal } from '@/components/billing/addon-purchase-modal';
import { BuyTutorBookButton } from '@/components/billing/buy-tutor-book-button';
import { LearnerSurfaceSectionHeader } from '@/components/domain';
import { buttonClassName } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import {
  resolveWebsitePackageByCode,
  resolveWebsitePackageBySlug,
  websitePackageCheckoutHref,
  websitePackagePurchaseHref,
} from '@/lib/catalog-website-packages';

// The one gold call to action on the OET-navy brand hero (DESIGN.md §2: corporate accents for billing).
const goldCtaClassName =
  'pressable mt-4 inline-flex w-full items-center justify-center gap-2 rounded-control bg-gold px-5 py-2.5 text-sm font-bold text-oet-navy shadow-sm transition-colors hover:bg-gold-dark focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-gold focus-visible:ring-offset-2 focus-visible:ring-offset-oet-navy';

export default function PackageDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const rawId = params?.id;
  const code = typeof rawId === 'string' ? decodeURIComponent(rawId) : '';
  const requestedWebsitePackage =
    resolveWebsitePackageBySlug(code) ??
    resolveWebsitePackageByCode(code) ??
    (code.startsWith('pkg_') ? resolveWebsitePackageByCode(code.slice(4)) : undefined);

  const [plans, setPlans] = useState<PublicCatalogPlanRow[]>([]);
  const [addOns, setAddOns] = useState<PublicCatalogAddOnRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [modalAddOn, setModalAddOn] = useState<PublicCatalogAddOnRow | null>(null);

  useEffect(() => {
    void (async () => {
      try {
        const response = await fetchPublicCatalog();
        setPlans(response.plans ?? []);
        setAddOns(response.addOns ?? []);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Could not load product.');
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  useEffect(() => {
    if (requestedWebsitePackage?.productType !== 'addon_purchase') return;
    router.replace(websitePackagePurchaseHref(requestedWebsitePackage));
  }, [requestedWebsitePackage, router]);

  const plan = useMemo(
    () =>
      plans.find((candidate) => {
        if (!requestedWebsitePackage) return candidate.code === code;
        return resolveWebsitePackageByCode(candidate.code)?.code === requestedWebsitePackage.code;
      }),
    [plans, code, requestedWebsitePackage],
  );
  const websitePackage = requestedWebsitePackage ?? (plan ? resolveWebsitePackageByCode(plan.code) : undefined);

  const writingAddons = useMemo(
    () => (plan?.writingAddonsEnabled ? addOns.filter((a) => a.eligibilityFlag === 'writing_addons') : []),
    [plan, addOns],
  );
  const speakingAddons = useMemo(
    () => (plan?.speakingAddonsEnabled ? addOns.filter((a) => a.eligibilityFlag === 'speaking_addons') : []),
    [plan, addOns],
  );
  const tutorBookAddon = useMemo(
    () => (plan?.tutorBookDiscountEnabled ? addOns.find((a) => a.eligibilityFlag === 'tutor_book_discount') : undefined),
    [plan, addOns],
  );
  const tutorBookWebsitePackage = tutorBookAddon
    ? resolveWebsitePackageByCode(tutorBookAddon.code)
    : undefined;

  if (requestedWebsitePackage?.productType === 'addon_purchase') {
    const purchaseHref = websitePackagePurchaseHref(requestedWebsitePackage);
    return (
      <Card padding="lg" className="text-center">
        <h1 className="text-xl font-bold text-navy">Opening {requestedWebsitePackage.name}</h1>
        <p className="mt-2 text-sm text-muted">
          Taking you to the correct purchase options for this add-on.
        </p>
        <Link href={purchaseHref} className="mt-6 inline-flex items-center gap-2 text-sm font-medium text-primary">
          Continue <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
        </Link>
      </Card>
    );
  }

  if (loading) {
    return (
      <div role="status" aria-label="Loading package" className="space-y-6">
        <Skeleton className="h-64 rounded-2xl sm:rounded-surface" />
        <Skeleton className="h-72 rounded-2xl" />
      </div>
    );
  }
  if (error || !plan) {
    return (
      <EmptyState
        icon={<Package className="h-7 w-7" />}
        title="Package not available"
        description={error ?? 'This product is not currently published. Please check back soon.'}
        action={{ label: 'Back to the catalogue', href: '/catalog' }}
      />
    );
  }

  const hasBonuses = plan.bundledWritingAssessments > 0
    || plan.bundledSpeakingSessions > 0
    || plan.bundledAiCredits > 0
    || plan.bundledTutorBook
    || plan.bundledBasicEnglish;

  return (
    <>
      <section className="rounded-2xl bg-oet-navy p-5 text-white shadow-clinical sm:rounded-surface sm:p-8">
        <Link href="/catalog" className="inline-flex items-center gap-1 text-xs text-white/70 hover:text-white">
          <ArrowLeft className="h-3 w-3 rtl:rotate-180" aria-hidden="true" /> All packages
        </Link>
        <div className="mt-3 flex flex-col gap-6 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0 flex-1">
            <p className="eyebrow text-gold">
              {websitePackage ? `Package ${websitePackage.packageNo} · ${websitePackage.category}` : plan.productCategory.replace(/_/g, ' ')}
            </p>
            <h1 className="mt-2 text-balance text-2xl font-bold sm:text-4xl">{websitePackage?.name ?? plan.name}</h1>
            {(websitePackage?.description ?? plan.description) ? (
              <p className="mt-3 max-w-2xl text-sm text-white/80">
                {websitePackage?.description ?? plan.description}
              </p>
            ) : null}
            <div className="mt-4 flex flex-wrap gap-2 text-xs">
              <HeroTag>{websitePackage?.profession ?? labelForProfession(plan.profession)}</HeroTag>
              <HeroTag><Clock className="me-1 h-3 w-3" aria-hidden="true" /> {websitePackage?.access ?? `${formatAccess(plan.accessDurationDays)} access`}</HeroTag>
              {plan.writingAddonsEnabled && <HeroTag gold>W add-ons</HeroTag>}
              {plan.speakingAddonsEnabled && <HeroTag gold>S add-ons</HeroTag>}
              {tutorBookAddon && <HeroTag gold>Tutor Book £{tutorBookAddon.price.toFixed(0)}</HeroTag>}
            </div>
          </div>
          <div className="rounded-2xl border border-white/15 bg-white/10 p-5 sm:p-6 lg:w-80 lg:text-end">
            <div className="text-4xl font-bold tabular-nums">£{plan.price.toFixed(0)}</div>
            {plan.originalPrice !== null && plan.originalPrice !== undefined && plan.originalPrice > plan.price && (
              <div className="mt-1 text-sm tabular-nums text-white/70 line-through">was £{plan.originalPrice.toFixed(0)}</div>
            )}
            {plan.code === 'tutor-book' ? (
              <BuyTutorBookButton className={goldCtaClassName}>
                Buy The Tutor Book <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
              </BuyTutorBookButton>
            ) : (
              <button
                type="button"
                onClick={() =>
                  router.push(
                    websitePackage
                      ? websitePackageCheckoutHref(websitePackage, plan.code)
                      : `/checkout/review?productType=plan_purchase&priceId=${encodeURIComponent(plan.code)}&quantity=1`,
                  )
                }
                className={goldCtaClassName}
              >
                Buy for £{plan.price.toFixed(0)} <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
              </button>
            )}
            <p className="mt-2 text-2xs text-white/60">
              {plan.code === 'tutor-book'
                ? (() => {
                    const discounted = addOns.find((a) => a.eligibilityFlag === 'tutor_book_discount')?.price;
                    return discounted != null
                      ? `We check your eligibility automatically — £${discounted.toFixed(0)} if you have an eligible course, £${plan.price.toFixed(0)} otherwise.`
                      : 'We check your eligibility automatically — a discounted price applies if you have an eligible course.';
                  })()
                : 'Charged in GBP. No auto-renewal.'}
            </p>
          </div>
        </div>
      </section>

      <MotionSection className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <Card padding="lg" className="lg:col-span-2">
          <h2 className="text-lg font-bold text-navy">What you&apos;ll get</h2>
          <ul className="mt-4 grid gap-2 sm:grid-cols-2">
            {(websitePackage?.features ?? plan.dashboardModules.map(prettyModule)).map((feature) => (
              <li key={feature} className="flex items-start gap-2 text-sm text-navy">
                <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success-strong" aria-hidden="true" />
                <span>{feature}</span>
              </li>
            ))}
          </ul>
          {websitePackage ? (
            <p className="mt-6 rounded-xl bg-background-light p-4 text-sm text-navy">
              <span className="font-bold">Best for:</span> {websitePackage.bestFor}
            </p>
          ) : null}

          {hasBonuses && (
            <div className="mt-6 rounded-xl border border-gold/40 bg-gold/5 p-5">
              <h3 className="flex items-center gap-2 text-sm font-bold text-gold-fg">
                <Sparkles className="h-4 w-4" aria-hidden="true" /> Bonuses included with this package
              </h3>
              <ul className="mt-2 space-y-1.5 text-sm text-navy">
                {plan.bundledWritingAssessments > 0 && (
                  <li>· {plan.bundledWritingAssessments} writing letter assessment{plan.bundledWritingAssessments === 1 ? '' : 's'}</li>
                )}
                {plan.bundledSpeakingSessions > 0 && (
                  <li>· {plan.bundledSpeakingSessions} private speaking session{plan.bundledSpeakingSessions === 1 ? '' : 's'}</li>
                )}
                {plan.bundledAiCredits > 0 && <li>· {plan.bundledAiCredits} AI practice credits</li>}
                {plan.bundledTutorBook && (
                  <li>
                    · The Tutor Book, First Edition 2026 + WhatsApp access:{' '}
                    <a className="font-semibold underline" href="https://wa.me/447961725989">
                      +44 7961 725989
                    </a>
                  </li>
                )}
                {plan.bundledBasicEnglish && <li>· Basic English foundation course (11+ hours)</li>}
              </ul>
            </div>
          )}
        </Card>

        <aside className="grid content-start gap-3 text-sm">
          <SidebarBlock title="Access window">
            {websitePackage?.access ?? `${formatAccess(plan.accessDurationDays)} from purchase.`}
          </SidebarBlock>
          <SidebarBlock title="Profession">{websitePackage?.profession ?? labelForProfession(plan.profession)}</SidebarBlock>
          <SidebarBlock title="Category">{websitePackage?.category ?? plan.productCategory.replace(/_/g, ' ')}</SidebarBlock>
          <SidebarBlock title="Format">{websitePackage?.formatLine ?? 'Recorded video + materials. Assessments are reviewed by Dr Ahmed (48-72h turnaround, Friday off).'}</SidebarBlock>
        </aside>
      </MotionSection>

      {/* Add-ons — conditional rendering per 3 flags */}
      {(writingAddons.length > 0 || speakingAddons.length > 0 || tutorBookAddon) && (
        <MotionSection className="space-y-6">
          <LearnerSurfaceSectionHeader
            title="Available add-ons"
            description="Add these alongside an eligible enrolment. Tutor Book orders are delivered manually through WhatsApp."
          />

          {writingAddons.length > 0 && (
            <AddonGroup title="Writing letter assessments" addons={writingAddons} onSelect={setModalAddOn} />
          )}
          {speakingAddons.length > 0 && (
            <AddonGroup title="Extra private speaking sessions" addons={speakingAddons} onSelect={setModalAddOn} />
          )}
          {tutorBookAddon && (
            <div>
              <h3 className="eyebrow text-muted">
                The Tutor Book (£{tutorBookAddon.price.toFixed(0)}, discount for enrolled candidates)
              </h3>
              <Card className="mt-3 max-w-sm">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <h4 className="font-bold text-navy">{tutorBookWebsitePackage?.name ?? tutorBookAddon.name}</h4>
                    {(tutorBookWebsitePackage?.description ?? tutorBookAddon.description) ? (
                      <p className="mt-1 text-xs text-muted">
                        {tutorBookWebsitePackage?.description ?? tutorBookAddon.description}
                      </p>
                    ) : null}
                  </div>
                  <div className="shrink-0 text-end">
                    <div className="text-lg font-bold tabular-nums text-navy">£{tutorBookAddon.price.toFixed(0)}</div>
                    {tutorBookAddon.originalPrice != null && tutorBookAddon.originalPrice > tutorBookAddon.price && (
                      <div className="text-xs tabular-nums text-muted line-through">was £{tutorBookAddon.originalPrice.toFixed(0)}</div>
                    )}
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => setModalAddOn(tutorBookAddon)}
                  className={buttonClassName({ variant: 'outline', size: 'sm', fullWidth: true, className: 'mt-4 bg-background-light' })}
                >
                  <TagIcon className="h-3 w-3" aria-hidden="true" /> Add to order
                </button>
                <p className="mt-2 text-2xs text-muted">After payment, contact us on WhatsApp for manual delivery. No platform access is unlocked.</p>
              </Card>
            </div>
          )}
        </MotionSection>
      )}

      <AddonPurchaseModal
        open={modalAddOn !== null}
        addOnCode={modalAddOn?.code ?? null}
        addOnLabel={
          modalAddOn
            ? resolveWebsitePackageByCode(modalAddOn.code)?.name ?? modalAddOn.name
            : null
        }
        addOnPriceGbp={modalAddOn?.price ?? null}
        onClose={() => setModalAddOn(null)}
        checkoutPath="/checkout/review"
      />
    </>
  );
}

function AddonGroup({
  title,
  addons,
  onSelect,
}: {
  title: string;
  addons: PublicCatalogAddOnRow[];
  onSelect: (addon: PublicCatalogAddOnRow) => void;
}) {
  return (
    <div>
      <h3 className="eyebrow text-muted">{title}</h3>
      <div className="mt-3 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {addons.map((addon, index) => {
          const websitePackage = resolveWebsitePackageByCode(addon.code);
          return (
            <MotionItem key={addon.code} delayIndex={Math.min(index, 5)} className="h-full">
              <Card className="flex h-full flex-col">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <h4 className="font-bold text-navy">{websitePackage?.name ?? addon.name}</h4>
                    {(websitePackage?.description ?? addon.description) ? (
                      <p className="mt-1 text-xs text-muted">
                        {websitePackage?.description ?? addon.description}
                      </p>
                    ) : null}
                  </div>
                  <div className="shrink-0 text-end">
                    <div className="text-lg font-bold tabular-nums text-navy">£{addon.price.toFixed(0)}</div>
                    {addon.originalPrice !== null && addon.originalPrice !== undefined && addon.originalPrice > addon.price && (
                      <div className="text-xs tabular-nums text-muted line-through">was £{addon.originalPrice.toFixed(0)}</div>
                    )}
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => onSelect(addon)}
                  className={buttonClassName({ variant: 'outline', size: 'sm', fullWidth: true, className: 'mt-auto bg-background-light' })}
                  data-addon-code={addon.code}
                >
                  <TagIcon className="h-3 w-3" aria-hidden="true" /> Add to order
                </button>
              </Card>
            </MotionItem>
          );
        })}
      </div>
    </div>
  );
}

function SidebarBlock({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <Card padding="sm">
      <h4 className="eyebrow text-muted">{title}</h4>
      <p className="mt-1.5 text-sm text-navy">{children}</p>
    </Card>
  );
}

function HeroTag({ children, gold = false }: { children: React.ReactNode; gold?: boolean }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs ${
        gold ? 'bg-gold/20 text-gold' : 'bg-white/10 text-white/80'
      }`}
    >
      {children}
    </span>
  );
}

function labelForProfession(profession: string): string {
  return ({
    all: 'All disciplines',
    medicine: 'Medicine',
    nursing: 'Nursing',
    pharmacy: 'Pharmacy',
  } as Record<string, string>)[profession] ?? profession;
}

function formatAccess(days: number): string {
  // No package grants automatic access beyond 6 months (owner directive,
  // 2026-08-31) — clamp display the same way the backend clamps the grant.
  const capped = Math.min(days, 180);
  if (capped >= 30) return `${Math.round(capped / 30)} months`;
  return `${capped} days`;
}

function prettyModule(slug: string): string {
  return slug
    .replace(/([A-Z])/g, ' $1')
    .replace(/^./, (s) => s.toUpperCase())
    .trim();
}
