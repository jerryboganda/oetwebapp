'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { Shuffle, BookOpen, Headphones, Mic, PenLine, Lightbulb } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { CountUp } from '@/components/ui/count-up';
import { Select } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

interface PracticeTask {
  order: number; contentId: string; title: string; subtestCode: string; taskType: string;
  durationMinutes: number; difficulty: string; isWeakArea: boolean;
  /** Where the task opens; set by the API. */
  route?: string;
}

interface InterleavedSession {
  sessionId: string; targetDurationMinutes: number; actualDurationMinutes: number; taskCount: number;
  tasks: PracticeTask[]; scienceBasis: string; tips: string[];
}

const apiRequest = apiClient.request;

const DURATION_OPTIONS = [10, 15, 20, 30, 45, 60].map((minutes) => ({ value: String(minutes), label: `${minutes} min` }));

const SUBTEST_ICON: Record<string, typeof BookOpen> = { reading: BookOpen, listening: Headphones, writing: PenLine, speaking: Mic };
/** Sub-test identity (DESIGN.md §2 skill tokens); today's codemod had them as status colours. */
const SUBTEST_COLOR: Record<string, string> = {
  reading: 'border-skill-reading/20 bg-skill-reading/10 text-skill-reading',
  listening: 'border-skill-listening/20 bg-skill-listening/10 text-skill-listening',
  writing: 'border-skill-writing/20 bg-skill-writing/10 text-skill-writing',
  speaking: 'border-skill-speaking/20 bg-skill-speaking/10 text-skill-speaking',
};

export default function InterleavedPracticePage() {
  const [session, setSession] = useState<InterleavedSession | null>(null);
  const [loading, setLoading] = useState(false);
  const [duration, setDuration] = useState(20);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { analytics.track('interleaved_practice_viewed'); }, []);

  const generate = async () => {
    setLoading(true);
    setError(null);
    try { setSession(await apiRequest<InterleavedSession>(`/v1/learner/interleaved-practice?durationMinutes=${duration}`)); }
    catch (err) {
      // Say so instead of silently clearing the plan.
      setSession(null);
      setError(err instanceof Error ? err.message : 'Could not generate a session. Please try again.');
    }
    finally { setLoading(false); }
  };

  return (
    <>
      <LearnerPageHero
        eyebrow="Practice"
        icon={Shuffle}
        title="Interleaved Practice"
        description="Mix different skill types in one session for better long-term retention."
      />

      <MotionSection>
        <Card padding="lg">
          <LearnerSurfaceSectionHeader
            eyebrow="Session builder"
            title="Generate a mixed practice set"
            description="Mix sub-tests in one session to build flexibility and stamina."
            className="mb-4"
          />
          <div className="flex flex-col gap-4 lg:flex-row lg:items-end">
            <div className="min-w-0 flex-1">
              <Select
                id="interleaved-duration"
                label="Duration (minutes)"
                value={String(duration)}
                onChange={(e) => setDuration(Number(e.target.value))}
                options={DURATION_OPTIONS}
                className="w-full lg:max-w-xs"
              />
            </div>
            <Button onClick={generate} disabled={loading} size="lg" className="lg:min-w-48">{loading ? 'Generating...' : 'Generate Session'}</Button>
          </div>
        </Card>
      </MotionSection>

      {loading ? (
        <div className="space-y-3" role="status" aria-busy="true" aria-label="Generating your session">
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-2xl" />)}
        </div>
      ) : null}

      {error ? <InlineAlert variant="error" dismissible onDismiss={() => setError(null)}>{error}</InlineAlert> : null}

      {session ? (
        <>
          <MotionSection>
            <Card className="flex items-start gap-3">
              <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                <Lightbulb className="h-4 w-4" aria-hidden="true" />
              </div>
              <div className="min-w-0">
                <p className="eyebrow text-muted">Why this mix works</p>
                <p className="mt-1 text-sm leading-6 text-muted">{session.scienceBasis}</p>
                <div className="mt-3 flex flex-wrap gap-2 text-sm text-navy">
                  <span className="rounded-full bg-background-light px-3 py-1 font-semibold"><strong className="tabular-nums"><CountUp value={session.taskCount} /></strong> tasks</span>
                  <span className="rounded-full bg-background-light px-3 py-1 font-semibold"><strong className="tabular-nums"><CountUp value={session.actualDurationMinutes} /></strong> min total</span>
                </div>
              </div>
            </Card>
          </MotionSection>

          <MotionSection delayIndex={1}>
            <LearnerSurfaceSectionHeader
              eyebrow="Session tasks"
              title="Balanced task order"
              description="The sequence intentionally rotates subtests so the learner does not settle into one mode too long."
              className="mb-4"
            />
            <ol className="space-y-3">
              {session.tasks.map((task, index) => {
                const Icon = SUBTEST_ICON[task.subtestCode] ?? BookOpen;
                return (
                  <li key={task.order}>
                    <MotionItem delayIndex={Math.min(index, 5)}>
                      <Card padding="sm" className="flex items-center gap-4">
                        <div className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl border ${SUBTEST_COLOR[task.subtestCode] ?? 'border-border bg-background-light text-navy'}`}>
                          <Icon className="h-5 w-5" aria-hidden="true" />
                        </div>
                        <div className="min-w-0 flex-1">
                          <div className="flex items-center gap-2">
                            <span className="eyebrow tabular-nums text-muted">#{task.order}</span>
                            <h3 className="truncate text-sm font-semibold text-navy">{task.title}</h3>
                            {task.isWeakArea && <Badge variant="danger" className="shrink-0 text-3xs">Weak area</Badge>}
                          </div>
                          <p className="text-xs capitalize text-muted">{task.subtestCode} • {task.taskType.replace(/-/g, ' ')} • <span className="tabular-nums">{task.durationMinutes}</span> min</p>
                        </div>
                        {task.route ? (
                          <Button asChild variant="outline" size="sm" className="shrink-0">
                            <Link href={task.route}>Start</Link>
                          </Button>
                        ) : null}
                      </Card>
                    </MotionItem>
                  </li>
                );
              })}
            </ol>
          </MotionSection>

          {session.tips.length > 0 && (
            <MotionSection delayIndex={2}>
              <Card>
                <h3 className="text-sm font-semibold text-navy">Tips</h3>
                <ul className="mt-2 list-disc space-y-2 ps-5 text-sm leading-6 text-muted">
                  {session.tips.map((tip, i) => <li key={i}>{tip}</li>)}
                </ul>
              </Card>
            </MotionSection>
          )}
        </>
      ) : null}
    </>
  );
}
