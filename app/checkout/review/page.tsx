'use client';

import { Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { AlertTriangle, ArrowLeft, CreditCard, ShieldCheck, ShoppingBag, Wallet } from 'lucide-react';

import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { PayPalExpandedCheckout } from '@/components/billing/paypal-expanded-checkout';
import { SendProofOnWhatsAppButton } from '@/components/billing/send-proof-whatsapp-button';
import { CheckoutPayRegion, type PayRegion } from '@/components/checkout/checkout-pay-region';
import { detectBillingRegion } from '@/lib/api/billing-region';
import { useAuth } from '@/contexts/auth-context';
import {
  ApiError,
  createBillingCheckoutSession,
  fetchAvailablePaymentGateways,
  fetchBillingQuote,
  type PaymentCaptureResult,
  type PaymentMethodMode,
  type PaymentMethodOption,
} from '@/lib/api';
import type { BillingProductType, BillingQuote } from '@/lib/billing-types';
import { formatMoney } from '@/lib/money';
import { openCheckoutUrl } from '@/lib/mobile/web-checkout';
import { cn } from '@/lib/utils';

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

/**
 * Checkout blocks the backend raises when a package can never serve this buyer
 * (spec 2026-07-15 §3). Both are terminal for this plan — no retry, coupon or
 * alternative payment route resolves them — so they replace the payment options
 * rather than sit above them as one more dismissable error.
 */
const PLAN_BLOCK_CODES = ['profession_mismatch', 'content_unavailable_for_profession'] as const;
type PlanBlockCode = (typeof PLAN_BLOCK_CODES)[number];

interface PlanBlock {
  code: PlanBlockCode;
  message: string;
}

function asPlanBlock(err: unknown): PlanBlock | null {
  if (err instanceof ApiError && (PLAN_BLOCK_CODES as readonly string[]).includes(err.code)) {
    return { code: err.code as PlanBlockCode, message: err.message };
  }
  return null;
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
  const { isAuthenticated, loading: authLoading, user } = useAuth();
  const productType = normalizeProductType(searchParams?.get('productType'));
  const priceId = searchParams?.get('priceId') ?? searchParams?.get('plan') ?? searchParams?.get('addOn') ?? '';
  const parentSubscriptionId = searchParams?.get('parentSubscriptionId') ?? searchParams?.get('parent') ?? null;
  // The URL `gateway` param is the learner's preferred method (e.g. routed from the
  // billing page); it becomes the initial selection, but the picker always reconciles
  // it against the methods the backend reports as actually available.
  const initialGateway = searchParams?.get('gateway') ?? '';
  const initialCoupon = searchParams?.get('couponCode') ?? '';
  const quantity = Number(searchParams?.get('quantity') ?? '1') || 1;
  // Cart checkout: the review page carries the primary item in `priceId` and every
  // additional cart line as repeated (or comma-joined) `addOnCodes`, so the quote and
  // the checkout session both cover the whole cart in one payment.
  const addOnCodes = useMemo(() => {
    const raw = searchParams?.getAll('addOnCodes') ?? [];
    return raw.flatMap((value) => value.split(',')).map((code) => code.trim()).filter(Boolean);
  }, [searchParams]);

  const [couponCode, setCouponCode] = useState(initialCoupon);
  const [quote, setQuote] = useState<BillingQuote | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [planBlock, setPlanBlock] = useState<PlanBlock | null>(null);
  const [quoteSecondsLeft, setQuoteSecondsLeft] = useState<number | null>(null);
  const [quoteRefreshed, setQuoteRefreshed] = useState(false);
  // When PayPal Expanded checkout is selected but the embedded config is unavailable
  // (no public client id), fall back to the hosted-portal redirect button.
  const [paypalUnavailable, setPaypalUnavailable] = useState(false);
  // Unified payment-method picker: the methods the backend says are usable here, plus
  // the learner's current selection.
  const [methods, setMethods] = useState<PaymentMethodOption[]>([]);
  const [selectedGateway, setSelectedGateway] = useState<string>(initialGateway);
  const [payRegion, setPayRegion] = useState<PayRegion>('global');
  const regionTouchedRef = useRef(false);
  const quoteStartedRef = useRef(false);
  const quoteRefreshingRef = useRef(false);

  const selectedMethod = useMemo(
    () => methods.find((method) => method.name === selectedGateway) ?? null,
    [methods, selectedGateway],
  );
  const selectedMode: PaymentMethodMode =
    selectedMethod?.mode ?? (selectedGateway === 'paypal' ? 'embedded' : 'redirect');

  const nextHref = useMemo(() => {
    const query = searchParams?.toString();
    return `/checkout/review${query ? `?${query}` : ''}`;
  }, [searchParams]);

  const courseLabel = quote?.items?.[0]?.name ?? '';

  // The plan's delivery method decides whether payment alone releases access. The
  // quote DTO does not carry it yet, so this is null until the backend adds it —
  // the success screen then falls back to its neutral confirmation copy.
  const deliveryMethod =
    (quote as (BillingQuote & { deliveryMethod?: string | null }) | null)?.deliveryMethod ?? null;

  // Pre-filled link to the manual / offline payment page (bank transfer,
  // InstaPay, Vodafone Cash, PayPal). Carries the quote so admin approval can
  // reliably resolve the same plan and activate access.
  const manualPaymentHref = useMemo(() => {
    const params = new URLSearchParams();
    if (quote?.quoteId) params.set('quoteId', quote.quoteId);
    if (courseLabel) params.set('course', courseLabel);
    if (quote?.totalAmount != null) params.set('amount', String(quote.totalAmount));
    if (quote?.currency) params.set('currency', quote.currency);
    const qs = params.toString();
    return `/billing/manual-payment${qs ? `?${qs}` : ''}`;
  }, [courseLabel, quote]);

  // Same target as the manual-payment link, focused on the Egypt section.
  const egyptHref = useMemo(
    () => `${manualPaymentHref}${manualPaymentHref.includes('?') ? '&' : '?'}region=egypt`,
    [manualPaymentHref],
  );

  const loadQuote = useCallback(async (couponOverride = couponCode) => {
    if (!priceId) {
      setError('Choose a product before checkout.');
      setLoading(false);
      return;
    }
    setLoading(true);
    setError(null);
    setPlanBlock(null);
    try {
      const result = await fetchBillingQuote({
        productType,
        quantity,
        priceId,
        couponCode: couponOverride.trim() || null,
        addOnCodes,
        parentSubscriptionId,
      });
      setQuote(result);
    } catch (err) {
      setQuote(null);
      const block = asPlanBlock(err);
      if (block) {
        setPlanBlock(block);
      } else {
        setError(err instanceof Error ? err.message : 'Could not prepare this order.');
      }
    } finally {
      setLoading(false);
    }
  }, [addOnCodes, couponCode, parentSubscriptionId, priceId, productType, quantity]);

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

  // Load the payment methods the backend can actually process in this environment so
  // the learner never picks an option that would fail. Falls back gracefully when the
  // API predates the richer `methods[]` shape (derive from the gateway-name list), and
  // to a single Stripe redirect if the lookup itself fails.
  useEffect(() => {
    let cancelled = false;
    const deriveMethod = (name: string): PaymentMethodOption => ({
      name,
      label: name === 'paypal' ? 'PayPal' : 'Credit or debit card',
      iconName: name === 'paypal' ? 'paypal' : 'credit-card',
      mode: name === 'paypal' ? 'embedded' : 'redirect',
    });
    (async () => {
      let options: PaymentMethodOption[];
      try {
        const res = await fetchAvailablePaymentGateways();
        options = res.methods && res.methods.length > 0
          ? res.methods
          : (res.gateways ?? []).map(deriveMethod);
      } catch {
        options = [deriveMethod('stripe')];
      }
      if (cancelled) return;
      if (options.length === 0) options = [deriveMethod('stripe')];
      setMethods(options);
      setSelectedGateway((current) =>
        current && options.some((option) => option.name === current)
          ? current
          : options[0]!.name);
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  // Default the payment region from the learner's detected billing region so
  // Egyptian learners see the local methods first. Falls back to global.
  useEffect(() => {
    let cancelled = false;
    Promise.resolve()
      .then(() => detectBillingRegion())
      .then((d) => {
        if (!cancelled && !regionTouchedRef.current && d?.region === 'EGYPT') setPayRegion('egypt');
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, []);

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
        addOnCodes,
        parentSubscriptionId,
        quoteId: quote.quoteId,
        gateway: selectedGateway,
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
      // The profession/content gate re-runs against the saved quote, so it can fire
      // here too — the learner's profession may have changed since the quote was built.
      const block = asPlanBlock(err);
      if (block) {
        setPlanBlock(block);
        setQuote(null);
      } else {
        setError(err instanceof Error ? err.message : 'Could not open secure checkout.');
      }
      setBusy(false);
    }
  };

  // Creates a PayPal order server-side and returns its id for the embedded SDK.
  const createPaypalOrder = useCallback(async (): Promise<string> => {
    if (!quote) throw new Error('Your order is not ready yet.');
    const checkout = await createBillingCheckoutSession({
      productType,
      quantity,
      priceId,
      couponCode: couponCode.trim() || null,
      addOnCodes,
      parentSubscriptionId,
      quoteId: quote.quoteId,
      gateway: selectedGateway || 'paypal',
      idempotencyKey: newIdempotencyKey(),
    });
    return checkout.checkoutSessionId;
  }, [quote, productType, quantity, priceId, couponCode, addOnCodes, parentSubscriptionId, selectedGateway]);

  // Deliberately ignores the server's `redirectTo` (a hardcoded
  // '/dashboard?purchase=success'): that drops the learner on the dashboard with no
  // order confirmation, no proof CTA, and — for a manual-fulfilment package — the
  // false impression that access is live. The success screen owns all three.
  const handlePaypalCaptured = useCallback(
    (result: PaymentCaptureResult) => {
      const params = new URLSearchParams();
      params.set('order', result.orderId);
      if (quote?.quoteId) params.set('quote', quote.quoteId);
      if (courseLabel) params.set('course', courseLabel);
      if (quote?.totalAmount != null) params.set('amount', String(quote.totalAmount));
      if (quote?.currency) params.set('currency', quote.currency);
      if (deliveryMethod) params.set('delivery', deliveryMethod);
      router.replace(`/checkout/success?${params.toString()}`);
    },
    [courseLabel, deliveryMethod, quote, router],
  );

  if (authLoading || loading) return <CheckoutReviewSkeleton />;

  if (planBlock) {
    return (
      <PlanBlockedScreen
        block={planBlock}
        course={courseLabel || priceId}
        professionLabel={user?.activeProfessionLabel ?? null}
      />
    );
  }

  return (
    <main className="min-h-screen bg-background-light text-navy">
      <section className="border-b border-border bg-surface">
        <div className="mx-auto max-w-5xl px-4 py-8 sm:px-6">
          <Link href="/catalog" className="inline-flex items-center gap-1 text-sm font-medium text-muted hover:text-navy">
            <ArrowLeft className="h-4 w-4" /> Back to catalogue
          </Link>
          <div className="mt-5 flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
            <div>
              <p className="text-xs font-semibold uppercase text-primary">Secure checkout</p>
              <h1 className="mt-1 text-3xl font-semibold tracking-tight">Choose how you&apos;d like to pay</h1>
              <p className="mt-2 text-sm text-muted">Pick a payment route on the left — your order summary is on the right.</p>
            </div>
            <div className="inline-flex items-center gap-2 rounded-lg border border-border bg-background-light px-3 py-2 text-sm">
              <ShieldCheck className="h-4 w-4 text-success" /> Secure checkout
            </div>
          </div>
        </div>
      </section>

      <section className="mx-auto grid max-w-5xl gap-6 px-4 py-8 sm:px-6 lg:grid-cols-[1.7fr_1fr] lg:items-start">
        {/* LEFT · payment routes (prominent) */}
        <div className="space-y-4">
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          {quoteRefreshed && !error ? (
            <InlineAlert variant="info">Your quote was refreshed with current pricing.</InlineAlert>
          ) : null}

          <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm sm:p-6">
            <h2 className="flex items-center gap-2 text-lg font-semibold">
              <Wallet className="h-5 w-5 text-primary" /> How would you like to pay?
            </h2>
            <p className="mt-1 text-sm text-muted">
              Every option is secure — your access unlocks as soon as we confirm the payment.
            </p>

            <div className="mt-5">
              <CheckoutPayRegion
                value={payRegion}
                onChange={(r) => {
                  regionTouchedRef.current = true;
                  setPayRegion(r);
                }}
                egyptHref={egyptHref}
                disabled={!quote}
              >
                {!quote ? (
                  <InlineAlert variant="warning">
                    This order could not be prepared. Return to the catalogue and try again.
                  </InlineAlert>
                ) : (
                  <>
                    {methods.length > 0 ? (
                      <fieldset>
                        <legend className="text-sm font-semibold text-navy">Card &amp; wallet</legend>
                        <div className="mt-2.5 space-y-2">
                          {methods.map((method) => {
                            const brand = methodBrand(method);
                            const active = selectedGateway === method.name;
                            const selectable = methods.length > 1;
                            return (
                              <label
                                key={method.name}
                                className={cn(
                                  'flex items-center gap-3 rounded-xl border-2 px-3.5 py-3 transition',
                                  selectable ? 'cursor-pointer' : 'cursor-default',
                                  active ? 'border-primary bg-primary/5' : 'border-border hover:border-primary/40',
                                )}
                              >
                                {selectable ? (
                                  <input
                                    type="radio"
                                    name="paymentMethod"
                                    value={method.name}
                                    checked={active}
                                    onChange={() => setSelectedGateway(method.name)}
                                    className="sr-only"
                                  />
                                ) : null}
                                <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-background-light text-primary">
                                  <MethodIcon iconName={method.iconName} name={method.name} />
                                </span>
                                <span className="min-w-0 flex-1">
                                  <span className="block text-sm font-bold text-navy">{brand.title}</span>
                                  <span className="block text-xs text-muted">{brand.subtitle}</span>
                                </span>
                                {selectable ? (
                                  <span
                                    className={cn(
                                      'flex h-5 w-5 shrink-0 items-center justify-center rounded-full border-2 transition',
                                      active ? 'border-primary' : 'border-border',
                                    )}
                                  >
                                    {active ? <span className="h-2.5 w-2.5 rounded-full bg-primary" /> : null}
                                  </span>
                                ) : null}
                              </label>
                            );
                          })}
                        </div>
                      </fieldset>
                    ) : null}

                    {selectedMode === 'embedded' && !paypalUnavailable ? (
                      <div className="mt-4">
                        <PayPalExpandedCheckout
                          createOrder={createPaypalOrder}
                          onCaptured={handlePaypalCaptured}
                          onError={(msg) => setError(msg)}
                          onUnavailable={() => setPaypalUnavailable(true)}
                          amountLabel={quote ? formatMoney(quote.totalAmount, { currency: quote.currency }) : ''}
                          disabled={!quote}
                        />
                        <p className="mt-3 text-xs leading-5 text-muted">
                          Pay securely without leaving this page. Your account unlocks the moment your payment is confirmed.
                        </p>
                      </div>
                    ) : (
                      <>
                        <Button className="mt-4" fullWidth loading={busy} disabled={!quote} onClick={startCheckout}>
                          <CreditCard className="h-4 w-4" /> Continue to secure payment
                        </Button>
                        <p className="mt-3 text-xs leading-5 text-muted">
                          Payment opens in the hosted portal. Your account unlocks after webhook confirmation.
                        </p>
                      </>
                    )}
                  </>
                )}
              </CheckoutPayRegion>
            </div>
          </div>
        </div>

        {/* RIGHT · order summary (compact, sticky) */}
        <aside className="space-y-4 lg:sticky lg:top-8">
          <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 className="flex items-center gap-2 text-lg font-semibold">
              <ShoppingBag className="h-5 w-5 text-primary" /> Order summary
            </h2>

            {quote ? (
              <ul className="mt-4 space-y-3">
                {quote.items.map((item) => (
                  <li key={`${item.kind}:${item.code}`} className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <p className="text-sm font-semibold text-navy">{item.name}</p>
                      {item.description ? (
                        <p className="mt-0.5 line-clamp-2 text-xs text-muted">{item.description}</p>
                      ) : null}
                      <p className="mt-1 text-[11px] font-medium uppercase tracking-wide text-muted">Qty {item.quantity}</p>
                    </div>
                    <p className="shrink-0 text-sm font-bold text-navy">
                      {formatMoney(item.amount, { currency: item.currency })}
                    </p>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="mt-4 text-sm text-muted">Preparing your order…</p>
            )}

            <div className="mt-4 space-y-2 border-t border-border pt-4 text-sm">
              <Row label="Subtotal" value={quote ? formatMoney(quote.subtotalAmount, { currency: quote.currency }) : '—'} />
              <Row label="Discount" value={quote ? `-${formatMoney(quote.discountAmount, { currency: quote.currency })}` : '—'} />
              <Row strong label="Amount due" value={quote ? formatMoney(quote.totalAmount, { currency: quote.currency }) : '—'} />
            </div>

            {quote && quoteSecondsLeft !== null ? (
              <p className="mt-3 text-xs text-muted">
                {quoteSecondsLeft > 0
                  ? `Quoted price valid for ${formatCountdown(quoteSecondsLeft)} — it refreshes automatically.`
                  : 'Refreshing your quote with current pricing…'}
              </p>
            ) : null}

            <div className="mt-4 border-t border-border pt-4">
              <label className="block text-sm font-medium">
                Coupon code
                <div className="mt-2 flex gap-2">
                  <input
                    className="w-full rounded-lg border border-border bg-background-light px-3 py-2 text-sm outline-none focus:border-primary"
                    value={couponCode}
                    onChange={(event) => setCouponCode(event.target.value)}
                    placeholder="Optional"
                  />
                  <Button
                    variant="outline"
                    disabled={busy}
                    onClick={() => {
                      setQuoteRefreshed(false);
                      void loadQuote(couponCode);
                    }}
                  >
                    Apply
                  </Button>
                </div>
              </label>
            </div>
          </div>

          {/* Every package carries a proof-of-payment WhatsApp route (spec §7) — it sits
              outside the region tabs so it is there whichever way the learner pays. */}
          <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <h2 className="text-sm font-bold text-navy">Already paid, or need a hand?</h2>
            <p className="mt-1 text-xs leading-5 text-muted">
              Send us your payment proof on WhatsApp and we&apos;ll verify it and activate your access.
            </p>
            <SendProofOnWhatsAppButton
              className="mt-3 w-full"
              course={courseLabel}
              amount={quote?.totalAmount}
              currency={quote?.currency}
              reference={quote?.quoteId}
            />
          </div>

          <div className="flex items-center justify-center gap-2 rounded-xl border border-border bg-surface px-3 py-2.5 text-xs text-muted">
            <ShieldCheck className="h-4 w-4 text-success" /> Encrypted, secure checkout
          </div>
        </aside>
      </section>
    </main>
  );
}

/**
 * Terminal checkout block. Says which of the two things went wrong, what it means for
 * this learner specifically, and gives them the two ways out (a package that does fit,
 * or a human on WhatsApp). A toast would not carry any of that.
 */
function PlanBlockedScreen({
  block,
  course,
  professionLabel,
}: {
  block: PlanBlock;
  course: string;
  professionLabel: string | null;
}) {
  const isMismatch = block.code === 'profession_mismatch';
  const title = isMismatch
    ? 'This package is for a different profession'
    : 'This package has no content for your profession yet';
  const registeredAs = professionLabel
    ? `Your account is registered as ${professionLabel}.`
    : 'Your account has no profession set yet.';
  const explanation = isMismatch
    ? `${registeredAs} Every package is built around one profession, so the videos and materials you would get here would not match your exam. Pick a package for your profession, or message us on WhatsApp if your profession is recorded incorrectly — we can change it for you.`
    : `${registeredAs} We have not finished building this package's content for your profession, so we cannot sell it to you yet. Message us on WhatsApp — we will tell you when it is ready, or point you at a package that already fits.`;

  return (
    <main className="min-h-screen bg-background-light text-navy">
      <div className="mx-auto max-w-2xl px-4 py-12 sm:px-6">
        <Link href="/catalog" className="inline-flex items-center gap-1 text-sm font-medium text-muted hover:text-navy">
          <ArrowLeft className="h-4 w-4" /> Back to catalogue
        </Link>

        <div className="mt-6 rounded-2xl border border-warning/30 bg-warning/10 p-6 shadow-sm">
          <div className="flex items-start gap-3">
            <AlertTriangle className="mt-0.5 h-6 w-6 flex-none text-warning" aria-hidden="true" />
            <div>
              <h1 className="text-xl font-semibold">{title}</h1>
              {course ? <p className="mt-1 text-sm font-medium text-navy">{course}</p> : null}
              <p className="mt-3 text-sm leading-6 text-navy">{block.message}</p>
              <p className="mt-3 text-sm leading-6 text-navy">{explanation}</p>
            </div>
          </div>

          <div className="mt-6 flex flex-wrap gap-3">
            <Button asChild>
              <Link href="/catalog">Browse packages for my profession</Link>
            </Button>
            <SendProofOnWhatsAppButton
              course={course}
              variant="outline"
              label={isMismatch ? 'Ask us to change my profession' : 'Ask us about this package'}
            />
          </div>
        </div>
      </div>
    </main>
  );
}

/** Brand-forward display for the method picker: the title always names the gateway
 *  (Stripe / PayPal) rather than a generic "Credit or debit card". */
function methodBrand(method: PaymentMethodOption): { title: string; subtitle: string } {
  switch (method.name) {
    case 'paypal':
      return { title: 'PayPal', subtitle: 'Pay with your PayPal balance or card' };
    case 'stripe':
      return { title: 'Stripe', subtitle: 'Credit or debit card — Visa, Mastercard, Amex' };
    case 'checkoutcom':
      return { title: 'Checkout.com', subtitle: 'Credit or debit card' };
    case 'easykash':
      return { title: 'EasyKash', subtitle: 'Cards, wallets, Fawry & instalments' };
    case 'paymob':
      return { title: 'Paymob', subtitle: 'Card & local wallets' };
    case 'paytabs':
      return { title: 'PayTabs', subtitle: 'Card & local payment methods' };
    default:
      return {
        title: method.label,
        subtitle: method.mode === 'embedded' ? 'Pay on this page' : 'Secure hosted checkout',
      };
  }
}

function MethodIcon({ iconName, name }: { iconName: string; name?: string }) {
  if (name === 'paypal' || iconName === 'paypal' || iconName === 'wallet') {
    return <Wallet className="h-4 w-4" />;
  }
  // "credit-card" and any unknown hint render the neutral card icon — lucide has no
  // brand marks and the title already names the gateway.
  return <CreditCard className="h-4 w-4" />;
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
