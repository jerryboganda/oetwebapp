'use client';

import { Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { ArrowLeft, CreditCard, ShieldCheck, ShoppingBag } from 'lucide-react';

import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { useAuth } from '@/contexts/auth-context';
import { createBillingCheckoutSession, fetchBillingQuote } from '@/lib/api';
import type { BillingProductType, BillingQuote } from '@/lib/billing-types';
import { formatMoney } from '@/lib/money';
import { openCheckoutUrl } from '@/lib/mobile/web-checkout';

function newIdempotencyKey() {
  return typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function'
    ? crypto.randomUUID()
    : `checkout-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`;
}

function formatCountdown(totalSeconds: number) {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${String(seconds).padStart(2, '0')}`;
}

function normalizeProductType(value: string | null): BillingProductType {
  if (value === 'addon_purchase' || value === 'review_credits' || value === 'plan_upgrade' || value === 'plan_downgrade') {
    return value;
  }
  return 'plan_purchase';
}

export default function CheckoutReviewPage() {
  return (
    <Suspense fallback={<CheckoutReviewSkeleton />}>
      <CheckoutReviewContent />
    </Suspense>
  );
}

function CheckoutReviewContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { isAuthenticated, loading: authLoading } = useAuth();
  const productType = normalizeProductType(searchParams?.get('productType'));
  const priceId = searchParams?.get('priceId') ?? searchParams?.get('plan') ?? searchParams?.get('addOn') ?? '';
  const parentSubscriptionId = searchParams?.get('parentSubscriptionId') ?? searchParams?.get('parent') ?? null;
  const gateway = (searchParams?.get('gateway') === 'paypal' ? 'paypal' : 'stripe') as 'stripe' | 'paypal';
  const initialCoupon = searchParams?.get('couponCode') ?? '';
  const quantity = Number(searchParams?.get('quantity') ?? '1') || 1;

  const [couponCode, setCouponCode] = useState(initialCoupon);
  const [quote, setQuote] = useState<BillingQuote | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [quoteSecondsLeft, setQuoteSecondsLeft] = useState<number | null>(null);
  const [quoteRefreshed, setQuoteRefreshed] = useState(false);
  const quoteStartedRef = useRef(false);
  const quoteRefreshingRef = useRef(false);

  const nextHref = useMemo(() => {
    const query = searchParams?.toString();
    return `/checkout/review${query ? `?${query}` : ''}`;
  }, [searchParams]);

  // Pre-filled link to the manual / offline payment page (bank transfer,
  // InstaPay, Vodafone Cash, PayPal). Carries the quote so admin approval can
  // reliably resolve the same plan and activate access.
  const manualPaymentHref = useMemo(() => {
    const params = new URLSearchParams();
    if (quote?.quoteId) params.set('quoteId', quote.quoteId);
    const courseLabel = quote?.items?.[0]?.name ?? '';
    if (courseLabel) params.set('course', courseLabel);
    if (quote?.totalAmount != null) params.set('amount', String(quote.totalAmount));
    if (quote?.currency) params.set('currency', quote.currency);
    const qs = params.toString();
    return `/billing/manual-payment${qs ? `?${qs}` : ''}`;
  }, [quote]);

  const loadQuote = useCallback(async (couponOverride = couponCode) => {
    if (!priceId) {
      setError('Choose a product before checkout.');
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    try {
      const result = await fetchBillingQuote({
        productType,
        quantity,
        priceId,
        couponCode: couponOverride.trim() || null,
        parentSubscriptionId,
      });
      setQuote(result);
    } catch (err) {
      setQuote(null);
      setError(err instanceof Error ? err.message : 'Could not prepare this order.');
    } finally {
      setLoading(false);
    }
  }, [couponCode, parentSubscriptionId, priceId, productType, quantity]);

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      router.replace(`/sign-in?next=${encodeURIComponent(nextHref)}`);
      return;
    }
    if (quoteStartedRef.current) return;
    quoteStartedRef.current = true;
    void loadQuote();
  }, [authLoading, isAuthenticated, loadQuote, nextHref, router]);

  // Quotes are valid for a limited window. Surface the remaining time and
  // refresh automatically at expiry so the learner never hits the backend's
  // billing_quote_expired error mid-checkout.
  useEffect(() => {
    if (!quote?.expiresAt) {
      setQuoteSecondsLeft(null);
      return;
    }
    const expiry = new Date(quote.expiresAt).getTime();
    if (Number.isNaN(expiry)) {
      setQuoteSecondsLeft(null);
      return;
    }
    const tick = () => {
      const remaining = Math.max(0, Math.ceil((expiry - Date.now()) / 1000));
      setQuoteSecondsLeft(remaining);
      if (remaining <= 0 && !quoteRefreshingRef.current && !busy) {
        quoteRefreshingRef.current = true;
        setQuoteRefreshed(true);
        void loadQuote().finally(() => {
          quoteRefreshingRef.current = false;
        });
      }
    };
    tick();
    const timer = window.setInterval(tick, 1000);
    return () => window.clearInterval(timer);
  }, [busy, loadQuote, quote?.expiresAt]);

  const startCheckout = async () => {
    if (!quote || busy) return;
    setBusy(true);
    setError(null);
    try {
      const checkout = await createBillingCheckoutSession({
        productType,
        quantity,
        priceId,
        couponCode: couponCode.trim() || null,
        parentSubscriptionId,
        quoteId: quote.quoteId,
        gateway,
        idempotencyKey: newIdempotencyKey(),
      });
      const opened = await openCheckoutUrl(checkout.checkoutUrl);
      if (opened === 'noop') {
        setError('Could not open the secure payment window. Please try again.');
        setBusy(false);
        return;
      }
      if (opened === 'window-open' || opened === 'capacitor-browser') {
        // Payment continues in another window. Turn this tab into the
        // payment-status poller so the learner gets confirmation here even
        // if the hosted portal's redirect never lands.
        const params = new URLSearchParams();
        params.set('quote', checkout.quoteId ?? quote.quoteId);
        params.set('session', checkout.checkoutSessionId);
        router.replace(`/billing/payment-return?${params.toString()}`);
      }
      // 'window-assign' navigates this tab to the portal itself — leave busy on.
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not open secure checkout.');
      setBusy(false);
    }
  };

  if (authLoading || loading) return <CheckoutReviewSkeleton />;

  return (
    <main className="min-h-screen bg-background-light text-navy">
      <section className="border-b border-border bg-surface">
        <div className="mx-auto max-w-4xl px-4 py-8 sm:px-6">
          <Link href="/catalog" className="inline-flex items-center gap-1 text-sm font-medium text-muted hover:text-navy">
            <ArrowLeft className="h-4 w-4" /> Back to catalogue
          </Link>
          <div className="mt-5 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
            <div>
              <p className="text-xs font-semibold uppercase text-primary">Secure checkout</p>
              <h1 className="mt-1 text-3xl font-semibold tracking-tight">Review your order</h1>
              <p className="mt-2 text-sm text-muted">Confirm the order details before opening the hosted payment portal.</p>
            </div>
            <div className="inline-flex items-center gap-2 rounded-lg border border-border bg-background-light px-3 py-2 text-sm">
              <ShieldCheck className="h-4 w-4 text-success" /> Hosted payment
            </div>
          </div>
        </div>
      </section>

      <section className="mx-auto grid max-w-4xl gap-6 px-4 py-8 sm:px-6 lg:grid-cols-[1.4fr_0.8fr]">
        <div className="space-y-4">
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          {quoteRefreshed && !error ? (
            <InlineAlert variant="info">Your quote was refreshed with current pricing.</InlineAlert>
          ) : null}
          {!quote ? (
            <InlineAlert variant="warning">This order could not be prepared. Return to the catalogue and try again.</InlineAlert>
          ) : (
            <div className="rounded-lg border border-border bg-surface p-5 shadow-sm">
              <h2 className="text-lg font-semibold">Order items</h2>
              <ul className="mt-4 divide-y divide-border">
                {quote.items.map((item) => (
                  <li key={`${item.kind}:${item.code}`} className="flex items-start justify-between gap-4 py-4 first:pt-0 last:pb-0">
                    <div>
                      <p className="font-medium">{item.name}</p>
                      {item.description ? <p className="mt-1 text-sm text-muted">{item.description}</p> : null}
                      <p className="mt-1 text-xs uppercase text-muted">Qty {item.quantity}</p>
                    </div>
                    <p className="font-semibold">{formatMoney(item.amount, { currency: item.currency })}</p>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>

        <aside className="h-fit rounded-lg border border-border bg-surface p-5 shadow-sm">
          <h2 className="flex items-center gap-2 text-lg font-semibold">
            <ShoppingBag className="h-5 w-5" /> Total
          </h2>
          <div className="mt-4 space-y-2 text-sm">
            <Row label="Subtotal" value={quote ? formatMoney(quote.subtotalAmount, { currency: quote.currency }) : '-'} />
            <Row label="Discount" value={quote ? `-${formatMoney(quote.discountAmount, { currency: quote.currency })}` : '-'} />
            <Row strong label="Amount due" value={quote ? formatMoney(quote.totalAmount, { currency: quote.currency }) : '-'} />
          </div>
          {quote && quoteSecondsLeft !== null ? (
            <p className="mt-3 text-xs text-muted">
              {quoteSecondsLeft > 0
                ? `Quoted price valid for ${formatCountdown(quoteSecondsLeft)} — it refreshes automatically.`
                : 'Refreshing your quote with current pricing…'}
            </p>
          ) : null}
          <label className="mt-5 block text-sm font-medium">
            Coupon code
            <input
              className="mt-2 w-full rounded-lg border border-border bg-background-light px-3 py-2 text-sm outline-none focus:border-primary"
              value={couponCode}
              onChange={(event) => setCouponCode(event.target.value)}
              placeholder="Optional"
            />
          </label>
          <Button
            className="mt-3"
            variant="outline"
            fullWidth
            disabled={busy}
            onClick={() => {
              setQuoteRefreshed(false);
              void loadQuote(couponCode);
            }}
          >
            Apply coupon
          </Button>
          <Button className="mt-3" fullWidth loading={busy} disabled={!quote} onClick={startCheckout}>
            <CreditCard className="h-4 w-4" /> Continue to secure payment
          </Button>
          <p className="mt-3 text-xs leading-5 text-muted">Payment opens in the hosted portal. Your account unlocks after webhook confirmation.</p>

          <div className="mt-4 border-t border-border pt-4">
            <Link href={manualPaymentHref} className="text-sm font-medium text-primary hover:underline">
              Other ways to pay (bank transfer, InstaPay, Vodafone Cash, PayPal)
            </Link>
            <p className="mt-1 text-xs leading-5 text-muted">
              Pay manually and upload your proof — our team activates access after verifying it.
            </p>
          </div>
        </aside>
      </section>
    </main>
  );
}

function Row({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <p className={`flex items-center justify-between gap-3 ${strong ? 'border-t border-border pt-3 text-base font-semibold' : ''}`}>
      <span className="text-muted">{label}</span>
      <span>{value}</span>
    </p>
  );
}

function CheckoutReviewSkeleton() {
  return (
    <main className="min-h-screen bg-background-light p-8">
      <div className="mx-auto max-w-4xl space-y-4">
        <Skeleton className="h-24 rounded-lg" />
        <Skeleton className="h-72 rounded-lg" />
      </div>
    </main>
  );
}
