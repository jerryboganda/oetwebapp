'use client';

import { useEffect, useState } from 'react';
import { CreditCard, Building2, Users, FileText, Download, Receipt, Clock } from 'lucide-react';
import { fetchSponsorBilling, downloadSponsorInvoice, isApiError, type SponsorBillingData, type SponsorInvoice } from '@/lib/api';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';

function formatCurrency(amount: number, currency = 'AUD') {
  const safeCurrency = /^[A-Z]{3}$/i.test(currency) ? currency.toUpperCase() : 'AUD';
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency: safeCurrency,
    minimumFractionDigits: 2,
  }).format(amount);
}

function formatDate(dateStr: string | null) {
  if (!dateStr) return '—';
  const date = new Date(dateStr);
  if (Number.isNaN(date.getTime())) return '—';
  return new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  }).format(date);
}

function formatCurrencies(data: SponsorBillingData | null) {
  if (data?.currencies && data.currencies.length > 0) {
    return data.currencies.join(' / ');
  }

  return data?.currency ?? 'AUD';
}

function formatSeatUsage(data: SponsorBillingData | null) {
  const seats = data?.seats;
  if (!seats) return '0';
  if (!seats.capacityTracked) return String(seats.assigned);
  return `${seats.assigned} / ${seats.capacity}`;
}

function InvoiceStatusBadge({ status }: { status: string }) {
  const statusVariants: Record<string, { variant: 'success' | 'warning' | 'danger' | 'muted'; label: string }> = {
    paid: { variant: 'success', label: 'Paid' },
    succeeded: { variant: 'success', label: 'Succeeded' },
    completed: { variant: 'success', label: 'Completed' },
    pending: { variant: 'warning', label: 'Pending' },
    overdue: { variant: 'danger', label: 'Overdue' },
    refunded: { variant: 'muted', label: 'Refunded' },
    draft: { variant: 'muted', label: 'Draft' },
    cancelled: { variant: 'muted', label: 'Cancelled' },
  };

  const config = statusVariants[status.toLowerCase()] || { variant: 'muted', label: status };

  return <Badge variant={config.variant}>{config.label}</Badge>;
}

export default function SponsorBillingPage() {
  const [data, setData] = useState<SponsorBillingData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyKey, setBusyKey] = useState<string | null>(null);

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

  const handleDownloadInvoice = async (invoice: SponsorInvoice) => {
    const busyKeyValue = `invoice:${invoice.invoiceId}`;
    setBusyKey(busyKeyValue);
    setDownloadError(null);

    try {
      const objectUrl = await downloadSponsorInvoice(invoice.invoiceId);
      const anchor = document.createElement('a');
      anchor.href = objectUrl;
      anchor.download = `invoice-${invoice.invoiceId}.txt`;
      anchor.click();
      URL.revokeObjectURL(objectUrl);
    } catch (err) {
      setDownloadError(err instanceof Error ? err.message : 'Could not download that invoice.');
    } finally {
      setBusyKey(null);
    }
  };

  if (loading) {
    return (
      <div className="space-y-6">
        <h1 className="text-2xl font-bold text-navy">Billing</h1>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {[1, 2, 3, 4].map((summaryIndex) => (
            <div key={summaryIndex} className="page-surface h-28 animate-pulse rounded-2xl" />
          ))}
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="space-y-6">
        <h1 className="text-2xl font-bold text-navy">Billing</h1>
        <div className="page-surface rounded-2xl p-6 text-center">
          <p className="text-sm text-red-600">{error}</p>
        </div>
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

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <div className="page-surface rounded-2xl p-6 shadow-sm">
          <div className="flex items-center gap-3 mb-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <Users className="h-5 w-5" />
            </div>
            <span className="text-sm font-medium text-muted">Seat Usage</span>
          </div>
          <p className="text-2xl font-bold text-navy">{formatSeatUsage(data)}</p>
          <p className="mt-1 text-xs text-muted">
            {data?.seats?.active ?? data?.activeSponsorships ?? 0} active, {data?.seats?.pending ?? data?.pendingSponsorships ?? 0} pending
          </p>
        </div>

        <div className="page-surface rounded-2xl p-6 shadow-sm">
          <div className="flex items-center gap-3 mb-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <CreditCard className="h-5 w-5" />
            </div>
            <span className="text-sm font-medium text-muted">This Month</span>
          </div>
          <p className="text-2xl font-bold text-navy">
            {formatCurrency(data?.currentMonthSpend ?? 0, data?.currency ?? 'AUD')}
          </p>
        </div>

        <div className="page-surface rounded-2xl p-6 shadow-sm">
          <div className="flex items-center gap-3 mb-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <Building2 className="h-5 w-5" />
            </div>
            <span className="text-sm font-medium text-muted">Total Spend</span>
          </div>
          <p className="text-2xl font-bold text-navy">
            {formatCurrency(data?.totalSpend ?? 0, data?.currency ?? 'AUD')}
          </p>
        </div>

        <div className="page-surface rounded-2xl p-6 shadow-sm">
          <div className="flex items-center gap-3 mb-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <FileText className="h-5 w-5" />
            </div>
            <span className="text-sm font-medium text-muted">Invoice Count</span>
          </div>
          <p className="text-2xl font-bold text-navy">
            {data?.paidInvoiceCount ?? 0} / {data?.invoiceCount ?? 0}
          </p>
        </div>
      </div>

      <div className="page-surface rounded-2xl p-6 shadow-sm">
        <h2 className="text-lg font-semibold text-navy mb-4">Billing Details</h2>
        <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-6">
          <div>
            <dt className="text-sm font-medium text-muted">Account</dt>
            <dd className="mt-1 text-sm text-navy">{data?.sponsorName ?? '—'}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-muted">Billing Cycle</dt>
            <dd className="mt-1 text-sm text-navy capitalize">{data?.billingCycle ?? 'monthly'}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-muted">Sponsored Learners</dt>
            <dd className="mt-1 text-sm text-navy">{data?.sponsoredLearnerCount ?? 0}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-muted">Seats Remaining</dt>
            <dd className="mt-1 text-sm text-navy">
              {data?.seats?.capacityTracked ? data.seats.remaining : 'Not tracked'}
            </dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-muted">Currencies</dt>
            <dd className="mt-1 text-sm text-navy">{formatCurrencies(data)}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-muted">Last Invoice</dt>
            <dd className="mt-1 text-sm text-navy">{formatDate(data?.lastInvoiceAt ?? null)}</dd>
          </div>
        </dl>
      </div>

      <div className="page-surface rounded-2xl p-6 shadow-sm">
        <h2 className="text-lg font-semibold text-navy mb-4">Invoices</h2>
        {downloadError && (
          <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
            {downloadError}
          </div>
        )}
        {(data?.invoices?.length ?? 0) === 0 ? (
          <div className="text-center py-8">
            <Receipt className="h-12 w-12 text-muted mx-auto mb-3" />
            <p className="text-sm text-muted mb-2">No invoices yet</p>
            <p className="text-xs text-muted">Invoices will appear here once you have billing activity.</p>
          </div>
        ) : (
          <div className="space-y-4">
            {data?.invoices.map((invoice) => (
              <div key={invoice.id} className="flex flex-col gap-4 rounded-xl border border-border p-4 sm:flex-row sm:items-center sm:justify-between">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-3 mb-2">
                    <p className="text-sm font-medium text-navy truncate">{invoice.learnerEmail ?? invoice.learnerUserId}</p>
                    <InvoiceStatusBadge status={invoice.status} />
                  </div>
                  <p className="text-sm text-muted mb-1">{invoice.description}</p>
                  <div className="flex items-center gap-4 text-xs text-muted">
                    <span className="flex items-center gap-1">
                      <Clock className="h-3 w-3" />
                      {formatDate(invoice.issuedAt)}
                    </span>
                    <span>ID: {invoice.invoiceId}</span>
                  </div>
                </div>
                <div className="flex items-center justify-between gap-3 sm:ml-4 sm:justify-end">
                  <div className="text-left sm:text-right">
                    <p className="text-sm font-semibold text-navy">
                      {formatCurrency(invoice.amount, invoice.currency)}
                    </p>
                  </div>
                  {invoice.downloadAvailable && (
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => handleDownloadInvoice(invoice)}
                      disabled={busyKey === `invoice:${invoice.invoiceId}`}
                      className="flex shrink-0 items-center gap-2"
                    >
                      <Download className="h-4 w-4" />
                      {busyKey === `invoice:${invoice.invoiceId}` ? 'Downloading...' : 'Download'}
                    </Button>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
