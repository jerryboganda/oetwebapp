'use client';

import { useCallback, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowRight, ShoppingBag, Tag, Trash2 } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { formatMoney } from '@/lib/money';
import { useCartItems, useCartStore } from '@/lib/cart/cart-store';
import { buildCheckoutReviewHref } from '@/lib/cart/checkout';

/**
 * Full-page cart view used by `/cart`. Reads items from the client cart
 * store, renders line items with a remove button, a promo-code field, a
 * totals summary, and the "Proceed to checkout" CTA (Stripe).
 *
 * TODO: embedded PayPal-for-cart is a follow-up — the server-cart PayPal
 * flow (`createCartPaypalOrder` / `PayPalExpandedCheckout`) does not yet
 * have a client-cart equivalent, so only Stripe is offered here for now.
 */

export interface CartPageViewProps {
  emptyStateHref?: string;
}

export function CartPageView({ emptyStateHref = '/catalog' }: CartPageViewProps) {
  const router = useRouter();
  const items = useCartItems();
  const removeItem = useCartStore((state) => state.removeItem);
  const [promoInput, setPromoInput] = useState('');
  const [checkoutBusy, setCheckoutBusy] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const onCheckout = useCallback(() => {
    if (items.length === 0) return;
    setErrorMessage(null);
    setCheckoutBusy(true);
    router.push(buildCheckoutReviewHref(items, promoInput.trim() || null));
  }, [items, promoInput, router]);

  const currency = items[0]?.currency ?? 'GBP';
  const hasItems = items.length > 0;
  const subtotal = items.reduce((sum, item) => sum + item.price, 0);

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
                  key={item.code}
                  className="flex items-start gap-4 rounded-2xl border border-border bg-surface p-4 shadow-sm"
                >
                  <div className="flex-1">
                    <p className="font-semibold text-navy">{item.name}</p>
                    <p className="mt-2 text-xs uppercase tracking-wider text-muted">{item.kind}</p>
                  </div>
                  <div className="flex flex-col items-end gap-2">
                    <p className="text-sm font-semibold text-navy">
                      {formatMoney(item.price, { currency: item.currency })}
                    </p>
                    <button
                      type="button"
                      onClick={() => removeItem(item.code)}
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
            <Card padding="none" className="p-5">
              <h2 className="text-sm font-semibold uppercase tracking-wider text-muted">
                Order summary
              </h2>
              <dl className="mt-4 space-y-2 text-sm">
                <div className="flex items-baseline justify-between border-t border-border pt-3 text-base font-semibold text-navy">
                  <span>Total</span>
                  <span>{formatMoney(subtotal, { currency })}</span>
                </div>
              </dl>

              <Button
                className="mt-5 w-full"
                disabled={!hasItems || checkoutBusy}
                loading={checkoutBusy}
                onClick={() => void onCheckout()}
                data-testid="cart-checkout-cta"
              >
                Proceed to checkout <ArrowRight className="ml-1 h-4 w-4" />
              </Button>
              <p className="mt-3 text-center text-[11px] text-muted">
                Secure checkout - Stripe handles your card details.
              </p>
            </Card>

            <Card padding="none" className="p-5">
              <h2 className="text-sm font-semibold uppercase tracking-wider text-muted">
                Promo code
              </h2>
              <form
                className="mt-3 flex items-center gap-2"
                onSubmit={(event) => {
                  event.preventDefault();
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
                <Tag className="h-4 w-4 shrink-0 text-muted" aria-hidden="true" />
              </form>
              <p className="mt-2 text-xs text-muted">
                Applied automatically at checkout.
              </p>
            </Card>
          </aside>
        </div>
      )}
    </div>
  );
}
