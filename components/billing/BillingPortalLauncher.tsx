'use client';

import { useState } from 'react';
import { ExternalLink, Loader2 } from 'lucide-react';

import { createSubscriptionPortalSession } from '@/lib/api';
import { Button } from '@/components/ui/button';

/**
 * Single-button launcher that opens the Stripe Customer Portal for the
 * signed-in learner. Anywhere the learner needs to manage their card,
 * subscription, or plan tier, drop this component in.
 */

export interface BillingPortalLauncherProps {
  children?: React.ReactNode;
  variant?: 'primary' | 'secondary' | 'ghost' | 'destructive' | 'outline';
  /** Where Stripe should return the user. Defaults to the current URL. */
  returnTo?: string;
  className?: string;
}

export function BillingPortalLauncher({
  children = 'Open billing portal',
  variant = 'primary',
  returnTo,
  className,
}: BillingPortalLauncherProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function launch() {
    setError(null);
    setBusy(true);
    try {
      const fallback = typeof window !== 'undefined' ? window.location.href : undefined;
      const response = await createSubscriptionPortalSession(returnTo ?? fallback);
      if (response?.url && typeof window !== 'undefined') {
        window.location.href = response.url;
      } else {
        throw new Error('Portal session did not return a redirect URL.');
      }
    } catch (err) {
      console.error(err);
      setError(err instanceof Error ? err.message : 'Could not open billing portal.');
      setBusy(false);
    }
  }

  return (
    <div className={['inline-flex flex-col gap-1', className].filter(Boolean).join(' ')}>
      <Button
        variant={variant}
        disabled={busy}
        onClick={() => void launch()}
      >
        {busy ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : <ExternalLink className="mr-1 h-4 w-4" />}
        {children}
      </Button>
      {error ? <p className="text-xs text-danger">{error}</p> : null}
    </div>
  );
}
