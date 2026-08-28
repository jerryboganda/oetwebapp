'use client';

import type { ReactNode } from 'react';
import { Download, FileSearch } from 'lucide-react';
import { Drawer } from '@/components/ui/modal';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { formatDateTime } from '@/lib/domain/datetime';
import type { AdminBillingInvoiceEvidence } from '@/lib/types/admin';

/**
 * Read-only invoice evidence panel: quote snapshot, matched payment(s), coupon
 * redemption, subscription items, catalog anchors, and the billing-event
 * timeline behind one invoice, plus a "Download invoice PDF" shortcut.
 *
 * Extracted from `app/admin/billing/page.tsx` (the Invoices tab) so the
 * Payment Proofs / Pending Fulfilment queue (`manual-payments/page.tsx`) can
 * open the exact same evidence view for a row without navigating away —
 * both screens read the same `/v1/admin/billing/invoices/{id}/evidence`
 * source and render it identically, so they can never disagree.
 */

export type InvoiceEvidenceTarget = { id: string; userName: string; plan: string };
export type InvoiceEvidenceStatus = 'empty' | 'loading' | 'success' | 'error';

interface InvoiceEvidenceDrawerProps {
  target: InvoiceEvidenceTarget | null;
  evidence: AdminBillingInvoiceEvidence | null;
  status: InvoiceEvidenceStatus;
  downloadingInvoiceId: string | null;
  onClose: () => void;
  onDownload: (invoiceId: string) => void;
}

export function InvoiceEvidenceDrawer({
  target,
  evidence,
  status,
  downloadingInvoiceId,
  onClose,
  onDownload,
}: InvoiceEvidenceDrawerProps) {
  return (
    <Drawer open={target !== null} onClose={onClose} title="Invoice Evidence" className="sm:max-w-3xl">
      {target ? (
        <div className="space-y-5">
          <div className="border-b border-border pb-4">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant="default">read only</Badge>
              {evidence ? (
                <Badge
                  variant={
                    evidence.invoice.status === 'paid'
                      ? 'success'
                      : evidence.invoice.status === 'failed'
                        ? 'danger'
                        : 'warning'
                  }
                >
                  {evidence.invoice.status}
                </Badge>
              ) : null}
              {evidence?.payments.length ? (
                <Badge variant="info">
                  {evidence.payments.length} payment {evidence.payments.length === 1 ? 'record' : 'records'}
                </Badge>
              ) : null}
              {evidence?.notRecorded.length ? <Badge variant="default">partial local evidence</Badge> : null}
            </div>
            <h2 className="mt-3 text-lg font-semibold text-admin-fg-strong">
              {evidence?.invoice.userName ?? target.userName}
            </h2>
            <p className="mt-1 break-words font-mono text-xs text-muted">{target.id}</p>
            <p className="mt-1 text-sm text-muted">{evidence?.invoice.description ?? target.plan}</p>
            <Button
              variant="primary"
              size="sm"
              className="mt-3 gap-2"
              disabled={downloadingInvoiceId === target.id}
              onClick={() => onDownload(target.id)}
            >
              <Download className="h-4 w-4" />
              {downloadingInvoiceId === target.id ? 'Downloading…' : 'Download invoice PDF'}
            </Button>
          </div>

          {status === 'loading' ? (
            <div className="space-y-3" role="status" aria-live="polite">
              {[0, 1, 2, 3].map((item) => (
                <div key={item} className="h-24 animate-pulse rounded-lg bg-admin-bg-subtle" />
              ))}
            </div>
          ) : null}

          {status === 'error' ? (
            <EmptyState
              icon={<FileSearch className="h-10 w-10 text-muted" />}
              title="Invoice evidence unavailable"
              description="Refresh the page or try the selected invoice again."
            />
          ) : null}

          {status === 'success' && evidence ? (
            <div className="space-y-5">
              {evidence.notRecorded.length > 0 ? (
                <div className="rounded-lg border border-border bg-admin-bg-subtle px-4 py-3">
                  <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Not recorded</p>
                  <div className="mt-2 flex flex-wrap gap-2">
                    {evidence.notRecorded.map((item) => (
                      <Badge key={item} variant="muted">
                        {evidenceGapLabel(item)}
                      </Badge>
                    ))}
                  </div>
                </div>
              ) : null}

              {evidence.integrityFlags.length > 0 ? (
                <div className="rounded-lg border border-warning/30 bg-warning/10 px-4 py-3">
                  <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">Integrity flags</p>
                  <div className="mt-2 flex flex-wrap gap-2">
                    {evidence.integrityFlags.map((item) => (
                      <Badge key={item} variant="warning">
                        {labelSummaryKey(item)}
                      </Badge>
                    ))}
                  </div>
                </div>
              ) : null}

              <EvidenceSection title="Invoice">
                <dl className="grid gap-3 sm:grid-cols-2">
                  <EvidenceField label="Invoice ID">{evidence.invoice.id}</EvidenceField>
                  <EvidenceField label="Learner">{evidence.invoice.userName}</EvidenceField>
                  <EvidenceField label="Issued">{formatDateTime(evidence.invoice.issuedAt)}</EvidenceField>
                  <EvidenceField label="Amount">{formatCurrency(evidence.invoice.amount, evidence.invoice.currency)}</EvidenceField>
                  <EvidenceField label="Quote ID">{evidence.invoice.quoteId ?? 'Not recorded'}</EvidenceField>
                  <EvidenceField label="Checkout Session">{evidence.invoice.checkoutSessionId ?? 'Not recorded'}</EvidenceField>
                </dl>
              </EvidenceSection>

              <EvidenceSection title="Quote Snapshot">
                {evidence.quote ? (
                  <div className="space-y-3">
                    <dl className="grid gap-3 sm:grid-cols-2">
                      <EvidenceField label="Quote ID">{evidence.quote.id}</EvidenceField>
                      <EvidenceField label="Status">{evidence.quote.status}</EvidenceField>
                      <EvidenceField label="Subtotal">{formatCurrency(evidence.quote.subtotalAmount, evidence.quote.currency)}</EvidenceField>
                      <EvidenceField label="Discount">{formatCurrency(evidence.quote.discountAmount, evidence.quote.currency)}</EvidenceField>
                      <EvidenceField label="Total">{formatCurrency(evidence.quote.totalAmount, evidence.quote.currency)}</EvidenceField>
                      <EvidenceField label="Expires">{formatDateTime(evidence.quote.expiresAt)}</EvidenceField>
                    </dl>
                    {evidence.quote.items.length > 0 ? (
                      <div className="space-y-2">
                        {evidence.quote.items.map((item) => (
                          <div
                            key={`${item.kind}-${item.code}`}
                            className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-admin-bg-subtle px-3 py-2 text-sm"
                          >
                            <div className="min-w-0">
                              <p className="font-medium text-admin-fg-strong">{item.name}</p>
                              <p className="text-xs uppercase tracking-[0.12em] text-muted">
                                {item.kind} - {item.code}
                              </p>
                            </div>
                            <p className="font-medium text-admin-fg-strong">
                              {formatCurrency(item.amount, item.currency)} x {item.quantity}
                            </p>
                          </div>
                        ))}
                      </div>
                    ) : (
                      <p className="text-sm text-muted">Not recorded</p>
                    )}
                  </div>
                ) : (
                  <p className="text-sm text-muted">Not recorded</p>
                )}
              </EvidenceSection>

              <EvidenceSection title="Payment">
                {evidence.payments.length > 0 ? (
                  <div className="space-y-3">
                    {evidence.payments.map((payment) => (
                      <dl key={payment.id} className="grid gap-3 rounded-lg border border-border p-3 sm:grid-cols-2">
                        <EvidenceField label="Gateway">{payment.gateway}</EvidenceField>
                        <EvidenceField label="Gateway Transaction">{payment.gatewayTransactionId}</EvidenceField>
                        <EvidenceField label="Status">{payment.status}</EvidenceField>
                        <EvidenceField label="Amount">{formatCurrency(payment.amount, payment.currency)}</EvidenceField>
                        <EvidenceField label="Product">
                          {payment.productType || 'Not recorded'} / {payment.productId || 'Not recorded'}
                        </EvidenceField>
                        <EvidenceField label="Updated">{formatDateTime(payment.updatedAt)}</EvidenceField>
                      </dl>
                    ))}
                  </div>
                ) : (
                  <p className="text-sm text-muted">Not recorded</p>
                )}
              </EvidenceSection>

              <EvidenceSection title="Coupon">
                {evidence.redemptions.length > 0 ? (
                  <div className="space-y-3">
                    {evidence.redemptions.map((redemption) => (
                      <dl key={redemption.id} className="grid gap-3 rounded-lg border border-border p-3 sm:grid-cols-2">
                        <EvidenceField label="Coupon">{redemption.couponCode}</EvidenceField>
                        <EvidenceField label="Status">{redemption.status}</EvidenceField>
                        <EvidenceField label="Discount">{formatCurrency(redemption.discountAmount, redemption.currency)}</EvidenceField>
                        <EvidenceField label="Redeemed">{formatDateTime(redemption.redeemedAt)}</EvidenceField>
                        <EvidenceField label="Coupon Version">{redemption.couponVersionId ?? 'Not recorded'}</EvidenceField>
                        <EvidenceField label="Subscription">{redemption.subscriptionId ?? 'Not recorded'}</EvidenceField>
                      </dl>
                    ))}
                  </div>
                ) : (
                  <p className="text-sm text-muted">Not recorded</p>
                )}
              </EvidenceSection>

              <EvidenceSection title="Subscription Items">
                {evidence.subscriptionItems.length > 0 ? (
                  <div className="space-y-2">
                    {evidence.subscriptionItems.map((item) => (
                      <div
                        key={item.id}
                        className="grid gap-2 rounded-lg bg-admin-bg-subtle px-3 py-2 text-sm sm:grid-cols-[1fr_auto]"
                      >
                        <div className="min-w-0">
                          <p className="font-medium text-admin-fg-strong">{item.itemCode}</p>
                          <p className="text-xs uppercase tracking-[0.12em] text-muted">
                            {item.itemType} - {item.addOnVersionId ?? 'Not recorded'}
                          </p>
                        </div>
                        <div className="flex flex-wrap items-center gap-2 sm:justify-end">
                          <Badge variant={item.status === 'active' ? 'success' : 'muted'}>{item.status}</Badge>
                          <span className="text-muted">qty {item.quantity}</span>
                        </div>
                      </div>
                    ))}
                  </div>
                ) : (
                  <p className="text-sm text-muted">Not recorded</p>
                )}
              </EvidenceSection>

              <EvidenceSection title="Catalog Anchors">
                <dl className="grid gap-3 sm:grid-cols-2">
                  <EvidenceField label="Source">
                    {evidence.catalogAnchors.source === 'not_recorded' ? 'Not recorded' : evidence.catalogAnchors.source}
                  </EvidenceField>
                  <EvidenceField label="Plan Version">{evidence.catalogAnchors.planVersionId ?? 'Not recorded'}</EvidenceField>
                  <EvidenceField label="Coupon Version">{evidence.catalogAnchors.couponVersionId ?? 'Not recorded'}</EvidenceField>
                  <EvidenceField label="Add-on Versions">
                    {Object.entries(evidence.catalogAnchors.addOnVersionIds).length > 0
                      ? Object.entries(evidence.catalogAnchors.addOnVersionIds)
                          .map(([code, version]) => `${code}: ${version}`)
                          .join(', ')
                      : 'Not recorded'}
                  </EvidenceField>
                </dl>
              </EvidenceSection>

              <EvidenceSection title="Events">
                {evidence.events.length > 0 ? (
                  <div className="space-y-3">
                    {evidence.events.map((event) => (
                      <div key={event.id} className="relative border-l border-border pl-4">
                        <span
                          className="absolute -left-1.5 top-2 h-3 w-3 rounded-full border-2 border-white bg-primary"
                          aria-hidden="true"
                        />
                        <div className="rounded-lg bg-admin-bg-subtle px-3 py-2">
                          <div className="flex flex-wrap items-start justify-between gap-3">
                            <div className="min-w-0">
                              <p className="break-words font-medium text-admin-fg-strong">{event.eventType}</p>
                              <p className="break-words text-xs uppercase tracking-[0.12em] text-muted">
                                {event.entityType} - {event.entityId || 'Not recorded'}
                              </p>
                            </div>
                            <p className="text-xs text-muted">{formatDateTime(event.occurredAt)}</p>
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>
                ) : (
                  <p className="text-sm text-muted">Not recorded</p>
                )}
              </EvidenceSection>
            </div>
          ) : null}
        </div>
      ) : null}
    </Drawer>
  );
}

function EvidenceSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="space-y-3 border-t border-border pt-4">
      <h3 className="text-sm font-semibold text-admin-fg-strong">{title}</h3>
      {children}
    </section>
  );
}

function EvidenceField({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="min-w-0 rounded-lg bg-admin-bg-subtle px-3 py-2">
      <dt className="text-[11px] uppercase tracking-[0.12em] text-muted">{label}</dt>
      <dd className="mt-1 break-words text-sm font-medium text-admin-fg-strong">{children || 'Not recorded'}</dd>
    </div>
  );
}

function formatCurrency(amount: number, currency = 'AUD') {
  return new Intl.NumberFormat('en-AU', {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
  }).format(amount);
}

function labelSummaryKey(key: string): string {
  return key
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/_/g, ' ')
    .replace(/^./, (char) => char.toUpperCase());
}

function evidenceGapLabel(value: string): string {
  const labels: Record<string, string> = {
    quote: 'Quote snapshot',
    payment: 'Payment transaction',
    couponRedemption: 'Coupon redemption',
    events: 'Event timeline',
    catalogAnchors: 'Catalog anchors',
  };
  return labels[value] ?? labelSummaryKey(value);
}
