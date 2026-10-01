'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Compass, CalendarDays, CheckCircle2, Target } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { cardClassName } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { getWritingPathway, writingSkillLabels, writingStageLabels, type WritingPathwayDto } from '@/lib/writing-pathway-api';
import { cn } from '@/lib/utils';

export default function WritingPathwayPage() {
  const [pathway, setPathway] = useState<WritingPathwayDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getWritingPathway().then(setPathway).catch(() => setError('Could not load your Writing pathway.'));
  }, []);

  const currentStage = pathway?.currentStage ?? 'onboarding';

  return (
    <>
      <LearnerPageHero
        eyebrow="Writing Pathway"
        icon={Compass}
        accent="writing"
        title="Your route from baseline to exam-ready Writing"
        description="The pathway reads your real attempts, evaluations, rule violations, and practice plan without replacing the existing grading pipeline."
        // Chips show once the pathway is in: before that the stage would read
        // the "onboarding" default, not the learner's real stage.
        highlights={pathway ? [
          { icon: Target, label: 'Stage', value: writingStageLabels[currentStage] ?? currentStage },
          { icon: CalendarDays, label: 'Weeks left', value: `${pathway.weeksRemaining}` },
          { icon: CheckCircle2, label: 'Readiness', value: `${pathway.readinessScore}%` },
        ] : []}
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="flex flex-wrap gap-3">
        <Button asChild><Link href="/writing/today">Open today&apos;s plan</Link></Button>
        <Button asChild variant="outline"><Link href="/writing/profile-setup">Edit profile</Link></Button>
        <Button asChild variant="ghost"><Link href="/writing/canon">Browse canon</Link></Button>
      </div>

      <MotionSection delayIndex={0} className="space-y-4">
        <LearnerSurfaceSectionHeader
          eyebrow="Roadmap"
          title="10-week Writing pathway"
          description="Weeks are generated from your profile and updated from your real Writing evidence."
        />
        {!pathway && !error ? (
          <LearnerSkeleton variant="card-grid" />
        ) : (
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
            {(pathway?.weeks ?? []).map((week, index) => {
              const isCurrent = week.weekNumber === pathway?.currentWeek;
              return (
                <MotionItem key={week.weekNumber} delayIndex={Math.min(index, 5)} className="min-w-0">
                  <article className={cn(cardClassName({ padding: 'md' }), 'h-full', isCurrent && !week.isCompleted && 'border-primary/40 ring-1 ring-primary/20')}>
                    <div className="mb-3 flex items-center justify-between gap-3">
                      <div className="min-w-0">
                        <p className="eyebrow tabular-nums text-muted">Week {week.weekNumber}</p>
                        <h3 className="text-base font-bold capitalize text-navy">{week.phase}</h3>
                      </div>
                      <Badge variant={week.isCompleted ? 'success' : isCurrent ? 'warning' : 'muted'} size="sm" className="shrink-0">
                        {week.isCompleted ? 'Done' : isCurrent ? 'Now' : 'Queued'}
                      </Badge>
                    </div>
                    <p className="text-sm text-muted">{week.theme}</p>
                    <div className="mt-3 flex flex-wrap gap-2">
                      {week.focusSkills.map((skill) => <Badge key={skill} variant="info" size="sm">{skill}: {writingSkillLabels[skill] ?? skill}</Badge>)}
                      {week.focusLetterTypes.map((type) => <Badge key={type} variant="muted" size="sm">{type}</Badge>)}
                      {week.mockScheduled ? <Badge variant="warning" size="sm">Mock</Badge> : null}
                    </div>
                  </article>
                </MotionItem>
              );
            })}
          </div>
        )}
      </MotionSection>
    </>
  );
}