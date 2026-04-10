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

  return (
    <LearnerDashboardShell>
      <LearnerPageHero title="Interleaved Practice" description="Mix different skill types in one session for better long-term retention." />

      <MotionSection className="px-4 py-6 space-y-6 max-w-4xl mx-auto">
        <MotionItem>
          <Card className="p-5">
            <div className="flex items-center gap-3 mb-3"><Shuffle className="w-5 h-5 text-primary" /><h3 className="font-semibold">Generate a Mixed Practice Session</h3></div>
            <div className="flex gap-3 items-end">
              <div>
                <label className="text-sm text-muted-foreground block mb-1">Duration (minutes)</label>
                <select value={duration} onChange={e => setDuration(Number(e.target.value))} className="border rounded-lg px-3 py-2 text-sm">
                  <option value={10}>10 min</option><option value={15}>15 min</option><option value={20}>20 min</option><option value={30}>30 min</option><option value={45}>45 min</option><option value={60}>60 min</option>
                </select>
              </div>
              <Button onClick={generate} disabled={loading}>{loading ? 'Generating...' : 'Generate Session'}</Button>
            </div>
          </Card>
        </MotionItem>

        {loading && <div className="space-y-3">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-xl" />)}</div>}

        {session && (
          <>
            <MotionItem>
              <Card className="p-4 bg-muted/50">
                <div className="flex items-start gap-2"><Lightbulb className="w-4 h-4 text-amber-500 mt-0.5 flex-shrink-0" /><p className="text-sm text-muted-foreground">{session.scienceBasis}</p></div>
                <div className="flex gap-4 mt-2 text-sm"><span><strong>{session.taskCount}</strong> tasks</span><span><strong>{session.actualDurationMinutes}</strong> min total</span></div>
              </Card>
            </MotionItem>

            <LearnerSurfaceSectionHeader title="Session Tasks" />
            <div className="space-y-3">
              {session.tasks.map(task => {
                const Icon = SUBTEST_ICON[task.subtestCode] ?? BookOpen;
                return (
                  <MotionItem key={task.order}>
                    <Card className="p-4 flex items-center gap-4">
                      <div className={`w-10 h-10 rounded-full flex items-center justify-center flex-shrink-0 ${SUBTEST_COLOR[task.subtestCode] ?? ''}`}>
                        <Icon className="w-5 h-5" />
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2">
                          <span className="text-sm font-medium text-muted-foreground">#{task.order}</span>
                          <h4 className="text-sm font-semibold truncate">{task.title}</h4>
                          {task.isWeakArea && <Badge variant="destructive" className="text-[10px]">Weak Area</Badge>}
                        </div>
                        <p className="text-xs text-muted-foreground capitalize">{task.subtestCode} • {task.taskType.replace(/-/g, ' ')} • {task.durationMinutes} min</p>
                      </div>
                      <Button variant="outline" size="sm">Start</Button>
                    </Card>
                  </MotionItem>
                );
              })}
            </div>

            {session.tips.length > 0 && (
              <Card className="p-4 space-y-1">
                <h4 className="text-sm font-semibold mb-2">Tips</h4>
                {session.tips.map((tip, i) => <p key={i} className="text-sm text-muted-foreground">• {tip}</p>)}
              </Card>
            )}
          </>
        )}
      </MotionSection>
    </LearnerDashboardShell>
  );
}
