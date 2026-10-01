'use client';

import { useEffect } from 'react';
import Link from 'next/link';
import { Globe, Mail } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { analytics } from '@/lib/analytics';

export default function IeltsGuidePage() {
  useEffect(() => {
    analytics.track('page_viewed', { page: 'ielts-guide' });
  }, []);

  return (
    <>
      {/* No highlight chips: "beta gated" and "OET only" restated the description, they were not data. */}
      <LearnerPageHero
        eyebrow="Beta foundation"
        icon={Globe}
        accent="primary"
        title="IELTS is not a public launch product"
        description="OET is the only public launch product. IELTS foundations remain informational beta work and are not available as learner practice, guidance, or adaptive content."
      />
      <InlineAlert variant="info" live="polite" title="IELTS unavailable">
        IELTS content and practice routes are closed until their dedicated beta readiness, content, support, and billing gates are complete.
      </InlineAlert>
      <div className="flex flex-wrap gap-3">
        <Button asChild>
          <Link href="/support">
            <Mail className="h-4 w-4" aria-hidden="true" /> Contact support
          </Link>
        </Button>
        <Button asChild variant="outline">
          <Link href="/dashboard">Return to dashboard</Link>
        </Button>
      </div>
    </>
  );
}
