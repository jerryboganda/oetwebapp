'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { ArrowRight, BookOpenCheck, CheckCircle2, Route, Target } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { ProgressBar } from '@/components/ui/progress';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { cn } from '@/lib/utils';
import { getWritingStatsSkills, listWritingLessons } from '@/lib/writing/api';
import type {
  WritingLessonCompletionDto,
  WritingLessonDto,
  WritingStatsSkillsDto,
  WritingSubSkill,
} from '@/lib/writing/types';

const SKILL_LABELS: Record<WritingSubSkill, string> = {
  W1: 'W1 Case-note triage',
  W2: 'W2 Letter framing',
  W3: 'W3 Opening purpose',
  W4: 'W4 Clinical narrative',
  W5: 'W5 Closure and request',
  W6: 'W6 Genre and tone',
  W7: 'W7 Grammar and abbreviations',
  W8: 'W8 Format and layout',
};

const SKILLS: WritingSubSkill[] = ['W1', 'W2', 'W3', 'W4', 'W5', 'W6', 'W7', 'W8'];

export default function WritingSkillTreePage() {
  const t = useTranslations();
  const [lessons, setLessons] = useState<WritingLessonDto[]>([]);
  const [completions, setCompletions] = useState<WritingLessonCompletionDto[]>([]);
  const [skills, setSkills] = useState<WritingStatsSkillsDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);

  useEffect(() => {
    let cancelled = false;
    Promise.all([listWritingLessons(), getWritingStatsSkills().catch(() => null)])
      .then(([lessonsResult, skillsResult]) => {
        if (cancelled) return;
        setLessons(lessonsResult.items);
        setCompletions(lessonsResult.completions);
        if (skillsResult) setSkills(skillsResult);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : t('writing.skillTree.error.load'));
      })
      .finally(() => {
        if (!cancelled) setLoaded(true);
      });
    return () => {
      cancelled = true;
    };
  }, [t]);

  const completionMap = useMemo(() => {
    const m = new Set<string>();
    for (const c of completions) m.add(c.lessonId);
    return m;
  }, [completions]);

  const lessonsBySkill = useMemo(() => {
    const m = new Map<WritingSubSkill, WritingLessonDto[]>();
    for (const skill of SKILLS) m.set(skill, []);
    for (const lesson of lessons) {
      m.get(lesson.subSkill)?.push(lesson);
    }
    for (const skill of SKILLS) {
      m.get(skill)?.sort((a, b) => a.orderInCourse - b.orderInCourse);
    }
    return m;
  }, [lessons]);

  const totalLessons = lessons.length;
  const totalComplete = completionMap.size;

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.skillTree.eyebrow')}
        icon={Route}
        accent="amber"
        title={t('writing.skillTree.title')}
        description={t('writing.skillTree.hero.description')}
        highlights={[
          { icon: BookOpenCheck, label: t('writing.skillTree.highlights.lessons'), value: `${totalLessons}` },
          { icon: CheckCircle2, label: t('writing.skillTree.highlights.complete'), value: `${totalComplete}` },
        ]}
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <MotionSection delayIndex={0} className="space-y-4">
        <LearnerSurfaceSectionHeader
          eyebrow={t('writing.skillTree.section.eyebrow')}
          title={t('writing.skillTree.section.title')}
          description={t('writing.skillTree.section.description')}
        />

        {/* Skeleton until the data settles: the cards would otherwise show 0% and 0/0 as if real. */}
        {!loaded ? (
          <LearnerSkeleton variant="card-grid" />
        ) : (
          <ul
            aria-label={t('writing.skillTree.list.label')}
            className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-4"
          >
            {SKILLS.map((skill, index) => {
              const masteryValue = Math.max(0, Math.min(100, Math.round(skills?.mastery?.[skill] ?? 0)));
              const lessonsForSkill = lessonsBySkill.get(skill) ?? [];
              const completedForSkill = lessonsForSkill.filter((l) => completionMap.has(l.id)).length;
              const allComplete = lessonsForSkill.length > 0 && completedForSkill === lessonsForSkill.length;
              const tone = allComplete
                ? 'border-success/30 bg-success/10'
                : masteryValue >= 70
                  ? 'border-warning/30 bg-warning/10'
                  : 'border-border bg-background';
              return (
                <li key={skill} className="min-w-0">
                  <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                    <Card padding="md" className={cn('flex h-full flex-col', tone)} aria-label={t('writing.skillTree.skillAria', { label: SKILL_LABELS[skill] })}>
                      <header className="flex items-start justify-between gap-2">
                        <div className="min-w-0">
                          <Badge variant={allComplete ? 'success' : 'muted'} size="sm">{skill}</Badge>
                          {/* SKILL_LABELS are OET-authored English content; force LTR inside RTL chrome. */}
                          <h2 className="mt-1 text-sm font-bold text-navy" dir="ltr">{SKILL_LABELS[skill]}</h2>
                        </div>
                        <Target className="h-5 w-5 shrink-0 text-skill-writing" aria-hidden="true" />
                      </header>
                      <div className="mt-3 space-y-1">
                        <div className="flex justify-between text-xs font-bold text-muted">
                          <span>{t('writing.skillTree.card.mastery')}</span>
                          <span className="tabular-nums">{masteryValue}%</span>
                        </div>
                        <ProgressBar
                          value={masteryValue}
                          ariaLabel={t('writing.skillTree.masteryAria', { label: SKILL_LABELS[skill], value: masteryValue })}
                        />
                        <p className="text-xs tabular-nums text-muted">
                          {t('writing.skillTree.card.lessonsLabel')} {t('writing.skillTree.card.lessonsRatio', { complete: completedForSkill, total: lessonsForSkill.length })}
                        </p>
                      </div>
                      <div className="mt-auto pt-3">
                        <Button asChild size="sm" variant="outline">
                          <Link href={`/writing/lessons?subSkill=${encodeURIComponent(skill)}`} aria-label={t('writing.skillTree.openLessonsAria', { skill: SKILL_LABELS[skill] })}>
                            {t('writing.skillTree.openLessons')} <ArrowRight className="h-3 w-3 rtl:rotate-180" aria-hidden="true" />
                          </Link>
                        </Button>
                      </div>
                    </Card>
                  </MotionItem>
                </li>
              );
            })}
          </ul>
        )}
      </MotionSection>
    </>
  );
}
