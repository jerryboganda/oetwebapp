'use client';

import { useEffect, useState } from 'react';
import { useSearchParams, useRouter } from 'next/navigation';
import { CreditCard } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { apiClient } from '@/lib/api';

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
        // AllowAnonymous endpoint: apiClient omits the bearer when signed out.
        setDetails(await apiClient.get<RedeemResponse>(`/v1/billing/update-card/${encodeURIComponent(token)}`));
        setState('success');
      } catch {
        setState('invalid');
      }
    })();
  }, [token]);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        icon={CreditCard}
        eyebrow="Billing"
        title="Update your card"
        description="Use this one-time link to update the card on file for your subscription."
      />

      <Card padding="lg" className="space-y-4">
        {state === 'loading' && <Skeleton className="h-24 w-full" />}

        {state === 'invalid' && (
          <InlineAlert variant="error">
            This link is invalid, expired, or has already been used. Please sign in and request a new card-update link from your billing page.
          </InlineAlert>
        )}

        {state === 'success' && details && (
          <>
            <p className="text-sm text-navy">
              Verified for subscription <code>{details.subscriptionId.slice(0, 12)}…</code>. Click below to open the secure card-update form for your gateway.
            </p>
            <div className="flex justify-end">
              <Button onClick={() => router.push('/billing?intent=update-card')}>Open card-update form</Button>
            </div>
          </>
        )}
      </Card>
    </LearnerDashboardShell>
  );
}
