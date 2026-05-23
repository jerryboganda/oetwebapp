'use client';

import { useEffect, useMemo, useState } from 'react';
import { ShoppingBag, Sparkles, Tag } from 'lucide-react';
import { fetchPublicCatalog } from '@/lib/api';
import type { PublicCatalogAddOnRow } from '@/lib/types/admin';
import { AddonPurchaseModal } from '@/components/billing/addon-purchase-modal';

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
      <section className="rounded-2xl border border-slate-200 bg-white p-5 shadow-sm dark:border-slate-800 dark:bg-slate-950">
        <div className="h-32 animate-pulse rounded bg-slate-100 dark:bg-slate-900" />
      </section>
    );
  }
  if (visibleAddOns.length === 0) return null;

  return (
    <>
      <section className="rounded-2xl border border-[#D4A44F]/40 bg-gradient-to-br from-[#D4A44F]/5 to-transparent p-5 shadow-sm dark:border-[#D4A44F]/30">
        <header className="flex items-center gap-2">
          <Sparkles className="h-4 w-4 text-[#996F1F]" />
          <h3 className="text-sm font-bold uppercase tracking-wider text-[#996F1F]">Boost your enrolment</h3>
        </header>
        <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
          Available add-ons for your current course — applied directly to your dashboard.
        </p>

        <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {visibleAddOns.map((addon) => (
            <article
              key={addon.code}
              className="rounded-xl border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900"
            >
              <div className="flex items-start justify-between gap-2">
                <div>
                  <h4 className="text-sm font-bold text-slate-900 dark:text-slate-50">{addon.name}</h4>
                  <p className="mt-1 text-[10px] uppercase tracking-wider text-slate-500 dark:text-slate-400">
                    <Tag className="mr-1 inline h-3 w-3" />
                    {addon.eligibilityFlag.replace(/_/g, ' ')}
                  </p>
                </div>
                <div className="text-right">
                  <div className="font-bold text-slate-900 dark:text-slate-50">£{addon.price.toFixed(0)}</div>
                  {addon.originalPrice !== null && addon.originalPrice !== undefined && addon.originalPrice > addon.price && (
                    <div className="text-[10px] text-slate-500 line-through dark:text-slate-500">£{addon.originalPrice.toFixed(0)}</div>
                  )}
                </div>
              </div>
              {addon.description && (
                <p className="mt-2 text-xs text-slate-600 dark:text-slate-400 line-clamp-2">{addon.description}</p>
              )}
              <button
                type="button"
                onClick={() => {
                  setModalCode(addon.code);
                  setModalLabel(addon.name);
                  setModalPrice(addon.price);
                }}
                className="mt-3 inline-flex w-full items-center justify-center gap-1.5 rounded-lg bg-[#0E2841] px-3 py-2 text-xs font-bold text-white transition-colors hover:bg-[#156082]"
              >
                <ShoppingBag className="h-3 w-3" /> Add to my course
              </button>
            </article>
          ))}
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
