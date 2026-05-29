'use client';

import { Suspense } from 'react';
import { useSearchParams } from 'next/navigation';

import { CheckoutSuccessPoller } from '@/components/checkout';

/**
 * Stripe redirects here after a successful checkout. Stripe replaces the
 * `{SESSION_ID}` token in the success URL with the actual checkout
 * session id. We poll until the backend confirms fulfilment, then unlock
 * the dashboard CTA.
 */
export default function CheckoutSuccessPage() {
  return (
    <div className="min-h-screen bg-background-light text-navy">
      <div className="mx-auto max-w-3xl space-y-6 px-4 py-12">
        <header>
          <h1 className="text-3xl font-bold">Thank you</h1>
          <p className="mt-1 text-sm text-muted">
            We are confirming your purchase with the payment processor.
          </p>
        </header>
        <Suspense fallback={<p className="text-muted">Loading session...</p>}>
          <CheckoutSuccessPollerRoute />
        </Suspense>
      </div>
    </div>
  );
}

function CheckoutSuccessPollerRoute() {
  const searchParams = useSearchParams();
  const sessionId = searchParams?.get('session_id') ?? '';
  return <CheckoutSuccessPoller sessionId={sessionId} />;
}
