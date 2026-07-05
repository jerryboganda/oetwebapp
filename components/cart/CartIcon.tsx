'use client';

import Link from 'next/link';
import { ShoppingCart } from 'lucide-react';

import { useCartCount } from '@/lib/cart/cart-store';

/**
 * Header-mount cart icon with a live item-count badge, sourced from the
 * client cart store (`lib/cart/cart-store.ts`). Renders as a `Link` to
 * `/cart` by default, or as a `<button>` (via `asButton` + `onClick`) so it
 * can open the `CartDrawer` instead — see `CartNavButton`.
 */

export interface CartIconProps {
  className?: string;
  /** Override the click target. Defaults to `/cart`. */
  href?: string;
  /** Render a `<button onClick>` instead of a `<Link href>`. */
  asButton?: boolean;
  /** Click handler used when `asButton` is true. */
  onClick?: () => void;
}

export function CartIcon({ className, href = '/cart', asButton, onClick }: CartIconProps) {
  const itemCount = useCartCount();

  const badgeMarkup = itemCount > 0 ? (
    <span className="absolute -right-1 -top-1 inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-primary px-1.5 text-[10px] font-semibold text-white dark:bg-violet-700">
      {itemCount > 99 ? '99+' : itemCount}
    </span>
  ) : null;

  const classes = [
    'relative inline-flex h-10 w-10 items-center justify-center rounded-full border border-border bg-surface text-navy transition-colors hover:bg-background-light',
    className,
  ]
    .filter(Boolean)
    .join(' ');

  const label = `Cart (${itemCount} item${itemCount === 1 ? '' : 's'})`;

  if (asButton) {
    return (
      <button type="button" aria-label={label} className={classes} onClick={onClick}>
        <ShoppingCart className="h-5 w-5" aria-hidden="true" />
        {badgeMarkup}
      </button>
    );
  }

  return (
    <Link href={href} aria-label={label} className={classes}>
      <ShoppingCart className="h-5 w-5" aria-hidden="true" />
      {badgeMarkup}
    </Link>
  );
}
