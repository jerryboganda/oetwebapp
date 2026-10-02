'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { BookOpen, ChevronRight, CheckCircle2, Target, Mic } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { ProgressBar } from '@/components/ui/progress';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

interface PathItem {
  id: string;
  title: string;
  difficulty: string;
  durationMinutes: number;
  completed: boolean;
  scenarioType: string | null;
}

interface SubtestPath {
  subtestCode: string;
  totalItems: number;
  completedItems: number;
  progressPercent: number;
  items: PathItem[];
}

interface LearningPathData {
  professionCode: string;
  professionLabel: string;
  examTypeCode: string;
  subtestPaths: SubtestPath[];
  overallProgress: number;
  totalContent: number;
  nextRecommended: { id: string; title: string; subtestCode: string; difficulty: string }[];
}

const apiRequest = apiClient.request;

const DIFFICULTY_BADGE: Record<string, BadgeProps['variant']> = {
  easy: 'success',
  medium: 'warning',
  hard: 'danger',
};

function learningPathHref(subtestCode: string, itemId?: string): string {
  switch (subtestCode.toLowerCase()) {
    case 'listening':
      return itemId ? `/listening/drills/${encodeURIComponent(itemId)}` : '/listening';
    case 'reading':
      return '/reading/practice';
    case 'writing':
      return '/writing/drills';
    case 'speaking':
      return '/speaking/drills';
    default:
      return '/practice';
  }
}

export default function LearningPathsPage() {
  const [data, setData] = useState<LearningPathData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { page: 'learning-paths' });
    apiRequest<LearningPathData>('/v1/learner/learning-path')
      .then(setData)
      .catch(() => setError('Unable to load learning path.'))
      .finally(() => setLoading(false));
  }, []);

  return (
    <>
      <LearnerPageHero
        title="Your Learning Path"
        description={data ? `${data.professionLabel} · ${data.examTypeCode.toUpperCase()} · ${data.overallProgress}% complete` : 'Personalized by your profession and goals'}
        icon={BookOpen}
      />

      {loading ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2" aria-hidden="true">
          {[1, 2, 3, 4].map((i) => <Skeleton key={i} className="h-40 rounded-2xl" />)}
        </div>
      ) : null}

      {error && <InlineAlert variant="error" title="Error">{error}</InlineAlert>}

      {/* Overall progress */}
      {data && (
        <MotionSection>
          <Card padding="lg">
            <div className="mb-3 flex items-center justify-between gap-3">
              <h2 className="font-semibold text-navy">Overall Progress</h2>
              <span className="text-2xl font-bold tabular-nums text-primary">{data.overallProgress}%</span>
            </div>
            <ProgressBar value={data.overallProgress} size="md" ariaLabel={`Overall progress ${data.overallProgress}%`} />
            <p className="mt-2 text-xs tabular-nums text-muted">{data.totalContent} total content items across all subtests</p>
          </Card>
        </MotionSection>
      )}

      {/* Next recommended */}
      {data && data.nextRecommended.length > 0 && (
        <section>
          <LearnerSurfaceSectionHeader icon={Target} title="Recommended Next" className="mb-3" />
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
            {data.nextRecommended.map((rec, index) => (
              <MotionItem key={rec.id} delayIndex={Math.min(index, 5)} className="h-full">
                <CardLink href={learningPathHref(rec.subtestCode, rec.id)} className="h-full">
                  <Badge variant={DIFFICULTY_BADGE[rec.difficulty] ?? 'muted'} className="capitalize">{rec.difficulty}</Badge>
                  <p className="mt-2 text-sm font-medium text-navy">{rec.title}</p>
                  <p className="text-xs capitalize text-muted">{rec.subtestCode}</p>
                </CardLink>
              </MotionItem>
            ))}
          </div>
        </section>
      )}

      {/* Wave 6 of docs/SPEAKING-MODULE-PLAN.md - "Speaking
          Foundations" pathway entry. Surfaces the §17 micro-drills
          catalogue alongside the per-subtest paths. */}
      <MotionSection>
        <Card padding="lg" className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex min-w-0 items-start gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10 text-primary">
              <Mic className="h-5 w-5" aria-hidden="true" />
            </div>
            <div className="min-w-0 space-y-1">
              <p className="eyebrow text-primary">
                Speaking Foundations
              </p>
              <h2 className="text-base font-bold text-navy">Short, focused speaking drills</h2>
              <p className="text-sm text-muted">
                Build the micro-skills behind every role-play: phrasing, intonation,
                pronunciation, vocabulary, chunking and empathy.
              </p>
            </div>
          </div>
          <Button variant="primary" asChild className="shrink-0">
            <Link href="/speaking/drills">Open drills</Link>
          </Button>
        </Card>
      </MotionSection>

      {/* Per-subtest paths */}
      {data?.subtestPaths.map((sp) => (
        <section key={sp.subtestCode}>
          <LearnerSurfaceSectionHeader
            icon={BookOpen}
            title={sp.subtestCode.charAt(0).toUpperCase() + sp.subtestCode.slice(1)}
          />
          <div className="mb-4 mt-2 flex items-center gap-3">
            <ProgressBar value={sp.progressPercent} className="flex-1" ariaLabel={`${sp.subtestCode} progress ${sp.progressPercent}%`} />
            <span className="shrink-0 text-sm font-medium tabular-nums text-muted">{sp.completedItems}/{sp.totalItems}</span>
          </div>
          <div className="space-y-2">
            {sp.items.map((item, index) => (
              <MotionItem key={item.id} delayIndex={Math.min(index, 5)}>
                <Card padding="sm" className={`flex items-center gap-3 ${item.completed ? 'opacity-60' : ''}`}>
                  {item.completed ? (
                    <CheckCircle2 className="h-5 w-5 shrink-0 text-success-strong" aria-hidden="true" />
                  ) : (
                    <ChevronRight className="h-5 w-5 shrink-0 text-muted rtl:rotate-180" aria-hidden="true" />
                  )}
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium text-navy">{item.title}</p>
                    <p className="text-xs tabular-nums text-muted">{item.durationMinutes}min · {item.difficulty}</p>
                  </div>
                  {!item.completed && (
                    <Button size="sm" variant="outline" asChild className="shrink-0">
                      <Link href={learningPathHref(sp.subtestCode, item.id)}>Start</Link>
                    </Button>
                  )}
                </Card>
              </MotionItem>
            ))}
          </div>
        </section>
      ))}
    </>
  );
}
