'use client';

import type { CheckoutSessionStatus } from '@/lib/api';
import { Card } from '@/components/ui/card';
import { formatMoney } from '@/lib/money';

/**
 * Renders the line-item summary returned from `/v1/checkout/sessions/{id}/status`.
 * Used by both the success and the timeout copy on `/checkout/success`.
 */

export interface CheckoutSessionSummaryProps {
  session: CheckoutSessionStatus;
}

export function CheckoutSessionSummary({ session }: CheckoutSessionSummaryProps) {
  const items = session.items ?? [];

  return (
    <Card padding="none" className="p-5">
      <h3 className="text-sm font-semibold uppercase tracking-wider text-muted">Order details</h3>
      {items.length === 0 ? (
        <p className="mt-3 text-sm text-muted">Receipt details are still being prepared.</p>
      ) : (
        <ul className="mt-3 space-y-2 text-sm">
          {items.map((item) => (
            <li key={item.productCode} className="flex items-start justify-between gap-3">
              <div>
                <p className="font-medium text-navy">{item.productName}</p>
                {item.description ? (
                  <p className="text-xs text-muted">{item.description}</p>
                ) : null}
              </div>
              <span className="text-xs text-muted">x {item.quantity}</span>
            </li>
          ))}
        </ul>
      )}
      {typeof session.totalAmount === 'number' ? (
        <p className="mt-4 flex items-baseline justify-between border-t border-border pt-3 text-sm">
          <span className="text-muted">Charged</span>
          <span className="text-base font-semibold text-navy">
            {formatMoney(session.totalAmount, { currency: session.currency ?? 'AUD' })}
          </span>
        </p>
      ) : null}
    </Card>
  );
}
