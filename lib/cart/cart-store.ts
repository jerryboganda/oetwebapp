import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import { registerResettable } from '@/lib/stores/registry';

/**
 * Client-side shopping cart (Zustand + localStorage persistence).
 *
 * Each line is a DISTINCT product; quantity is fixed at 1 (no steppers — add
 * the same code twice and it is a no-op). At most one `plan` line is allowed
 * at a time: adding a second plan REPLACES the first. Checkout maps these
 * lines onto the existing `/v1/billing/checkout-sessions` contract in
 * `lib/cart/checkout.ts`.
 */
export interface CartItem {
  code: string;
  kind: 'plan' | 'addon';
  name: string;
  price: number;
  currency: string;
}

interface CartState {
  items: CartItem[];
  drawerOpen: boolean;
  addItem: (item: CartItem) => void;
  removeItem: (code: string) => void;
  clear: () => void;
  openDrawer: () => void;
  closeDrawer: () => void;
  reset: () => void;
}

export const useCartStore = create<CartState>()(
  persist(
    (set, get) => ({
      items: [],
      drawerOpen: false,

      addItem: (item) => set((state) => {
        if (state.items.some((existing) => existing.code === item.code)) {
          return state;
        }
        if (item.kind === 'plan') {
          return { items: [...state.items.filter((existing) => existing.kind !== 'plan'), item] };
        }
        return { items: [...state.items, item] };
      }),

      removeItem: (code) => set((state) => ({
        items: state.items.filter((existing) => existing.code !== code),
      })),

      clear: () => set({ items: [] }),
      openDrawer: () => set({ drawerOpen: true }),
      closeDrawer: () => set({ drawerOpen: false }),
      reset: () => set({ items: [], drawerOpen: false }),
    }),
    {
      name: 'oet-cart-store',
      partialize: (state) => ({ items: state.items }),
    },
  ),
);

// Purge in-memory state AND the persisted localStorage entry on logout, so
// the next user on a shared device never sees the previous user's cart.
registerResettable(() => {
  useCartStore.getState().reset();
  useCartStore.persist.clearStorage();
});

export function useCartItems(): CartItem[] {
  return useCartStore((state) => state.items);
}

export function useCartCount(): number {
  return useCartStore((state) => state.items.length);
}

export function useCartSubtotal(): number {
  return useCartStore((state) => state.items.reduce((sum, item) => sum + item.price, 0));
}
