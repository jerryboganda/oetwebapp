'use client';

import { useCallback, useState } from 'react';
import { usePathname, useRouter } from 'next/navigation';

import { useAuth } from '@/contexts/auth-context';
import { toast } from '@/components/admin/ui/toaster';
import { useCartStore, type CartItem } from './cart-store';

/**
 * `addToCart` — the single entry point every storefront CTA should call
 * instead of pushing straight to `/checkout/review`. Redirects signed-out
 * visitors to sign-in (preserving the current path), otherwise adds the item
 * to the client cart and surfaces a toast + opens the cart drawer.
 */
export function useAddToCart() {
  const router = useRouter();
  const pathname = usePathname();
  const { isAuthenticated, loading } = useAuth();
  const [pending, setPending] = useState(false);

  const addToCart = useCallback(
    (item: CartItem) => {
      if (loading) return;
      if (!isAuthenticated) {
        const next = pathname || '/';
        router.push(`/sign-in?next=${encodeURIComponent(next)}`);
        return;
      }

      setPending(true);
      try {
        const store = useCartStore.getState();
        store.addItem(item);
        toast.success('Added to cart', {
          action: {
            label: 'View cart',
            onClick: () => useCartStore.getState().openDrawer(),
          },
        });
        store.openDrawer();
      } finally {
        setPending(false);
      }
    },
    [isAuthenticated, loading, pathname, router],
  );

  return { addToCart, pending };
}
