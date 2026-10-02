'use client';

import { useEffect } from 'react';
import Link from 'next/link';
import { Sparkles } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';

export default function BillingPlansPage() {
  useEffect(() => {
    analytics.track('page_viewed', { page: 'billing-plans' });
  }, []);

  return (
    <>
      <LearnerPageHero
        eyebrow="OET Billing"
        icon={Sparkles}
        accent="primary"
        title="Plans are managed from Billing"
        description="Public launch pricing and entitlements are loaded from the billing backend. Static plan cards are not shown here."
      />
      <InlineAlert
        variant="info"
        live="polite"
        action={(
          <Button asChild variant="primary" size="sm">
            <Link href="/billing">Open Billing</Link>
          </Button>
        )}
      >
        Open Billing to view your current subscription and any server-published OET plan actions.
      </InlineAlert>
    </>
  );
}
