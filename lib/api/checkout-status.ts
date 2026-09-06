/**
 * Checkout session status polling.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
import { apiRequest } from './client';

// Restored: dropped entirely (not just un-re-exported) somewhere in the
// lib/api.ts split — still consumed by components/checkout/CheckoutSessionSummary.tsx
// and CheckoutSuccessPoller.tsx, which failed to compile without it.
export interface CheckoutSessionStatusItem {
  productCode: string;
  productName: string;
  quantity: number;
  description?: string | null;
}

export interface CheckoutSessionStatus {
  sessionId: string;
  status: 'pending' | 'fulfilled' | 'failed' | 'expired' | string;
  totalAmount?: number;
  currency?: string;
  items?: CheckoutSessionStatusItem[];
  failureReason?: string | null;
  fulfilledAt?: string | null;
  /**
   * How the purchased package is handed over — `automatic_web` | `manual_web` |
   * `whatsapp` | `manual_material`. `status: 'fulfilled'` only means the PAYMENT
   * cleared; for anything other than `automatic_web` access is NOT live, because the
   * subscription stays Pending until an admin marks it fulfilled (spec 2026-07-15
   * §2/§6.6). The success page must branch on this before claiming access was added.
   */
  deliveryMethod?: string | null;
  /**
   * `auto` | `pending_manual` | `fulfilled` for the subscription this order opened.
   * Null when the order granted no course subscription, and also null on cart-pipeline
   * orders that never created a domain Subscription — so treat `deliveryMethod` as the
   * load-bearing signal and this as best-effort enrichment.
   */
  fulfilmentStatus?: string | null;
  /** Server-confirmed external-only delivery. Undefined/null means unknown, never false. */
  externalOnly?: boolean | null;
}

export async function fetchCheckoutSessionStatus(sessionId: string): Promise<CheckoutSessionStatus> {
  return apiRequest<CheckoutSessionStatus>(`/v1/checkout/sessions/${encodeURIComponent(sessionId)}/status`);
}
