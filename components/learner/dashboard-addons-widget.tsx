'use client';

import { useEffect, useMemo, useState } from 'react';
import { ShoppingBag, Sparkles, Tag } from 'lucide-react';
import { fetchPublicCatalog } from '@/lib/api';
import type { PublicCatalogAddOnRow } from '@/lib/types/admin';
import { Card } from '@/components/ui/card';
import { AddonPurchaseModal } from '@/components/billing/addon-purchase-modal';

/** Human-readable labels for the raw eligibility flags stored on catalog rows. */
const ELIGIBILITY_LABELS: Record<string, string> = {
  writing_addons: 'Writing',
  speaking_addons: 'Speaking',
  tutor_book_discount: 'Tutor Book',
};

interface DashboardAddonsWidgetProps {
  /** Three eligibility flags from the buyer's active enrolment. */
  writingAddonsEnabled?: boolean;
  speakingAddonsEnabled?: boolean;
  tutorBookDiscountEnabled?: boolean;
}

/**
 * Dashboard add-on widget — surfaces the conditional add-on cards on the
 * learner dashboard when the active enrolment has any of the three OET 2026
 * eligibility flags set. Hides itself entirely when all three are false.
 */
export function DashboardAddonsWidget({
  writingAddonsEnabled = false,
  speakingAddonsEnabled = false,
  tutorBookDiscountEnabled = false,
}: DashboardAddonsWidgetProps) {
  const [addOns, setAddOns] = useState<PublicCatalogAddOnRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [modalCode, setModalCode] = useState<string | null>(null);
  const [modalLabel, setModalLabel] = useState<string | null>(null);
  const [modalPrice, setModalPrice] = useState<number | null>(null);

  const anyFlagOn = writingAddonsEnabled || speakingAddonsEnabled || tutorBookDiscountEnabled;

  useEffect(() => {
    if (!anyFlagOn) {
      setLoading(false);
      return;
    }
    void (async () => {
      try {
        const response = await fetchPublicCatalog();
        setAddOns(response.addOns ?? []);
      } catch {
        // Silent fail — widget hides
      } finally {
        setLoading(false);
      }
    })();
  }, [anyFlagOn]);

  const visibleAddOns = useMemo(() => {
    return addOns.filter((addon) => {
      if (addon.eligibilityFlag === 'writing_addons') return writingAddonsEnabled;
      if (addon.eligibilityFlag === 'speaking_addons') return speakingAddonsEnabled;
      if (addon.eligibilityFlag === 'tutor_book_discount') return tutorBookDiscountEnabled;
      return false;
    });
  }, [addOns, writingAddonsEnabled, speakingAddonsEnabled, tutorBookDiscountEnabled]);

  if (!anyFlagOn) return null;
  if (loading) {
    return (
      <Card padding="none" className="p-5">
        <div className="h-32 motion-safe:animate-pulse rounded bg-background-light" />
      </Card>
    );
  }
  if (visibleAddOns.length === 0) return null;

  return (
    <>
      <section className="rounded-2xl border border-gold/40 bg-gold/[0.06] p-5 shadow-sm sm:p-6 dark:border-gold/30 dark:bg-gold/[0.08]">
        <header className="flex items-center gap-2.5">
          <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-gold/15 text-gold-fg ring-1 ring-inset ring-gold/30">
            <Sparkles className="h-4 w-4" aria-hidden="true" />
          </span>
          <h3 className="text-sm font-bold uppercase tracking-wider text-gold-fg">Boost your enrolment</h3>
        </header>
        <p className="mt-2 text-sm text-muted">
          Available add-ons for your current course, applied directly to your dashboard.
        </p>

        <div className="mt-5 grid items-stretch gap-4 sm:grid-cols-2">
          {visibleAddOns.map((addon) => {
            const hasDiscount =
              addon.originalPrice !== null &&
              addon.originalPrice !== undefined &&
              addon.originalPrice > addon.price;
            const savings = hasDiscount ? addon.originalPrice! - addon.price : 0;
            return (
              <article
                key={addon.code}
                className="group flex h-full flex-col rounded-xl border border-gold/25 bg-surface p-4 shadow-sm transition-[border-color,box-shadow,transform] duration-200 ease-[var(--ease-spring)] hover:border-gold/50 hover:shadow-clinical hoverable:-translate-y-0.5 focus-within:border-gold/50"
              >
                <div className="flex items-start justify-between gap-3">
                  <h4 className="text-sm font-bold leading-snug text-navy">{addon.name}</h4>
                  <div className="shrink-0 text-right">
                    <div className="rounded-lg bg-gold/12 px-2.5 py-1 text-base font-extrabold leading-none text-gold-fg">
                      £{addon.price.toFixed(0)}
                    </div>
                    {hasDiscount && (
                      <div className="mt-1 text-[10px] leading-none text-muted line-through">
                        £{addon.originalPrice!.toFixed(0)}
                      </div>
                    )}
                  </div>
                </div>

                <div className="mt-2 flex flex-wrap items-center gap-1.5">
                  <span className="inline-flex items-center gap-1 rounded-full bg-gold/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-gold-fg">
                    <Tag className="h-2.5 w-2.5" aria-hidden="true" />
                    {ELIGIBILITY_LABELS[addon.eligibilityFlag] ?? addon.eligibilityFlag.replace(/_/g, ' ')}
                  </span>
                  {hasDiscount && savings > 0 && (
                    <span className="inline-flex items-center rounded-full bg-success/10 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider text-success">
                      Save £{savings.toFixed(0)}
                    </span>
                  )}
                </div>

                {addon.description && (
                  <p className="mt-2.5 text-xs leading-relaxed text-muted line-clamp-2">{addon.description}</p>
                )}

                <button
                  type="button"
                  onClick={() => {
                    setModalCode(addon.code);
                    setModalLabel(addon.name);
                    setModalPrice(addon.price);
                  }}
                  className="mt-4 inline-flex w-full items-center justify-center gap-1.5 rounded-lg bg-oet-navy px-3 py-2.5 text-xs font-bold text-white shadow-sm transition-colors hover:bg-oet-teal focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-gold/50 focus-visible:ring-offset-1"
                >
                  <ShoppingBag className="h-3.5 w-3.5" aria-hidden="true" /> Add to my course
                </button>
              </article>
            );
          })}
        </div>
      </section>

      <AddonPurchaseModal
        open={modalCode !== null}
        addOnCode={modalCode}
        addOnLabel={modalLabel}
        addOnPriceGbp={modalPrice}
        onClose={() => {
          setModalCode(null);
          setModalLabel(null);
          setModalPrice(null);
        }}
      />
    </>
  );
}
