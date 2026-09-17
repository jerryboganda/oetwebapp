'use client';

import type { ReactNode } from 'react';
import { useEffect, useState } from 'react';
import { ExternalLink, ShoppingCart } from 'lucide-react';
import { getAppRuntimeKind, getCapacitorPlatform } from '@/lib/runtime-signals';

const WEB_SITE_URL = 'https://oetwithdrhesham.co.uk';

/**
 * App Store compliance gate (handover 4D, 17 Sep 2026): the iOS app sells
 * nothing. Enrolments and course purchases happen on the website via Stripe;
 * inside the iOS shell every purchase surface is replaced with this notice.
 * Android/web/desktop render the wrapped children unchanged, so this never
 * touches the live Android or web checkout flows.
 */
export function IosPurchaseGate({ children }: { children: ReactNode }) {
  const [isIosNative, setIsIosNative] = useState(false);

  useEffect(() => {
    setIsIosNative(
      getAppRuntimeKind() === 'capacitor-native' && getCapacitorPlatform() === 'ios',
    );
  }, []);

  if (!isIosNative) {
    return <>{children}</>;
  }

  return (
    <div className="mx-auto flex max-w-xl flex-col items-center gap-4 rounded-3xl border border-border bg-surface p-8 text-center shadow-sm">
      <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-primary/10 text-primary">
        <ShoppingCart className="h-6 w-6" aria-hidden="true" />
      </div>
      <h1 className="text-lg font-bold text-navy">Enrol on our website</h1>
      <p className="text-sm leading-relaxed text-muted">
        Course enrolments and purchases happen securely on our website, not inside
        this app. If you already have a package, simply sign in — your courses are
        waiting here.
      </p>
      <a
        href={WEB_SITE_URL}
        target="_blank"
        rel="noopener noreferrer"
        className="inline-flex items-center gap-2 rounded-2xl bg-black px-5 py-3 text-sm font-semibold text-white transition hover:bg-black/85"
      >
        <ExternalLink className="h-4 w-4" aria-hidden="true" />
        Open oetwithdrhesham.co.uk
      </a>
    </div>
  );
}
