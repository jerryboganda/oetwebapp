'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { ArrowDownUp, ChevronRight, Dumbbell, FileText, Hash, ListChecks, MessageSquareQuote, Sparkles, Target } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { getWritingDrills, type WritingDrillSummaryDto, writingSkillLabels } from '@/lib/writing-pathway-api';

const categories = [
  { type: 'relevance', title: 'Case-note selection', icon: ListChecks },
  { type: 'opening', title: 'Opening paragraphs', icon: MessageSquareQuote },
  { type: 'ordering', title: 'Paragraph ordering', icon: ArrowDownUp },
  { type: 'expansion', title: 'Sentence expansion', icon: FileText },
  { type: 'tone', title: 'Formal tone', icon: Sparkles },
  { type: 'abbreviation', title: 'Abbreviations', icon: Hash },
];

export default function WritingDrillsPage() {
  const searchParams = useSearchParams();
  const skill = searchParams?.get('skill') ?? undefined;
  const [drills, setDrills] = useState<WritingDrillSummaryDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  // The skill filter whose list last settled; anything else is still loading.
  const [settledSkill, setSettledSkill] = useState<string | null>(null);
  const loading = settledSkill !== (skill ?? '');

  useEffect(() => {
    getWritingDrills(skill)
      .then(setDrills)
      .catch(() => setError('Could not load Writing drills.'))
      .finally(() => setSettledSkill(skill ?? ''));
  }, [skill]);

  return (
    <>
      <LearnerPageHero
        eyebrow="Writing Practice"
        icon={Dumbbell}
        accent="writing"
        title={skill ? `${skill}: ${writingSkillLabels[skill] ?? 'Targeted drills'}` : 'Targeted Writing drills'}
        description="Short deterministic drills practise one Writing skill at a time and store attempts separately from exam submissions."
        highlights={[{ icon: Target, label: 'Available', value: `${drills.length}` }]}
      />
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <MotionSection delayIndex={0} className="space-y-4">
        <LearnerSurfaceSectionHeader eyebrow="Categories" title="Authored drill bank" />
        <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-3">
          {categories.map((category, index) => {
            const Icon = category.icon;
            return (
              <li key={category.type}>
                <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                  <CardLink href={`/writing/drills/${category.type}`} className="flex h-full items-center gap-3">
                    <span className="inline-flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-lavender text-primary">
                      <Icon className="h-5 w-5" aria-hidden="true" />
                    </span>
                    <span className="min-w-0 flex-1 font-bold text-navy">{category.title}</span>
                    <ChevronRight className="h-4 w-4 shrink-0 text-muted rtl:rotate-180" aria-hidden="true" />
                  </CardLink>
                </MotionItem>
              </li>
            );
          })}
        </ul>
      </MotionSection>

      <MotionSection delayIndex={1} className="space-y-4">
        <LearnerSurfaceSectionHeader eyebrow="Pathway Drills" title="Skill-targeted practice queue" />
        {loading && drills.length === 0 ? (
          <LearnerSkeleton variant="card-grid" />
        ) : drills.length === 0 ? (
          error ? null : <EmptyState icon={<Dumbbell className="h-8 w-8" />} title="No drills available for this filter yet." />
        ) : (
          <ul className="grid grid-cols-1 gap-3 md:grid-cols-2">
            {drills.map((drill, index) => (
              <li key={drill.id} className="min-w-0">
                <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                  <Card padding="md" className="flex h-full flex-col">
                    <div className="flex flex-wrap gap-2">
                      <Badge variant="info" size="sm">{drill.targetSubSkill}</Badge>
                      {drill.attemptCount ? <Badge variant="success" size="sm" className="tabular-nums">{drill.attemptCount} attempts</Badge> : null}
                    </div>
                    <h2 className="mt-3 text-base font-bold text-navy">{drill.title}</h2>
                    <p className="mt-1 text-sm text-muted">{writingSkillLabels[drill.targetSubSkill] ?? drill.targetSubSkill}</p>
                    <div className="mt-auto pt-4">
                      <Button asChild size="sm"><Link href={`/writing/drills/practice/${drill.id}`}>Open drill</Link></Button>
                    </div>
                  </Card>
                </MotionItem>
              </li>
            ))}
          </ul>
        )}
      </MotionSection>
    </>
  );
}
