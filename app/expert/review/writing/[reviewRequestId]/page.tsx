'use client';

import { useParams, useRouter } from 'next/navigation';
import { UserRoundCheck } from 'lucide-react';
import { ExpertRouteHero, ExpertRouteWorkspace } from '@/components/domain/expert-route-surface';
import { TutorMarkingWorkspace } from '@/components/domain/writing/marking/TutorMarkingWorkspace';

/**
 * Expert writing review screen (spec §12/§13/§14, WS-F5).
 *
 * The expert variant of the tutor marking surface: rubric marking with span
 * annotations + content checklist + AI pre-analysis + double-marking/moderation,
 * driven from getTutorMarkingContext. It reuses the shared TutorMarkingWorkspace
 * inside the expert console shell.
 *
 * NOTE: the route param is historically named `reviewRequestId`; for the WS-F5
 * marking contract it is the submission identifier passed to
 * getTutorMarkingContext(submissionId).
 */
export default function ExpertWritingReviewPage() {
  const params = useParams<{ reviewRequestId: string }>();
  const router = useRouter();
  const submissionId = String(params?.reviewRequestId ?? '');

  return (
    <ExpertRouteWorkspace>
      <ExpertRouteHero
        eyebrow="Expert review"
        icon={UserRoundCheck}
        title="Mark a learner letter"
        description="Annotate the response, score the six OET criteria, mark the content checklist, and add feedback. Acting as a senior marker, you can also moderate divergent double-markings."
      />

      <div className="mt-4">
        <TutorMarkingWorkspace
          submissionId={submissionId}
          variant="expert"
          onComplete={() => router.push('/expert/queue')}
        />
      </div>
    </ExpertRouteWorkspace>
  );
}
