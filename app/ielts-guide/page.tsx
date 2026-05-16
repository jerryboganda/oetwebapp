'use client';

import { useEffect } from 'react';
import Link from 'next/link';
import { Globe, LockKeyhole, Mail } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';

export default function IeltsGuidePage() {
  useEffect(() => {
    analytics.track('page_viewed', { page: 'ielts-guide' });
  }, []);

  return (
    <LearnerDashboardShell pageTitle="IELTS Guide">
      <main className="space-y-6">
        <LearnerPageHero
          eyebrow="Beta foundation"
          icon={Globe}
          accent="primary"
          title="IELTS is not a public launch product"
          description="OET is the only public launch product. IELTS foundations remain informational beta work and are not available as learner practice, guidance, or adaptive content."
          highlights={[
            { icon: LockKeyhole, label: 'Status', value: 'Beta gated' },
            { icon: Globe, label: 'Public product', value: 'OET only' },
          ]}
        />
        <InlineAlert variant="info" title="IELTS unavailable">
          IELTS content and practice routes are closed until their dedicated beta readiness, content, support, and billing gates are complete.
        </InlineAlert>
        <div className="flex flex-wrap gap-3">
          <Link className="inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-medium text-white hover:bg-primary/90" href="/support">
            <Mail className="h-4 w-4" /> Contact support
          </Link>
          <Link className="rounded-lg border border-border px-4 py-2 text-sm font-medium text-navy hover:bg-background-light" href="/dashboard">
            Return to dashboard
          </Link>
        </div>
      </main>
    </LearnerDashboardShell>
  );
}
