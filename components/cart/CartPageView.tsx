'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowRight, Loader2, Minus, Plus, ShoppingBag, Tag, Trash2 } from 'lucide-react';

import {
  applyCartPromoCode,
  createCheckoutSession,
  fetchCart,
  fetchCatalogRecommendations,
  removeCartItem,
  removeCartPromoCode,
  updateCartItem,
  type Cart,
  type CartLineItem,
  type CatalogRecommendation,
} from '@/lib/api';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { formatMoney } from '@/lib/money';
import { openCheckoutUrl } from '@/lib/mobile/web-checkout';

import { CART_CHANGED_EVENT } from './CartIcon';

/**
 * Full-page cart view used by `/cart`. Renders line items with quantity
 * stepper + remove buttons, a promo-code field, totals breakdown, the
 * "Proceed to checkout" CTA, and a "Frequently bought together" strip.
 */

export interface CartPageViewProps {
  emptyStateHref?: string;
}

export function CartPageView({ emptyStateHref = '/catalog' }: CartPageViewProps) {
  const [cart, setCart] = useState<Cart | null>(null);
  const [loading, setLoading] = useState(true);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [promoInput, setPromoInput] = useState('');
  const [promoError, setPromoError] = useState<string | null>(null);
  const [recommendations, setRecommendations] = useState<CatalogRecommendation[]>([]);
  const [checkoutBusy, setCheckoutBusy] = useState(false);

  const refresh = useCallback(async () => {
    try {
      const next = await fetchCart();
      setCart(next);
      setErrorMessage(null);
    } catch (err) {
      console.error(err);
      setErrorMessage(err instanceof Error ? err.message : 'Failed to load cart.');
    }
  }, []);

  useEffect(() => {
    void (async () => {
      setLoading(true);
      await refresh();
      try {
        setRecommendations(await fetchCatalogRecommendations());
      } catch {
        setRecommendations([]);
      } finally {
        setLoading(false);
      }
    })();
  }, [refresh]);

  const broadcast = useCallback(() => {
    if (typeof window !== 'undefined') {
      window.dispatchEvent(new CustomEvent(CART_CHANGED_EVENT));
    }
  }, []);

  const onUpdateQuantity = useCallback(
    async (item: CartLineItem, nextQty: number) => {
      if (nextQty < 0 || nextQty === item.quantity) return;
      setBusy(true);
      try {
        if (nextQty === 0) {
          const next = await removeCartItem(item.itemId);
          setCart(next);
        } else {
          const next = await updateCartItem(item.itemId, nextQty);
          setCart(next);
        }
        broadcast();
      } catch (err) {
        console.error(err);
        setErrorMessage(err instanceof Error ? err.message : 'Could not update item.');
      } finally {
        setBusy(false);
      }
    },
    [broadcast],
  );

  const onRemoveItem = useCallback(
    async (item: CartLineItem) => {
      setBusy(true);
      try {
        const next = await removeCartItem(item.itemId);
        setCart(next);
        broadcast();
      } catch (err) {
        console.error(err);
        setErrorMessage(err instanceof Error ? err.message : 'Could not remove item.');
      } finally {
        setBusy(false);
      }
    },
    [broadcast],
  );

  const onApplyPromo = useCallback(async () => {
    if (!promoInput.trim()) return;
    setPromoError(null);
    setBusy(true);
    try {
      const next = await applyCartPromoCode(promoInput.trim().toUpperCase());
      setCart(next);
      setPromoInput('');
      broadcast();
    } catch (err) {
      console.error(err);
      setPromoError(err instanceof Error ? err.message : 'Promo code rejected.');
    } finally {
      setBusy(false);
    }
  }, [promoInput, broadcast]);

  const onRemovePromo = useCallback(
    async (code: string) => {
      setBusy(true);
      try {
        const next = await removeCartPromoCode(code);
        setCart(next);
        broadcast();
      } catch (err) {
        console.error(err);
        setPromoError(err instanceof Error ? err.message : 'Could not remove promo code.');
      } finally {
        setBusy(false);
      }
    },
    [broadcast],
  );

  const onCheckout = useCallback(async () => {
    setCheckoutBusy(true);
    setErrorMessage(null);
    try {
      const response = await createCheckoutSession({
        successUrl: typeof window !== 'undefined'
          ? `${window.location.origin}/checkout/success?session_id={SESSION_ID}`
          : undefined,
        cancelUrl: typeof window !== 'undefined' ? `${window.location.origin}/checkout/cancel` : undefined,
        cartId: cart?.cartId ?? null,
      });
      if (response?.url) {
        try {
          await openCheckoutUrl(response.url);
        } catch {
          window.location.href = response.url;
        }
      } else {
        throw new Error('Checkout session did not return a redirect URL.');
      }
    } catch (err) {
      console.error(err);
      setErrorMessage(err instanceof Error ? err.message : 'Could not start checkout.');
      setCheckoutBusy(false);
    }
  }, [cart?.cartId]);

  if (loading) {
    return (
      <div className="mx-auto flex max-w-5xl items-center justify-center p-12 text-muted">
        <Loader2 className="mr-2 h-5 w-5 animate-spin" aria-hidden="true" /> Loading your cart...
      </div>
    );
  }

  const currency = cart?.currency ?? 'AUD';
  const items = cart?.items ?? [];
  const hasItems = items.length > 0;

  return (
    <div className="mx-auto max-w-6xl space-y-8 px-4 py-10">
      <header>
        <h1 className="text-3xl font-bold text-navy">Your cart</h1>
        <p className="mt-1 text-sm text-muted">
          Review your selections, apply a promo code, then continue to secure checkout.
        </p>
      </header>

      {errorMessage ? (
        <InlineAlert variant="error" title="Cart error">
          {errorMessage}
        </InlineAlert>
      ) : null}

      {!hasItems ? (
        <div className="rounded-2xl border border-border bg-surface p-10 text-center">
          <ShoppingBag className="mx-auto h-10 w-10 text-muted" aria-hidden="true" />
          <h2 className="mt-4 text-lg font-semibold text-navy">Your cart is empty</h2>
          <p className="mt-1 text-sm text-muted">
            Browse the OET 2026 catalogue to find a course, bundle, or add-on.
          </p>
          <Button asChild className="mt-6">
            <Link href={emptyStateHref}>
              Browse catalogue <ArrowRight className="ml-1 h-4 w-4" />
            </Link>
          </Button>
        </div>
      ) : (
        <div className="grid gap-6 lg:grid-cols-3">
          <div className="space-y-3 lg:col-span-2">
            <ul className="space-y-3" data-testid="cart-line-items">
              {items.map((item) => (
                <li
                  key={item.itemId}
                  className="flex items-start gap-4 rounded-2xl border border-border bg-surface p-4 shadow-sm"
                >
                  <div className="flex-1">
                    <p className="font-semibold text-navy">{item.productName}</p>
                    {item.description ? (
                      <p className="mt-1 text-sm text-muted line-clamp-2">{item.description}</p>
                    ) : null}
                    <p className="mt-2 text-xs uppercase tracking-wider text-muted">
                      {formatMoney(item.unitAmount, { currency })} each
                    </p>
                  </div>
                  <div className="flex flex-col items-end gap-2">
                    <div className="inline-flex items-center gap-1 rounded-lg border border-border bg-background-light p-0.5">
                      <button
                        type="button"
                        aria-label="Decrease quantity"
                        className="rounded-md p-1.5 text-navy hover:bg-surface"
                        disabled={busy}
                        onClick={() => void onUpdateQuantity(item, item.quantity - 1)}
                      >
                        <Minus className="h-4 w-4" />
                      </button>
                      <span className="w-8 text-center text-sm font-medium" data-testid="cart-item-qty">
                        {item.quantity}
                      </span>
                      <button
                        type="button"
                        aria-label="Increase quantity"
                        className="rounded-md p-1.5 text-navy hover:bg-surface"
                        disabled={busy}
                        onClick={() => void onUpdateQuantity(item, item.quantity + 1)}
                      >
                        <Plus className="h-4 w-4" />
                      </button>
                    </div>
                    <p className="text-sm font-semibold text-navy">
                      {formatMoney(item.totalAmount, { currency })}
                    </p>
                    <button
                      type="button"
                      onClick={() => void onRemoveItem(item)}
                      disabled={busy}
                      className="inline-flex items-center gap-1 text-xs text-danger hover:text-danger/80"
                    >
                      <Trash2 className="h-3.5 w-3.5" /> Remove
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          </div>

          <aside className="space-y-4">
            <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
              <h2 className="text-sm font-semibold uppercase tracking-wider text-muted">
                Order summary
              </h2>
              <dl className="mt-4 space-y-2 text-sm">
                <Row label="Subtotal" value={formatMoney(cart?.subtotalAmount ?? 0, { currency })} />
                {cart && cart.discountAmount > 0 ? (
                  <Row
                    label="Discount"
                    value={`-${formatMoney(cart.discountAmount, { currency })}`}
                    accent="success"
                  />
                ) : null}
                {cart && cart.taxAmount > 0 ? (
                  <Row label="Tax (estimated)" value={formatMoney(cart.taxAmount, { currency })} />
                ) : null}
                <div className="flex items-baseline justify-between border-t border-border pt-3 text-base font-semibold text-navy">
                  <span>Total</span>
                  <span>{formatMoney(cart?.totalAmount ?? 0, { currency })}</span>
                </div>
              </dl>
              <Button
                className="mt-5 w-full"
                disabled={!hasItems || busy || checkoutBusy}
                onClick={() => void onCheckout()}
                data-testid="cart-checkout-cta"
              >
                {checkoutBusy ? (
                  <>
                    <Loader2 className="mr-1 h-4 w-4 animate-spin" /> Redirecting...
                  </>
                ) : (
                  <>
                    Proceed to checkout <ArrowRight className="ml-1 h-4 w-4" />
                  </>
                )}
              </Button>
              <p className="mt-3 text-center text-[11px] text-muted">
                Secure checkout - Stripe handles your card details.
              </p>
            </div>

            <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
              <h2 className="text-sm font-semibold uppercase tracking-wider text-muted">
                Promo code
              </h2>
              <form
                className="mt-3 flex items-center gap-2"
                onSubmit={(event) => {
                  event.preventDefault();
                  void onApplyPromo();
                }}
              >
                <input
                  type="text"
                  value={promoInput}
                  onChange={(event) => setPromoInput(event.target.value)}
                  placeholder="Enter code"
                  aria-label="Promo code"
                  className="flex-1 rounded-lg border border-border bg-background-light px-3 py-2 text-sm uppercase tracking-wider focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
                />
                <Button type="submit" variant="outline" disabled={busy || !promoInput.trim()}>
                  Apply
                </Button>
              </form>
              {promoError ? (
                <p className="mt-2 text-xs text-danger" role="alert">
                  {promoError}
                </p>
              ) : null}
              {cart?.promoCodes && cart.promoCodes.length > 0 ? (
                <ul className="mt-3 space-y-1.5">
                  {cart.promoCodes.map((promo) => (
                    <li
                      key={promo.code}
                      className="flex items-center justify-between gap-2 rounded-lg bg-background-light px-3 py-1.5 text-sm"
                    >
                      <span className="inline-flex items-center gap-1 font-medium text-navy">
                        <Tag className="h-3.5 w-3.5 text-[#996F1F]" /> {promo.code}
                      </span>
                      <button
                        type="button"
                        onClick={() => void onRemovePromo(promo.code)}
                        disabled={busy}
                        className="text-xs text-danger hover:text-danger/80"
                      >
                        Remove
                      </button>
                    </li>
                  ))}
                </ul>
              ) : null}
            </div>
          </aside>
        </div>
      )}

      {hasItems && recommendations.length > 0 ? (
        <section className="border-t border-border pt-8">
          <h2 className="text-xl font-bold text-navy">Frequently bought together</h2>
          <div className="mt-4 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {recommendations.slice(0, 3).map((rec) => (
              <article
                key={rec.productCode}
                className="flex h-full flex-col rounded-2xl border border-border bg-surface p-5 shadow-sm"
              >
                <h3 className="font-semibold text-navy">{rec.name}</h3>
                {rec.description ? (
                  <p className="mt-1 text-sm text-muted line-clamp-3">{rec.description}</p>
                ) : null}
                <div className="mt-auto flex items-center justify-between pt-4">
                  <span className="text-lg font-bold text-navy">
                    {formatMoney(rec.price, { currency: rec.currency })}
                  </span>
                  <Link
                    href={`/marketplace/packages/${encodeURIComponent(rec.productCode)}`}
                    className="text-sm font-medium text-primary hover:underline"
                  >
                    View
                  </Link>
                </div>
              </article>
            ))}
          </div>
        </section>
      ) : null}
    </div>
  );
}

function Row({
  label,
  value,
  accent,
}: {
  label: string;
  value: string;
  accent?: 'success';
}) {
  return (
    <div className="flex items-center justify-between text-sm">
      <span className="text-muted">{label}</span>
      <span
        className={[
          'font-medium',
          accent === 'success' ? 'text-success' : 'text-navy',
        ]
          .filter(Boolean)
          .join(' ')}
      >
        {value}
      </span>
    </div>
  );
}
