'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import {
  ArrowDownCircle,
  ArrowUpCircle,
  Bot,
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
import { useRouter, useSearchParams } from 'next/navigation';
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
  fetchAiPackages,
  fetchAvailablePaymentGateways,
  fetchBilling,
  fetchBillingChangePreview,
  fetchBillingContent,
  fetchBillingQuote,
  fetchWalletTopUpTiers,
  fetchWalletTransactions,
  pauseSubscription,
  resumeSubscription,
} from '@/lib/api';
import { makeBillingCopy } from '@/lib/billing-copy-defaults';
import type { WalletTopUpTier } from '@/lib/api';
import type {
  AiPackage,
  AiPackagesResponse,
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

type BillingTabId = 'overview' | 'plans' | 'credits' | 'ai-credits' | 'invoices';

const BILLING_TABS: Array<{ id: BillingTabId; label: string; icon: React.ReactNode }> = [
  { id: 'overview', label: 'Overview', icon: <LayoutDashboard className="h-4 w-4" /> },
  { id: 'plans', label: 'Plans', icon: <Layers className="h-4 w-4" /> },
  { id: 'credits', label: 'Credits & Add-ons', icon: <Sparkles className="h-4 w-4" /> },
  { id: 'ai-credits', label: 'AI Credits', icon: <Bot className="h-4 w-4" /> },
  { id: 'invoices', label: 'Invoices', icon: <Receipt className="h-4 w-4" /> },
];

const AI_PACKAGE_SUBTEST_SECTIONS: Array<{ key: 'listening' | 'reading' | 'writing' | 'speaking'; copyKey: string; headerClass: string }> = [
  { key: 'listening', copyKey: 'billing.ai.section.listening', headerClass: 'bg-blue-700' },
  { key: 'reading', copyKey: 'billing.ai.section.reading', headerClass: 'bg-purple-700' },
  { key: 'writing', copyKey: 'billing.ai.section.writing', headerClass: 'bg-amber-600' },
  { key: 'speaking', copyKey: 'billing.ai.section.speaking', headerClass: 'bg-emerald-700' },
];

const TAB_COPY_KEYS: Record<BillingTabId, string> = {
  overview: 'billing.tab.overview',
  plans: 'billing.tab.plans',
  credits: 'billing.tab.credits',
  'ai-credits': 'billing.tab.aiCredits',
  invoices: 'billing.tab.invoices',
};

function aiPackageValidityLabel(validityDays: number): string {
  if (validityDays <= 0) return '';
  return validityDays >= 180 ? '6-month validity' : `${validityDays}-day validity`;
}

function aiPackageHeadline(pkg: AiPackage): string {
  if (pkg.group === 'mock') return `${pkg.mocks} full mock${pkg.mocks === 1 ? '' : 's'}`;
  if (pkg.credits > 0) return `${pkg.credits} AI credit${pkg.credits === 1 ? '' : 's'}`;
  return 'Unlimited practice access';
}

function newIdempotencyKey() {
  return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `idem-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

// ─── Component ───────────────────────────────────────────────────────

export default function BillingPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const paymentStatus = searchParams?.get('payment') ?? null;
  const paymentGateway = searchParams?.get('gateway') ?? null;
  const requestedPlanId = searchParams?.get('planId') ?? searchParams?.get('plan') ?? null;
  const requestedAddOnCode = searchParams?.get('addOn') ?? null;
  const requestedParentSubscriptionId = searchParams?.get('parent') ?? null;
  const initialTab = (searchParams?.get('tab') as BillingTabId | null) ?? (requestedAddOnCode ? 'credits' : 'overview');

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

  const [aiPackages, setAiPackages] = useState<AiPackagesResponse | null>(null);
  const [aiPackagesLoading, setAiPackagesLoading] = useState(true);
  const [aiPackageView, setAiPackageView] = useState<'full' | 'separate'>('full');

  // Admin-editable page copy: DB overrides merged over in-code defaults. Optional —
  // fails silently to defaults so nothing ever renders blank.
  const [billingCopy, setBillingCopy] = useState<Record<string, string> | null>(null);
  const copy = useMemo(() => makeBillingCopy(billingCopy), [billingCopy]);
  const billingTabs = useMemo(
    () => BILLING_TABS.map((tab) => ({ ...tab, label: copy(TAB_COPY_KEYS[tab.id]) })),
    [copy],
  );

  const [freezeState, setFreezeState] = useState<LearnerFreezeStatus | null>(null);
  const [freezeLoadFailed, setFreezeLoadFailed] = useState(false);
  // Gateways the backend can actually create checkout sessions for. Defaults to
  // card-only so an unconfigured PayPal account is never offered to the learner.
  const [availableGateways, setAvailableGateways] = useState<string[]>(['stripe']);
  const [selectedGateway, setSelectedGateway] = useState<'stripe' | 'paypal'>('stripe');
  const [pauseLoading, setPauseLoading] = useState(false);

  // Double-submit guard shared across all paid-action handlers; complements
  // the per-button busyKey for cases where rapid clicks fire before
  // setBusyKey('…') has flushed through React's state queue.
  const submittingRef = useRef(false);
  const autoCheckoutStartedRef = useRef(false);

  const reducedMotion = useReducedMotion() ?? false;

  const paymentBanner = useMemo(
    () => getPaymentBanner(paymentStatus, paymentGateway),
    [paymentGateway, paymentStatus],
  );
  const isFrozen = isFreezeEffective(freezeState);
  const isPastDue = data?.status?.toLowerCase() === 'pastdue' || data?.status?.toLowerCase() === 'past_due';
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

  const loadBilling = useCallback(() => {
    setLoading(true);
    setError(null);
    Promise.allSettled([fetchBilling(), fetchFreezeStatus(), fetchWalletTopUpTiers(), fetchAvailablePaymentGateways()])
      .then(([billingResult, freezeResult, tiersResult, gatewaysResult]) => {
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

        if (gatewaysResult.status === 'fulfilled' && Array.isArray(gatewaysResult.value?.gateways) && gatewaysResult.value.gateways.length > 0) {
          const gateways = gatewaysResult.value.gateways;
          setAvailableGateways(gateways);
          if (!gateways.includes('paypal')) {
            setSelectedGateway('stripe');
          } else if (!gateways.includes('stripe')) {
            setSelectedGateway('paypal');
          }
        }
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load billing data.'))
      .finally(() => setLoading(false));
    loadWallet();
  }, [loadWallet]);

  useEffect(() => {
    analytics.track('content_view', { page: 'subscriptions' });
    loadBilling();
  }, [loadBilling]);

  useEffect(() => {
    let cancelled = false;
    setAiPackagesLoading(true);
    fetchAiPackages()
      .then((res) => {
        if (!cancelled) setAiPackages(res);
      })
      .catch(() => {
        /* AI packages are optional; fail silently and show empty state */
      })
      .finally(() => {
        if (!cancelled) setAiPackagesLoading(false);
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

  const isAddOnEligible = useCallback(
    (addOn: { appliesToAllPlans: boolean; compatiblePlanCodes: string[] }) => {
      if (addOn.appliesToAllPlans) return true;
      const currentPlanCode = data?.currentPlanCode ?? '';
      if (!currentPlanCode) return false;
      return addOn.compatiblePlanCodes.includes(currentPlanCode);
    },
    [data?.currentPlanCode],
  );

  // Split rather than hide: plan-locked add-ons stay visible with a
  // "requires plan" explanation so learners understand why they can't buy.
  const visibleAddOns = useMemo(
    () => (data?.addOns ?? []).filter((addOn) => isAddOnEligible(addOn)),
    [data?.addOns, isAddOnEligible],
  );
  const lockedAddOns = useMemo(
    () => (data?.addOns ?? []).filter((addOn) => !isAddOnEligible(addOn)),
    [data?.addOns, isAddOnEligible],
  );

  const startCheckout = useCallback(async (
    productType: BillingProductType,
    quantity: number,
    priceId?: string | null,
    label?: string,
    parentSubscriptionId?: string | null,
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
      const quoteResponse = await fetchBillingQuote({ productType, quantity, priceId, couponCode: coupon, parentSubscriptionId });
      setQuote(quoteResponse);
      setQuoteLabel(label ?? prettyProductType(productType));
      const checkoutPayload = {
        productType,
        quantity,
        priceId,
        couponCode: coupon,
        parentSubscriptionId,
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
  }, [billingBlockedMessage, billingMutationsBlocked, couponCode, selectedGateway]);

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

  useEffect(() => {
    if (loading || !data || !requestedAddOnCode || autoCheckoutStartedRef.current) {
      return;
    }

    const requestedAddOn = data.addOns.find((addOn) => addOn.code === requestedAddOnCode || addOn.id === requestedAddOnCode);
    if (!requestedAddOn) {
      setError('The requested add-on is not available for this account.');
      return;
    }

    if (!isAddOnEligible(requestedAddOn)) {
      autoCheckoutStartedRef.current = true;
      setActiveTab('credits');
      setError(`${requestedAddOn.name} is not available on your current plan. Upgrade your plan to unlock it.`);
      return;
    }

    autoCheckoutStartedRef.current = true;
    setActiveTab('credits');
    void startCheckout(
      billingAddOnCheckoutProductType(requestedAddOn.productType),
      requestedAddOn.quantity,
      requestedAddOn.code,
      requestedAddOn.name,
      requestedParentSubscriptionId,
    );
  }, [data, isAddOnEligible, loading, requestedAddOnCode, requestedParentSubscriptionId, startCheckout]);

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

  async function handlePause() {
    setPauseLoading(true);
    setError(null);
    try {
      await pauseSubscription(undefined, 'learner_requested_pause');
      const refreshed = await fetchBilling();
      setData(refreshed);
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to pause subscription.');
    } finally {
      setPauseLoading(false);
    }
  }

  async function handleResume() {
    setPauseLoading(true);
    setError(null);
    try {
      await resumeSubscription();
      const refreshed = await fetchBilling();
      setData(refreshed);
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to resume subscription.');
    } finally {
      setPauseLoading(false);
    }
  }

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle={copy('billing.page.title')} backHref="/">
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
      <LearnerDashboardShell pageTitle={copy('billing.page.title')} backHref="/">
        <InlineAlert variant="error">{error ?? 'Subscription data could not be loaded.'}</InlineAlert>
        <Button className="mt-4" onClick={loadBilling}>
          Try again
        </Button>
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

  const renderAiPackageCard = (pkg: AiPackage) => {
    const busy = busyKey === `addon_purchase:${pkg.code}`;
    const validity = aiPackageValidityLabel(pkg.validityDays);
    return (
      <motion.article
        key={pkg.code}
        initial={reducedMotion ? false : { opacity: 0, y: 6 }}
        animate={reducedMotion ? undefined : { opacity: 1, y: 0 }}
        transition={reducedMotion ? undefined : { duration: 0.25 }}
        className="flex flex-col rounded-2xl border border-border bg-surface p-5 shadow-sm"
      >
        <div className="flex items-start justify-between gap-3">
          <h3 className="text-xl font-semibold tracking-tight text-navy">{pkg.name}</h3>
          {pkg.priorityQueue ? (
            <span className="inline-flex items-center rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.08em] text-amber-800 dark:bg-amber-300/20 dark:text-amber-200">
              {copy('billing.ai.priority')}
            </span>
          ) : null}
        </div>
        <p className="mt-2 text-sm leading-6 text-muted">{pkg.description}</p>
        <div className="mt-4 rounded-xl border border-border/70 bg-background-light/60 p-4">
          <p className="text-2xl font-semibold tracking-tight text-navy">{formatCurrency(pkg.price, pkg.currency)}</p>
          <p className="mt-1 text-sm text-muted">
            {aiPackageHeadline(pkg)}
            {validity ? ` · ${validity}` : ''}
          </p>
        </div>
        {pkg.features.length > 0 ? (
          <ul className="mt-4 flex-1 space-y-2 text-sm text-navy">
            {pkg.features.map((feature, index) => (
              <li key={index} className="flex items-start gap-2">
                <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
                <span>{feature}</span>
              </li>
            ))}
          </ul>
        ) : (
          <div className="flex-1" />
        )}
        <Button
          className="mt-5"
          fullWidth
          loading={busy}
          disabled={billingMutationsBlocked}
          aria-busy={busy}
          aria-disabled={billingMutationsBlocked || undefined}
          title={billingMutationsBlocked ? billingBlockedMessage : undefined}
          onClick={() => router.push(`/checkout/review?productType=addon_purchase&priceId=${encodeURIComponent(pkg.code)}&quantity=1`)}
        >
          <ShoppingCart className="h-4 w-4" />
          {copy('billing.ai.buyNow')}
        </Button>
      </motion.article>
    );
  };

  return (
    <LearnerDashboardShell
      pageTitle={copy('billing.page.title')}
      subtitle={copy('billing.page.subtitle')}
      backHref="/"
    >
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow={copy('billing.hero.eyebrow')}
          icon={CreditCard}
          accent="navy"
          title={copy('billing.hero.title')}
          description={copy('billing.hero.description')}
          highlights={[
            { icon: ShieldCheck, label: copy('billing.hero.highlight.currentPlan'), value: data.currentPlan },
            { icon: Wallet, label: copy('billing.hero.highlight.creditBalance'), value: walletLoading ? '-' : `${wallet?.balance ?? 0} credits` },
            { icon: ShoppingCart, label: copy('billing.hero.highlight.activeAddons'), value: `${activeAddOns.length}` },
            { icon: Calendar, label: copy('billing.hero.highlight.nextRenewal'), value: formatOptionalDate(data.nextRenewal) },
          ]}
        />

        {/* Status banners — only render when relevant */}
        {(paymentBanner || isFrozen || isPastDue || freezeLoadFailed || error || success) && (
          <div className="space-y-2" role="status" aria-live="polite">
            {paymentBanner ? <InlineAlert variant={paymentBanner.variant}>{paymentBanner.message}</InlineAlert> : null}
            {isFrozen ? (
              <InlineAlert variant="warning">
                Your account is frozen, so checkout, plan changes, and top-ups are paused. Billing history remains visible.
              </InlineAlert>
            ) : null}
            {isPastDue ? (
              <InlineAlert variant="error">
                Your last payment failed. Please{' '}
                <a href="/billing/update-card" className="underline font-medium">update your payment method</a>
                {' '}to restore full access.
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
          <div className="rounded-2xl border border-success/30 bg-success/10 p-4 text-success shadow-sm">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div className="flex items-center gap-3">
                <CheckCircle2 className="h-5 w-5 text-success" />
                <div>
                  <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-success/80">{copy('billing.quote.validatedLabel')}</p>
                  <p className="mt-0.5 text-sm font-semibold">
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
                className="inline-flex items-center gap-1 rounded-full px-3 py-1 text-xs font-bold text-success transition-colors hover:bg-success/15"
              >
                <X className="h-3 w-3" /> {copy('billing.quote.dismiss')}
              </button>
            </div>
          </div>
        ) : null}

        {/* Section navigation */}
        <Tabs
          tabs={billingTabs}
          activeTab={activeTab}
          onChange={(id) => setActiveTab(id as BillingTabId)}
        />

        {/* ── OVERVIEW ────────────────────────────────────────────── */}
        <TabPanel id="overview" activeTab={activeTab}>
          <div className="grid gap-6 lg:grid-cols-[1.4fr_1fr] lg:items-stretch">
            <section className="flex h-full flex-col rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">{copy('billing.overview.currentSubscription')}</p>
                  <h2 className="mt-2 text-2xl font-semibold tracking-tight text-navy">{data.currentPlan}</h2>
                  <p className="mt-1 text-sm text-muted">
                    {data.price} / {data.interval}
                  </p>
                  {data.planDescription ? (
                    <p className="mt-3 max-w-md text-sm leading-6 text-muted">{data.planDescription}</p>
                  ) : null}
                </div>
                <span className="inline-flex items-center gap-2 text-sm font-extrabold text-success">
                  <span className="h-1.5 w-1.5 rounded-full bg-success" aria-hidden="true" />
                  {data.status}
                </span>
              </div>

              <dl className="mt-6 grid gap-3 sm:grid-cols-3">
                <div className="rounded-xl border border-border/70 bg-background-light/60 p-4">
                  <dt className="text-[11px] font-medium uppercase tracking-[0.1em] text-muted/80">{copy('billing.overview.renews')}</dt>
                  <dd className="mt-1.5 text-sm font-semibold text-navy">
                    {formatOptionalDate(data.nextRenewal)}
                  </dd>
                </div>
                <div className="rounded-xl border border-border/70 bg-background-light/60 p-4">
                  <dt className="text-[11px] font-medium uppercase tracking-[0.1em] text-muted/80">{copy('billing.overview.tutorReviews')}</dt>
                  <dd className="mt-1.5 text-sm font-semibold text-navy">
                    {supportedReviewSubtests.length > 0 ? supportedReviewSubtests.join(' + ') : copy('billing.overview.tutorReviewsNotIncluded')}
                  </dd>
                </div>
                <div className="rounded-xl border border-border/70 bg-background-light/60 p-4">
                  <dt className="text-[11px] font-medium uppercase tracking-[0.1em] text-muted/80">{copy('billing.overview.invoiceAccess')}</dt>
                  <dd className="mt-1.5 text-sm font-semibold text-navy">
                    {invoiceDownloadsAvailable ? copy('billing.overview.invoiceAccessAvailable') : copy('billing.overview.invoiceAccessUnavailable')}
                  </dd>
                </div>
              </dl>

              <div className="mt-auto flex flex-wrap gap-2 pt-6">
                <Button onClick={() => setActiveTab('plans')}>
                  {copy('billing.overview.changePlan')}
                </Button>
                <Button variant="outline" onClick={() => setActiveTab('credits')}>
                  <Wallet className="h-4 w-4" /> {copy('billing.overview.topUpCredits')}
                </Button>
                <Button variant="outline" onClick={() => setActiveTab('invoices')}>
                  <Receipt className="h-4 w-4" /> {copy('billing.overview.viewInvoices')}
                </Button>
                {data.status === 'active' || data.status === 'Active' ? (
                  <Button variant="outline" onClick={handlePause} disabled={pauseLoading}>
                    {pauseLoading ? copy('billing.overview.pausing') : copy('billing.overview.pause')}
                  </Button>
                ) : data.status === 'paused' || data.status === 'Paused' ? (
                  <Button variant="outline" onClick={handleResume} disabled={pauseLoading}>
                    {pauseLoading ? copy('billing.overview.resuming') : copy('billing.overview.resume')}
                  </Button>
                ) : null}
              </div>
            </section>

            <section className="flex h-full flex-col rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">{copy('billing.overview.creditWallet')}</p>
                  <h3 className="mt-2 text-4xl font-semibold tracking-tight text-navy tabular-nums">
                    {walletLoading ? '-' : (wallet?.balance ?? 0)}
                  </h3>
                  <p className="mt-1 text-sm text-muted">{copy('billing.overview.creditsAvailable')}</p>
                </div>
                <div className="rounded-xl bg-success/10 p-2.5 text-success">
                  <Wallet className="h-5 w-5" />
                </div>
              </div>
              <p className="mt-4 text-sm leading-6 text-muted">
                {copy('billing.overview.creditsHelp')}
              </p>
              <div className="mt-auto pt-4">
                <Button fullWidth onClick={() => setActiveTab('credits')}>
                  {copy('billing.overview.manageCredits')}
                </Button>
              </div>
            </section>
          </div>

          {/* Recent activity */}
          <div className="mt-6 grid gap-6 lg:grid-cols-2">
            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">{copy('billing.overview.activity')}</p>
                  <h3 className="mt-1 text-base font-semibold text-navy">{copy('billing.overview.recentInvoices')}</h3>
                </div>
                <button
                  type="button"
                  onClick={() => setActiveTab('invoices')}
                  className="text-xs font-medium text-primary transition-colors hover:text-primary/80"
                >
                  {copy('billing.overview.viewAll')}
                </button>
              </div>
              {recentInvoices.length === 0 ? (
                <p className="mt-4 text-sm text-muted">{copy('billing.overview.noInvoices')}</p>
              ) : (
                <ul className="mt-4 divide-y divide-border/60">
                  {recentInvoices.map((invoice) => (
                    <li
                      key={invoice.id}
                      className="flex items-center justify-between gap-3 py-3 first:pt-0 last:pb-0"
                    >
                      <div className="flex items-center gap-3">
                        <div className="rounded-lg bg-background-light p-2 text-muted">
                          <FileText className="h-4 w-4" />
                        </div>
                        <div>
                          <p className="text-sm font-semibold text-navy">
                            {new Date(invoice.date).toLocaleDateString()}
                          </p>
                          <p className="text-[11px] text-muted">
                            {invoice.amount} · <span title={`Invoice reference (masked)`}>{maskProviderId(invoice.id)}</span>
                          </p>
                        </div>
                      </div>
                      <span className="inline-flex items-center gap-1.5 text-sm font-extrabold text-success">
                        <span className="h-1.5 w-1.5 rounded-full bg-success" aria-hidden="true" />
                        {invoice.status}
                      </span>
                    </li>
                  ))}
                </ul>
              )}
            </section>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">{copy('billing.overview.activity')}</p>
                  <h3 className="mt-1 text-base font-semibold text-navy">{copy('billing.overview.recentCreditActivity')}</h3>
                </div>
                <button
                  type="button"
                  onClick={() => setActiveTab('credits')}
                  className="text-xs font-medium text-primary transition-colors hover:text-primary/80"
                >
                  {copy('billing.overview.viewAll')}
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
                  <p className="text-sm text-muted">{copy('billing.overview.noCreditActivity')}</p>
                </div>
              ) : (
                <ul className="mt-4 divide-y divide-border/60">
                  {recentTransactions.map((tx) => (
                    <li
                      key={tx.id}
                      className="flex items-center justify-between gap-3 py-3 first:pt-0 last:pb-0"
                    >
                      <div className="flex items-center gap-3">
                        <div
                          className={`rounded-lg p-1.5 ${tx.amount >= 0 ? 'bg-success/10 text-success' : 'bg-danger/10 text-danger'}`}
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
                        className={`text-sm font-semibold tabular-nums ${tx.amount >= 0 ? 'text-success' : 'text-danger'}`}
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
            eyebrow={copy('billing.plans.eyebrow')}
            title={copy('billing.plans.title')}
            description={copy('billing.plans.description')}
            className="mb-4"
          />

          {plans.length === 0 ? (
            <div className="rounded-2xl border border-border bg-surface p-5 text-sm text-muted shadow-sm">
              {copy('billing.plans.empty')}
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
                        <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">
                          {plan.tier}
                        </p>
                        <h3 className="mt-1 text-xl font-semibold tracking-tight text-navy">{plan.label}</h3>
                      </div>
                      {isCurrent ? (
                        <span className="inline-flex items-center gap-1.5 text-xs font-medium text-primary">
                          <span className="h-1.5 w-1.5 rounded-full bg-primary" aria-hidden="true" />
                          {copy('billing.plans.current')}
                        </span>
                      ) : plan.changeDirection === 'upgrade' ? (
                        <ArrowUpCircle className="h-5 w-5 text-success" />
                      ) : (
                        <ArrowDownCircle className="h-5 w-5 text-warning" />
                      )}
                    </div>

                    <p className="mt-3 text-sm leading-6 text-muted">{plan.description}</p>

                    <div className="mt-5 rounded-xl border border-border/70 bg-background-light/60 p-4">
                      <p className="text-2xl font-semibold tracking-tight text-navy">
                        {plan.price}
                        <span className="ml-1 text-sm font-medium text-muted">/ {plan.interval}</span>
                      </p>
                      <p className="mt-1 text-sm text-muted">
                        {plan.reviewCredits} {copy('billing.plans.reviewCreditsIncluded')}
                      </p>
                    </div>

                    <ul className="mt-4 space-y-2 text-sm text-navy">
                      <li className="flex items-start gap-2">
                        <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
                        <span>
                          {copy('billing.plans.tutorReviewsFor')}{' '}
                          <strong>
                            {plan.includedSubtests && plan.includedSubtests.length > 0
                              ? plan.includedSubtests.join(' & ')
                              : copy('billing.plans.noSubtests')}
                          </strong>
                        </span>
                      </li>
                      {plan.trialDays > 0 ? (
                        <li className="flex items-start gap-2">
                          <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
                          <span>{plan.trialDays}{copy('billing.plans.trialSuffix')}</span>
                        </li>
                      ) : null}
                      {plan.isRenewable ? (
                        <li className="flex items-start gap-2">
                          <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
                          <span>{copy('billing.plans.autoRenewing')}</span>
                        </li>
                      ) : null}
                      {(plan.entitlements ?? {})['invoiceDownloadsAvailable'] === true ? (
                        <li className="flex items-start gap-2">
                          <CheckCircle2 className="mt-0.5 h-4 w-4 flex-none text-success" />
                          <span>{copy('billing.plans.invoiceDownloads')}</span>
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
                          {copy('billing.plans.previewPrefix')} {plan.changeDirection}
                        </Button>
                        {isPreviewing ? (
                          <div className="rounded-xl border border-border bg-background-light p-4 text-sm text-muted">
                            <p className="font-semibold text-navy">{preview.summary}</p>
                            <p className="mt-2">{copy('billing.plans.prorated')} {preview.proratedAmount}</p>
                            <p className="mt-1">
                              {copy('billing.plans.effective')} {new Date(preview.effectiveAt).toLocaleDateString()}
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
                              {copy('billing.plans.continueToCheckout')}
                            </Button>
                          </div>
                        ) : null}
                      </div>
                    ) : (
                      <div className="mt-5 rounded-xl border border-dashed border-border bg-background-light/50 px-4 py-3 text-center text-[11px] font-medium uppercase tracking-[0.1em] text-muted">
                        {copy('billing.plans.activePlan')}
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
                eyebrow={copy('billing.wallet.eyebrow')}
                title={copy('billing.wallet.title')}
                description={copy('billing.wallet.description')}
                className="mb-4"
              />

              {availableGateways.length > 1 ? (
                <div className="mb-5 flex items-center gap-3">
                  <p className="text-[11px] font-medium uppercase tracking-[0.1em] text-muted/80">{copy('billing.wallet.payWith')}</p>
                  <div className="inline-flex rounded-xl border border-border bg-background-light p-1">
                    {(['stripe', 'paypal'] as const).filter((gw) => availableGateways.includes(gw)).map((gw) => (
                      <button
                        key={gw}
                        type="button"
                        onClick={() => setSelectedGateway(gw)}
                        className={`rounded-lg px-3 py-1.5 text-xs font-medium transition-[color,background-color,box-shadow] duration-200 ${
                          selectedGateway === gw
                            ? 'bg-emerald-700 text-white shadow-sm'
                            : 'text-navy hover:bg-surface'
                        }`}
                      >
                        {gw === 'stripe' ? copy('billing.wallet.gateway.stripe') : copy('billing.wallet.gateway.paypal')}
                      </button>
                    ))}
                  </div>
                </div>
              ) : null}

              <div className="grid grid-cols-2 gap-3">
                {topUpTiers.length === 0 ? (
                  <div className="col-span-2 rounded-2xl border border-dashed border-border bg-background-light p-5 text-center text-sm text-muted">
                    {copy('billing.wallet.tiersEmpty')}
                    <Button className="mt-3" disabled aria-disabled="true" fullWidth>
                      {copy('billing.wallet.topUpUnavailable')}
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
                      className={`relative rounded-2xl border p-4 text-left transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200 disabled:cursor-not-allowed disabled:opacity-60 ${
                        tier.isPopular
                          ? 'border-emerald-300 bg-surface shadow-md'
                          : 'border-border bg-background-light hover:border-emerald-200 hover:shadow-sm'
                      }`}
                    >
                      {tier.isPopular ? (
                        <span className="absolute -top-2 right-3 rounded-full bg-emerald-700 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-[0.08em] text-white">
                          {copy('billing.wallet.popular')}
                        </span>
                      ) : null}
                      <p className="text-lg font-semibold tracking-tight text-navy">
                        {formatCurrency(tier.amount, walletCurrency)}
                      </p>
                      <p className="text-xs text-muted">
                        {tier.credits} {copy('billing.wallet.creditsSuffix')}
                        {tier.bonus > 0 ? (
                          <span className="ml-1 font-semibold text-emerald-700">+{tier.bonus} {copy('billing.wallet.bonusSuffix')}</span>
                        ) : null}
                      </p>
                      {tier.label ? (
                        <p className="mt-1 text-[10px] font-medium uppercase tracking-[0.1em] text-muted/80">
                          {tier.label}
                        </p>
                      ) : null}
                      {busyKey === `topup:${tier.amount}` ? (
                        <p className="mt-2 text-xs font-medium text-emerald-700">{copy('billing.wallet.processing')}</p>
                      ) : null}
                    </motion.button>
                  ))
                )}
              </div>

              <div className="mt-6">
                <Input
                  label={copy('billing.wallet.couponLabel')}
                  value={couponCode}
                  onChange={(event) => setCouponCode(event.target.value.toUpperCase())}
                  placeholder={copy('billing.wallet.couponPlaceholder')}
                  hint={copy('billing.wallet.couponHint')}
                />
              </div>
            </section>

            {/* Add-ons panel */}
            <section>
              <LearnerSurfaceSectionHeader
                eyebrow={copy('billing.addons.eyebrow')}
                title={copy('billing.addons.title')}
                description={copy('billing.addons.description')}
                className="mb-4"
              />
              {activeAddOns.length > 0 ? (
                <div className="mb-4 rounded-2xl border border-primary/15 bg-primary/5 p-4">
                  <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-primary">
                    {copy('billing.addons.activeOnAccount')}
                  </p>
                  <ul className="mt-2 space-y-1">
                    {activeAddOns.map((addOn) => (
                      <li key={addOn.id} className="flex items-center justify-between text-sm">
                        <span className="font-semibold text-navy">{addOn.name}</span>
                        <span className="text-xs font-medium text-primary tabular-nums">
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
                      checkoutProductType === 'review_credits'
                        ? copy('billing.addons.purchaseCredits')
                        : copy('billing.addons.purchaseAddon');
                    return (
                      <article
                        key={addOn.id}
                        className="rounded-2xl border border-border bg-surface p-5 shadow-sm"
                      >
                        <div className="flex flex-wrap items-start justify-between gap-3">
                          <div>
                            <p className="text-[11px] font-medium uppercase tracking-[0.1em] text-muted/80">
                              {addOn.productType.replace(/_/g, ' ')}
                            </p>
                            <h3 className="mt-1 text-base font-semibold tracking-tight text-navy">{addOn.name}</h3>
                            <p className="mt-2 text-sm text-muted">{addOn.description}</p>
                            <div className="mt-3 flex flex-wrap gap-2 text-[11px] font-medium text-muted">
                              <span className="rounded-full border border-border/60 px-2.5 py-0.5">
                                {addOn.quantity} {copy('billing.addons.creditsSuffix')}
                              </span>
                              <span className="rounded-full border border-border/60 px-2.5 py-0.5">
                                {addOn.interval}
                              </span>
                              {addOn.isRecurring ? (
                                <span className="rounded-full border border-border/60 px-2.5 py-0.5">
                                  {copy('billing.addons.recurring')}
                                </span>
                              ) : null}
                            </div>
                          </div>
                          <div className="rounded-xl bg-background-light px-3 py-2 text-sm font-semibold tabular-nums text-navy">
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
                  {copy('billing.addons.empty')}
                </div>
              )}

              {lockedAddOns.length > 0 ? (
                <div className="mt-4 space-y-3">
                  <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">
                    Available on other plans
                  </p>
                  {lockedAddOns.map((addOn) => {
                    const requiredPlans = addOn.compatiblePlanCodes
                      .map((code) => plans.find((plan) => plan.code === code)?.name ?? code)
                      .join(' or ');
                    return (
                      <article
                        key={addOn.id}
                        className="rounded-2xl border border-dashed border-border bg-background-light p-5 opacity-80"
                      >
                        <div className="flex flex-wrap items-start justify-between gap-3">
                          <div>
                            <h3 className="text-base font-semibold tracking-tight text-navy">{addOn.name}</h3>
                            <p className="mt-2 text-sm text-muted">{addOn.description}</p>
                            <span className="mt-3 inline-flex items-center rounded-full border border-warning/40 bg-warning/10 px-2.5 py-0.5 text-[11px] font-medium text-navy">
                              {requiredPlans ? `Requires the ${requiredPlans} plan` : 'Requires a different plan'}
                            </span>
                          </div>
                          <div className="rounded-xl bg-surface px-3 py-2 text-sm font-semibold tabular-nums text-navy">
                            {addOn.price}
                          </div>
                        </div>
                        <Button
                          className="mt-4"
                          fullWidth
                          variant="outline"
                          onClick={() => setActiveTab('plans')}
                        >
                          View plans
                        </Button>
                      </article>
                    );
                  })}
                </div>
              ) : null}
            </section>
          </div>

          {/* Full transaction history */}
          <section className="mt-6 rounded-2xl border border-border bg-surface p-6 shadow-sm">
            <div className="flex items-center justify-between gap-3">
              <div>
                <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">{copy('billing.txn.eyebrow')}</p>
                <h3 className="mt-1 text-base font-semibold text-navy">{copy('billing.txn.title')}</h3>
              </div>
              <button
                type="button"
                onClick={() => loadWallet()}
                className="text-xs font-medium text-primary transition-colors hover:text-primary/80"
              >
                {copy('billing.txn.refresh')}
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
                  {copy('billing.txn.empty')}
                </p>
              </div>
            ) : (
              <div className="mt-4 max-h-[420px] overflow-y-auto pr-1">
                <ul className="divide-y divide-border/60">
                  {wallet.transactions.map((tx) => (
                    <li
                      key={tx.id}
                      className="flex items-center justify-between gap-3 py-3 first:pt-0 last:pb-0"
                    >
                      <div className="flex items-center gap-3">
                        <div
                          className={`rounded-lg p-1.5 ${tx.amount >= 0 ? 'bg-success/10 text-success' : 'bg-danger/10 text-danger'}`}
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
                          className={`text-sm font-semibold tabular-nums ${tx.amount >= 0 ? 'text-success' : 'text-danger'}`}
                        >
                          {tx.amount >= 0 ? '+' : ''}
                          {tx.amount}
                        </p>
                        <p className="text-[11px] text-muted tabular-nums">{copy('billing.txn.balancePrefix')} {tx.balanceAfter}</p>
                      </div>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </section>
        </TabPanel>

        {/* ── AI CREDITS ────────────────────────────────────────── */}
        <TabPanel id="ai-credits" activeTab={activeTab}>
          <LearnerSurfaceSectionHeader
            eyebrow={copy('billing.ai.eyebrow')}
            title={copy('billing.ai.title')}
            description={copy('billing.ai.description')}
            className="mb-4"
          />

          {aiPackagesLoading ? (
            <div className="grid gap-4 lg:grid-cols-3">
              {[1, 2, 3].map((i) => (
                <Skeleton key={i} className="h-72 rounded-2xl" />
              ))}
            </div>
          ) : !aiPackages ? (
            <div className="rounded-2xl border border-dashed border-border bg-background-light p-5 text-center text-sm text-muted">
              {copy('billing.ai.unavailable')}
            </div>
          ) : (
            <>
              <div className="mb-5 inline-flex rounded-xl border border-border bg-background-light p-1">
                {(
                  [
                    { id: 'full' as const, label: copy('billing.ai.toggle.full') },
                    { id: 'separate' as const, label: copy('billing.ai.toggle.separate') },
                  ]
                ).map((view) => (
                  <button
                    key={view.id}
                    type="button"
                    onClick={() => setAiPackageView(view.id)}
                    className={`rounded-lg px-4 py-1.5 text-sm font-medium transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200 ${
                      aiPackageView === view.id
                        ? 'bg-emerald-700 text-white shadow-sm'
                        : 'text-navy hover:bg-surface'
                    }`}
                    aria-pressed={aiPackageView === view.id}
                  >
                    {view.label}
                  </button>
                ))}
              </div>

              <p className="mb-5 text-sm text-muted">
                {aiPackageView === 'full'
                  ? copy('billing.ai.fullIntro')
                  : copy('billing.ai.separateIntro')}
              </p>

              {aiPackageView === 'full' ? (
                aiPackages.full.length > 0 ? (
                  <div className="grid gap-4 lg:grid-cols-3">{aiPackages.full.map(renderAiPackageCard)}</div>
                ) : (
                  <div className="rounded-2xl border border-dashed border-border bg-background-light p-5 text-center text-sm text-muted">
                    {copy('billing.ai.fullEmpty')}
                  </div>
                )
              ) : (
                <div className="space-y-6">
                  {AI_PACKAGE_SUBTEST_SECTIONS.map((section) => {
                    const packages = aiPackages.separate[section.key];
                    if (!packages || packages.length === 0) return null;
                    return (
                      <section key={section.key}>
                        <div
                          className={`mb-3 inline-block rounded-lg px-3 py-1.5 text-sm font-semibold text-white ${section.headerClass}`}
                        >
                          {copy(section.copyKey)} {copy('billing.ai.sectionSuffix')}
                        </div>
                        <div className="grid gap-4 lg:grid-cols-3">{packages.map(renderAiPackageCard)}</div>
                      </section>
                    );
                  })}
                </div>
              )}

              {aiPackages.mock.length > 0 ? (
                <div className="mt-8">
                  <LearnerSurfaceSectionHeader
                    eyebrow={copy('billing.ai.mock.eyebrow')}
                    title={copy('billing.ai.mock.title')}
                    description={copy('billing.ai.mock.description')}
                    className="mb-4"
                  />
                  <div className="grid gap-4 lg:grid-cols-3">{aiPackages.mock.map(renderAiPackageCard)}</div>
                </div>
              ) : null}
            </>
          )}
        </TabPanel>

        {/* ── INVOICES ──────────────────────────────────────────── */}
        <TabPanel id="invoices" activeTab={activeTab}>
          <LearnerSurfaceSectionHeader
            eyebrow={copy('billing.invoices.eyebrow')}
            title={copy('billing.invoices.title')}
            description={copy('billing.invoices.description')}
            className="mb-4"
          />
          <div className="overflow-hidden rounded-2xl border border-border bg-surface shadow-sm">
            {invoices.length === 0 ? (
              <div className="p-10 text-center">
                <Receipt className="mx-auto h-10 w-10 text-muted/40" />
                <p className="mt-3 text-sm text-muted">
                  {copy('billing.invoices.empty')}
                </p>
              </div>
            ) : (
              <ul className="divide-y divide-border/60">
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
                        <p className="text-sm font-semibold text-navy">
                          {new Date(invoice.date).toLocaleDateString()}
                        </p>
                        <p className="text-sm text-muted">
                          {invoice.amount} · <span title={`Invoice reference (masked)`}>{maskProviderId(invoice.id)}</span>
                        </p>
                      </div>
                    </div>
                    <div className="flex items-center gap-3">
                      <span className="inline-flex items-center gap-1.5 text-sm font-extrabold text-success">
                        <span className="h-1.5 w-1.5 rounded-full bg-success" aria-hidden="true" />
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
                        {copy('billing.invoices.download')}
                      </Button>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>
          {!invoiceDownloadsAvailable ? (
            <p className="mt-3 text-xs text-muted">
              {copy('billing.invoices.unavailableNote')}
            </p>
          ) : null}
        </TabPanel>
      </div>
    </LearnerDashboardShell>
  );
}
