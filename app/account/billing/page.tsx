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
import { formatMoney } from '@/lib/money';
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
    <div className="mx-auto max-w-5xl space-y-8 px-4 py-10">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-3xl font-bold text-navy">Billing overview</h1>
          <p className="mt-1 text-sm text-muted">
            Manage your subscription, view recent invoices, and update your payment methods.
          </p>
        </div>
        <Button asChild variant="outline">
          <Link href="/billing">
            Open full billing dashboard <ArrowRight className="ml-1 h-4 w-4" />
          </Link>
        </Button>
      </header>

      {error ? (
        <InlineAlert variant="error" title="Could not load billing">
          {error}
        </InlineAlert>
      ) : null}

      <section className="grid gap-4 md:grid-cols-3">
        <div className="rounded-2xl border border-border bg-background p-5 shadow-sm">
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-muted">
            <Wallet className="h-4 w-4" /> Current plan
          </div>
          {loading ? (
            <p className="mt-3 text-sm text-muted">Loading...</p>
          ) : subscription ? (
            <>
              <p className="mt-2 text-lg font-semibold text-navy">{subscription.planName}</p>
              <p className="text-sm text-muted">
                {formatMoney(subscription.price, { currency: subscription.currency })} / {subscription.interval}
              </p>
              <p className="mt-2 text-xs text-muted">
                Next renewal: {subscription.nextRenewalAt ? new Date(subscription.nextRenewalAt).toLocaleDateString() : 'Not scheduled'}
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
        </div>

        <div className="rounded-2xl border border-border bg-background p-5 shadow-sm">
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-muted">
            <Receipt className="h-4 w-4" /> Wallet
          </div>
          {loading ? (
            <p className="mt-3 text-sm text-muted">Loading...</p>
          ) : (
            <>
              <p className="mt-2 text-lg font-semibold text-navy">
                {subscription?.walletBalance != null
                  ? formatMoney(subscription.walletBalance, { currency: subscription.walletCurrency ?? subscription.currency })
                  : 'Not available'}
              </p>
              <p className="text-xs text-muted">Credits and refunds applied here first.</p>
              <Button asChild variant="outline" className="mt-3" size="sm">
                <Link href="/billing?tab=credits">Manage credits</Link>
              </Button>
            </>
          )}
        </div>

        <div className="rounded-2xl border border-border bg-background p-5 shadow-sm">
          <div className="flex items-center gap-2 text-xs uppercase tracking-wider text-muted">
            <CreditCard className="h-4 w-4" /> Payment method
          </div>
          <p className="mt-2 text-sm text-muted">Stripe stores your card details securely.</p>
          <BillingPortalLauncher variant="outline" className="mt-3">
            Update card
          </BillingPortalLauncher>
        </div>
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
            <p className="text-sm text-muted">Loading invoices...</p>
          ) : recentInvoices.length === 0 ? (
            <p className="text-sm text-muted">No invoices yet.</p>
          ) : (
            recentInvoices.map((invoice) => (
              <div
                key={invoice.invoiceId}
                className="flex items-center justify-between rounded-lg border border-border bg-background px-4 py-3 text-sm"
              >
                <div>
                  <p className="font-medium text-navy">{invoice.number ?? invoice.invoiceId}</p>
                  <p className="text-xs text-muted">
                    {invoice.date ? new Date(invoice.date).toLocaleDateString() : '-'} - {invoice.status}
                  </p>
                </div>
                <p className="font-semibold text-navy">
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
          className="rounded-2xl border border-border bg-background p-5 shadow-sm transition-colors hover:bg-background-light"
        >
          <h3 className="font-semibold text-navy">All invoices</h3>
          <p className="mt-1 text-sm text-muted">Download PDFs or revisit receipts.</p>
        </Link>
        <Link
          href="/account/billing/payment-methods"
          className="rounded-2xl border border-border bg-background p-5 shadow-sm transition-colors hover:bg-background-light"
        >
          <h3 className="font-semibold text-navy">Payment methods</h3>
          <p className="mt-1 text-sm text-muted">Manage cards via Stripe Customer Portal.</p>
        </Link>
      </section>
    </div>
  );
}
