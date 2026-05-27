'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft } from 'lucide-react';

import { fetchSubscriptionInvoices, type SubscriptionInvoice } from '@/lib/api';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { InvoiceTable } from '@/components/billing/InvoiceTable';

const PAGE_SIZE = 20;

/**
 * Paginated invoice history. The download links resolve to Stripe-hosted
 * PDF and receipt URLs returned by the backend, so we never proxy PDFs
 * through our own server (cheaper and keeps audit retention in Stripe).
 */
export default function AccountInvoicesPage() {
  const [invoices, setInvoices] = useState<SubscriptionInvoice[]>([]);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (targetPage: number) => {
    setLoading(true);
    setError(null);
    try {
      const result = await fetchSubscriptionInvoices({ page: targetPage, pageSize: PAGE_SIZE });
      setInvoices(result.items);
      setTotal(result.total);
      setPage(result.page);
    } catch (err) {
      console.error(err);
      setError(err instanceof Error ? err.message : 'Failed to load invoices.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load(1);
  }, [load]);

  const lastPage = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="mx-auto max-w-5xl space-y-6 px-4 py-10">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <Link
            href="/account/billing"
            className="inline-flex items-center gap-1 text-xs text-muted hover:text-navy"
          >
            <ArrowLeft className="h-3.5 w-3.5" /> Billing overview
          </Link>
          <h1 className="mt-2 text-3xl font-bold text-navy">Invoices</h1>
          <p className="mt-1 text-sm text-muted">
            Every receipt for your OET subscriptions and one-time purchases.
          </p>
        </div>
        <Button asChild variant="outline">
          <Link href="/account/billing/payment-methods">Payment methods</Link>
        </Button>
      </header>

      {error ? (
        <InlineAlert variant="error" title="Could not load invoices">
          {error}
        </InlineAlert>
      ) : null}

      <InvoiceTable invoices={invoices} loading={loading} />

      {total > PAGE_SIZE ? (
        <div className="flex items-center justify-between">
          <p className="text-xs text-muted">
            Page {page} of {lastPage} - {total} invoice{total === 1 ? '' : 's'}
          </p>
          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={page <= 1 || loading}
              onClick={() => void load(page - 1)}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled={page >= lastPage || loading}
              onClick={() => void load(page + 1)}
            >
              Next
            </Button>
          </div>
        </div>
      ) : null}
    </div>
  );
}
