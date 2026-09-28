'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowRight, CreditCard, Receipt, Wallet } from 'lucide-react';

import {
  fetchSubscriptionInvoices,
  fetchSubscriptionMe,
  type SubscriptionInvoice,
  type SubscriptionMe,
} from '@/lib/api';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { formatMoney } from '@/lib/money';
import { formatDate } from '@/lib/domain/datetime';
import { BillingPortalLauncher } from '@/components/billing/BillingPortalLauncher';

/**
 * Account-billing overview surface. Mirrors what the legacy
 * `/billing` page already shows but in a slimmer, account-style shell.
 * Power features (plan switching, checkout, score guarantee redemption)
 * stay on `/billing`; this page links over for them.
 */
export default function AccountBillingPage() {
  const [subscription, setSubscription] = useState<SubscriptionMe | null>(null);
  const [recentInvoices, setRecentInvoices] = useState<SubscriptionInvoice[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      setLoading(true);
      try {
        const [sub, invoices] = await Promise.all([
          fetchSubscriptionMe(),
          fetchSubscriptionInvoices({ pageSize: 3 }),
        ]);
        if (cancelled) return;
        setSubscription(sub);
        setRecentInvoices(invoices.items.slice(0, 3));
      } catch (err) {
        if (!cancelled) {
          console.error(err);
          setError(err instanceof Error ? err.message : 'Failed to load billing overview.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <div className="mx-auto max-w-5xl space-y-5 sm:space-y-8 px-4 py-10">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-3xl font-bold text-navy">Billing overview</h1>
          <p className="mt-1 text-sm text-muted">
            Manage your subscription, view recent invoices, and update your payment methods.
          </p>
        </div>
        <Button asChild variant="outline">
          <Link href="/billing">
            Open full billing dashboard <ArrowRight className="h-4 w-4" aria-hidden="true" />
          </Link>
        </Button>
      </header>

      {error ? (
        <InlineAlert variant="error" title="Could not load billing">
          {error}
        </InlineAlert>
      ) : null}

      <section className="grid gap-4 md:grid-cols-3">
        <Card padding="lg">
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-muted">
            <Wallet className="h-4 w-4" aria-hidden="true" /> Current plan
          </div>
          {loading ? (
            <Skeleton className="mt-3 h-12 w-full" />
          ) : subscription ? (
            <>
              <p className="mt-2 text-lg font-semibold text-navy">{subscription.planName}</p>
              <p className="text-sm text-muted">
                {formatMoney(subscription.price, { currency: subscription.currency })} / {subscription.interval}
              </p>
              <p className="mt-2 text-xs text-muted">
                {subscription.endDate
                  ? `Access expires: ${formatDate(subscription.endDate)}`
                  : subscription.nextRenewalAt
                    ? `Next renewal: ${formatDate(subscription.nextRenewalAt)}`
                    : 'Not scheduled'}
              </p>
            </>
          ) : (
            <>
              <p className="mt-2 text-sm text-muted">No active subscription.</p>
              <Button asChild variant="outline" className="mt-3" size="sm">
                <Link href="/catalog">Browse plans</Link>
              </Button>
            </>
          )}
        </Card>

        <Card padding="lg">
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-muted">
            <Receipt className="h-4 w-4" aria-hidden="true" /> Wallet
          </div>
          {loading ? (
            <Skeleton className="mt-3 h-12 w-full" />
          ) : (
            <>
              <p className="mt-2 text-lg font-semibold text-navy">
                {subscription?.walletBalance != null
                  ? formatMoney(subscription.walletBalance, { currency: subscription.walletCurrency ?? subscription.currency })
                  : 'Not available'}
              </p>
              <p className="text-xs text-muted">Credits and refunds applied here first.</p>
            </>
          )}
        </Card>

        <Card padding="lg">
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-muted">
            <CreditCard className="h-4 w-4" aria-hidden="true" /> Payment method
          </div>
          <p className="mt-2 text-sm text-muted">Stripe stores your card details securely.</p>
          <BillingPortalLauncher variant="outline" className="mt-3">
            Update card
          </BillingPortalLauncher>
        </Card>
      </section>

      <section>
        <div className="flex items-center justify-between">
          <h2 className="text-xl font-bold text-navy">Recent invoices</h2>
          <Link href="/account/billing/invoices" className="text-sm font-medium text-primary hover:underline">
            View all
          </Link>
        </div>
        <div className="mt-3 space-y-2">
          {loading ? (
            <div className="space-y-2">{[0, 1, 2].map((i) => <Skeleton key={i} className="h-14 w-full rounded-lg" />)}</div>
          ) : recentInvoices.length === 0 ? (
            <p className="rounded-lg border border-dashed border-border bg-background-light px-4 py-6 text-center text-sm text-muted">No invoices yet.</p>
          ) : (
            recentInvoices.map((invoice) => (
              <div
                key={invoice.invoiceId}
                className="flex items-center justify-between gap-3 rounded-lg border border-border bg-surface px-4 py-3 text-sm"
              >
                <div className="min-w-0">
                  <p className="truncate font-medium text-navy">{invoice.number ?? invoice.invoiceId}</p>
                  <p className="text-xs text-muted">
                    {invoice.date ? formatDate(invoice.date) : '-'} - {invoice.status}
                  </p>
                </div>
                <p className="shrink-0 font-semibold text-navy">
                  {formatMoney(invoice.amount, { currency: invoice.currency })}
                </p>
              </div>
            ))
          )}
        </div>
      </section>

      <section className="grid gap-3 sm:grid-cols-2">
        <Link
          href="/account/billing/invoices"
          className="rounded-2xl border border-border bg-surface p-5 shadow-sm transition-[border-color,box-shadow] hover:border-border-hover hover:shadow-clinical focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          <h3 className="font-semibold text-navy">All invoices</h3>
          <p className="mt-1 text-sm text-muted">Download PDFs or revisit receipts.</p>
        </Link>
        <Link
          href="/account/billing/payment-methods"
          className="rounded-2xl border border-border bg-surface p-5 shadow-sm transition-[border-color,box-shadow] hover:border-border-hover hover:shadow-clinical focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
        >
          <h3 className="font-semibold text-navy">Payment methods</h3>
          <p className="mt-1 text-sm text-muted">Manage cards via Stripe Customer Portal.</p>
        </Link>
      </section>
    </div>
  );
}
