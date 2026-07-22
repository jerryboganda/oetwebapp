'use client';

import { useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, ArrowRight, CheckCircle2, Clock, MessageCircleQuestion, Sparkles, Tag as TagIcon } from 'lucide-react';
import { fetchPublicCatalog } from '@/lib/api';
import type { PublicCatalogPlanRow, PublicCatalogAddOnRow } from '@/lib/types/admin';
import { AddonPurchaseModal } from '@/components/billing/addon-purchase-modal';

export default function PackageDetailPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const rawId = params?.id;
  const code = typeof rawId === 'string' ? decodeURIComponent(rawId) : '';

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

  const plan = useMemo(() => plans.find((p) => p.code === code), [plans, code]);

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

  if (loading) {
    return <div className="min-h-screen bg-background-light p-12"><div className="mx-auto h-96 max-w-5xl animate-pulse rounded-2xl bg-surface" /></div>;
  }
  if (error || !plan) {
    return (
      <div className="min-h-screen bg-background-light p-12">
        <div className="mx-auto max-w-2xl rounded-2xl border border-border bg-surface p-8 text-center">
          <h1 className="text-xl font-bold text-navy">Package not available</h1>
          <p className="mt-2 text-sm text-muted">{error ?? 'This product is not currently published. Please check back soon.'}</p>
          <Link href="/catalog" className="mt-6 inline-flex items-center gap-2 text-sm font-medium text-primary">
            <ArrowLeft className="h-4 w-4" /> Back to the catalogue
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background-light text-navy">
      {/* Hero */}
      <section className="bg-navy px-4 py-16 text-white">
        <div className="mx-auto max-w-5xl">
          <Link href="/catalog" className="inline-flex items-center gap-1 text-xs text-white/70 hover:text-white">
            <ArrowLeft className="h-3 w-3" /> All packages
          </Link>
          <div className="mt-3 flex flex-wrap items-start justify-between gap-6">
            <div className="flex-1 min-w-[260px]">
              <p className="text-xs font-semibold uppercase tracking-[0.3em] text-[#D4A44F]">{plan.productCategory.replace(/_/g, ' ')}</p>
              <h1 className="mt-2 text-3xl font-bold sm:text-4xl">{plan.name}</h1>
              {plan.description && <p className="mt-3 max-w-2xl text-sm text-white/80">{plan.description}</p>}
              <div className="mt-4 flex flex-wrap gap-2 text-xs">
                <HeroTag>{labelForProfession(plan.profession)}</HeroTag>
                <HeroTag><Clock className="mr-1 h-3 w-3" /> {formatAccess(plan.accessDurationDays)} access</HeroTag>
                {plan.writingAddonsEnabled && <HeroTag gold>W add-ons</HeroTag>}
                {plan.speakingAddonsEnabled && <HeroTag gold>S add-ons</HeroTag>}
                {plan.tutorBookDiscountEnabled && <HeroTag gold>Tutor Book £32</HeroTag>}
              </div>
            </div>
            <div className="rounded-2xl border border-white/15 bg-white/10 p-6 text-right">
              <div className="text-4xl font-bold">£{plan.price.toFixed(0)}</div>
              {plan.originalPrice !== null && plan.originalPrice !== undefined && plan.originalPrice > plan.price && (
                <div className="mt-1 text-sm text-white/70 line-through">was £{plan.originalPrice.toFixed(0)}</div>
              )}
              {plan.code === 'tutor-book' ? (
                <div className="mt-4 flex flex-col items-center gap-1 rounded-lg border border-dashed border-white/30 bg-white/5 px-5 py-2.5 text-center">
                  <span className="inline-flex items-center gap-2 text-sm font-bold text-white">
                    <MessageCircleQuestion className="h-4 w-4" /> Contact admin to enable
                  </span>
                  <span className="text-[10px] text-white/60">Manual access only — not sold through self-checkout</span>
                </div>
              ) : (
                <>
                  <button
                    type="button"
                    onClick={() => router.push(`/checkout/review?productType=plan_purchase&priceId=${encodeURIComponent(plan.code)}&quantity=1`)}
                    className="mt-4 inline-flex w-full items-center justify-center gap-2 rounded-lg bg-[#D4A44F] px-5 py-2.5 text-sm font-bold text-[#0E2841] shadow-sm transition-colors hover:bg-[#bf8e3d]"
                  >
                    Buy for £{plan.price.toFixed(0)} <ArrowRight className="h-4 w-4" />
                  </button>
                  <p className="mt-2 text-[10px] text-white/60">Charged in GBP. No auto-renewal.</p>
                </>
              )}
            </div>
          </div>
        </div>
      </section>

      {/* What's included + bonuses */}
      <section className="px-4 py-14">
        <div className="mx-auto grid max-w-5xl gap-10 lg:grid-cols-3">
          <div className="lg:col-span-2">
            <h2 className="text-xl font-bold">What you&apos;ll get</h2>
            <ul className="mt-4 space-y-2">
              {plan.dashboardModules.map((module) => (
                <li key={module} className="flex items-start gap-2 text-sm">
                  <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
                  <span>{prettyModule(module)}</span>
                </li>
              ))}
            </ul>

            {(plan.bundledWritingAssessments > 0 ||
              plan.bundledSpeakingSessions > 0 ||
              plan.bundledAiCredits > 0 ||
              plan.bundledTutorBook ||
              plan.bundledBasicEnglish) && (
              <div className="mt-8 rounded-2xl border border-[#D4A44F]/40 bg-[#D4A44F]/5 p-5">
                <h3 className="flex items-center gap-2 text-sm font-bold text-[#996F1F]">
                  <Sparkles className="h-4 w-4" /> Bonuses included with this package
                </h3>
                <ul className="mt-2 space-y-1.5 text-sm">
                  {plan.bundledWritingAssessments > 0 && (
                    <li>· {plan.bundledWritingAssessments} writing letter assessment{plan.bundledWritingAssessments === 1 ? '' : 's'}</li>
                  )}
                  {plan.bundledSpeakingSessions > 0 && (
                    <li>· {plan.bundledSpeakingSessions} private speaking session{plan.bundledSpeakingSessions === 1 ? '' : 's'}</li>
                  )}
                  {plan.bundledAiCredits > 0 && <li>· {plan.bundledAiCredits} AI practice credits</li>}
                  {plan.bundledTutorBook && <li>· The Tutor Book, First Edition 2026 + private Telegram channel</li>}
                  {plan.bundledBasicEnglish && <li>· Basic English foundation course (11+ hours)</li>}
                </ul>
              </div>
            )}
          </div>

          <aside className="space-y-4 text-sm">
            <SidebarBlock title="Access window">
              {formatAccess(plan.accessDurationDays)} from purchase.
            </SidebarBlock>
            <SidebarBlock title="Profession">{labelForProfession(plan.profession)}</SidebarBlock>
            <SidebarBlock title="Format">Recorded video + materials. Assessments are reviewed by Dr Ahmed (48-72h turnaround, Friday off).</SidebarBlock>
          </aside>
        </div>
      </section>

      {/* Add-ons — conditional rendering per 3 flags */}
      {(writingAddons.length > 0 || speakingAddons.length > 0 || tutorBookAddon) && (
        <section className="border-t border-border bg-surface px-4 py-14">
          <div className="mx-auto max-w-5xl">
            <h2 className="text-xl font-bold">Available add-ons</h2>
            <p className="mt-1 text-sm text-muted">Add these alongside this package, applied automatically to your enrolment.</p>

            {writingAddons.length > 0 && (
              <AddonGroup title="Writing letter assessments" addons={writingAddons} onSelect={setModalAddOn} />
            )}
            {speakingAddons.length > 0 && (
              <AddonGroup title="Extra private speaking sessions" addons={speakingAddons} onSelect={setModalAddOn} />
            )}
            {tutorBookAddon && (
              <div className="mt-6">
                <h3 className="text-sm font-semibold uppercase tracking-wider text-muted">
                  The Tutor Book (£32, discount for enrolled students)
                </h3>
                <div className="mt-3 max-w-sm rounded-2xl border border-border bg-surface p-5">
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <h4 className="font-bold">{tutorBookAddon.name}</h4>
                      {tutorBookAddon.description && <p className="mt-1 text-xs text-muted">{tutorBookAddon.description}</p>}
                    </div>
                    <div className="text-right">
                      <div className="text-lg font-bold">£{tutorBookAddon.price.toFixed(0)}</div>
                      {tutorBookAddon.originalPrice != null && tutorBookAddon.originalPrice > tutorBookAddon.price && (
                        <div className="text-xs text-muted line-through">was £{tutorBookAddon.originalPrice.toFixed(0)}</div>
                      )}
                    </div>
                  </div>
                  <div className="mt-4 flex flex-col items-center gap-1 rounded-lg border border-dashed border-border bg-background-light px-3 py-2 text-center">
                    <span className="inline-flex items-center gap-1.5 text-xs font-semibold text-muted">
                      <MessageCircleQuestion className="h-3.5 w-3.5" /> Contact admin to enable
                    </span>
                    <span className="text-[10px] text-muted">Manual access only — not sold through self-checkout</span>
                  </div>
                </div>
              </div>
            )}
          </div>
        </section>
      )}

      <AddonPurchaseModal
        open={modalAddOn !== null}
        addOnCode={modalAddOn?.code ?? null}
        addOnLabel={modalAddOn?.name ?? null}
        addOnPriceGbp={modalAddOn?.price ?? null}
        onClose={() => setModalAddOn(null)}
        checkoutPath="/checkout/review"
      />
    </div>
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
    <div className="mt-6">
      <h3 className="text-sm font-semibold uppercase tracking-wider text-muted">{title}</h3>
      <div className="mt-3 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {addons.map((addon) => (
          <div key={addon.code} className="rounded-2xl border border-border bg-surface p-5">
            <div className="flex items-start justify-between gap-3">
              <div>
                <h4 className="font-bold">{addon.name}</h4>
                {addon.description && <p className="mt-1 text-xs text-muted">{addon.description}</p>}
              </div>
              <div className="text-right">
                <div className="text-lg font-bold">£{addon.price.toFixed(0)}</div>
                {addon.originalPrice !== null && addon.originalPrice !== undefined && addon.originalPrice > addon.price && (
                  <div className="text-xs text-muted line-through">was £{addon.originalPrice.toFixed(0)}</div>
                )}
              </div>
            </div>
            <button
              type="button"
              onClick={() => onSelect(addon)}
              className="mt-4 inline-flex w-full items-center justify-center gap-1.5 rounded-lg border border-border bg-background-light px-3 py-2 text-xs font-medium text-navy transition-colors hover:bg-surface"
              data-addon-code={addon.code}
            >
              <TagIcon className="h-3 w-3" /> Add to order
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}

function SidebarBlock({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="rounded-2xl border border-border bg-surface p-4">
      <h4 className="text-xs font-semibold uppercase tracking-wider text-muted">{title}</h4>
      <p className="mt-1.5 text-sm">{children}</p>
    </div>
  );
}

function HeroTag({ children, gold = false }: { children: React.ReactNode; gold?: boolean }) {
  return (
    <span
      className={`inline-flex items-center rounded-full px-2.5 py-1 text-xs ${
        gold ? 'bg-[#D4A44F]/20 text-[#FFE9BD]' : 'bg-white/10 text-white/80'
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
  if (days >= 9000) return 'Permanent';
  if (days >= 365) return `${Math.round(days / 365)} year${days >= 730 ? 's' : ''}`;
  if (days >= 30) return `${Math.round(days / 30)} months`;
  return `${days} days`;
}

function prettyModule(slug: string): string {
  return slug
    .replace(/([A-Z])/g, ' $1')
    .replace(/^./, (s) => s.toUpperCase())
    .trim();
}
