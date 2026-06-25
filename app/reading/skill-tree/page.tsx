'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { BookOpen } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import {
  getLessons,
  getSkillRadar,
  type ReadingLessonWithProgressDto,
  type SkillRadarDto,
} from '@/lib/reading-pathway-api';

// ─── Static skill definitions ────────────────────────────────────────────────

interface SkillDef {
  code: string;
  name: string;
  description: string;
}

const SKILLS: SkillDef[] = [
  { code: 'S1', name: 'Scanning for Specific Information', description: 'Locate factual details quickly without reading every word.' },
  { code: 'S2', name: 'Skimming for Gist', description: 'Grasp the main idea of a passage at speed.' },
  { code: 'S3', name: 'Paraphrase Recognition', description: 'Match re-worded statements to original text meaning.' },
  { code: 'S4', name: 'Distractor Pattern Recognition', description: 'Identify the techniques used to craft wrong-answer traps.' },
  { code: 'S5', name: 'Inference & Implied Meaning', description: 'Read between the lines and draw logical conclusions.' },
  { code: 'S6', name: 'Reference Resolution', description: 'Track pronouns and referential language back to their antecedents.' },
  { code: 'S7', name: 'Vocabulary in Context', description: 'Derive word meaning from surrounding context clues.' },
  { code: 'S8', name: 'Time Management', description: 'Allocate time across parts to maximise your score under pressure.' },
];

// ─── Score bar ────────────────────────────────────────────────────────────────

function ScoreBar({ score, max = 10 }: { score: number; max?: number }) {
  const pct = Math.min(100, (score / max) * 100);
  const color =
    pct >= 70 ? 'bg-emerald-500' : pct >= 40 ? 'bg-amber-400' : 'bg-rose-500';

  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between text-xs text-muted">
        <span>Score</span>
        <span className="font-semibold text-foreground">{score.toFixed(1)} / {max}</span>
      </div>
      <div className="h-2 w-full rounded-full bg-border">
        <div
          className={cn('h-2 rounded-full transition-[width,background-color] duration-300', color)}
          style={{ width: `${pct}%` }}
          role="progressbar"
          aria-valuenow={score}
          aria-valuemax={max}
          aria-label={`Skill score ${score} of ${max}`}
        />
      </div>
    </div>
  );
}

// ─── Skill node card ──────────────────────────────────────────────────────────

interface SkillNodeProps {
  skill: SkillDef;
  radarSkill: SkillRadarDto['skills'][number] | null;
  lesson: ReadingLessonWithProgressDto | null;
}

function SkillNode({ skill, radarSkill, lesson }: SkillNodeProps) {
  const score = radarSkill?.current ?? 0;
  const isComplete = lesson?.progress?.completedAt != null;

  return (
    <div className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm transition-shadow hover:shadow-md">
      <div className="flex items-start justify-between gap-2">
        <div>
          <span className="inline-block rounded-full bg-primary/10 px-2 py-0.5 text-xs font-bold text-primary dark:bg-violet-900/30 dark:text-violet-400">
            {skill.code}
          </span>
          <h3 className="mt-1.5 text-sm font-semibold leading-snug text-foreground">{skill.name}</h3>
        </div>
        {isComplete && (
          <span className="shrink-0 rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-semibold text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400">
            Done
          </span>
        )}
      </div>

      <p className="text-xs text-muted leading-relaxed">{skill.description}</p>

      <ScoreBar score={score} />

      <div className="flex flex-col gap-2 pt-1">
        <Button asChild variant="primary" size="sm" fullWidth>
          <Link href={`/reading/practice?skill=${skill.code}`}>
            Practice
          </Link>
        </Button>

        {lesson ? (
          <Button asChild variant="outline" size="sm" fullWidth>
            <Link href={`/reading/lessons/${lesson.lesson.slug}`}>
              <BookOpen className="h-4 w-4" aria-hidden />
              {lesson.progress?.completedAt ? 'Review lesson' : 'Study lesson'}
            </Link>
          </Button>
        ) : null}
      </div>
    </div>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function SkillTreePage() {
  const [radar, setRadar] = useState<SkillRadarDto | null>(null);
  const [lessons, setLessons] = useState<ReadingLessonWithProgressDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [radarData, lessonsData] = await Promise.all([
          getSkillRadar().catch(() => null),
          getLessons().catch(() => []),
        ]);
        if (!cancelled) {
          setRadar(radarData);
          setLessons(lessonsData);
        }
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load skill data.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  return (
    <LearnerDashboardShell pageTitle="Skill Tree">
      <div className="space-y-5 sm:space-y-8">
        <div>
          <h1 className="text-2xl font-bold text-foreground">Reading Skill Tree</h1>
          <p className="mt-1 text-sm text-muted">
            8 core sub-skills that determine your OET Reading score. Build each to reach exam readiness.
          </p>
        </div>

        {error ? (
          <InlineAlert variant="error">{error}</InlineAlert>
        ) : loading ? (
          <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
            {Array.from({ length: 8 }, (_, i) => (
              <Skeleton key={i} className="h-52 w-full rounded-2xl" />
            ))}
          </div>
        ) : (
          <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
            {SKILLS.map((skill) => {
              const radarSkill = radar?.skills.find((s) => s.code === skill.code) ?? null;
              const lesson = lessons.find((l) => l.lesson.skillCode === skill.code) ?? null;
              return (
                <SkillNode
                  key={skill.code}
                  skill={skill}
                  radarSkill={radarSkill}
                  lesson={lesson}
                />
              );
            })}
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
