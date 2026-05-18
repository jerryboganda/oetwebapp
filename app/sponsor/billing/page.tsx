'use client';

import { useEffect, useState } from 'react';
import { CreditCard, Building2, Users } from 'lucide-react';
import { fetchSponsorBilling, isApiError, type SponsorBillingData, type SponsorInvoice } from '@/lib/api';
import { Card } from '@/components/ui/card';
import { StatCard } from '@/components/ui/stat-card';

function formatCurrency(amount: number, currency: string | null | undefined): string {
  const normalizedCurrency = currency && /^[A-Z]{3}$/.test(currency) ? currency : 'GBP';
  return new Intl.NumberFormat('en-GB', {
    style: 'currency',
    currency: normalizedCurrency,
  }).format(amount);
}

function formatInvoiceDate(value: string): string {
  return new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium' }).format(new Date(value));
}

function invoiceReference(invoice: SponsorInvoice): string {
  if (invoice.gatewayTransactionId) {
    return invoice.gatewayTransactionId;
  }
  return invoice.id.slice(0, 8).toUpperCase();
}

export default function SponsorBillingPage() {
  const [data, setData] = useState<SponsorBillingData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const result = await fetchSponsorBilling();
        if (!cancelled) {
          setData(result);
        }
      } catch (err) {
        if (!cancelled) {
          setError(isApiError(err) ? err.userMessage : 'Failed to load billing data.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void load();
    return () => { cancelled = true; };
  }, []);

  if (loading) {
    return (
      <div className="space-y-6">
        <h1 className="text-2xl font-bold text-navy">Billing</h1>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-28 animate-pulse rounded-2xl border border-border bg-surface shadow-sm" />
          ))}
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="space-y-6">
        <h1 className="text-2xl font-bold text-navy">Billing</h1>
        <Card padding="lg" className="text-center">
          <p className="text-sm text-danger">{error}</p>
        </Card>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-navy">Billing</h1>
        {data?.organizationName && (
          <p className="mt-1 text-sm text-muted">{data.organizationName}</p>
        )}
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <StatCard
          label="Active Sponsorships"
          value={data?.totalSponsorships ?? 0}
          icon={<Users />}
          tone="info"
        />
        <StatCard
          label="This Month"
          value={formatCurrency(data?.currentMonthSpend ?? 0, data?.currency)}
          icon={<CreditCard />}
          tone="warning"
        />
        <StatCard
          label="Total Spend"
          value={formatCurrency(data?.totalSpend ?? 0, data?.currency)}
          icon={<Building2 />}
        />
      </div>

      {/* Billing cycle */}
      <Card padding="lg">
        <h2 className="text-lg font-bold text-navy mb-4">Billing Details</h2>
        <dl className="grid gap-4 sm:grid-cols-2">
          <div>
            <dt className="text-sm font-medium text-muted">Billing Cycle</dt>
            <dd className="mt-1 text-sm text-navy capitalize">{data?.billingCycle ?? 'monthly'}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-muted">Account</dt>
            <dd className="mt-1 text-sm text-navy">{data?.sponsorName ?? '—'}</dd>
          </div>
        </dl>
      </Card>

      {/* Invoices */}
      <Card padding="lg">
        <h2 className="text-lg font-bold text-navy mb-4">Invoices</h2>
        {(data?.invoices?.length ?? 0) === 0 ? (
          <p className="text-sm text-muted">No invoices yet. Invoices will appear here once billing is active.</p>
        ) : (
          <div className="overflow-hidden rounded-2xl border border-border">
            <div className="grid grid-cols-[1.2fr_1.4fr_0.8fr_0.8fr] gap-3 bg-surface-subtle px-4 py-3 text-xs font-bold uppercase tracking-wide text-muted">
              <span>Invoice</span>
              <span>Learner</span>
              <span>Status</span>
              <span className="text-right">Amount</span>
            </div>
            <div className="divide-y divide-border">
              {data?.invoices.map((invoice) => (
                <div
                  key={invoice.id}
                  className="grid grid-cols-1 gap-3 px-4 py-4 text-sm sm:grid-cols-[1.2fr_1.4fr_0.8fr_0.8fr] sm:items-center"
                >
                  <div>
                    <p className="font-semibold text-navy">{invoiceReference(invoice)}</p>
                    <p className="mt-1 text-xs text-muted">{formatInvoiceDate(invoice.createdAt)}</p>
                  </div>
                  <div>
                    <p className="font-medium text-navy">{invoice.learnerEmail}</p>
                    <p className="mt-1 text-xs text-muted">{invoice.productType ?? invoice.transactionType}</p>
                  </div>
                  <div>
                    <span className="inline-flex rounded-full bg-success/10 px-2.5 py-1 text-xs font-semibold capitalize text-success">
                      {invoice.status}
                    </span>
                  </div>
                  <p className="font-semibold text-navy sm:text-right">
                    {formatCurrency(invoice.amount, invoice.currency)}
                  </p>
                </div>
              ))}
            </div>
          </div>
        )}
      </Card>
    </div>
  );
}
