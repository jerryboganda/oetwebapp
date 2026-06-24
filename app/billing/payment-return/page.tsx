'use client';

import { Suspense, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { AlertCircle, ArrowRight, CheckCircle2, Clock, Loader2, RefreshCw } from 'lucide-react';

import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { fetchBillingPaymentStatus } from '@/lib/api';
import type { BillingPaymentStatus } from '@/lib/billing-types';
import { formatMoney } from '@/lib/money';

type Phase = 'polling' | 'completed' | 'failed' | 'expired' | 'cancelled' | 'timeout';

export default function BillingPaymentReturnPage() {
  return (
    <Suspense fallback={<PaymentReturnShell phase="polling" />}>
      <BillingPaymentReturnContent />
    </Suspense>
  );
}

// Hosted-portal webhooks normally land within seconds, but slow gateways can
// take a minute or more. Poll generously so a learner is not told "still
// processing" while their payment is actually fine.
const POLL_WINDOW_MS = 120_000;
const POLL_FAST_INTERVAL_MS = 1000;
const POLL_SLOW_INTERVAL_MS = 2500;
const POLL_BACKOFF_AFTER_MS = 15_000;

function BillingPaymentReturnContent() {
  const searchParams = useSearchParams();
  const quoteId = searchParams?.get('quote') ?? searchParams?.get('quoteId') ?? null;
  const sessionId = searchParams?.get('session') ?? searchParams?.get('session_id') ?? null;
  const initialStatus = searchParams?.get('status') ?? null;
  const missingReference = !quoteId && !sessionId;
  const cancelledByLearner = initialStatus === 'cancelled';
  const [phase, setPhase] = useState<Phase>(() => {
    if (cancelledByLearner) return 'cancelled';
    return missingReference ? 'failed' : 'polling';
  });
  const [status, setStatus] = useState<BillingPaymentStatus | null>(null);
  const [pollAttempt, setPollAttempt] = useState(0);
  const [error, setError] = useState<string | null>(() =>
    missingReference && !cancelledByLearner
      ? 'Missing checkout reference. Please open Billing to confirm your purchase status.'
      : null);

  useEffect(() => {
    // A learner-cancelled checkout stays on the cancelled screen — the
    // backend may still report the session as pending until it expires.
    if (missingReference || cancelledByLearner) {
      return;
    }

    let cancelled = false;
    const started = Date.now();
    setPhase('polling');
    const poll = async () => {
      try {
        const result = await fetchBillingPaymentStatus({ quoteId, sessionId });
        if (cancelled) return;
        setError(null);
        setStatus(result);
        if (result.status === 'completed') {
          setPhase('completed');
          return;
        }
        if (result.status === 'cancelled' || result.status === 'failed') {
          setPhase(result.status === 'failed' ? 'failed' : 'cancelled');
          return;
        }
        if (result.status === 'expired') {
          setPhase('expired');
          return;
        }
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not confirm payment yet.');
      }

      if (cancelled) return;
      if (Date.now() - started > POLL_WINDOW_MS) {
        setPhase('timeout');
        return;
      }
      const interval = Date.now() - started > POLL_BACKOFF_AFTER_MS ? POLL_SLOW_INTERVAL_MS : POLL_FAST_INTERVAL_MS;
      window.setTimeout(() => void poll(), interval);
    };

    void poll();
    return () => {
      cancelled = true;
    };
  }, [cancelledByLearner, missingReference, pollAttempt, quoteId, sessionId]);

  return (
    <PaymentReturnShell
      phase={phase}
      status={status}
      error={error}
      onCheckAgain={() => setPollAttempt((attempt) => attempt + 1)}
    />
  );
}

function PaymentReturnShell({ phase, status, error, onCheckAgain }: { phase: Phase; status?: BillingPaymentStatus | null; error?: string | null; onCheckAgain?: () => void }) {
  const destination = useMemo(() => {
    if (status?.productType === 'addon_purchase' && status.addOnCodes.some((code) => code.startsWith('pkg_'))) return '/ai-packages';
    if (status?.productType === 'addon_purchase') return '/billing';
    return '/dashboard?purchase=success';
  }, [status]);
  const retryHref = useMemo(() => {
    if (!status) return '/catalog';
    const params = new URLSearchParams();
    params.set('productType', status.productType || (status.targetPlanId ? 'plan_purchase' : 'addon_purchase'));
    if (status.targetPlanId) {
      params.set('priceId', status.targetPlanId);
    } else if (status.addOnCodes[0]) {
      params.set('priceId', status.addOnCodes[0]);
    }
    params.set('quantity', String(status.items[0]?.quantity ?? 1));
    return `/checkout/review?${params.toString()}`;
  }, [status]);

  const title = {
    polling: 'Confirming your payment',
    completed: 'Payment confirmed',
    failed: 'Payment could not be confirmed',
    expired: 'Checkout expired',
    cancelled: 'Checkout cancelled',
    timeout: 'Still processing',
  }[phase];

  return (
    <main className="min-h-screen bg-background-light text-navy">
      <section className="mx-auto max-w-3xl px-4 py-14">
        <div className="rounded-lg border border-border bg-surface p-6 shadow-sm">
          <div className="flex items-start gap-3">
            <StatusIcon phase={phase} />
            <div>
              <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
              <p className="mt-2 text-sm leading-6 text-muted">{messageFor(phase, status)}</p>
            </div>
          </div>

          {error ? <InlineAlert className="mt-5" variant="error">{error}</InlineAlert> : null}

          {status ? (
            <div className="mt-6 rounded-lg border border-border bg-background-light p-4">
              <h2 className="text-sm font-semibold uppercase text-muted">Order summary</h2>
              <ul className="mt-3 space-y-2 text-sm">
                {status.items.map((item) => (
                  <li key={`${item.kind}:${item.code}`} className="flex items-start justify-between gap-3">
                    <span>{item.name}</span>
                    <span className="font-medium">{formatMoney(item.amount, { currency: item.currency })}</span>
                  </li>
                ))}
              </ul>
              <p className="mt-3 flex items-center justify-between border-t border-border pt-3 font-semibold">
                <span>Total</span>
                <span>{formatMoney(status.totalAmount, { currency: status.currency })}</span>
              </p>
            </div>
          ) : null}

          <div className="mt-6 flex flex-wrap gap-3">
            {phase === 'completed' ? (
              <Button asChild>
                <Link href={destination}>Continue <ArrowRight className="h-4 w-4" /></Link>
              </Button>
            ) : null}
            {phase === 'polling' ? (
              <Button disabled>
                <Loader2 className="h-4 w-4 animate-spin" /> Waiting for confirmation
              </Button>
            ) : null}
            {phase === 'timeout' && onCheckAgain ? (
              <Button onClick={onCheckAgain}>
                <RefreshCw className="h-4 w-4" /> Check again
              </Button>
            ) : null}
            {phase === 'failed' || phase === 'expired' || phase === 'cancelled' ? (
              <Button asChild>
                <Link href={retryHref}>Try again</Link>
              </Button>
            ) : null}
            <Button asChild variant="outline">
              <Link href="/billing">Billing center</Link>
            </Button>
          </div>
        </div>
      </section>
    </main>
  );
}

function StatusIcon({ phase }: { phase: Phase }) {
  if (phase === 'completed') return <CheckCircle2 className="mt-1 h-7 w-7 flex-none text-success" />;
  if (phase === 'polling') return <Loader2 className="mt-1 h-7 w-7 flex-none animate-spin text-primary" />;
  if (phase === 'timeout') return <Clock className="mt-1 h-7 w-7 flex-none text-warning" />;
  return <AlertCircle className="mt-1 h-7 w-7 flex-none text-danger" />;
}

function messageFor(phase: Phase, status?: BillingPaymentStatus | null) {
  if (phase === 'completed') return 'Your payment has been received and your account has been updated.';
  if (phase === 'cancelled') return status?.failureReason ?? 'No charge was made. You can safely retry when ready.';
  if (phase === 'expired') return status?.failureReason ?? 'The checkout window expired before payment was completed.';
  if (phase === 'failed') return status?.failureReason ?? 'The payment portal did not complete this order.';
  if (phase === 'timeout') return 'Your payment may still be processing — your purchase activates automatically once it is confirmed. Use "Check again", or look at Billing or your email in a few minutes. You will not be charged twice for this order.';
  return 'We are confirming your payment with the provider. This usually takes a few seconds.';
}
