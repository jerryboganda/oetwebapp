'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowRight, Loader2, ShoppingBag, X } from 'lucide-react';

import { fetchCart, removeCartItem, updateCartItem, type Cart } from '@/lib/api';
import { Button } from '@/components/ui/button';
import { formatMoney } from '@/lib/money';

import { CART_CHANGED_EVENT } from './CartIcon';

/**
 * Slide-out cart drawer — lightweight peek into the cart from any page.
 * Quick quantity edits and remove buttons only; full management still
 * goes to `/cart` (linked at the footer).
 */

export interface CartDrawerProps {
  open: boolean;
  onClose: () => void;
}

export function CartDrawer({ open, onClose }: CartDrawerProps) {
  const [cart, setCart] = useState<Cart | null>(null);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      setCart(await fetchCart());
    } catch (err) {
      console.error(err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (open) void refresh();
  }, [open, refresh]);

  useEffect(() => {
    if (!open) return;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, onClose]);

  const broadcast = useCallback(() => {
    if (typeof window !== 'undefined') {
      window.dispatchEvent(new CustomEvent(CART_CHANGED_EVENT));
    }
  }, []);

  const onQty = useCallback(
    async (itemId: string, qty: number) => {
      if (!cart) return;
      setBusy(true);
      try {
        if (qty <= 0) {
          setCart(await removeCartItem(cart.cartId, itemId));
        } else {
          setCart(await updateCartItem(cart.cartId, itemId, qty));
        }
        broadcast();
      } catch (err) {
        console.error(err);
      } finally {
        setBusy(false);
      }
    },
    [cart, broadcast],
  );

  if (!open) return null;

  const items = cart?.items ?? [];
  const currency = cart?.currency ?? 'AUD';

  return (
    <div className="fixed inset-0 z-50 flex" role="dialog" aria-modal="true" aria-labelledby="cart-drawer-title">
      <button
        type="button"
        aria-label="Close cart"
        className="flex-1 bg-navy/40 backdrop-blur-sm"
        onClick={onClose}
      />
      <div className="flex h-full w-full max-w-md flex-col overflow-hidden bg-surface shadow-xl">
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
          {loading ? (
            <div className="flex items-center justify-center py-12 text-muted">
              <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Loading...
            </div>
          ) : items.length === 0 ? (
            <div className="rounded-2xl border border-border bg-surface p-6 text-center">
              <ShoppingBag className="mx-auto h-8 w-8 text-muted" aria-hidden="true" />
              <p className="mt-3 text-sm text-muted">Your cart is empty.</p>
            </div>
          ) : (
            <ul className="space-y-3">
              {items.map((item) => (
                <li
                  key={item.itemId}
                  className="rounded-xl border border-border bg-surface p-3 shadow-sm"
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <p className="font-medium text-navy">{item.productName}</p>
                      <p className="mt-0.5 text-xs text-muted">
                        {formatMoney(item.unitAmount, { currency })} x {item.quantity}
                      </p>
                    </div>
                    <p className="text-sm font-semibold text-navy">
                      {formatMoney(item.totalAmount, { currency })}
                    </p>
                  </div>
                  <div className="mt-2 flex items-center gap-2">
                    <button
                      type="button"
                      onClick={() => void onQty(item.itemId, item.quantity - 1)}
                      disabled={busy}
                      className="rounded-md border border-border bg-background-light px-2 py-1 text-xs"
                      aria-label="Decrease"
                    >
                      -
                    </button>
                    <span className="text-xs font-medium">{item.quantity}</span>
                    <button
                      type="button"
                      onClick={() => void onQty(item.itemId, item.quantity + 1)}
                      disabled={busy}
                      className="rounded-md border border-border bg-background-light px-2 py-1 text-xs"
                      aria-label="Increase"
                    >
                      +
                    </button>
                    <button
                      type="button"
                      onClick={() => void onQty(item.itemId, 0)}
                      disabled={busy}
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
              {formatMoney(cart?.subtotalAmount ?? 0, { currency })}
            </span>
          </div>
          <Button asChild className="mt-3 w-full" disabled={items.length === 0}>
            <Link href="/cart" onClick={onClose}>
              View cart <ArrowRight className="ml-1 h-4 w-4" />
            </Link>
          </Button>
        </footer>
      </div>
    </div>
  );
}
