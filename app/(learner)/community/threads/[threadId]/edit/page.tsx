'use client';

import { useEffect } from 'react';
import { useRouter, useParams } from 'next/navigation';
import { PenLine, ArrowLeft } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
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
    <>
      <LearnerPageHero
        title="Edit Thread"
        description="Update your thread content."
        icon={PenLine}
      />

      <MotionSection className="space-y-4">
        <Button
          variant="outline"
          size="sm"
          onClick={() => router.push(`/community/threads/${threadId}`)}
        >
          <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Back to Thread
        </Button>

        <InlineAlert variant="info" live="polite">
          Editing threads isn&apos;t available yet. You can still view the thread
          and add replies. Thank you for your patience while we build this out.
        </InlineAlert>
      </MotionSection>
    </>
  );
}
