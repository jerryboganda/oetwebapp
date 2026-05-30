'use client';

import { useParams, useRouter } from 'next/navigation';
import { UserRoundCheck } from 'lucide-react';
import { TutorRouteHero, TutorRouteWorkspace } from '@/components/domain/tutor-route-surface';
import { TutorMarkingWorkspace } from '@/components/domain/writing/marking/TutorMarkingWorkspace';

/**
 * Tutor writing review screen (spec §12/§13/§14, WS-F5).
 *
 * Rubric marking with span annotations + content checklist + AI pre-analysis +
 * double-marking/moderation. All marking behaviour lives in the shared
 * TutorMarkingWorkspace; this route keeps the tutor console shell + hero and
 * supplies the submissionId from the URL.
 */
export default function TutorWritingReviewPage() {
  const params = useParams<{ submissionId: string }>();
  const router = useRouter();
  const submissionId = String(params?.submissionId ?? '');

  return (
    <TutorRouteWorkspace>
      <TutorRouteHero
        eyebrow="Tutor review"
        icon={UserRoundCheck}
        title="Mark a learner letter"
        description="Annotate the response, score the six OET criteria, mark the content checklist, and add feedback. The AI pre-analysis is a starting point you can confirm, edit, or reject."
      />

      <div className="mt-4">
        <TutorMarkingWorkspace
          submissionId={submissionId}
          variant="tutor"
          onComplete={() => router.push('/tutor/writing/queue')}
        />
      </div>
    </TutorRouteWorkspace>
  );
}
