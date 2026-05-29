'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { ArrowRight, BookOpenCheck, CheckCircle2, Route, Target } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
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
    <LearnerDashboardShell pageTitle={t('writing.skillTree.pageTitle')}>
      <div className="space-y-6">
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

        <LearnerSurfaceSectionHeader
          eyebrow={t('writing.skillTree.section.eyebrow')}
          title={t('writing.skillTree.section.title')}
          description={t('writing.skillTree.section.description')}
        />

        <ul
          aria-label={t('writing.skillTree.list.label')}
          className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4"
        >
          {SKILLS.map((skill) => {
            const masteryValue = Math.max(0, Math.min(100, Math.round(skills?.mastery?.[skill] ?? 0)));
            const lessonsForSkill = lessonsBySkill.get(skill) ?? [];
            const completedForSkill = lessonsForSkill.filter((l) => completionMap.has(l.id)).length;
            const allComplete = lessonsForSkill.length > 0 && completedForSkill === lessonsForSkill.length;
            const tone = allComplete
              ? 'border-emerald-300/70 bg-emerald-50/60'
              : masteryValue >= 70
                ? 'border-amber-300/70 bg-amber-50/60'
                : 'border-border bg-background';
            return (
              <li key={skill}>
                <Card padding="md" className={tone} aria-label={t('writing.skillTree.skillAria', { label: SKILL_LABELS[skill] })}>
                  <CardContent>
                    <header className="flex items-start justify-between gap-2">
                      <div>
                        <Badge variant={allComplete ? 'success' : 'muted'} size="sm">{skill}</Badge>
                        {/* SKILL_LABELS are OET-authored English content; force LTR inside RTL chrome. */}
                        <h2 className="mt-1 text-sm font-bold text-navy" dir="ltr">{SKILL_LABELS[skill]}</h2>
                      </div>
                      <Target className="h-5 w-5 text-amber-600" aria-hidden="true" />
                    </header>
                    <div className="mt-3 space-y-1">
                      <div className="flex justify-between text-xs font-bold text-muted">
                        <span>{t('writing.skillTree.card.mastery')}</span>
                        <span>{masteryValue}%</span>
                      </div>
                      <div
                        className="h-2 overflow-hidden rounded-full bg-border"
                        role="progressbar"
                        aria-valuemin={0}
                        aria-valuemax={100}
                        aria-valuenow={masteryValue}
                        aria-label={t('writing.skillTree.masteryAria', { label: SKILL_LABELS[skill], value: masteryValue })}
                      >
                        <div
                          className="h-full bg-primary"
                          style={{ width: `${masteryValue}%` }}
                        />
                      </div>
                      <p className="text-xs text-muted">
                        {t('writing.skillTree.card.lessonsLabel')} {t('writing.skillTree.card.lessonsRatio', { complete: completedForSkill, total: lessonsForSkill.length })}
                      </p>
                    </div>
                    <div className="mt-3">
                      <Button asChild size="sm" variant="outline">
                        <Link href={`/writing/lessons?subSkill=${encodeURIComponent(skill)}`} aria-label={t('writing.skillTree.openLessonsAria', { skill: SKILL_LABELS[skill] })}>
                          {t('writing.skillTree.openLessons')} <ArrowRight className="h-3 w-3" aria-hidden="true" />
                        </Link>
                      </Button>
                    </div>
                  </CardContent>
                </Card>
              </li>
            );
          })}
        </ul>
      </div>
    </LearnerDashboardShell>
  );
}
