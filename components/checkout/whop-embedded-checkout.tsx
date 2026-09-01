'use client';

import { useEffect, useId, useRef } from 'react';

const LOADER_SRC = 'https://js.whop.com/static/checkout/loader.js';

export function isWhopPlanId(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.startsWith('plan_');
}

export function isWhopCheckoutSessionId(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.startsWith('ch_');
}

type WhopEmbeddedCheckoutProps = {
  planId: string;
  checkoutUrl: string;
  returnUrl: string;
  sessionId?: string | null;
  onComplete: () => void;
  onUnavailable: () => void;
};

export function WhopEmbeddedCheckout({
  planId,
  checkoutUrl,
  returnUrl,
  sessionId,
  onComplete,
  onUnavailable,
}: WhopEmbeddedCheckoutProps) {
  const rawId = useId();
  const containerId = `whop-checkout-${rawId.replace(/:/g, '')}`;
  const onCompleteRef = useRef(onComplete);
  const onUnavailableRef = useRef(onUnavailable);
  onCompleteRef.current = onComplete;
  onUnavailableRef.current = onUnavailable;

  useEffect(() => {
    if (!isWhopPlanId(planId)) {
      onUnavailableRef.current();
      return;
    }

    const callbackName = `__oetWhopComplete_${containerId.replace(/-/g, '_')}`;
    const errorName = `__oetWhopError_${containerId.replace(/-/g, '_')}`;
    const scopedWindow = window as unknown as Window & Record<string, unknown>;
    scopedWindow[callbackName] = () => onCompleteRef.current();
    scopedWindow[errorName] = () => onUnavailableRef.current();

    let script = document.querySelector<HTMLScriptElement>(`script[src="${LOADER_SRC}"]`);
    if (!script) {
      script = document.createElement('script');
      script.src = LOADER_SRC;
      script.async = true;
      script.defer = true;
      script.dataset.oetWhopLoader = 'true';
      script.onerror = () => onUnavailableRef.current();
      document.head.appendChild(script);
    }

    const timeout = window.setTimeout(() => {
      const host = document.getElementById(containerId);
      if (host && !host.querySelector('iframe')) {
        onUnavailableRef.current();
      }
    }, 12_000);

    return () => {
      window.clearTimeout(timeout);
      delete scopedWindow[callbackName];
      delete scopedWindow[errorName];
    };
  }, [containerId, planId]);

  if (!isWhopPlanId(planId)) {
    return null;
  }

  const callbackName = `__oetWhopComplete_${containerId.replace(/-/g, '_')}`;
  const errorName = `__oetWhopError_${containerId.replace(/-/g, '_')}`;
  const whopSession = isWhopCheckoutSessionId(sessionId) ? sessionId : undefined;

  return (
    <div className="mt-4" data-testid="whop-embedded-checkout">
      <div
        id={containerId}
        data-whop-checkout-plan-id={planId}
        data-whop-checkout-return-url={returnUrl}
        data-whop-checkout-theme="light"
        data-whop-checkout-theme-accent-color="#7c3aed"
        data-whop-checkout-skip-redirect="true"
        data-whop-checkout-on-complete={callbackName}
        data-whop-checkout-on-payment-error={errorName}
        {...(whopSession ? { 'data-whop-checkout-session': whopSession } : {})}
        className="min-h-[480px] w-full overflow-hidden rounded-xl border border-border bg-white"
      />
      <p className="mt-3 text-xs leading-5 text-muted">
        Pay on this page. Access unlocks after the payment provider confirms the charge.
      </p>
      <noscript>
        <a href={checkoutUrl}>Continue on Whop checkout</a>
      </noscript>
    </div>
  );
}
