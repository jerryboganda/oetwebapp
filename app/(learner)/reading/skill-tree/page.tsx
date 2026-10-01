'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { BookOpen } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { ErrorState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { ProgressBar } from '@/components/ui/progress';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
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
  const color = pct >= 70 ? 'success' : pct >= 40 ? 'warning' : 'danger';

  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between text-xs text-muted">
        <span>Score</span>
        <span className="font-semibold tabular-nums text-navy">{score.toFixed(1)} / {max}</span>
      </div>
      <ProgressBar value={score} max={max} color={color} ariaLabel={`Skill score ${score} of ${max}`} />
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
  const isComplete = lesson?.progress?.completedAt != null;

  // Not clickable itself (its buttons are), so no hover lift.
  return (
    <Card className="flex h-full flex-col gap-3">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <Badge className="tabular-nums">{skill.code}</Badge>
          <h3 className="mt-1.5 text-sm font-semibold leading-snug text-navy">{skill.name}</h3>
        </div>
        {isComplete && (
          <Badge variant="success" className="shrink-0">
            Done
          </Badge>
        )}
      </div>

      <p className="text-xs leading-relaxed text-muted">{skill.description}</p>

      {/* Only a real radar score; no skill data is not a 0.0 / 10 score. */}
      {radarSkill ? <ScoreBar score={radarSkill.current} /> : null}

      <div className="mt-auto flex flex-col gap-2 pt-1">
        <Button asChild variant="primary" size="sm" fullWidth>
          <Link href={`/reading/practice?skill=${skill.code}`}>
            Practice
          </Link>
        </Button>
        {/* No "Study lesson" link: /reading/lessons/[slug] has no page yet, so it could only 404. */}
      </div>
    </Card>
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
    <>
      <LearnerPageHero
        eyebrow="Reading"
        icon={BookOpen}
        accent="reading"
        title="Reading Skill Tree"
        description="8 core sub-skills that determine your OET Reading score. Build each to reach exam readiness."
      />

      {error ? (
        <ErrorState message={error} />
      ) : loading ? (
        <div className="grid grid-cols-1 gap-4 min-[420px]:grid-cols-2 md:grid-cols-4">
          {Array.from({ length: 8 }, (_, i) => (
            <Skeleton key={i} className="h-52 w-full rounded-2xl" />
          ))}
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-4 min-[420px]:grid-cols-2 md:grid-cols-4">
          {SKILLS.map((skill, index) => {
            const radarSkill = radar?.skills.find((s) => s.code === skill.code) ?? null;
            const lesson = lessons.find((l) => l.lesson.skillCode === skill.code) ?? null;
            return (
              <MotionItem key={skill.code} delayIndex={Math.min(index, 5)} className="h-full">
                <SkillNode
                  skill={skill}
                  radarSkill={radarSkill}
                  lesson={lesson}
                />
              </MotionItem>
            );
          })}
        </div>
      )}
    </>
  );
}
