'use client';

import { ClipboardList } from 'lucide-react';
import { ExpertRouteHero, ExpertRouteWorkspace } from '@/components/domain/expert-route-surface';
import { WritingReviewQueue } from '@/components/domain/writing/writing-review-queue';

/**
 * Expert writing review queue.
 *
 * Writing reviews are V2 submission-based (WritingSubmission → WritingTutorReviewAssignment):
 * the expert is the tutor for writing, so this surfaces `/v1/tutors/writing/queue` inside the
 * expert console and opens each review in the submission-keyed marking workspace at
 * `/expert/review/writing/{submissionId}`. There is intentionally no link to the legacy
 * V1 ReviewRequest writing surface — a `review-…` id has no WritingSubmission and cannot
 * resolve there. The tutor console renders the same queue at `/tutor/writing/queue`.
 */
export default function ExpertWritingQueuePage() {
  return (
    <ExpertRouteWorkspace role="main" aria-label="Writing reviews">
      <ExpertRouteHero
        eyebrow="Expert queue"
        icon={ClipboardList}
        accent="emerald"
        title="Writing review queue"
        description="Claim a learner letter to lock it to you, then open it to mark the six OET criteria with span annotations, the content checklist, and AI pre-analysis. Claims auto-expire after 36 hours so unclaimed work re-pools."
      />

      <WritingReviewQueue reviewHrefBase="/expert/review/writing" initialStatus="" />
    </ExpertRouteWorkspace>
  );
}
