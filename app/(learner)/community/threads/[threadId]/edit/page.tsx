'use client';

import { useEffect } from 'react';
import { useRouter, useParams } from 'next/navigation';
import { PenLine, ArrowLeft } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';

export default function EditThreadPage() {
  const router = useRouter();
  const params = useParams();
  const threadId = params?.threadId as string;

  useEffect(() => {
    analytics.track('community_edit_thread_viewed', { threadId });
  }, [threadId]);

  return (
    <LearnerDashboardShell pageTitle="Edit Thread">
      <LearnerPageHero
        title="Edit Thread"
        description="Update your thread content."
        icon={PenLine}
      />

      <MotionSection className="mx-auto max-w-2xl space-y-4">
        <Button
          variant="outline"
          size="sm"
          onClick={() => router.push(`/community/threads/${threadId}`)}
        >
          <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to Thread
        </Button>

        <Card className="p-6 shadow-sm">
          <InlineAlert variant="info">
            Editing threads isn&apos;t available yet. You can still view the thread
            and add replies. Thank you for your patience while we build this out.
          </InlineAlert>
        </Card>
      </MotionSection>
    </LearnerDashboardShell>
  );
}
