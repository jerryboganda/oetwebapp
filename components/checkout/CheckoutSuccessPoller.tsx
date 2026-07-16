'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { AlertCircle, ArrowRight, CheckCircle2, Clock, Hourglass, Loader2, Mail } from 'lucide-react';

import { fetchCheckoutSessionStatus, type CheckoutSessionStatus } from '@/lib/api';
import { Button } from '@/components/ui/button';

import { CheckoutSessionSummary } from './CheckoutSessionSummary';

/**
 * Stripe-checkout success poller — re-queries the backend every second
 * for up to `pollDurationMs` (default 120s). Branches on access-granted,
 * paid-but-access-unknown, pending-manual-fulfilment, failed, or pending-timeout.
 */

/** Delivery methods that park the subscription at Pending until an admin hands it over. */
const MANUAL_DELIVERY_METHODS = new Set(['manual_web', 'telegram', 'manual_material']);

/**
 * What we can honestly tell the buyer once `status: 'fulfilled'` lands.
 *
 * `status: 'fulfilled'` only ever means the PAYMENT cleared. Whether ACCESS is live is a
 * separate question answered by `deliveryMethod`/`fulfilmentStatus` — and for a cart order
 * the backend cannot answer it at all (delivery is a BillingPlan property and a cart order
 * references no plan; see CheckoutService.ResolveDelivery). It reports null rather than
 * guessing, so null must stay UNKNOWN here and never be read as "automatic".
 *
 * - `manual`  — access is NOT live; an admin hands the package over (spec §2/§6.6).
 * - `granted` — access IS live; only then may we say entitlements were added.
 * - `unknown` — payment cleared, access state not known. Neutral: confirm the payment and
 *   point at billing. Claiming entitlements were added would be a fabrication; claiming a
 *   manual hand-over would alarm ordinary card buyers over something equally unknown.
 */
type AccessOutcome = 'manual' | 'granted' | 'unknown';

function accessOutcome(session: CheckoutSessionStatus): AccessOutcome {
  // An explicit subscription state is definitive when present.
  if (session.fulfilmentStatus === 'fulfilled' || session.fulfilmentStatus === 'auto') return 'granted';
  if (session.fulfilmentStatus === 'pending_manual') return 'manual';

  const delivery = session.deliveryMethod;
  if (delivery == null || delivery === '') return 'unknown';
  if (MANUAL_DELIVERY_METHODS.has(delivery)) return 'manual';
  if (delivery === 'automatic_web') return 'granted';
  return 'unknown';
}

export interface CheckoutSuccessPollerProps {
  sessionId: string;
  /** Override the polling cadence (default 1000ms). */
  pollIntervalMs?: number;
  /** Total time to wait before showing the patience fallback. */
  pollDurationMs?: number;
  /** Where to send the user after a successful purchase. */
  postPurchaseHref?: string;
}

type ViewState =
  | { phase: 'polling'; session: CheckoutSessionStatus | null }
  | { phase: 'fulfilled'; session: CheckoutSessionStatus }
  | { phase: 'paid'; session: CheckoutSessionStatus }
  | { phase: 'pending-manual'; session: CheckoutSessionStatus }
  | { phase: 'failed'; session: CheckoutSessionStatus | null; reason: string }
  | { phase: 'timeout'; session: CheckoutSessionStatus | null };

/** Maps the access outcome onto the view that states exactly that much and no more. */
const PHASE_FOR_OUTCOME: Record<AccessOutcome, 'fulfilled' | 'paid' | 'pending-manual'> = {
  granted: 'fulfilled',
  unknown: 'paid',
  manual: 'pending-manual',
};

export function CheckoutSuccessPoller({
  sessionId,
  pollIntervalMs = 1000,
  pollDurationMs = 120_000,
  postPurchaseHref = '/dashboard',
}: CheckoutSuccessPollerProps) {
  const [state, setState] = useState<ViewState>(() =>
    sessionId
      ? { phase: 'polling', session: null }
      : { phase: 'failed', session: null, reason: 'Missing checkout session id.' },
  );

  useEffect(() => {
    if (!sessionId) {
      return;
    }

    let cancelled = false;
    const started = Date.now();

    async function tick() {
      try {
        const session = await fetchCheckoutSessionStatus(sessionId);
        if (cancelled) return;
        if (session.status === 'fulfilled') {
          setState({ phase: PHASE_FOR_OUTCOME[accessOutcome(session)], session });
          return;
        }
        if (session.status === 'failed' || session.status === 'expired') {
          setState({
            phase: 'failed',
            session,
            reason: session.failureReason ?? 'Payment did not complete.',
          });
          return;
        }
        if (Date.now() - started >= pollDurationMs) {
          setState({ phase: 'timeout', session });
          return;
        }
        setState({ phase: 'polling', session });
        window.setTimeout(() => void tick(), pollIntervalMs);
      } catch (err) {
        if (cancelled) return;
        console.error(err);
        if (Date.now() - started >= pollDurationMs) {
          setState({ phase: 'timeout', session: null });
          return;
        }
        window.setTimeout(() => void tick(), pollIntervalMs);
      }
    }

    void tick();

    return () => {
      cancelled = true;
    };
  }, [sessionId, pollIntervalMs, pollDurationMs]);

  switch (state.phase) {
    case 'fulfilled':
      return (
        <div className="space-y-6">
          <div className="rounded-2xl border border-success/30 bg-success/10 p-6">
            <div className="flex items-start gap-3">
              <CheckCircle2 className="mt-0.5 h-6 w-6 flex-none text-success" aria-hidden="true" />
              <div>
                <h2 className="text-lg font-semibold text-success">Payment received</h2>
                <p className="mt-1 text-sm text-navy">
                  Thanks for your purchase. We have added the new entitlements to your account.
                </p>
              </div>
            </div>
          </div>
          <CheckoutSessionSummary session={state.session} />
          <div className="flex flex-wrap gap-3">
            <Button asChild>
              <Link href={postPurchaseHref}>
                Start using credits <ArrowRight className="ml-1 h-4 w-4" />
              </Link>
            </Button>
            <Button asChild variant="outline">
              <Link href="/account/billing">View billing</Link>
            </Button>
          </div>
        </div>
      );

    // Payment confirmed, access state unknown. Everything here is verifiable from the
    // session alone — no claim about entitlements, and nothing that reads as a problem.
    case 'paid':
      return (
        <div className="space-y-6">
          <div className="rounded-2xl border border-success/30 bg-success/10 p-6">
            <div className="flex items-start gap-3">
              <CheckCircle2 className="mt-0.5 h-6 w-6 flex-none text-success" aria-hidden="true" />
              <div>
                <h2 className="text-lg font-semibold text-success">Payment received</h2>
                <p className="mt-1 text-sm text-navy">
                  Thanks for your purchase — your payment went through and your order is
                  confirmed. Your billing page has the details of what you bought.
                </p>
              </div>
            </div>
          </div>
          <CheckoutSessionSummary session={state.session} />
          <div className="flex flex-wrap gap-3">
            <Button asChild>
              <Link href="/account/billing">View billing</Link>
            </Button>
            <Button asChild variant="outline">
              <Link href={postPurchaseHref}>
                Go to dashboard <ArrowRight className="ml-1 h-4 w-4" />
              </Link>
            </Button>
          </div>
        </div>
      );

    case 'pending-manual':
      return (
        <div className="space-y-6">
          <div className="rounded-2xl border border-warning/30 bg-warning/10 p-6">
            <div className="flex items-start gap-3">
              <Hourglass className="mt-0.5 h-6 w-6 flex-none text-warning" aria-hidden="true" />
              <div>
                <h2 className="text-lg font-semibold text-warning">
                  Payment received — pending manual fulfilment
                </h2>
                <p className="mt-1 text-sm text-navy">
                  Thanks for your purchase. This package is delivered by hand, so it is not
                  unlocked yet. Our team will verify your payment proof and provide your access -
                  you will be notified as soon as it is ready.
                </p>
              </div>
            </div>
          </div>
          <CheckoutSessionSummary session={state.session} />
          <div className="flex flex-wrap gap-3">
            <Button asChild variant="outline">
              <Link href="/account/billing">View billing</Link>
            </Button>
            <Button asChild variant="outline">
              <Link href="/dashboard">Back to dashboard</Link>
            </Button>
          </div>
        </div>
      );

    case 'failed':
      return (
        <div className="space-y-6">
          <div className="rounded-2xl border border-danger/30 bg-danger/10 p-6">
            <div className="flex items-start gap-3">
              <AlertCircle className="mt-0.5 h-6 w-6 flex-none text-danger" aria-hidden="true" />
              <div>
                <h2 className="text-lg font-semibold text-danger">Checkout did not complete</h2>
                <p className="mt-1 text-sm text-navy">{state.reason}</p>
              </div>
            </div>
          </div>
          <div className="flex flex-wrap gap-3">
            <Button asChild>
              <Link href="/cart">Return to cart</Link>
            </Button>
            <Button asChild variant="outline">
              <Link href="/support">Contact support</Link>
            </Button>
          </div>
        </div>
      );

    case 'timeout':
      return (
        <div className="space-y-6">
          <div className="rounded-2xl border border-warning/30 bg-warning/10 p-6">
            <div className="flex items-start gap-3">
              <Mail className="mt-0.5 h-6 w-6 flex-none text-warning" aria-hidden="true" />
              <div>
                <h2 className="text-lg font-semibold text-warning">We will email when ready</h2>
                <p className="mt-1 text-sm text-navy">
                  Your payment may still be processing. You will receive a confirmation email as
                  soon as your purchase is fulfilled - usually within a few minutes.
                </p>
              </div>
            </div>
          </div>
          {state.session ? <CheckoutSessionSummary session={state.session} /> : null}
          <div className="flex flex-wrap gap-3">
            <Button asChild variant="outline">
              <Link href="/account/billing">Go to billing</Link>
            </Button>
            <Button asChild variant="outline">
              <Link href="/dashboard">Back to dashboard</Link>
            </Button>
          </div>
        </div>
      );

    case 'polling':
    default:
      return (
        <div className="space-y-6">
          <div className="rounded-2xl border border-border bg-surface p-6 text-navy">
            <div className="flex items-start gap-3">
              <Loader2 className="mt-0.5 h-6 w-6 flex-none animate-spin text-primary" aria-hidden="true" />
              <div>
                <h2 className="text-lg font-semibold">Finalising your purchase</h2>
                <p className="mt-1 text-sm text-muted">
                  Stripe just confirmed the payment. We are now provisioning your entitlements -
                  this usually takes a few seconds.
                </p>
              </div>
            </div>
          </div>
          <p className="inline-flex items-center gap-2 text-xs text-muted">
            <Clock className="h-3.5 w-3.5" aria-hidden="true" /> Checking automatically — you can keep this tab open.
          </p>
        </div>
      );
  }
}
