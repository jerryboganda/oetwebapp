'use client';

import { useEffect, useRef, useState } from 'react';
import { loadWhop } from '@whop/elements';
import { Checkout, CheckoutElement, WhopElements } from '@whop/elements-react';
import { isWhopCheckoutSessionId, isWhopPlanId } from '@/lib/billing/whop-ids';

// Re-exported so existing imports keep working. New code that only needs the id checks should
// import them from '@/lib/billing/whop-ids', which does not pull in the Whop SDK.
export { isWhopCheckoutSessionId, isWhopPlanId };

export type WhopEmbeddedCheckoutProps = {
  planId: string;
  checkoutUrl: string;
  returnUrl: string;
  sessionId?: string | null;
  onComplete: () => void;
  onUnavailable: () => void;
};

const READY_TIMEOUT_MS = 12_000;

/**
 * Whop Elements checkout (replaces the legacy js.whop.com loader embed, retired
 * by Whop on 21 Oct 2026). Mounted from the server-created checkout configuration
 * (`ch_…`), so price, currency and the order_id/quote_id metadata the webhook
 * maps back to the quote all come from the backend — the browser asserts nothing.
 * Apple Pay / Google Pay appear inside this element once the payment domain is
 * verified in Whop. Any load failure falls back to the hosted checkout.
 */
export function WhopEmbeddedCheckout({
  planId,
  checkoutUrl,
  returnUrl,
  sessionId,
  onComplete,
  onUnavailable,
}: WhopEmbeddedCheckoutProps) {
  const onCompleteRef = useRef(onComplete);
  const onUnavailableRef = useRef(onUnavailable);
  onCompleteRef.current = onComplete;
  onUnavailableRef.current = onUnavailable;
  const readyRef = useRef(false);
  const unavailableFiredRef = useRef(false);
  // Only mounted after the learner presses Pay, never during SSR.
  const [whop] = useState(() => (typeof window === 'undefined' ? null : loadWhop()));
  const mountable = isWhopPlanId(planId) && isWhopCheckoutSessionId(sessionId);

  // SDK load error, element error and the ready timeout can all fire; open the
  // hosted checkout once.
  const [fallBackToHosted] = useState(() => () => {
    if (unavailableFiredRef.current) return;
    unavailableFiredRef.current = true;
    onUnavailableRef.current();
  });

  useEffect(() => {
    // Without the ch_ configuration the payment would carry no quote metadata
    // and could not be matched to this order — use the hosted checkout instead.
    if (!mountable) {
      fallBackToHosted();
      return;
    }
    const timeout = window.setTimeout(() => {
      if (!readyRef.current) fallBackToHosted();
    }, READY_TIMEOUT_MS);
    return () => window.clearTimeout(timeout);
  }, [fallBackToHosted, mountable]);

  if (!mountable) {
    return null;
  }

  return (
    <div className="mt-4" data-testid="whop-embedded-checkout">
      <WhopElements
        elements={whop}
        appearance={{ theme: { appearance: 'light', accentColor: 'violet' } }}
        onLoadError={fallBackToHosted}
      >
        <Checkout
          checkoutConfiguration={sessionId}
          returnUrl={returnUrl}
          onComplete={(payload) => {
            if (payload.result === 'payment') onCompleteRef.current();
          }}
        >
          <CheckoutElement
            className="min-h-[480px] w-full overflow-hidden rounded-xl border border-border bg-white"
            onReady={() => {
              readyRef.current = true;
            }}
            onError={fallBackToHosted}
          />
        </Checkout>
      </WhopElements>
      <p className="mt-3 text-xs leading-5 text-muted">
        Pay on this page. Access unlocks after the payment provider confirms the charge.
      </p>
      <noscript>
        <a href={checkoutUrl}>Continue on Whop checkout</a>
      </noscript>
    </div>
  );
}
