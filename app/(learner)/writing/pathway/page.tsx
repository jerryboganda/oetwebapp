'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Compass, CalendarDays, CheckCircle2, Target } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { getWritingPathway, writingSkillLabels, writingStageLabels, type WritingPathwayDto } from '@/lib/writing-pathway-api';

export default function WritingPathwayPage() {
  const [pathway, setPathway] = useState<WritingPathwayDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getWritingPathway().then(setPathway).catch(() => setError('Could not load your Writing pathway.'));
  }, []);

  const currentStage = pathway?.currentStage ?? 'onboarding';

  return (
    <LearnerDashboardShell pageTitle="Writing Pathway">
      <div className="space-y-5 sm:space-y-8">
        <LearnerPageHero
          eyebrow="Writing Pathway"
          icon={Compass}
          accent="amber"
          title="Your route from baseline to exam-ready Writing"
          description="The pathway reads your real attempts, evaluations, rule violations, and practice plan without replacing the existing grading pipeline."
          highlights={[
            { icon: Target, label: 'Stage', value: writingStageLabels[currentStage] ?? currentStage },
            { icon: CalendarDays, label: 'Weeks left', value: pathway ? `${pathway.weeksRemaining}` : 'Loading' },
            { icon: CheckCircle2, label: 'Readiness', value: pathway ? `${pathway.readinessScore}%` : 'Loading' },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <div className="flex flex-wrap gap-3">
          <Button asChild><Link href="/writing/today">Open today&apos;s plan</Link></Button>
          <Button asChild variant="outline"><Link href="/writing/profile-setup">Edit profile</Link></Button>
          <Button asChild variant="ghost"><Link href="/writing/canon">Browse canon</Link></Button>
        </div>

        <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <LearnerSurfaceSectionHeader
            eyebrow="Roadmap"
            title="10-week Writing pathway"
            description="Weeks are generated from your profile and updated from your real Writing evidence."
            className="mb-5"
          />
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {(pathway?.weeks ?? []).map((week) => (
              <article key={week.weekNumber} className="rounded-xl border border-border bg-background p-4">
                <div className="mb-3 flex items-center justify-between gap-3">
                  <div>
                    <p className="text-xs font-bold uppercase tracking-widest text-muted">Week {week.weekNumber}</p>
                    <h2 className="text-base font-bold text-navy capitalize">{week.phase}</h2>
                  </div>
                  <Badge variant={week.isCompleted ? 'success' : week.weekNumber === pathway?.currentWeek ? 'warning' : 'muted'} size="sm">
                    {week.isCompleted ? 'Done' : week.weekNumber === pathway?.currentWeek ? 'Now' : 'Queued'}
                  </Badge>
                </div>
                <p className="text-sm text-muted">{week.theme}</p>
                <div className="mt-3 flex flex-wrap gap-2">
                  {week.focusSkills.map((skill) => <Badge key={skill} variant="info" size="sm">{skill}: {writingSkillLabels[skill] ?? skill}</Badge>)}
                  {week.focusLetterTypes.map((type) => <Badge key={type} variant="muted" size="sm">{type}</Badge>)}
                  {week.mockScheduled ? <Badge variant="warning" size="sm">Mock</Badge> : null}
                </div>
              </article>
            ))}
          </div>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}