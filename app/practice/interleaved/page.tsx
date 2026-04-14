'use client';

import { useEffect, useState } from 'react';
import { Shuffle, BookOpen, Headphones, Mic, PenLine, Lightbulb } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

interface PracticeTask {
  order: number; contentId: string; title: string; subtestCode: string; taskType: string;
  durationMinutes: number; difficulty: string; isWeakArea: boolean;
}

interface InterleavedSession {
  sessionId: string; targetDurationMinutes: number; actualDurationMinutes: number; taskCount: number;
  tasks: PracticeTask[]; scienceBasis: string; tips: string[];
}

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

const SUBTEST_ICON: Record<string, typeof BookOpen> = { reading: BookOpen, listening: Headphones, writing: PenLine, speaking: Mic };
const SUBTEST_COLOR: Record<string, string> = { reading: 'bg-blue-100 text-blue-700 dark:bg-blue-950 dark:text-blue-300', listening: 'bg-purple-100 text-purple-700 dark:bg-purple-950 dark:text-purple-300', writing: 'bg-amber-100 text-amber-700 dark:bg-amber-950 dark:text-amber-300', speaking: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-950 dark:text-emerald-300' };

export default function InterleavedPracticePage() {
  const [session, setSession] = useState<InterleavedSession | null>(null);
  const [loading, setLoading] = useState(false);
  const [duration, setDuration] = useState(20);

  useEffect(() => { analytics.track('interleaved_practice_viewed'); }, []);

  const generate = async () => {
    setLoading(true);
    try { setSession(await apiRequest<InterleavedSession>(`/v1/learner/interleaved-practice?durationMinutes=${duration}`)); }
    catch { setSession(null); }
    finally { setLoading(false); }
  };

  const heroHighlights = [
    { icon: Shuffle, label: 'Mixing rule', value: 'Spaced rotation' },
    { icon: BookOpen, label: 'Focus', value: 'Retention' },
    { icon: Lightbulb, label: 'Output', value: 'Adaptive session' },
  ];

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Practice"
          icon={Shuffle}
          title="Interleaved Practice"
          description="Mix different skill types in one session for better long-term retention."
          highlights={heroHighlights}
        />

      <MotionSection className="mx-auto max-w-5xl space-y-6 px-4 py-6">
        <MotionItem>
          <Card className="p-5 shadow-sm">
            <LearnerSurfaceSectionHeader
              eyebrow="Session builder"
              title="Generate a mixed practice set"
              description="Use the same dashboard cards and spacing so this page feels like part of the platform rather than a side tool."
              className="mb-4"
            />
            <div className="flex flex-col gap-4 lg:flex-row lg:items-end">
              <div className="min-w-0 flex-1">
                <label className="mb-1 block text-sm font-semibold text-navy">Duration (minutes)</label>
                <select value={duration} onChange={e => setDuration(Number(e.target.value))} className="w-full rounded-2xl border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2 lg:max-w-xs">
                  <option value={10}>10 min</option><option value={15}>15 min</option><option value={20}>20 min</option><option value={30}>30 min</option><option value={45}>45 min</option><option value={60}>60 min</option>
                </select>
              </div>
              <Button onClick={generate} disabled={loading} size="lg" className="lg:min-w-48">{loading ? 'Generating...' : 'Generate Session'}</Button>
            </div>
          </Card>
        </MotionItem>

        {loading && <div className="space-y-3">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-2xl" />)}</div>}

        {session && (
          <>
            <MotionItem>
              <Card className="p-5 shadow-sm">
                <div className="flex items-start gap-3">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-amber-50 text-amber-600">
                    <Lightbulb className="h-4 w-4" />
                  </div>
                  <div className="min-w-0">
                    <p className="text-xs font-semibold uppercase tracking-[0.18em] text-muted">Why this mix works</p>
                    <p className="mt-1 text-sm leading-6 text-muted">{session.scienceBasis}</p>
                    <div className="mt-3 flex flex-wrap gap-2 text-sm text-navy">
                      <span className="rounded-full bg-background-light px-3 py-1 font-semibold"><strong>{session.taskCount}</strong> tasks</span>
                      <span className="rounded-full bg-background-light px-3 py-1 font-semibold"><strong>{session.actualDurationMinutes}</strong> min total</span>
                    </div>
                  </div>
                </div>
              </Card>
            </MotionItem>

            <LearnerSurfaceSectionHeader
              eyebrow="Session tasks"
              title="Balanced task order"
              description="The sequence intentionally rotates subtests so the learner does not settle into one mode too long."
            />
            <div className="space-y-3">
              {session.tasks.map(task => {
                const Icon = SUBTEST_ICON[task.subtestCode] ?? BookOpen;
                return (
                  <MotionItem key={task.order}>
                    <Card className="flex items-center gap-4 p-4 shadow-sm">
                      <div className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl border ${SUBTEST_COLOR[task.subtestCode] ?? 'bg-slate-100 text-slate-700'}`}>
                        <Icon className="w-5 h-5" />
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2">
                          <span className="text-xs font-semibold uppercase tracking-[0.18em] text-muted">#{task.order}</span>
                          <h4 className="truncate text-sm font-semibold text-navy">{task.title}</h4>
                          {task.isWeakArea && <Badge variant="danger" className="text-[10px]">Weak area</Badge>}
                        </div>
                        <p className="text-xs capitalize text-muted">{task.subtestCode} • {task.taskType.replace(/-/g, ' ')} • {task.durationMinutes} min</p>
                      </div>
                      <Button variant="outline" size="sm" className="shrink-0">Start</Button>
                    </Card>
                  </MotionItem>
                );
              })}
            </div>

            {session.tips.length > 0 && (
              <Card className="space-y-2 p-5 shadow-sm">
                <h4 className="text-sm font-semibold text-navy">Tips</h4>
                {session.tips.map((tip, i) => <p key={i} className="text-sm leading-6 text-muted">• {tip}</p>)}
              </Card>
            )}
          </>
        )}
      </MotionSection>
      </div>
    </LearnerDashboardShell>
  );
}
