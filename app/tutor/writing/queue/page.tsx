'use client';

import { ClipboardList } from 'lucide-react';
import { TutorRouteHero, TutorRouteWorkspace } from '@/components/domain/tutor-route-surface';
import { WritingReviewQueue } from '@/components/domain/writing/writing-review-queue';

export default function TutorWritingQueuePage() {
  return (
    <TutorRouteWorkspace>
      <TutorRouteHero
        eyebrow="Tutor portal"
        icon={ClipboardList}
        title="Writing review queue"
        description="Claim a submission to start a review. Each claim auto-expires after 36 hours."
      />

      <WritingReviewQueue reviewHrefBase="/tutor/writing/reviews" initialStatus="pending" />
    </TutorRouteWorkspace>
  );
}
