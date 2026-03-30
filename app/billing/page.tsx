'use client';

import { useEffect, useMemo, useState } from 'react';
import { motion } from 'motion/react';
import {
  ArrowDownCircle,
  ArrowUpCircle,
  CheckCircle2,
  CreditCard,
  Download,
  FileText,
  Receipt,
  ShoppingCart,
  Sparkles,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import {
  createBillingCheckoutSession,
  downloadInvoice,
  fetchBilling,
  fetchBillingChangePreview,
} from '@/lib/api';
import type { BillingChangePreview, BillingData } from '@/lib/mock-data';

export default function BillingPage() {
  const [data, setData] = useState<BillingData | null>(null);
  const [preview, setPreview] = useState<BillingChangePreview | null>(null);
  const [previewPlanId, setPreviewPlanId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { page: 'billing' });
    fetchBilling()
      .then((result) => setData(result))
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load billing data.'))
      .finally(() => setLoading(false));
  }, []);

  const upgradePlans = useMemo(
    () => (data?.plans ?? []).filter((plan) => plan.changeDirection === 'upgrade'),
    [data],
  );
  const downgradePlans = useMemo(
    () => (data?.plans ?? []).filter((plan) => plan.changeDirection === 'downgrade'),
    [data],
  );

  const openCheckout = async (productType: 'review_credits' | 'plan_upgrade' | 'plan_downgrade', quantity: number, priceId?: string | null) => {
    setBusyKey(`${productType}:${priceId ?? quantity}`);
    setError(null);
    setSuccess(null);
    try {
      const response = await createBillingCheckoutSession({ productType, quantity, priceId });
      window.open(response.checkoutUrl, '_blank', 'noopener,noreferrer');
      setSuccess('Checkout session created. Finish payment in the new tab.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not start checkout.');
    } finally {
      setBusyKey(null);
    }
  };

  const loadPreview = async (planId: string) => {
    setBusyKey(`preview:${planId}`);
    setError(null);
    setSuccess(null);
    try {
      const result = await fetchBillingChangePreview(planId);
      setPreview(result);
      setPreviewPlanId(planId);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load the plan-change preview.');
    } finally {
      setBusyKey(null);
    }
  };

  const handleDownloadInvoice = async (invoiceId: string) => {
    setBusyKey(`invoice:${invoiceId}`);
    setError(null);
    setSuccess(null);
    try {
      const objectUrl = await downloadInvoice(invoiceId);
      const anchor = document.createElement('a');
      anchor.href = objectUrl;
      anchor.download = `${invoiceId}.txt`;
      anchor.click();
      URL.revokeObjectURL(objectUrl);
      setSuccess('Invoice download started.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not download that invoice.');
    } finally {
      setBusyKey(null);
    }
  };

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Billing" backHref="/">
        <div className="space-y-6">
          {[1, 2, 3].map((item) => <Skeleton key={item} className="h-40 rounded-[24px]" />)}
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!data) {
    return (
      <LearnerDashboardShell pageTitle="Billing" backHref="/">
        <div>
          <InlineAlert variant="error">{error ?? 'Billing data could not be loaded.'}</InlineAlert>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Billing" subtitle="Manage plans, credits, invoices, and learner entitlements." backHref="/">
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Billing Focus"
          icon={CreditCard}
          accent="navy"
          title="See what changes before you spend or switch plans"
          description="Use this page to review entitlements, pricing changes, and credit impact before you open checkout."
          highlights={[
            { icon: CreditCard, label: 'Current plan', value: data.currentPlan },
            { icon: Sparkles, label: 'Review credits', value: `${data.reviewCredits} available` },
            { icon: Receipt, label: 'Next renewal', value: new Date(data.nextRenewal).toLocaleDateString() },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {success ? <InlineAlert variant="success">{success}</InlineAlert> : null}

        <section className="grid gap-6 lg:grid-cols-[1.3fr_0.9fr]">
          <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} className="rounded-[32px] border border-gray-200 bg-surface p-6 shadow-sm">
            <div className="flex flex-wrap items-start justify-between gap-4">
              <div>
                <p className="text-xs font-black uppercase tracking-widest text-muted">Current Plan</p>
                <h2 className="mt-2 text-2xl font-black text-navy">{data.currentPlan}</h2>
                <p className="mt-1 text-sm text-muted">{data.price} / {data.interval}</p>
              </div>
              <span className="inline-flex items-center gap-2 rounded-full bg-emerald-50 px-3 py-1 text-xs font-black uppercase tracking-widest text-emerald-700">
                <CheckCircle2 className="h-4 w-4" />
                {data.status}
              </span>
            </div>

            <div className="mt-6 grid gap-4 sm:grid-cols-3">
              <div className="rounded-2xl border border-gray-100 bg-background-light p-4">
                <p className="text-xs font-black uppercase tracking-widest text-muted">Next Renewal</p>
                <p className="mt-2 text-sm font-bold text-navy">{new Date(data.nextRenewal).toLocaleDateString()}</p>
              </div>
              <div className="rounded-2xl border border-gray-100 bg-background-light p-4">
                <p className="text-xs font-black uppercase tracking-widest text-muted">Review Coverage</p>
                <p className="mt-2 text-sm font-bold text-navy">{data.entitlements.supportedReviewSubtests.join(' + ') || 'None'}</p>
              </div>
              <div className="rounded-2xl border border-gray-100 bg-background-light p-4">
                <p className="text-xs font-black uppercase tracking-widest text-muted">Invoices</p>
                <p className="mt-2 text-sm font-bold text-navy">{data.entitlements.invoiceDownloadsAvailable ? 'Downloads available' : 'Unavailable'}</p>
              </div>
            </div>
          </motion.div>

          <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: 0.04 }} className="rounded-[32px] border border-indigo-100 bg-indigo-50 p-6 shadow-sm">
            <div className="flex items-start gap-4">
              <div className="rounded-2xl bg-indigo-600 p-3 text-white shadow-sm">
                <Sparkles className="h-5 w-5" />
              </div>
              <div>
                <p className="text-xs font-black uppercase tracking-widest text-indigo-700/70">Productive Skill Reviews</p>
                <h2 className="mt-2 text-xl font-black text-indigo-950">Entitlements stay aligned with the OET business model</h2>
                <p className="mt-2 text-sm leading-6 text-indigo-900/80">
                  Human review is available only for Writing and Speaking. Reading and Listening stay AI-evaluated and transcript-backed.
                </p>
              </div>
            </div>
          </motion.div>
        </section>

        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Plans"
            title="Preview a plan change before you commit"
            description="Upgrade and downgrade actions should explain the proration, included review capacity, and effective date before checkout opens."
            className="mb-4"
          />

          <div className="grid gap-4 lg:grid-cols-3">
            {data.plans.map((plan, index) => {
              const isCurrent = plan.changeDirection === 'current';
              const isPreviewing = previewPlanId === plan.id && preview;
              return (
                <motion.div
                  key={plan.id}
                  initial={{ opacity: 0, y: 16 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: index * 0.05 }}
                  className={`rounded-[28px] border p-5 shadow-sm ${isCurrent ? 'border-navy bg-navy text-white' : 'border-gray-200 bg-surface'}`}
                >
                  <div className="flex items-start justify-between gap-4">
                    <div>
                      <p className={`text-xs font-black uppercase tracking-widest ${isCurrent ? 'text-white/70' : 'text-muted'}`}>{plan.badge || plan.tier}</p>
                      <h3 className={`mt-2 text-xl font-black ${isCurrent ? 'text-white' : 'text-navy'}`}>{plan.label}</h3>
                      <p className={`mt-2 text-sm leading-6 ${isCurrent ? 'text-white/80' : 'text-muted'}`}>{plan.description}</p>
                    </div>
                    {plan.changeDirection === 'upgrade' ? <ArrowUpCircle className="h-5 w-5 text-emerald-500" /> : null}
                    {plan.changeDirection === 'downgrade' ? <ArrowDownCircle className="h-5 w-5 text-amber-500" /> : null}
                  </div>

                  <div className="mt-5 rounded-2xl border border-white/10 bg-white/5 p-4 text-sm">
                    <p className={`font-black ${isCurrent ? 'text-white' : 'text-navy'}`}>{plan.price} / {plan.interval}</p>
                    <p className={`mt-1 ${isCurrent ? 'text-white/70' : 'text-muted'}`}>{plan.reviewCredits} review credits included</p>
                  </div>

                  {!isCurrent ? (
                    <div className="mt-5 space-y-3">
                      <Button
                        variant="outline"
                        fullWidth
                        loading={busyKey === `preview:${plan.id}`}
                        onClick={() => loadPreview(plan.id)}
                      >
                        Preview {plan.changeDirection}
                      </Button>

                      {isPreviewing ? (
                        <div className="rounded-2xl border border-gray-200 bg-background-light p-4 text-sm text-muted">
                          <p className="font-bold text-navy">{preview.summary}</p>
                          <p className="mt-2">Prorated charge: {preview.proratedAmount}</p>
                          <p className="mt-1">Effective date: {new Date(preview.effectiveAt).toLocaleDateString()}</p>
                          <Button
                            className="mt-4"
                            fullWidth
                            loading={busyKey === `${plan.changeDirection}:${plan.id}`}
                            onClick={() => openCheckout(plan.changeDirection === 'upgrade' ? 'plan_upgrade' : 'plan_downgrade', 1, plan.id)}
                          >
                            Continue to checkout
                          </Button>
                        </div>
                      ) : null}
                    </div>
                  ) : null}
                </motion.div>
              );
            })}
          </div>
        </section>

        <section className="grid gap-6 lg:grid-cols-[1fr_1.2fr]">
          <div>
            <LearnerSurfaceSectionHeader
              eyebrow="Extras"
              title="Purchase review credits with clear product boundaries"
              description="Extras only increase productive-skill review capacity. They do not affect Reading or Listening scoring."
              className="mb-4"
            />

            <div className="space-y-4">
              {data.extras.map((extra) => (
                <div key={extra.id} className="rounded-[24px] border border-gray-200 bg-surface p-5 shadow-sm">
                  <div className="flex items-start justify-between gap-4">
                    <div>
                      <p className="text-xs font-black uppercase tracking-widest text-muted">Review credit pack</p>
                      <h3 className="mt-2 text-lg font-black text-navy">{extra.quantity} review credits</h3>
                      <p className="mt-2 text-sm text-muted">{extra.description}</p>
                    </div>
                    <div className="rounded-2xl bg-background-light px-4 py-3 text-sm font-black text-navy">{extra.price}</div>
                  </div>
                  <Button
                    className="mt-4"
                    fullWidth
                    loading={busyKey === `review_credits:${extra.quantity}`}
                    onClick={() => openCheckout('review_credits', extra.quantity)}
                  >
                    <ShoppingCart className="h-4 w-4" />
                    Purchase extras
                  </Button>
                </div>
              ))}
            </div>
          </div>

          <div>
            <LearnerSurfaceSectionHeader
              eyebrow="Invoices"
              title="Keep billing evidence downloadable"
              description="Each invoice should be visible, dated, and immediately downloadable without a dead button."
              className="mb-4"
            />

            <div className="overflow-hidden rounded-[28px] border border-gray-200 bg-surface shadow-sm">
              {data.invoices.length === 0 ? (
                <div className="p-8 text-center text-sm text-muted">No invoices yet. Billing history will appear here after the first paid plan or credit purchase.</div>
              ) : (
                <div className="divide-y divide-gray-100">
                  {data.invoices.map((invoice) => (
                    <div key={invoice.id} className="flex flex-col gap-4 p-5 sm:flex-row sm:items-center sm:justify-between">
                      <div className="flex items-center gap-4">
                        <div className="rounded-2xl bg-background-light p-3 text-muted">
                          <FileText className="h-5 w-5" />
                        </div>
                        <div>
                          <p className="text-sm font-black text-navy">{new Date(invoice.date).toLocaleDateString()}</p>
                          <p className="text-sm text-muted">{invoice.amount} · {invoice.id}</p>
                        </div>
                      </div>
                      <div className="flex items-center gap-3">
                        <span className="rounded-full bg-emerald-50 px-3 py-1 text-xs font-black uppercase tracking-widest text-emerald-700">{invoice.status}</span>
                        <Button
                          variant="outline"
                          loading={busyKey === `invoice:${invoice.id}`}
                          onClick={() => handleDownloadInvoice(invoice.id)}
                        >
                          <Download className="h-4 w-4" />
                          Download invoice
                        </Button>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        </section>

        {(upgradePlans.length === 0 && downgradePlans.length === 0) ? (
          <div className="rounded-[24px] border border-gray-200 bg-surface p-5 text-sm text-muted shadow-sm">
            Plan changes are not available for the current learner subscription state yet.
          </div>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
