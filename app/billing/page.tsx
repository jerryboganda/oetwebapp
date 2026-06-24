'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Calendar,
  CheckCircle2,
  CreditCard,
  Download,
  FileText,
  Receipt,
  ShieldCheck,
  Snowflake,
} from 'lucide-react';
import { useRouter, useSearchParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import {
  fetchFreezeStatus,
  downloadInvoice,
  fetchBilling,
  fetchBillingContent,
} from '@/lib/api';
import { makeBillingCopy } from '@/lib/billing-copy-defaults';
import { formatBillingInterval, formatSubscriptionStatus } from '@/lib/domain/format';
import type { BillingData } from '@/lib/billing-types';
import type { LearnerFreezeStatus } from '@/lib/types/freeze';
import { isFreezeEffective, maskProviderId } from '@/components/domain/billing';
import { FreezeRequestModal } from '@/components/billing/FreezeRequestModal';

// ─── Helpers ─────────────────────────────────────────────────────────

function formatOptionalDate(value?: string | null, fallback = 'Not scheduled') {
  if (!value) return fallback;
  const time = new Date(value).getTime();
  return Number.isNaN(time) ? fallback : new Date(time).toLocaleDateString();
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
      return { variant: 'error' as const, message: `The ${gatewayLabel} checkout ${payment} before completion.` };
    default:
      return { variant: 'info' as const, message: `Checkout status: ${payment}.` };
  }
}

type BillingTabId = 'overview' | 'invoices';

const BILLING_TABS: Array<{ id: BillingTabId; label: string; icon: React.ReactNode }> = [
  { id: 'overview', label: 'Overview', icon: <CreditCard className="h-4 w-4" /> },
  { id: 'invoices', label: 'Invoices', icon: <Receipt className="h-4 w-4" /> },
];

const TAB_COPY_KEYS: Record<BillingTabId, string> = {
  overview: 'billing.tab.overview',
  invoices: 'billing.tab.invoices',
};

// ─── Component ───────────────────────────────────────────────────────

export default function BillingPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const paymentStatus = searchParams?.get('payment') ?? null;
  const paymentGateway = searchParams?.get('gateway') ?? null;
  const initialTab = (searchParams?.get('tab') as BillingTabId | null) ?? 'overview';

  const [activeTab, setActiveTab] = useState<BillingTabId>(
    BILLING_TABS.some((t) => t.id === initialTab) ? initialTab : 'overview',
  );

  const [data, setData] = useState<BillingData | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);

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
  const [freezeModalOpen, setFreezeModalOpen] = useState(false);

  // Double-submit guard for the invoice download handler.
  const submittingRef = useRef(false);

  const paymentBanner = useMemo(
    () => getPaymentBanner(paymentStatus, paymentGateway),
    [paymentGateway, paymentStatus],
  );
  const isFrozen = isFreezeEffective(freezeState);
  const isPastDue = data?.status?.toLowerCase() === 'pastdue' || data?.status?.toLowerCase() === 'past_due';
  const freezeEligible = freezeState
    ? Boolean(
        freezeState.policy?.isEnabled &&
          freezeState.policy?.selfServiceEnabled &&
          !freezeState.currentFreeze &&
          freezeState.entitlement?.used !== true &&
          freezeState.eligibility?.eligible !== false &&
          freezeState.eligibility?.canRequest !== false,
      )
    : false;
  const freezeDisabledReason = freezeState?.entitlement?.used
    ? 'You’ve already used your one freeze for this subscription. Buying a new course renews it.'
    : 'A freeze isn’t available for your account right now.';

  const loadBilling = useCallback(() => {
    setLoading(true);
    setError(null);
    Promise.allSettled([fetchBilling(), fetchFreezeStatus()])
      .then(([billingResult, freezeResult]) => {
        if (billingResult.status === 'rejected') {
          throw billingResult.reason;
        }
        setData(billingResult.value);

        if (freezeResult.status === 'fulfilled') {
          setFreezeState(freezeResult.value as LearnerFreezeStatus);
          setFreezeLoadFailed(false);
        } else {
          setFreezeState(null);
          setFreezeLoadFailed(true);
        }
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load billing data.'))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    analytics.track('content_view', { page: 'subscriptions' });
    loadBilling();
  }, [loadBilling]);

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
      anchor.download = `${invoiceId}.pdf`;
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

  const invoices = data.invoices ?? [];
  const invoiceDownloadsAvailable = data.entitlements?.invoiceDownloadsAvailable ?? false;
  const recentInvoices = invoices.slice(0, 3);

  const currentFreeze = freezeState?.currentFreeze ?? null;
  const freezeStart = currentFreeze?.startedAt ?? currentFreeze?.scheduledStartAt ?? null;

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
            { icon: CheckCircle2, label: 'Status', value: formatSubscriptionStatus(data.status) },
            { icon: Calendar, label: 'Subscription ends', value: formatOptionalDate(data.nextRenewal) },
            { icon: Receipt, label: copy('billing.overview.invoiceAccess'), value: invoiceDownloadsAvailable ? copy('billing.overview.invoiceAccessAvailable') : copy('billing.overview.invoiceAccessUnavailable') },
          ]}
        />

        {/* Status banners — only render when relevant */}
        {(paymentBanner || isPastDue || freezeLoadFailed || error || success) && (
          <div className="space-y-2" role="status" aria-live="polite">
            {paymentBanner ? <InlineAlert variant={paymentBanner.variant}>{paymentBanner.message}</InlineAlert> : null}
            {isPastDue ? (
              <InlineAlert variant="error">
                Your last payment failed. Please{' '}
                <a href="/billing/update-card" className="underline font-medium">update your payment method</a>
                {' '}to restore full access.
              </InlineAlert>
            ) : null}
            {freezeLoadFailed ? (
              <InlineAlert variant="error">
                Freeze status could not be verified. Refresh the page to see the latest state of your subscription.
              </InlineAlert>
            ) : null}
            {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
            {success ? <InlineAlert variant="success">{success}</InlineAlert> : null}
          </div>
        )}

        {/* Section navigation */}
        <Tabs
          tabs={billingTabs}
          activeTab={activeTab}
          onChange={(id) => setActiveTab(id as BillingTabId)}
        />

        {/* ── OVERVIEW ────────────────────────────────────────────── */}
        <TabPanel id="overview" activeTab={activeTab}>
          <div className="grid gap-6 lg:grid-cols-[1.4fr_1fr] lg:items-stretch">
            {/* Current subscription */}
            <section className="flex h-full flex-col rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">{copy('billing.overview.currentSubscription')}</p>
                  <h2 className="mt-2 text-2xl font-semibold tracking-tight text-navy">{data.currentPlan}</h2>
                  <p className="mt-1 text-sm text-muted">
                    {data.price} / {formatBillingInterval(data.interval)}
                  </p>
                  {data.planDescription ? (
                    <p className="mt-3 max-w-md text-sm leading-6 text-muted">{data.planDescription}</p>
                  ) : null}
                </div>
                <span className="inline-flex items-center gap-2 text-sm font-extrabold text-success">
                  <span className="h-1.5 w-1.5 rounded-full bg-success" aria-hidden="true" />
                  {formatSubscriptionStatus(data.status)}
                </span>
              </div>

              <dl className="mt-6 grid gap-3 sm:grid-cols-2">
                <div className="rounded-xl border border-border/70 bg-background-light/60 p-4">
                  <dt className="text-[11px] font-medium uppercase tracking-[0.1em] text-muted/80">Subscription ends</dt>
                  <dd className="mt-1.5 text-sm font-semibold text-navy">
                    {formatOptionalDate(data.nextRenewal)}
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
                <Button variant="outline" onClick={() => setActiveTab('invoices')}>
                  <Receipt className="h-4 w-4" /> {copy('billing.overview.viewInvoices')}
                </Button>
              </div>
            </section>

            {/* Subscription freeze */}
            <section className="flex h-full flex-col rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-[11px] font-semibold uppercase tracking-[0.12em] text-muted">Subscription</p>
                  <h3 className="mt-2 text-lg font-semibold tracking-tight text-navy">Subscription freeze</h3>
                </div>
                <div className={`rounded-xl p-2.5 ${isFrozen ? 'bg-sky-500/10 text-sky-600' : 'bg-success/10 text-success'}`}>
                  {isFrozen ? <Snowflake className="h-5 w-5" /> : <CheckCircle2 className="h-5 w-5" />}
                </div>
              </div>

              {isFrozen && currentFreeze ? (
                <>
                  <dl className="mt-4 space-y-3 text-sm">
                    <div className="flex items-center justify-between gap-3">
                      <dt className="text-muted">Status</dt>
                      <dd className="font-semibold text-navy">{currentFreeze.status}</dd>
                    </div>
                    {currentFreeze.reason ? (
                      <div className="flex items-start justify-between gap-3">
                        <dt className="text-muted">Reason</dt>
                        <dd className="max-w-[60%] text-right font-semibold text-navy">{currentFreeze.reason}</dd>
                      </div>
                    ) : null}
                    <div className="flex items-center justify-between gap-3">
                      <dt className="text-muted">From</dt>
                      <dd className="font-semibold text-navy">{formatOptionalDate(freezeStart, 'Now')}</dd>
                    </div>
                    <div className="flex items-center justify-between gap-3">
                      <dt className="text-muted">Until</dt>
                      <dd className="font-semibold text-navy">{formatOptionalDate(currentFreeze.endedAt, 'Indefinite')}</dd>
                    </div>
                    {currentFreeze.durationDays ? (
                      <div className="flex items-center justify-between gap-3">
                        <dt className="text-muted">Duration</dt>
                        <dd className="font-semibold text-navy">{currentFreeze.durationDays} days</dd>
                      </div>
                    ) : null}
                  </dl>
                  <p className="mt-4 text-xs leading-5 text-muted">
                    While your subscription is frozen, access is paused. Your billing history stays available.
                  </p>
                </>
              ) : (
                <div className="mt-4 flex flex-1 flex-col items-center justify-center gap-2 py-6 text-center">
                  <p className="text-sm font-semibold text-navy">No active freeze</p>
                  <p className="text-xs leading-5 text-muted">Your subscription is running normally.</p>
                </div>
              )}

              <div className="mt-auto space-y-2 pt-4">
                {!isFrozen ? (
                  <Button
                    fullWidth
                    onClick={() => setFreezeModalOpen(true)}
                    disabled={!freezeEligible}
                    title={!freezeEligible ? freezeDisabledReason : undefined}
                  >
                    <Snowflake className="h-4 w-4" /> Freeze my subscription
                  </Button>
                ) : null}
                {!isFrozen && !freezeEligible ? (
                  <p className="text-center text-[11px] leading-4 text-muted">{freezeDisabledReason}</p>
                ) : null}
                <Button variant="outline" fullWidth onClick={() => router.push('/freeze')}>
                  View freeze details &amp; history
                </Button>
              </div>
            </section>
          </div>

          {/* Recent invoices */}
          <section className="mt-6 rounded-2xl border border-border bg-surface p-6 shadow-sm">
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
                          {invoice.amount} · <span title="Invoice reference (masked)">{maskProviderId(invoice.id)}</span>
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
                          {invoice.amount} · <span title="Invoice reference (masked)">{maskProviderId(invoice.id)}</span>
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

      <FreezeRequestModal
        open={freezeModalOpen}
        onClose={() => setFreezeModalOpen(false)}
        freezeState={freezeState}
        onCompleted={async () => {
          setSuccess('Your subscription freeze has been submitted.');
          try {
            const refreshed = await fetchFreezeStatus();
            setFreezeState(refreshed as LearnerFreezeStatus);
            setFreezeLoadFailed(false);
          } catch {
            /* status refresh is best-effort; the page reloads it on next visit */
          }
        }}
      />
    </LearnerDashboardShell>
  );
}
