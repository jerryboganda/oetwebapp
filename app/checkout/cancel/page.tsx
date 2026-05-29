'use client';

import Link from 'next/link';
import { ArrowLeft, ShoppingCart } from 'lucide-react';

import { Button } from '@/components/ui/button';

/**
 * Landing page for the "cancel" branch of a Stripe Checkout session.
 * The cart is preserved server-side so the user can resume the same
 * order without re-adding items.
 */
export default function CheckoutCancelPage() {
  return (
    <div className="min-h-screen bg-background-light text-navy">
      <div className="mx-auto max-w-3xl space-y-6 px-4 py-16 text-center">
        <ShoppingCart className="mx-auto h-10 w-10 text-muted" aria-hidden="true" />
        <h1 className="text-3xl font-bold">Checkout cancelled</h1>
        <p className="mx-auto max-w-xl text-sm text-muted">
          No charge was made. Your cart is still saved - jump back in whenever you are ready.
        </p>
        <div className="flex flex-wrap items-center justify-center gap-3">
          <Button asChild>
            <Link href="/cart">
              <ArrowLeft className="mr-1 h-4 w-4" /> Back to cart
            </Link>
          </Button>
          <Button asChild variant="outline">
            <Link href="/catalog">Continue browsing</Link>
          </Button>
        </div>
      </div>
    </div>
  );
}
