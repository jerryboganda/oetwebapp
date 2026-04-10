'use client';

import { useEffect, useState } from 'react';
import { Mic, Play, AlertTriangle, Gauge, Clock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

interface Segment {
  index: number;
  startTime: number;
  endTime: number;
  text: string;
  wordCount: number;
  wordsPerMinute: number;
  pauseBefore: number;
  isPause: boolean;
  fillerCount: number;
  fluencyRating: 'good' | 'fair' | 'poor';
}

interface FluencyData {
  attemptId: string;
  totalDurationSeconds: number;
  totalWords: number;
  totalFillerWords: number;
  fillerRatio: number;
  averageWordsPerMinute: number;
  pauseCount: number;
  timeline: Segment[];
  benchmarks: { idealWordsPerMinute: { min: number; max: number }; maxAcceptableFillerRatio: number; maxAcceptablePauseSeconds: number };
}

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

const RATING_COLORS: Record<string, string> = { good: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950 dark:text-emerald-300', fair: 'bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-300', poor: 'bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-300' };

export default function FluencyTimelinePage() {
  const [data, setData] = useState<FluencyData | null>(null);
  const [loading, setLoading] = useState(true);
  const [attemptId, setAttemptId] = useState('');

  useEffect(() => { analytics.track('fluency_timeline_viewed'); }, []);

  const load = async (id: string) => {
    if (!id) return;
    setLoading(true);
    try { setData(await apiRequest<FluencyData>(`/v1/learner/speaking/${id}/fluency-timeline`)); }
    catch { setData(null); }
    finally { setLoading(false); }
  };

  return (
    <LearnerDashboardShell>
      <LearnerPageHero title="Fluency Timeline" description="Visualize your speaking pace, pauses, and filler words across the recording." />

      <MotionSection className="px-4 py-6 space-y-6 max-w-5xl mx-auto">
        <div className="flex gap-3 items-end">
          <div className="flex-1">
            <label className="text-sm font-medium text-muted-foreground mb-1 block">Speaking Attempt ID</label>
            <input className="w-full border rounded-lg px-3 py-2 text-sm" placeholder="Enter attempt ID..." value={attemptId} onChange={e => setAttemptId(e.target.value)} />
          </div>
          <button onClick={() => load(attemptId)} className="px-4 py-2 bg-primary text-primary-foreground rounded-lg text-sm font-medium hover:bg-primary/90">Analyze</button>
        </div>

        {loading && !data && <div className="grid grid-cols-4 gap-4">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>}

        {data && (
          <>
            <LearnerSurfaceSectionHeader title="Overview Metrics" />
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <MotionItem><Card className="p-4 text-center"><Gauge className="w-5 h-5 mx-auto mb-2 text-primary" /><p className="text-2xl font-bold">{data.averageWordsPerMinute}</p><p className="text-xs text-muted-foreground">Avg WPM</p><p className="text-xs text-muted-foreground/70">Ideal: {data.benchmarks.idealWordsPerMinute.min}–{data.benchmarks.idealWordsPerMinute.max}</p></Card></MotionItem>
              <MotionItem><Card className="p-4 text-center"><AlertTriangle className="w-5 h-5 mx-auto mb-2 text-amber-500" /><p className="text-2xl font-bold">{data.totalFillerWords}</p><p className="text-xs text-muted-foreground">Filler Words</p><p className="text-xs text-muted-foreground/70">{data.fillerRatio}% ratio</p></Card></MotionItem>
              <MotionItem><Card className="p-4 text-center"><Clock className="w-5 h-5 mx-auto mb-2 text-blue-500" /><p className="text-2xl font-bold">{data.pauseCount}</p><p className="text-xs text-muted-foreground">Long Pauses</p></Card></MotionItem>
              <MotionItem><Card className="p-4 text-center"><Mic className="w-5 h-5 mx-auto mb-2 text-purple-500" /><p className="text-2xl font-bold">{Math.round(data.totalDurationSeconds)}s</p><p className="text-xs text-muted-foreground">Total Duration</p><p className="text-xs text-muted-foreground/70">{data.totalWords} words</p></Card></MotionItem>
            </div>

            <LearnerSurfaceSectionHeader title="Segment Timeline" />
            <div className="space-y-2">
              {data.timeline.map(seg => (
                <MotionItem key={seg.index}>
                  <Card className="p-3 flex items-start gap-3">
                    <div className="flex-shrink-0 w-16 text-center">
                      <p className="text-xs font-mono text-muted-foreground">{seg.startTime.toFixed(1)}s</p>
                      <Badge className={`mt-1 text-[10px] ${RATING_COLORS[seg.fluencyRating]}`}>{seg.fluencyRating}</Badge>
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="text-sm">{seg.text}</p>
                      <div className="flex gap-3 mt-1">
                        <span className="text-xs text-muted-foreground">{seg.wordsPerMinute} WPM</span>
                        {seg.fillerCount > 0 && <span className="text-xs text-amber-600">{seg.fillerCount} filler{seg.fillerCount > 1 ? 's' : ''}</span>}
                        {seg.isPause && <span className="text-xs text-red-600">{seg.pauseBefore}s pause</span>}
                      </div>
                    </div>
                    <div className="flex-shrink-0 w-20">
                      <div className="h-2 rounded-full bg-muted overflow-hidden">
                        <div className={`h-full rounded-full ${seg.fluencyRating === 'good' ? 'bg-emerald-500' : seg.fluencyRating === 'fair' ? 'bg-amber-500' : 'bg-red-500'}`} style={{ width: `${Math.min(100, seg.wordsPerMinute / 2)}%` }} />
                      </div>
                    </div>
                  </Card>
                </MotionItem>
              ))}
            </div>
          </>
        )}
      </MotionSection>
    </LearnerDashboardShell>
  );
}
