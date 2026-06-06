'use client';

import { Download, ExternalLink, FileText } from 'lucide-react';

import type { SubscriptionInvoice } from '@/lib/api';
import { formatMoney } from '@/lib/money';

/**
 * Renders the learner-side paginated invoice table used on
 * `/account/billing/invoices`. Stripe-hosted PDF + receipt URLs surface
 * as inline "Download" / "View" links.
 */

export interface InvoiceTableProps {
  invoices: SubscriptionInvoice[];
  loading?: boolean;
  emptyMessage?: string;
}

export function InvoiceTable({ invoices, loading, emptyMessage }: InvoiceTableProps) {
  if (loading) {
    return (
      <div className="rounded-2xl border border-border bg-surface p-8 text-center text-muted">
        Loading invoices...
      </div>
    );
  }
  if (invoices.length === 0) {
    return (
      <div className="rounded-2xl border border-border bg-surface p-10 text-center">
        <FileText className="mx-auto h-8 w-8 text-muted" aria-hidden="true" />
        <p className="mt-3 text-sm text-muted">
          {emptyMessage ?? 'You have no invoices on record yet.'}
        </p>
      </div>
    );
  }

  return (
    <div className="overflow-x-auto rounded-2xl border border-border bg-surface shadow-sm">
      <table className="min-w-full divide-y divide-border text-sm">
        <thead className="bg-background-light text-left text-xs font-medium uppercase tracking-wider text-muted">
          <tr>
            <th scope="col" className="px-4 py-3">Date</th>
            <th scope="col" className="px-4 py-3">Number</th>
            <th scope="col" className="px-4 py-3">Description</th>
            <th scope="col" className="px-4 py-3">Status</th>
            <th scope="col" className="px-4 py-3 text-right">Amount</th>
            <th scope="col" className="px-4 py-3 text-right">Actions</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {invoices.map((invoice) => (
            <tr key={invoice.invoiceId} className="hover:bg-background-light/50">
              <td className="px-4 py-3 text-muted">
                {invoice.date ? new Date(invoice.date).toLocaleDateString() : '-'}
              </td>
              <td className="px-4 py-3 font-medium text-navy">{invoice.number ?? invoice.invoiceId}</td>
              <td className="px-4 py-3 text-muted">{invoice.description ?? '-'}</td>
              <td className="px-4 py-3">
                <StatusPill status={invoice.status} />
              </td>
              <td className="px-4 py-3 text-right font-medium text-navy">
                {formatMoney(invoice.amount, { currency: invoice.currency })}
              </td>
              <td className="px-4 py-3 text-right text-xs">
                <div className="inline-flex items-center justify-end gap-2">
                  {invoice.pdfUrl ? (
                    <a
                      href={invoice.pdfUrl}
                      target="_blank"
                      rel="noreferrer"
                      className="inline-flex items-center gap-1 text-primary hover:underline"
                    >
                      <Download className="h-3.5 w-3.5" /> PDF
                    </a>
                  ) : null}
                  {invoice.hostedInvoiceUrl ? (
                    <a
                      href={invoice.hostedInvoiceUrl}
                      target="_blank"
                      rel="noreferrer"
                      className="inline-flex items-center gap-1 text-primary hover:underline"
                    >
                      <ExternalLink className="h-3.5 w-3.5" /> View
                    </a>
                  ) : null}
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function StatusPill({ status }: { status: string }) {
  const normalized = status.toLowerCase();
  const tone =
    normalized === 'paid'
      ? 'bg-success/10 text-success'
      : normalized === 'pending' || normalized === 'open'
        ? 'bg-warning/10 text-warning'
        : normalized === 'failed' || normalized === 'void' || normalized === 'uncollectible'
          ? 'bg-danger/10 text-danger'
          : 'bg-background-light text-muted';
  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-semibold uppercase tracking-wider ${tone}`}
    >
      {status || 'unknown'}
    </span>
  );
}
