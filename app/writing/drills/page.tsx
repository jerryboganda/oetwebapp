'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { ArrowDownUp, Dumbbell, FileText, Hash, ListChecks, MessageSquareQuote, Repeat2, Sparkles, Target } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
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

  useEffect(() => {
    getWritingDrills(skill).then(setDrills).catch(() => setError('Could not load Writing drills.'));
  }, [skill]);

  return (
    <LearnerDashboardShell pageTitle="Writing Drills">
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Writing Practice"
          icon={Dumbbell}
          accent="amber"
          title={skill ? `${skill}: ${writingSkillLabels[skill] ?? 'Targeted drills'}` : 'Targeted Writing drills'}
          description="Short deterministic drills practise one Writing skill at a time and store attempts separately from exam submissions."
          highlights={[{ icon: Target, label: 'Available', value: `${drills.length}` }, { icon: Repeat2, label: 'Mode', value: 'Deterministic' }]}
        />
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <section className="space-y-4">
          <LearnerSurfaceSectionHeader eyebrow="Categories" title="Authored drill bank" />
          <div className="grid gap-3 md:grid-cols-3">
            {categories.map((category) => {
              const Icon = category.icon;
              return (
                <Link key={category.type} href={`/writing/drills/${category.type}`} className="rounded-2xl border border-border bg-surface p-4 shadow-sm transition-colors hover:border-primary/40">
                  <Icon className="h-5 w-5 text-primary" />
                  <p className="mt-3 font-bold text-navy">{category.title}</p>
                </Link>
              );
            })}
          </div>
        </section>

        <section className="space-y-4">
          <LearnerSurfaceSectionHeader eyebrow="Pathway Drills" title="Skill-targeted practice queue" />
          <div className="grid gap-3 md:grid-cols-2">
            {drills.map((drill) => (
              <div key={drill.id} className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
                <div className="flex flex-wrap gap-2">
                  <Badge variant="info" size="sm">{drill.targetSubSkill}</Badge>
                  <Badge variant="warning" size="sm">difficulty {drill.difficulty}</Badge>
                  {drill.attemptCount ? <Badge variant="success" size="sm">{drill.attemptCount} attempts</Badge> : null}
                </div>
                <h2 className="mt-3 text-base font-bold text-navy">{drill.title}</h2>
                <p className="mt-1 text-sm text-muted">{writingSkillLabels[drill.targetSubSkill] ?? drill.targetSubSkill}</p>
                <Button asChild size="sm" className="mt-4"><Link href={`/writing/drills/practice/${drill.id}`}>Open drill</Link></Button>
              </div>
            ))}
          </div>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
