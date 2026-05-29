'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ShoppingCart } from 'lucide-react';

import { fetchCart } from '@/lib/api';

/**
 * Header-mount cart icon with a live item-count badge. Polls the cart
 * once on mount and on every `cart:changed` window event so any other
 * surface (CartDrawer, CartPageView, AddOn modal) can push updates by
 * dispatching the event.
 */

export interface CartIconProps {
  className?: string;
  /** Override the click target. Defaults to `/cart`. */
  href?: string;
  /** When false, render only the link target without auto-refreshing the badge. */
  autoRefresh?: boolean;
}

export const CART_CHANGED_EVENT = 'cart:changed';

export function CartIcon({ className, href = '/cart', autoRefresh = true }: CartIconProps) {
  const [itemCount, setItemCount] = useState<number>(0);

  useEffect(() => {
    if (!autoRefresh) return;
    let cancelled = false;

    async function refresh() {
      try {
        const cart = await fetchCart();
        if (!cancelled) {
          setItemCount(cart.items.reduce((sum, item) => sum + (item.quantity || 0), 0));
        }
      } catch {
        if (!cancelled) setItemCount(0);
      }
    }

    void refresh();
    const handler = () => void refresh();
    window.addEventListener(CART_CHANGED_EVENT, handler);
    return () => {
      cancelled = true;
      window.removeEventListener(CART_CHANGED_EVENT, handler);
    };
  }, [autoRefresh]);

  return (
    <Link
      href={href}
      aria-label={`Cart (${itemCount} item${itemCount === 1 ? '' : 's'})`}
      className={[
        'relative inline-flex h-10 w-10 items-center justify-center rounded-full border border-border bg-surface text-navy transition-colors hover:bg-background-light',
        className,
      ]
        .filter(Boolean)
        .join(' ')}
    >
      <ShoppingCart className="h-5 w-5" aria-hidden="true" />
      {itemCount > 0 ? (
        <span className="absolute -right-1 -top-1 inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-primary px-1.5 text-[10px] font-semibold text-white dark:bg-violet-700">
          {itemCount > 99 ? '99+' : itemCount}
        </span>
      ) : null}
    </Link>
  );
}
