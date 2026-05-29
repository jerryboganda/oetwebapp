'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { AlertCircle, ArrowRight, CheckCircle2, Clock, Loader2, Mail } from 'lucide-react';

import { fetchCheckoutSessionStatus, type CheckoutSessionStatus } from '@/lib/api';
import { Button } from '@/components/ui/button';

import { CheckoutSessionSummary } from './CheckoutSessionSummary';

/**
 * Stripe-checkout success poller — re-queries the backend every second
 * for up to `pollDurationMs` (default 30s). Branches on fulfilled,
 * failed, or pending-timeout.
 */

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
  | { phase: 'failed'; session: CheckoutSessionStatus | null; reason: string }
  | { phase: 'timeout'; session: CheckoutSessionStatus | null };

export function CheckoutSuccessPoller({
  sessionId,
  pollIntervalMs = 1000,
  pollDurationMs = 30_000,
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
          setState({ phase: 'fulfilled', session });
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
            <Clock className="h-3.5 w-3.5" aria-hidden="true" /> Polling every second for up to 30 seconds...
          </p>
        </div>
      );
  }
}
