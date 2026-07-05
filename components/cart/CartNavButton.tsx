'use client';

import { CartIcon } from './CartIcon';
import { CartDrawer } from './CartDrawer';
import { useCartStore } from '@/lib/cart/cart-store';

/**
 * Header-mount cart affordance: the badge icon (as a button) plus its
 * drawer, both bound to the client cart store. Drop this into any page's
 * nav actions to get add-to-cart chrome with zero prop wiring.
 */
export function CartNavButton() {
  const drawerOpen = useCartStore((state) => state.drawerOpen);
  const openDrawer = useCartStore((state) => state.openDrawer);
  const closeDrawer = useCartStore((state) => state.closeDrawer);

  return (
    <>
      <CartIcon asButton onClick={openDrawer} />
      <CartDrawer open={drawerOpen} onClose={closeDrawer} />
    </>
  );
}
