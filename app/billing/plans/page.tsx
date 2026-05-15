'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { CreditCard, Sparkles } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';

export default function BillingPlansPage() {
  const router = useRouter();

  useEffect(() => {
    analytics.track('page_viewed', { page: 'billing-plans' });
  }, []);

  return (
    <LearnerDashboardShell pageTitle="Plans & Pricing">
      <main className="space-y-6">
        <LearnerPageHero
          eyebrow="OET Billing"
          icon={Sparkles}
          accent="primary"
          title="Plans are managed from Billing"
          description="Public launch pricing and entitlements are loaded from the billing backend. Static plan cards are not shown here."
          highlights={[
            { icon: CreditCard, label: 'Source', value: 'Billing backend' },
            { icon: Sparkles, label: 'Product', value: 'OET only' },
          ]}
        />
        <InlineAlert variant="info">
          Open Billing to view your current subscription and any server-published OET plan actions.
        </InlineAlert>
        <Button variant="primary" onClick={() => router.push('/billing')}>
          Open Billing
        </Button>
      </main>
    </LearnerDashboardShell>
  );
}
