'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { AlertCircle, ArrowRight, Check, Loader2, ShoppingBag } from 'lucide-react';
import { quoteAddonEligibility } from '@/lib/api';
import type { AddonQuoteResponse, AddonEligibleParent } from '@/lib/types/admin';

type Status = 'idle' | 'loading' | 'eligible' | 'ineligible' | 'error';

export interface AddonPurchaseModalProps {
  open: boolean;
  addOnCode: string | null;
  addOnLabel?: string | null;
  addOnPriceGbp?: number | null;
  onClose: () => void;
  /** Optional override for the checkout entry point. Defaults to /billing/checkout */
  checkoutPath?: string;
}

/**
 * Add-on purchase modal — calls `/v1/billing/quote/addon` to resolve eligibility,
 * then either redirects to the cheapest upsell plan (ineligible), pushes straight
 * to checkout (single eligible parent), or asks the buyer to pick a parent
 * (multiple eligible parents).
 */
export function AddonPurchaseModal({
  open,
  addOnCode,
  addOnLabel,
  addOnPriceGbp,
  onClose,
  checkoutPath = '/billing/checkout',
}: AddonPurchaseModalProps) {
  const router = useRouter();
  const [status, setStatus] = useState<Status>('idle');
  const [quote, setQuote] = useState<AddonQuoteResponse | null>(null);
  const [selectedParent, setSelectedParent] = useState<string | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!open || !addOnCode) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setStatus('loading');
    setQuote(null);
    setSelectedParent(null);
    setErrorMessage(null);
    void (async () => {
      try {
        const response = await quoteAddonEligibility(addOnCode);
        setQuote(response);
        if (!response.eligible) {
          setStatus('ineligible');
        } else if (response.eligibleParents.length <= 1) {
          setSelectedParent(response.eligibleParents[0]?.subscriptionId ?? null);
          setStatus('eligible');
        } else {
          setStatus('eligible');
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : 'Unable to verify eligibility.';
        setErrorMessage(message);
        setStatus('error');
      }
    })();
  }, [open, addOnCode]);

  if (!open) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      onClick={onClose}
    >
      <div
        className="w-full max-w-lg rounded-2xl bg-white p-6 shadow-2xl dark:bg-slate-900"
        onClick={(e) => e.stopPropagation()}
      >
        <header className="flex items-start justify-between gap-3">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-[#996F1F]">Add-on</p>
            <h2 className="text-xl font-bold text-slate-900 dark:text-slate-50">
              {addOnLabel ?? addOnCode ?? 'Add-on purchase'}
            </h2>
            {typeof addOnPriceGbp === 'number' && (
              <p className="mt-1 text-2xl font-bold text-slate-900 dark:text-slate-50">£{addOnPriceGbp.toFixed(0)}</p>
            )}
          </div>
          <button
            type="button"
            onClick={onClose}
            className="text-slate-400 hover:text-slate-700 dark:hover:text-slate-200"
            aria-label="Close"
          >
            ✕
          </button>
        </header>

        <div className="mt-5 min-h-[160px]">
          {status === 'loading' && (
            <div className="flex items-center gap-3 text-sm text-slate-600 dark:text-slate-300">
              <Loader2 className="h-4 w-4 animate-spin" /> Checking eligibility…
            </div>
          )}

          {status === 'error' && (
            <div className="rounded-lg border border-rose-200 bg-rose-50 p-4 text-sm text-rose-800 dark:border-rose-900/40 dark:bg-rose-900/20 dark:text-rose-200">
              {errorMessage ?? 'Unable to verify eligibility.'}
            </div>
          )}

          {status === 'ineligible' && quote && (
            <div className="space-y-4">
              <div className="flex items-start gap-3 rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900 dark:border-amber-900/40 dark:bg-amber-900/15 dark:text-amber-200">
                <AlertCircle className="mt-0.5 h-4 w-4 flex-none" />
                <div>
                  <p className="font-medium">You need an eligible course first.</p>
                  <p className="mt-1 text-xs">
                    This add-on requires a parent enrolment with{' '}
                    <code className="rounded bg-amber-100 px-1 dark:bg-amber-900/40">{quote.requiredFlag ?? 'required flag'}</code> set.
                  </p>
                </div>
              </div>
              {quote.redirectSku && (
                <button
                  type="button"
                  onClick={() => {
                    router.push(`/marketplace/packages/${encodeURIComponent(quote.redirectSku!)}`);
                    onClose();
                  }}
                  className="inline-flex w-full items-center justify-center gap-2 rounded-lg bg-[#D4A44F] px-4 py-2.5 text-sm font-bold text-[#0E2841] transition-colors hover:bg-[#bf8e3d]"
                >
                  View the eligible course <ArrowRight className="h-4 w-4" />
                </button>
              )}
            </div>
          )}

          {status === 'eligible' && quote && (
            <div className="space-y-4">
              {quote.eligibleParents.length > 1 ? (
                <>
                  <p className="text-sm text-slate-600 dark:text-slate-300">
                    Which enrolment should this add-on apply to?
                  </p>
                  <ul className="space-y-2">
                    {quote.eligibleParents.map((parent) => (
                      <ParentRow
                        key={parent.subscriptionId}
                        parent={parent}
                        selected={selectedParent === parent.subscriptionId}
                        onSelect={() => setSelectedParent(parent.subscriptionId)}
                      />
                    ))}
                  </ul>
                </>
              ) : quote.eligibleParents.length === 1 ? (
                <div className="rounded-lg border border-emerald-200 bg-emerald-50 p-4 text-sm dark:border-emerald-900/40 dark:bg-emerald-900/15">
                  <div className="flex items-center gap-2 font-medium text-emerald-900 dark:text-emerald-200">
                    <Check className="h-4 w-4" /> Will apply to:
                  </div>
                  <div className="mt-1 text-sm text-emerald-800 dark:text-emerald-300">
                    {quote.eligibleParents[0].planName}
                  </div>
                </div>
              ) : (
                <p className="text-sm text-slate-600 dark:text-slate-300">No parent selector available.</p>
              )}

              <button
                type="button"
                disabled={!selectedParent}
                onClick={() => {
                  const query = new URLSearchParams({
                    addOn: addOnCode ?? '',
                    parent: selectedParent ?? '',
                  });
                  router.push(`${checkoutPath}?${query.toString()}`);
                  onClose();
                }}
                className="inline-flex w-full items-center justify-center gap-2 rounded-lg bg-[#0E2841] px-4 py-2.5 text-sm font-bold text-white transition-colors hover:bg-[#156082] disabled:cursor-not-allowed disabled:opacity-60"
              >
                <ShoppingBag className="h-4 w-4" /> Continue to checkout
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function ParentRow({
  parent,
  selected,
  onSelect,
}: {
  parent: AddonEligibleParent;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <li>
      <button
        type="button"
        onClick={onSelect}
        className={`flex w-full items-center justify-between gap-3 rounded-lg border px-3 py-2.5 text-left text-sm transition-colors ${
          selected
            ? 'border-[#D4A44F] bg-[#D4A44F]/10 text-slate-900 dark:text-slate-100'
            : 'border-slate-200 bg-white text-slate-700 hover:border-slate-300 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-300'
        }`}
      >
        <div>
          <div className="font-medium">{parent.planName}</div>
          {parent.expiresAt && (
            <div className="text-xs text-slate-500 dark:text-slate-400">
              Access until {new Date(parent.expiresAt).toLocaleDateString()}
            </div>
          )}
        </div>
        {selected && <Check className="h-4 w-4 flex-none text-[#996F1F]" />}
      </button>
    </li>
  );
}
