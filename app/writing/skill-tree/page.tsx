'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
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
        setError(err instanceof Error ? err.message : 'Could not load the skill tree.');
      });
    return () => {
      cancelled = true;
    };
  }, []);

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
    <LearnerDashboardShell pageTitle="Writing Skill Tree">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Foundation skills"
          icon={Route}
          accent="amber"
          title="W1-W8 — the eight skills every OET letter rests on"
          description="Each node holds two micro-lessons. Pass the quiz at ≥80% to mark it complete."
          highlights={[
            { icon: BookOpenCheck, label: 'Lessons', value: `${totalLessons}` },
            { icon: CheckCircle2, label: 'Complete', value: `${totalComplete}` },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <LearnerSurfaceSectionHeader
          eyebrow="Skill tree"
          title="Pick a sub-skill to drill into"
          description="Mastery percentages reflect your last 20 letters; lessons are independent and stay open."
        />

        <ul
          aria-label="Writing skill tree"
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
                <Card padding="md" className={tone} aria-label={`Skill ${SKILL_LABELS[skill]}`}>
                  <CardContent>
                    <header className="flex items-start justify-between gap-2">
                      <div>
                        <Badge variant={allComplete ? 'success' : 'muted'} size="sm">{skill}</Badge>
                        <h2 className="mt-1 text-sm font-bold text-navy">{SKILL_LABELS[skill]}</h2>
                      </div>
                      <Target className="h-5 w-5 text-amber-600" aria-hidden="true" />
                    </header>
                    <div className="mt-3 space-y-1">
                      <div className="flex justify-between text-xs font-bold text-muted">
                        <span>Mastery</span>
                        <span>{masteryValue}%</span>
                      </div>
                      <div
                        className="h-2 overflow-hidden rounded-full bg-slate-200"
                        role="progressbar"
                        aria-valuemin={0}
                        aria-valuemax={100}
                        aria-valuenow={masteryValue}
                        aria-label={`${SKILL_LABELS[skill]} mastery: ${masteryValue}%`}
                      >
                        <div
                          className="h-full bg-primary"
                          style={{ width: `${masteryValue}%` }}
                        />
                      </div>
                      <p className="text-xs text-muted">
                        Lessons: {completedForSkill}/{lessonsForSkill.length}
                      </p>
                    </div>
                    <div className="mt-3">
                      <Button asChild size="sm" variant="outline">
                        <Link href={`/writing/lessons?subSkill=${encodeURIComponent(skill)}`} aria-label={`Open lessons for ${SKILL_LABELS[skill]}`}>
                          Open lessons <ArrowRight className="h-3 w-3" aria-hidden="true" />
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
