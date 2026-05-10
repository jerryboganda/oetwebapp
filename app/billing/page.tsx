'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import {
  ArrowDownCircle,
  ArrowUpCircle,
  Calendar,
  CheckCircle2,
  Clock,
  CreditCard,
  Download,
  FileText,
  LayoutDashboard,
  Layers,
  Receipt,
  ShieldCheck,
  ShoppingCart,
  Sparkles,
  Wallet,
  X,
} from 'lucide-react';
import { useSearchParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Input } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import {
  fetchFreezeStatus,
  createBillingCheckoutSession,
  createWalletTopUp,
  downloadInvoice,
  fetchBilling,
  fetchBillingChangePreview,
  fetchBillingQuote,
  fetchWalletTopUpTiers,
  fetchWalletTransactions,
} from '@/lib/api';
import type { WalletTopUpTier } from '@/lib/api';
import type {
  BillingChangePreview,
  BillingData,
  BillingQuote,
  BillingProductType,
  WalletData,
  WalletTransactionDto,
} from '@/lib/billing-types';
import type { LearnerFreezeStatus } from '@/lib/types/freeze';
import { formatMoney } from '@/lib/money';
import { isFreezeEffective, maskProviderId } from '@/components/domain/billing';
import { openCheckoutUrl } from '@/lib/mobile/web-checkout';

// ─── Helpers ─────────────────────────────────────────────────────────

function formatCurrency(amount: number, currency = 'AUD') {
  return formatMoney(amount, { currency });
}

function formatOptionalDate(value?: string | null) {
  if (!value) return 'Not scheduled';
  const time = new Date(value).getTime();
  return Number.isNaN(time) ? 'Not scheduled' : new Date(time).toLocaleDateString();
}

function prettyProductType(productType: BillingProductType) {
  return productType.replace(/_/g, ' ');
}

function billingAddOnCheckoutProductType(productType: string): BillingProductType {
  return productType === 'review_credits' ? 'review_credits' : 'addon_purchase';
}

function getPaymentBanner(payment: string | null, gateway: string | null) {
  if (!payment) return null;
  const gatewayLabel = gateway ? gateway.charAt(0).toUpperCase() + gateway.slice(1) : 'payment';
  switch (payment) {
    case 'success':
      return { variant: 'success' as const, message: `Your ${gatewayLabel} checkout completed. We'll refresh your subscription once the webhook arrives.` };
    case 'cancelled':
      return { variant: 'warning' as const, message: `Your ${gatewayLabel} checkout was cancelled before payment. Nothing changed on your subscription.` };
    case 'failed':
    case 'expired':
      return { variant: 'error' as const, message: `The ${gatewayLabel} checkout ${payment} before completion. Please start a new validated quote.` };
    default:
      return { variant: 'info' as const, message: `Checkout status: ${payment}.` };
  }
}

type BillingTabId = 'overview' | 'plans' | 'credits' | 'invoices';

const BILLING_TABS: Array<{ id: BillingTabId; label: string; icon: React.ReactNode }> = [
  { id: 'overview', label: 'Overview', icon: <LayoutDashboard className="h-4 w-4" /> },
  { id: 'plans', label: 'Plans', icon: <Layers className="h-4 w-4" /> },
  { id: 'credits', label: 'Credits & Add-ons', icon: <Sparkles className="h-4 w-4" /> },
  { id: 'invoices', label: 'Invoices', icon: <Receipt className="h-4 w-4" /> },
];

// ─── Component ───────────────────────────────────────────────────────

export default function BillingPage() {
  const searchParams = useSearchParams();
  const paymentStatus = searchParams?.get('payment') ?? null;
  const paymentGateway = searchParams?.get('gateway') ?? null;
  const requestedPlanId = searchParams?.get('planId') ?? null;
  const initialTab = (searchParams?.get('tab') as BillingTabId | null) ?? 'overview';

  const [activeTab, setActiveTab] = useState<BillingTabId>(
    BILLING_TABS.some((t) => t.id === initialTab) ? initialTab : 'overview',
  );

  const [data, setData] = useState<BillingData | null>(null);
  const [preview, setPreview] = useState<BillingChangePreview | null>(null);
  const [previewPlanId, setPreviewPlanId] = useState<string | null>(null);
  const [quote, setQuote] = useState<BillingQuote | null>(null);
  const [quoteLabel, setQuoteLabel] = useState<string | null>(null);
  const [couponCode, setCouponCode] = useState('');
  const [loading, setLoading] = useState(true);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

  const [wallet, setWallet] = useState<WalletData | null>(null);
  const [walletLoading, setWalletLoading] = useState(true);
  const [topUpTiers, setTopUpTiers] = useState<WalletTopUpTier[]>([]);
  const [walletCurrency, setWalletCurrency] = useState('AUD');

  const [freezeState, setFreezeState] = useState<LearnerFreezeStatus | null>(null);
  const [freezeLoadFailed, setFreezeLoadFailed] = useState(false);
  const [selectedGateway, setSelectedGateway] = useState<'stripe' | 'paypal'>('stripe');

  // Double-submit guard shared across all paid-action handlers; complements
  // the per-button busyKey for cases where rapid clicks fire before
  // setBusyKey('…') has flushed through React's state queue.
  const submittingRef = useRef(false);

  const reducedMotion = useReducedMotion() ?? false;

  // Stable idempotency key generator for paid actions.
  const newIdempotencyKey = () =>
    typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
      ? crypto.randomUUID()
      : `idem-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;

  const paymentBanner = useMemo(
    () => getPaymentBanner(paymentStatus, paymentGateway),
    [paymentGateway, paymentStatus],
  );
  const isFrozen = isFreezeEffective(freezeState);
  const billingMutationsBlocked = freezeLoadFailed || isFrozen;
  const billingBlockedMessage = freezeLoadFailed
    ? 'Billing actions are temporarily paused because freeze status could not be verified. Refresh the page before checkout, plan changes, or top-ups.'
    : 'Billing actions are read-only while your account is frozen.';

  const loadWallet = useCallback(async () => {
    setWalletLoading(true);
    try {
      const result = (await fetchWalletTransactions(20)) as unknown as WalletData;
      setWallet(result);
    } catch {
      /* wallet is optional, fail silently */
    } finally {
      setWalletLoading(false);
    }
  }, []);

  useEffect(() => {
    analytics.track('content_view', { page: 'subscriptions' });
    Promise.allSettled([fetchBilling(), fetchFreezeStatus(), fetchWalletTopUpTiers()])
      .then(([billingResult, freezeResult, tiersResult]) => {
        if (billingResult.status === 'rejected') {
          throw billingResult.reason;
        }
        setData(billingResult.value);
        setQuote(billingResult.value.quote);

        if (freezeResult.status === 'fulfilled') {
          setFreezeState(freezeResult.value as LearnerFreezeStatus);
          setFreezeLoadFailed(false);
        } else {
          setFreezeState(null);
          setFreezeLoadFailed(true);
        }

        if (tiersResult.status === 'fulfilled' && tiersResult.value && Array.isArray(tiersResult.value.tiers)) {
          // Honour an explicit empty array from the backend so the UI shows
          // an empty state rather than masking it with the fallback tiers.
          setTopUpTiers(tiersResult.value.tiers);
          setWalletCurrency(tiersResult.value.currency || 'AUD');
        }
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load billing data.'))
      .finally(() => setLoading(false));
    loadWallet();
  }, [loadWallet]);

  const visibleAddOns = useMemo(
    () =>
      (data?.addOns ?? []).filter((addOn) => {
        if (addOn.appliesToAllPlans) return true;
        const currentPlanCode = data?.currentPlanCode ?? '';
        if (!currentPlanCode) return false;
        return addOn.compatiblePlanCodes.includes(currentPlanCode);
      }),
    [data?.addOns, data?.currentPlanCode],
  );

  const startCheckout = async (
    productType: BillingProductType,
    quantity: number,
    priceId?: string | null,
    label?: string,
  ) => {
    if (billingMutationsBlocked) {
      setError(billingBlockedMessage);
      return;
    }
    if (submittingRef.current) return;
    submittingRef.current = true;
    const coupon = couponCode.trim() || null;
    const quoteKey = `${productType}:${priceId ?? quantity}`;
    setBusyKey(quoteKey);
    setError(null);
    setSuccess(null);
    try {
      const quoteResponse = await fetchBillingQuote({ productType, quantity, priceId, couponCode: coupon });
      setQuote(quoteResponse);
      setQuoteLabel(label ?? prettyProductType(productType));
      const checkoutPayload = {
        productType,
        quantity,
        priceId,
        couponCode: coupon,
        quoteId: quoteResponse.quoteId,
        gateway: selectedGateway,
        // Optimistic idempotency key. Impl D should formally accept this in
        // CheckoutSessionCreateRequest; until then the helper internally
        // generates one too, so this is harmless.
        idempotencyKey: newIdempotencyKey(),
      } as Parameters<typeof createBillingCheckoutSession>[0];
      const response = await createBillingCheckoutSession(checkoutPayload);
      await openCheckoutUrl(response.checkoutUrl);
      setSuccess(`${label ?? prettyProductType(productType)} checkout opened with a validated quote.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not start checkout.');
    } finally {
      setBusyKey(null);
      submittingRef.current = false;
    }
  };

  const loadPreview = useCallback(async (planId: string) => {
    if (billingMutationsBlocked) {
      setError(billingBlockedMessage);
      return;
    }
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
  }, [billingBlockedMessage, billingMutationsBlocked]);

  useEffect(() => {
    if (loading || !data || activeTab !== 'plans' || !requestedPlanId) {
      return;
    }

    const requestedPlan = data.plans.find((plan) => plan.id === requestedPlanId || plan.code === requestedPlanId);
    if (requestedPlan && previewPlanId !== requestedPlan.id) {
      void loadPreview(requestedPlan.id);
    }
  }, [activeTab, data, loadPreview, loading, previewPlanId, requestedPlanId]);

  const handleDownloadInvoice = async (invoiceId: string) => {
    if (submittingRef.current) return;
    submittingRef.current = true;
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
      submittingRef.current = false;
    }
  };

  const handleTopUp = async (amount: number) => {
    if (billingMutationsBlocked) {
      setError(billingBlockedMessage);
      return;
    }
    if (submittingRef.current) return;
    submittingRef.current = true;
    setBusyKey(`topup:${amount}`);
    setError(null);
    setSuccess(null);
    try {
      const result = (await createWalletTopUp(amount, selectedGateway, newIdempotencyKey())) as Record<string, unknown>;
      const checkoutUrl = typeof result.checkoutUrl === 'string' ? result.checkoutUrl : null;
      if (checkoutUrl) {
        await openCheckoutUrl(checkoutUrl);
      }
      setSuccess(
        `Top-up checkout opened. ${result.totalCredits ?? amount} credits will be added after payment is confirmed.`,
      );
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not create top-up session.');
    } finally {
      setBusyKey(null);
      submittingRef.current = false;
    }
  };

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Billing & subscriptions" backHref="/">
        <div className="space-y-6">
          <Skeleton className="h-44 rounded-2xl" />
          <Skeleton className="h-12 rounded-2xl" />
          <Skeleton className="h-64 rounded-2xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!data) {
    return (
      <LearnerDashboardShell pageTitle="Billing & subscriptions" backHref="/">
        <InlineAlert variant="error">{error ?? 'Subscription data could not be loaded.'}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  const activeAddOns = data.activeAddOns ?? [];
  const plans = data.plans ?? [];
  const addOns = visibleAddOns;
  const invoices = data.invoices ?? [];
  const supportedReviewSubtests = data.entitlements?.supportedReviewSubtests ?? [];
  const invoiceDownloadsAvailable = data.entitlements?.invoiceDownloadsAvailable ?? false;
  const recentInvoices = invoices.slice(0, 3);
  const recentTransactions: WalletTransactionDto[] = wallet?.transactions?.slice(0, 4) ?? [];

  return (
    <LearnerDashboardShell
      pageTitle="Billing & subscriptions"
      subtitle="Manage your plan, credits, invoices, and entitlements in one place."
      backHref="/"
    >
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Billing"
          icon={CreditCard}
          accent="navy"
          title="Your billing center"
          description="Review your plan, top up review credits, and download invoices — everything stays validated server-side before checkout opens."
          highlights={[
            { icon: ShieldCheck, label: 'Current plan', value: data.currentPlan },
            { icon: Wallet, label: 'Credit balance', value: walletLoading ? '—' : `${wallet?.balance ?? 0} credits` },
            { icon: ShoppingCart, label: 'Active add-ons', value: `${activeAddOns.length}` },
            { icon: Calendar, label: 'Next renewal', value: formatOptionalDate(data.nextRenewal) },
          ]}
        />

        {/* Status banners — only render when relevant */}
        {(paymentBanner || isFrozen || freezeLoadFailed || error || success) && (
          <div className="space-y-2" role="status" aria-live="polite">
            {paymentBanner ? <InlineAlert variant={paymentBanner.variant}>{paymentBanner.message}</InlineAlert> : null}
            {isFrozen ? (
              <InlineAlert variant="warning">
                Your account is frozen, so checkout, plan changes, and top-ups are paused. Billing history remains visible.
              </InlineAlert>
            ) : null}
            {freezeLoadFailed ? (
              <InlineAlert variant="error">
                Freeze status could not be verified, so checkout, plan changes, and top-ups are paused until this page is refreshed successfully.
              </InlineAlert>
            ) : null}
            {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
            {success ? <InlineAlert variant="success">{success}</InlineAlert> : null}
          </div>
        )}

        {/* Persistent quote bar — only when a validated quote is held */}
        {quote ? (
          <div className="rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-emerald-900 shadow-sm dark:border-emerald-300/30 dark:bg-emerald-300/10 dark:text-emerald-100">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex items-center gap-3">
                <CheckCircle2 className="h-5 w-5 text-emerald-700 dark:text-emerald-200" />
                <div>
                  <p className="text-xs font-black uppercase tracking-widest">Validated quote</p>
                  <p className="text-sm font-bold">
                    {quoteLabel ?? 'Checkout'} · {formatCurrency(quote.totalAmount, quote.currency)}
                    {quote.discountAmount > 0 ? (
                      <span className="ml-2 font-semibold">
                        ({formatCurrency(quote.discountAmount, quote.currency)} off)
                      </span>
                    ) : null}
                  </p>
                </div>
              </div>
              <button
                type="button"
                onClick={() => {
                  // Clear the coupon along with the quote when the dismissed
                  // quote was carrying one, so a stale code can't silently
                  // re-apply on the next checkout.
                  if (quote?.couponCode) {
                    setCouponCode('');
                  }
                  setQuote(null);
                  setQuoteLabel(null);
                }}
                className="inline-flex items-center gap-1 rounded-full px-3 py-1 text-xs font-bold text-emerald-800 transition-colors hover:bg-emerald-100 dark:text-emerald-100 dark:hover:bg-emerald-300/10"
              >
                <X className="h-3 w-3" /> Dismiss
              </button>
            </div>
          </div>
        ) : null}

        {/* Section navigation */}
        <Tabs
          tabs={BILLING_TABS}
          activeTab={activeTab}
          onChange={(id) => setActiveTab(id as BillingTabId)}
        />

        {/* ── OVERVIEW ────────────────────────────────────────────── */}
        <TabPanel id="overview" activeTab={activeTab}>
          <div className="grid gap-6 lg:grid-cols-[1.4fr_1fr]">
            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <p className="text-xs font-black uppercase tracking-widest text-muted">Current subscription</p>
                  <h2 className="mt-2 text-2xl font-black text-navy">{data.currentPlan}</h2>
                  <p className="mt-1 text-sm text-muted">
                    {data.price} / {data.interval}
                  </p>
                  {data.planDescription ? (
                    <p className="mt-3 max-w-md text-sm leading-6 text-muted">{data.planDescription}</p>
                  ) : null}
                </div>
                <span className="inline-flex items-center gap-2 rounded-full bg-emerald-50 px-3 py-1 text-xs font-black uppercase tracking-widest text-emerald-700">
                  <CheckCircle2 className="h-4 w-4" />
                  {data.status}
                </span>
              </div>

              <dl className="mt-6 grid gap-3 sm:grid-cols-3">
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <dt className="text-[11px] font-black uppercase tracking-widest text-muted">Renews</dt>
                  <dd className="mt-1 text-sm font-bold text-navy">
                    {formatOptionalDate(data.nextRenewal)}
                  </dd>
                </div>
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <dt className="text-[11px] font-black uppercase tracking-widest text-muted">Tutor reviews</dt>
                  <dd className="mt-1 text-sm font-bold text-navy">
                    {supportedReviewSubtests.length > 0 ? supportedReviewSubtests.join(' + ') : 'Not included'}
                  </dd>
                </div>
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <dt className="text-[11px] font-black uppercase tracking-widest text-muted">Invoice access</dt>
                  <dd className="mt-1 text-sm font-bold text-navy">
                    {invoiceDownloadsAvailable ? 'Downloads available' : 'Unavailable'}
                  </dd>
                </div>
              </dl>

              <div className="mt-6 flex flex-wrap gap-2">
                <Button variant="outline" onClick={() => setActiveTab('plans')}>
                  Change plan
                </Button>
                <Button variant="outline" onClick={() => setActiveTab('credits')}>
                  <Wallet className="h-4 w-4" /> Top up credits
                </Button>
                <Button variant="outline" onClick={() => setActiveTab('invoices')}>
                  <Receipt className="h-4 w-4" /> View invoices
                </Button>
              </div>
            </section>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-xs font-black uppercase tracking-widest text-muted">Credit wallet</p>
                  <h3 className="mt-2 text-3xl font-black text-navy">
                    {walletLoading ? '—' : (wallet?.balance ?? 0)}
                  </h3>
                  <p className="mt-1 text-sm text-muted">review credits available</p>
                </div>
                <div className="rounded-2xl bg-success/10 p-3 text-success">
                  <Wallet className="h-5 w-5" />
                </div>
              </div>
              <p className="mt-4 text-sm leading-6 text-muted">
                Credits unlock tutor review for Writing and Speaking. Reading and Listening stay AI-evaluated.
              </p>
              <Button className="mt-4" fullWidth onClick={() => setActiveTab('credits')}>
                Manage credits
              </Button>
            </section>
          </div>

          {/* Recent activity */}
          <div className="mt-6 grid gap-6 lg:grid-cols-2">
            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex items-center justify-between gap-3">
                <h3 className="text-sm font-black uppercase tracking-widest text-muted">Recent invoices</h3>
                <button
                  type="button"
                  onClick={() => setActiveTab('invoices')}
                  className="text-xs font-bold text-primary transition-colors hover:text-primary/80"
                >
                  View all
                </button>
              </div>
              {recentInvoices.length === 0 ? (
                <p className="mt-4 text-sm text-muted">No invoices yet. They will appear after your first paid checkout.</p>
              ) : (
                <ul className="mt-4 space-y-2">
                  {recentInvoices.map((invoice) => (
                    <li
                      key={invoice.id}
                      className="flex items-center justify-between gap-3 rounded-xl border border-border bg-background-light px-4 py-3"
                    >
                      <div className="flex items-center gap-3">
                        <div className="rounded-lg bg-surface p-2 text-muted">
                          <FileText className="h-4 w-4" />
                        </div>
                        <div>
                          <p className="text-sm font-bold text-navy">
                            {new Date(invoice.date).toLocaleDateString()}
                          </p>
                          <p className="text-[11px] text-muted">
                            {invoice.amount} · <span title={`Invoice reference (masked)`}>{maskProviderId(invoice.id)}</span>
                          </p>
                        </div>
                      </div>
                      <span className="rounded-full bg-emerald-50 px-2.5 py-1 text-[10px] font-black uppercase tracking-widest text-emerald-700">
                        {invoice.status}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </section>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex items-center justify-between gap-3">
                <h3 className="text-sm font-black uppercase tracking-widest text-muted">Recent credit activity</h3>
                <button
                  type="button"
                  onClick={() => setActiveTab('credits')}
                  className="text-xs font-bold text-primary transition-colors hover:text-primary/80"
                >
                  View all
                </button>
              </div>
              {walletLoading ? (
                <div className="mt-4 space-y-2">
                  {[1, 2, 3].map((i) => (
                    <Skeleton key={i} className="h-12 rounded-xl" />
                  ))}
                </div>
              ) : recentTransactions.length === 0 ? (
                <div className="mt-4 flex flex-col items-center gap-2 py-6 text-center">
                  <Clock className="h-7 w-7 text-muted/40" />
                  <p className="text-sm text-muted">No credit activity yet.</p>
                </div>
              ) : (
                <ul className="mt-4 space-y-2">
                  {recentTransactions.map((tx) => (
                    <li
                      key={tx.id}
                      className="flex items-center justify-between gap-3 rounded-xl border border-border bg-background-light px-4 py-3"
                    >
                      <div className="flex items-center gap-3">
                        <div
                          className={`rounded-lg p-1.5 ${tx.amount >= 0 ? 'bg-emerald-50 text-emerald-700' : 'bg-rose-100 text-rose-700'}`}
                        >
                          {tx.amount >= 0 ? (
                            <ArrowDownCircle className="h-4 w-4" />
                          ) : (
                            <ArrowUpCircle className="h-4 w-4" />
                          )}
                        </div>
                        <div>
                          <p className="text-sm font-semibold text-navy">{tx.description ?? tx.type}</p>
                          <p className="text-[11px] text-muted">
                            {new Date(tx.createdAt).toLocaleDateString()}
                          </p>
                        </div>
                      </div>
                      <p
                        className={`text-sm font-bold ${tx.amount >= 0 ? 'text-success' : 'text-danger'}`}
                      >
                        {tx.amount >= 0 ? '+' : ''}
                        {tx.amount}
                      </p>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </div>
        </TabPanel>

        {/* ── PLANS ───────────────────────────────────────────────── */}
        <TabPanel id="plans" activeTab={activeTab}>
          <LearnerSurfaceSectionHeader
            eyebrow="Plans"
            title="Compare plans and preview a change"
            description="Plans are managed by the admin team. Upgrades and downgrades show a server-validated proration before checkout."
            className="mb-4"
          />

          {plans.length === 0 ? (
            <div className="rounded-2xl border border-border bg-surface p-5 text-sm text-muted shadow-sm">
              No published billing plans are available yet.
            </div>
          ) : (
            <div className="grid gap-4 lg:grid-cols-3">
              {plans.map((plan, index) => {
                const isCurrent = plan.changeDirection === 'current';
                const isPreviewing = previewPlanId === plan.id && preview;
                return (
                  <motion.article
                    key={plan.id}
                    initial={reducedMotion ? false : { opacity: 0, y: 6 }}
                    animate={reducedMotion ? undefined : { opacity: 1, y: 0 }}
                    transition={reducedMotion ? undefined : { duration: 0.25, delay: index * 0.04 }}
                    className={`flex flex-col rounded-2xl border p-5 shadow-sm ${
                      isCurrent
                        ? 'border-primary/30 bg-primary/5 dark:bg-primary/10'
                        : 'border-border bg-surface'
                    }`}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <p className="text-[11px] font-black uppercase tracking-widest text-muted">
                          {plan.tier}
                        </p>
                        <h3 className="mt-1 text-xl font-black text-navy">{plan.label}</h3>
                      </div>
                      {isCurrent ? (
                        <span className="rounded-full bg-primary/10 px-2.5 py-1 text-[10px] font-black uppercase tracking-widest text-primary">
                          Current
                        </span>
                      ) : plan.changeDirection === 'upgrade' ? (
                        <ArrowUpCircle className="h-5 w-5 text-success" />
                      ) : (
                        <ArrowDownCircle className="h-5 w-5 text-amber-500" />
                      )}
                    </div>

                    <p className="mt-3 text-sm leading-6 text-muted">{plan.description}</p>

                    <div className="mt-5 rounded-xl border border-border bg-background-light p-4">
                      <p className="text-2xl font-black text-navy">
                        {plan.price}
                        <span className="ml-1 text-sm font-semibold text-muted">/ {plan.interval}</span>
                      </p>
                      <p className="mt-1 text-sm text-muted">
                        {plan.reviewCredits} review credits included
                      </p>
                    </div>

                    <ul className="mt-4 space-y-2 text-sm text-navy">
                      <li className="flex items-start gap-2">
                        <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-emerald-600" />
                        <span>
                          Tutor reviews for{' '}
                          <strong>
                            {plan.includedSubtests && plan.includedSubtests.length > 0
                              ? plan.includedSubtests.join(' & ')
                              : 'no subtests'}
                          </strong>
                        </span>
                      </li>
                      {plan.trialDays > 0 ? (
                        <li className="flex items-start gap-2">
                          <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-emerald-600" />
                          <span>{plan.trialDays}-day trial</span>
                        </li>
                      ) : null}
                      {plan.isRenewable ? (
                        <li className="flex items-start gap-2">
                          <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-emerald-600" />
                          <span>Auto-renewing</span>
                        </li>
                      ) : null}
                      {(plan.entitlements ?? {})['invoiceDownloadsAvailable'] === true ? (
                        <li className="flex items-start gap-2">
                          <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-emerald-600" />
                          <span>Invoice downloads</span>
                        </li>
                      ) : null}
                    </ul>

                    {!isCurrent ? (
                      <div className="mt-5 space-y-3">
                        <Button
                          variant="outline"
                          fullWidth
                          loading={busyKey === `preview:${plan.id}`}
                          disabled={billingMutationsBlocked}
                          aria-busy={busyKey === `preview:${plan.id}`}
                          aria-disabled={billingMutationsBlocked || undefined}
                          title={billingMutationsBlocked ? billingBlockedMessage : undefined}
                          onClick={() => loadPreview(plan.id)}
                        >
                          Preview {plan.changeDirection}
                        </Button>
                        {isPreviewing ? (
                          <div className="rounded-xl border border-border bg-background-light p-4 text-sm text-muted">
                            <p className="font-bold text-navy">{preview.summary}</p>
                            <p className="mt-2">Prorated: {preview.proratedAmount}</p>
                            <p className="mt-1">
                              Effective {new Date(preview.effectiveAt).toLocaleDateString()}
                            </p>
                            <Button
                              className="mt-3"
                              fullWidth
                              loading={busyKey === `${plan.changeDirection}:${plan.code}`}
                              disabled={billingMutationsBlocked}
                              aria-busy={busyKey === `${plan.changeDirection}:${plan.code}`}
                              aria-disabled={billingMutationsBlocked || undefined}
                              title={billingMutationsBlocked ? billingBlockedMessage : undefined}
                              onClick={() =>
                                startCheckout(
                                  plan.changeDirection === 'upgrade' ? 'plan_upgrade' : 'plan_downgrade',
                                  1,
                                  plan.code,
                                  `${plan.label} ${plan.changeDirection}`,
                                )
                              }
                            >
                              Continue to checkout
                            </Button>
                          </div>
                        ) : null}
                      </div>
                    ) : (
                      <div className="mt-5 rounded-xl border border-dashed border-border bg-background-light/60 px-4 py-3 text-center text-xs font-bold uppercase tracking-widest text-muted">
                        Active plan
                      </div>
                    )}
                  </motion.article>
                );
              })}
            </div>
          )}
        </TabPanel>

        {/* ── CREDITS & ADD-ONS ─────────────────────────────────── */}
        <TabPanel id="credits" activeTab={activeTab}>
          <div className="grid gap-6 lg:grid-cols-[1fr_1.2fr]">
            {/* Top-up panel */}
            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Wallet"
                title="Top up review credits"
                description="Bonus credits scale with the tier amount. Tiers and bonuses are configurable from the platform."
                className="mb-4"
              />

              <div className="mb-5 flex items-center gap-3">
                <p className="text-[11px] font-black uppercase tracking-widest text-muted">Pay with</p>
                <div className="inline-flex rounded-xl border border-border bg-background-light p-1">
                  {(['stripe', 'paypal'] as const).map((gw) => (
                    <button
                      key={gw}
                      type="button"
                      onClick={() => setSelectedGateway(gw)}
                      className={`rounded-lg px-3 py-1.5 text-xs font-bold transition-all ${
                        selectedGateway === gw
                          ? 'bg-emerald-700 text-white shadow-sm'
                          : 'text-navy hover:bg-surface'
                      }`}
                    >
                      {gw === 'stripe' ? 'Stripe' : 'PayPal'}
                    </button>
                  ))}
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3">
                {topUpTiers.length === 0 ? (
                  <div className="col-span-2 rounded-2xl border border-dashed border-border bg-background-light p-5 text-center text-sm text-muted">
                    Top-up tiers are not configured yet. Please check back shortly or contact support if this persists.
                    <Button className="mt-3" disabled aria-disabled="true" fullWidth>
                      Top up unavailable
                    </Button>
                  </div>
                ) : (
                  topUpTiers.map((tier) => (
                    <motion.button
                      key={tier.amount}
                      type="button"
                      whileHover={reducedMotion ? undefined : { scale: 1.015 }}
                      whileTap={reducedMotion ? undefined : { scale: 0.98 }}
                      disabled={billingMutationsBlocked || busyKey === `topup:${tier.amount}`}
                      aria-busy={busyKey === `topup:${tier.amount}`}
                      aria-disabled={billingMutationsBlocked || undefined}
                      title={billingMutationsBlocked ? billingBlockedMessage : undefined}
                      onClick={() => handleTopUp(tier.amount)}
                      className={`relative rounded-2xl border p-4 text-left transition-all disabled:cursor-not-allowed disabled:opacity-60 ${
                        tier.isPopular
                          ? 'border-emerald-300 bg-surface shadow-md'
                          : 'border-border bg-background-light hover:border-emerald-200 hover:shadow-sm'
                      }`}
                    >
                      {tier.isPopular ? (
                        <span className="absolute -top-2 right-3 rounded-full bg-emerald-700 px-2 py-0.5 text-[9px] font-black uppercase tracking-widest text-white">
                          Popular
                        </span>
                      ) : null}
                      <p className="text-lg font-black text-navy">
                        {formatCurrency(tier.amount, walletCurrency)}
                      </p>
                      <p className="text-xs text-muted">
                        {tier.credits} credits
                        {tier.bonus > 0 ? (
                          <span className="ml-1 font-bold text-emerald-700">+{tier.bonus} bonus</span>
                        ) : null}
                      </p>
                      {tier.label ? (
                        <p className="mt-1 text-[10px] font-black uppercase tracking-widest text-muted">
                          {tier.label}
                        </p>
                      ) : null}
                      {busyKey === `topup:${tier.amount}` ? (
                        <p className="mt-2 text-xs font-bold text-emerald-700">Processing…</p>
                      ) : null}
                    </motion.button>
                  ))
                )}
              </div>

              <div className="mt-6">
                <Input
                  label="Coupon code (optional)"
                  value={couponCode}
                  onChange={(event) => setCouponCode(event.target.value.toUpperCase())}
                  placeholder="WELCOME10"
                  hint="Applied to the next validated quote (add-ons and plan changes)."
                />
              </div>
            </section>

            {/* Add-ons panel */}
            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Add-ons"
                title="Subscription extras compatible with your plan"
                description="Add-ons follow the same quote → checkout flow as plan changes. Tutor reviews only apply to Writing and Speaking."
                className="mb-4"
              />
              {activeAddOns.length > 0 ? (
                <div className="mb-4 rounded-2xl border border-primary/15 bg-primary/5 p-4">
                  <p className="text-[11px] font-black uppercase tracking-widest text-primary">
                    Active on this account
                  </p>
                  <ul className="mt-2 space-y-1">
                    {activeAddOns.map((addOn) => (
                      <li key={addOn.id} className="flex items-center justify-between text-sm">
                        <span className="font-bold text-navy">{addOn.name}</span>
                        <span className="text-xs font-bold uppercase tracking-widest text-primary">
                          ×{addOn.quantity}
                        </span>
                      </li>
                    ))}
                  </ul>
                </div>
              ) : null}

              {addOns.length > 0 ? (
                <div className="space-y-3">
                  {addOns.map((addOn) => {
                    const checkoutProductType = billingAddOnCheckoutProductType(addOn.productType);
                    const checkoutLabel =
                      checkoutProductType === 'review_credits' ? 'Purchase credits' : 'Purchase add-on';
                    return (
                      <article
                        key={addOn.id}
                        className="rounded-2xl border border-border bg-surface p-5 shadow-sm"
                      >
                        <div className="flex flex-wrap items-start justify-between gap-3">
                          <div>
                            <p className="text-[11px] font-black uppercase tracking-widest text-muted">
                              {addOn.productType.replace(/_/g, ' ')}
                            </p>
                            <h3 className="mt-1 text-base font-black text-navy">{addOn.name}</h3>
                            <p className="mt-2 text-sm text-muted">{addOn.description}</p>
                            <div className="mt-3 flex flex-wrap gap-2 text-[11px] font-black uppercase tracking-widest text-muted">
                              <span className="rounded-full bg-background-light px-2.5 py-1">
                                {addOn.quantity} credits
                              </span>
                              <span className="rounded-full bg-background-light px-2.5 py-1">
                                {addOn.interval}
                              </span>
                              {addOn.isRecurring ? (
                                <span className="rounded-full bg-background-light px-2.5 py-1">
                                  Recurring
                                </span>
                              ) : null}
                            </div>
                          </div>
                          <div className="rounded-xl bg-background-light px-3 py-2 text-sm font-black text-navy">
                            {addOn.price}
                          </div>
                        </div>
                        <Button
                          className="mt-4"
                          fullWidth
                          loading={busyKey === `${checkoutProductType}:${addOn.code}`}
                          disabled={billingMutationsBlocked}
                          aria-busy={busyKey === `${checkoutProductType}:${addOn.code}`}
                          aria-disabled={billingMutationsBlocked || undefined}
                          title={billingMutationsBlocked ? billingBlockedMessage : undefined}
                          onClick={() =>
                            startCheckout(checkoutProductType, addOn.quantity, addOn.code, addOn.name)
                          }
                        >
                          <ShoppingCart className="h-4 w-4" />
                          {checkoutLabel}
                        </Button>
                      </article>
                    );
                  })}
                </div>
              ) : (
                <div className="rounded-2xl border border-dashed border-border bg-background-light p-5 text-center text-sm text-muted">
                  No add-ons are compatible with your current plan.
                </div>
              )}
            </section>
          </div>

          {/* Full transaction history */}
          <section className="mt-6 rounded-2xl border border-border bg-surface p-6 shadow-sm">
            <div className="flex items-center justify-between gap-3">
              <h3 className="text-sm font-black uppercase tracking-widest text-muted">
                Wallet transaction history
              </h3>
              <button
                type="button"
                onClick={() => loadWallet()}
                className="text-xs font-bold text-primary transition-colors hover:text-primary/80"
              >
                Refresh
              </button>
            </div>
            {walletLoading ? (
              <div className="mt-4 space-y-2">
                {[1, 2, 3, 4].map((i) => (
                  <Skeleton key={i} className="h-12 rounded-xl" />
                ))}
              </div>
            ) : !wallet || wallet.transactions.length === 0 ? (
              <div className="mt-4 flex flex-col items-center gap-2 py-8 text-center">
                <Clock className="h-8 w-8 text-muted/40" />
                <p className="text-sm text-muted">
                  No transactions yet. Your credit history will appear here.
                </p>
              </div>
            ) : (
              <div className="mt-4 max-h-[420px] overflow-y-auto pr-1">
                <ul className="space-y-2">
                  {wallet.transactions.map((tx) => (
                    <li
                      key={tx.id}
                      className="flex items-center justify-between gap-3 rounded-xl border border-border bg-background-light px-4 py-3"
                    >
                      <div className="flex items-center gap-3">
                        <div
                          className={`rounded-lg p-1.5 ${tx.amount >= 0 ? 'bg-emerald-50 text-emerald-700' : 'bg-rose-100 text-rose-700'}`}
                        >
                          {tx.amount >= 0 ? (
                            <ArrowDownCircle className="h-4 w-4" />
                          ) : (
                            <ArrowUpCircle className="h-4 w-4" />
                          )}
                        </div>
                        <div>
                          <p className="text-sm font-semibold text-navy">{tx.description ?? tx.type}</p>
                          <p className="text-[11px] text-muted">
                            {new Date(tx.createdAt).toLocaleDateString()}
                          </p>
                        </div>
                      </div>
                      <div className="text-right">
                        <p
                          className={`text-sm font-bold ${tx.amount >= 0 ? 'text-success' : 'text-danger'}`}
                        >
                          {tx.amount >= 0 ? '+' : ''}
                          {tx.amount}
                        </p>
                        <p className="text-[11px] text-muted">bal: {tx.balanceAfter}</p>
                      </div>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </section>
        </TabPanel>

        {/* ── INVOICES ──────────────────────────────────────────── */}
        <TabPanel id="invoices" activeTab={activeTab}>
          <LearnerSurfaceSectionHeader
            eyebrow="Invoices"
            title="Billing history"
            description="Each paid checkout produces an invoice you can download for your records."
            className="mb-4"
          />
          <div className="overflow-hidden rounded-2xl border border-border bg-surface shadow-sm">
            {invoices.length === 0 ? (
              <div className="p-10 text-center">
                <Receipt className="mx-auto h-10 w-10 text-muted/40" />
                <p className="mt-3 text-sm text-muted">
                  No invoices yet. They will appear after your first paid checkout.
                </p>
              </div>
            ) : (
              <ul className="divide-y divide-border">
                {invoices.map((invoice) => (
                  <li
                    key={invoice.id}
                    className="flex flex-col gap-3 p-5 sm:flex-row sm:items-center sm:justify-between"
                  >
                    <div className="flex items-center gap-4">
                      <div className="rounded-xl bg-background-light p-3 text-muted">
                        <FileText className="h-5 w-5" />
                      </div>
                      <div>
                        <p className="text-sm font-black text-navy">
                          {new Date(invoice.date).toLocaleDateString()}
                        </p>
                        <p className="text-sm text-muted">
                          {invoice.amount} · <span title={`Invoice reference (masked)`}>{maskProviderId(invoice.id)}</span>
                        </p>
                      </div>
                    </div>
                    <div className="flex items-center gap-3">
                      <span className="rounded-full bg-emerald-50 px-3 py-1 text-xs font-black uppercase tracking-widest text-emerald-700">
                        {invoice.status}
                      </span>
                      <Button
                        variant="outline"
                        loading={busyKey === `invoice:${invoice.id}`}
                        disabled={!invoiceDownloadsAvailable}
                        aria-busy={busyKey === `invoice:${invoice.id}`}
                        aria-disabled={!invoiceDownloadsAvailable || undefined}
                        title={!invoiceDownloadsAvailable ? 'Invoice downloads are unavailable on your current plan.' : undefined}
                        onClick={() => handleDownloadInvoice(invoice.id)}
                      >
                        <Download className="h-4 w-4" />
                        Download
                      </Button>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>
          {!invoiceDownloadsAvailable ? (
            <p className="mt-3 text-xs text-muted">
              Invoice downloads are unavailable on your current plan. Upgrade to enable downloadable invoices.
            </p>
          ) : null}
        </TabPanel>
      </div>
    </LearnerDashboardShell>
  );
}
