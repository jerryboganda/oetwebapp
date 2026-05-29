'use client';

import { useEffect, useState } from 'react';
import { useSearchParams, useRouter } from 'next/navigation';
import { CreditCard } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

interface RedeemResponse {
  userId: string;
  subscriptionId: string;
}

/**
 * Phase 5 — redeem a one-time signed link emailed during dunning so a
 * learner can update their saved payment method without logging in.
 * After redeeming the token, redirect into the existing checkout-session
 * card-update flow.
 */
export default function UpdateCardPage() {
  const params = useSearchParams();
  const router = useRouter();
  const token = params?.get('token') ?? null;

  const [state, setState] = useState<'loading' | 'success' | 'invalid'>('loading');
  const [details, setDetails] = useState<RedeemResponse | null>(null);

  useEffect(() => {
    if (!token) {
      setState('invalid');
      return;
    }
    (async () => {
      try {
        const res = await fetch(`/api/backend/v1/billing/update-card/${encodeURIComponent(token)}`);
        if (!res.ok) {
          setState('invalid');
          return;
        }
        setDetails(await res.json());
        setState('success');
      } catch {
        setState('invalid');
      }
    })();
  }, [token]);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        icon={<CreditCard className="h-6 w-6" />}
        eyebrow="Billing"
        title="Update your card"
        description="Use this one-time link to update the card on file for your subscription."
      />

      <div className="space-y-4 rounded-2xl border border-border bg-surface p-6 shadow-sm">
        {state === 'loading' && <Skeleton className="h-24 w-full" />}

        {state === 'invalid' && (
          <InlineAlert variant="error">
            This link is invalid, expired, or has already been used. Please sign in and request a new card-update link from your billing page.
          </InlineAlert>
        )}

        {state === 'success' && details && (
          <>
            <p className="text-sm">
              Verified for subscription <code>{details.subscriptionId.slice(0, 12)}…</code>. Click below to open the secure card-update form for your gateway.
            </p>
            <div className="flex justify-end">
              <Button onClick={() => router.push('/billing?intent=update-card')}>Open card-update form</Button>
            </div>
          </>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
