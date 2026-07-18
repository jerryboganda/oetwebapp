'use client';

import { useCallback, useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowRight, ShoppingBag, X } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { formatMoney } from '@/lib/money';
import { useCartItems, useCartStore } from '@/lib/cart/cart-store';
import { buildCheckoutReviewHref } from '@/lib/cart/checkout';

/**
 * Slide-out cart drawer — lightweight peek into the cart from any page.
 * Reads items from the client cart store (source of truth); a Remove button
 * per line and a "Proceed to checkout" CTA (Stripe). Full management still
 * goes to `/cart` (linked at the footer).
 */

export interface CartDrawerProps {
  open: boolean;
  onClose: () => void;
}

export function CartDrawer({ open, onClose }: CartDrawerProps) {
  const router = useRouter();
  const items = useCartItems();
  const removeItem = useCartStore((state) => state.removeItem);
  const [checkoutBusy, setCheckoutBusy] = useState(false);
  const [mounted, setMounted] = useState(false);

  useEffect(() => {
    setMounted(true);
  }, []);

  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  const onCheckout = useCallback(() => {
    if (items.length === 0) return;
    setCheckoutBusy(true);
    onClose();
    router.push(buildCheckoutReviewHref(items));
  }, [items, onClose, router]);

  if (!open || !mounted) return null;

  const currency = items[0]?.currency ?? 'GBP';
  const subtotal = items.reduce((sum, item) => sum + item.price, 0);

  return createPortal(
    <div className="fixed inset-0 z-50 flex" role="dialog" aria-modal="true" aria-labelledby="cart-drawer-title">
      <button
        type="button"
        aria-label="Close cart"
        className="flex-1 bg-navy/40 backdrop-blur-sm"
        onClick={onClose}
      />
      <div className="flex h-full w-full max-w-md flex-col overflow-hidden bg-surface pb-[env(safe-area-inset-bottom)] pt-[env(safe-area-inset-top)] shadow-xl">
        <header className="flex items-center justify-between border-b border-border px-4 py-3">
          <h2 id="cart-drawer-title" className="text-lg font-semibold text-navy">
            Your cart
          </h2>
          <button
            type="button"
            aria-label="Close"
            onClick={onClose}
            className="rounded-full p-2 text-muted hover:bg-background-light"
          >
            <X className="h-5 w-5" />
          </button>
        </header>

        <div className="flex-1 overflow-y-auto px-4 py-3">
          {items.length === 0 ? (
            <div className="rounded-2xl border border-border bg-surface p-6 text-center">
              <ShoppingBag className="mx-auto h-8 w-8 text-muted" aria-hidden="true" />
              <p className="mt-3 text-sm text-muted">Your cart is empty.</p>
            </div>
          ) : (
            <ul className="space-y-3">
              {items.map((item) => (
                <li
                  key={item.code}
                  className="rounded-xl border border-border bg-surface p-3 shadow-sm"
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <p className="font-medium text-navy">{item.name}</p>
                      <p className="mt-0.5 text-xs uppercase tracking-wide text-muted">{item.kind}</p>
                    </div>
                    <p className="text-sm font-semibold text-navy">
                      {formatMoney(item.price, { currency: item.currency })}
                    </p>
                  </div>
                  <div className="mt-2 flex items-center gap-2">
                    <button
                      type="button"
                      onClick={() => removeItem(item.code)}
                      className="ml-auto text-xs text-danger hover:text-danger/80"
                    >
                      Remove
                    </button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>

        <footer className="border-t border-border bg-background-light px-4 py-3">
          <div className="flex items-baseline justify-between text-sm">
            <span className="text-muted">Subtotal</span>
            <span className="text-base font-semibold text-navy">
              {formatMoney(subtotal, { currency })}
            </span>
          </div>
          <Button
            className="mt-3 w-full"
            disabled={items.length === 0 || checkoutBusy}
            loading={checkoutBusy}
            onClick={() => void onCheckout()}
          >
            Proceed to checkout <ArrowRight className="ml-1 h-4 w-4" />
          </Button>
          <Button asChild variant="outline" className="mt-2 w-full" disabled={items.length === 0}>
            <Link href="/cart" onClick={onClose}>
              View cart
            </Link>
          </Button>
        </footer>
      </div>
    </div>,
    document.body,
  );
}
