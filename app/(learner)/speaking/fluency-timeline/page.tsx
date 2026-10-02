'use client';

import { useEffect, useState } from 'react';
import { Mic, AlertTriangle, Gauge, Clock } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { Button } from '@/components/ui/button';
import { CountUp } from '@/components/ui/count-up';
import { ErrorState } from '@/components/ui/empty-error';
import { Input } from '@/components/ui/form-controls';
import { analytics } from '@/lib/analytics';
import { fetchFluencyTimeline } from '@/lib/api';

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

const RATING_BADGE = { good: 'success', fair: 'warning', poor: 'danger' } as const;
const RATING_BAR = { good: 'bg-success', fair: 'bg-warning', poor: 'bg-danger' } as const;

// Tiles size to their container, not the viewport (DESIGN.md §7).
const TILE_GRID = 'grid grid-cols-[repeat(auto-fit,minmax(min(100%,8.5rem),1fr))] gap-4';

export default function FluencyTimelinePage() {
  const [data, setData] = useState<FluencyData | null>(null);
  // Nothing loads until the learner asks for an attempt.
  const [loading, setLoading] = useState(false);
  const [failed, setFailed] = useState(false);
  const [attemptId, setAttemptId] = useState('');

  useEffect(() => { analytics.track('fluency_timeline_viewed'); }, []);

  const load = async (id: string) => {
    if (!id) return;
    setLoading(true);
    setFailed(false);
    try { setData(await fetchFluencyTimeline(id) as FluencyData); }
    catch { setData(null); setFailed(true); }
    finally { setLoading(false); }
  };

  return (
    <>
      <LearnerPageHero
        eyebrow="Speaking"
        icon={Gauge}
        accent="speaking"
        title="Fluency Timeline"
        description="Visualize your speaking pace, pauses, and filler words across the recording."
      />

      <Card padding="md">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <div className="flex-1">
            <Input
              id="fluency-attempt-id"
              label="Speaking Attempt ID"
              placeholder="Enter attempt ID..."
              value={attemptId}
              onChange={(e) => setAttemptId(e.target.value)}
            />
          </div>
          <Button onClick={() => load(attemptId)} loading={loading}>Analyze</Button>
        </div>
      </Card>

      {loading && !data ? (
        <div className={TILE_GRID} aria-hidden="true">
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-28 rounded-2xl" />)}
        </div>
      ) : null}

      {failed ? <ErrorState onRetry={() => load(attemptId)} /> : null}

      {data ? (
        <>
          <MotionSection className="space-y-4">
            <LearnerSurfaceSectionHeader title="Overview Metrics" />
            <div className={TILE_GRID}>
              <MotionItem>
                <Card padding="md" className="h-full text-center">
                  <Gauge className="mx-auto mb-2 h-5 w-5 text-primary" aria-hidden="true" />
                  <p className="text-2xl font-bold tabular-nums text-navy">{data.averageWordsPerMinute}</p>
                  <p className="mt-1 tile-label text-muted">Avg WPM</p>
                  <p className="mt-1 text-xs tabular-nums text-muted">Ideal: {data.benchmarks.idealWordsPerMinute.min}–{data.benchmarks.idealWordsPerMinute.max}</p>
                </Card>
              </MotionItem>
              <MotionItem delayIndex={1}>
                <Card padding="md" className="h-full text-center">
                  <AlertTriangle className="mx-auto mb-2 h-5 w-5 text-warning-strong" aria-hidden="true" />
                  <p className="text-2xl font-bold text-navy"><CountUp value={data.totalFillerWords} /></p>
                  <p className="mt-1 tile-label text-muted">Filler Words</p>
                  <p className="mt-1 text-xs tabular-nums text-muted">{data.fillerRatio}% ratio</p>
                </Card>
              </MotionItem>
              <MotionItem delayIndex={2}>
                <Card padding="md" className="h-full text-center">
                  <Clock className="mx-auto mb-2 h-5 w-5 text-info" aria-hidden="true" />
                  <p className="text-2xl font-bold text-navy"><CountUp value={data.pauseCount} /></p>
                  <p className="mt-1 tile-label text-muted">Long Pauses</p>
                </Card>
              </MotionItem>
              <MotionItem delayIndex={3}>
                <Card padding="md" className="h-full text-center">
                  <Mic className="mx-auto mb-2 h-5 w-5 text-primary" aria-hidden="true" />
                  <p className="text-2xl font-bold text-navy"><CountUp value={data.totalDurationSeconds} suffix="s" /></p>
                  <p className="mt-1 tile-label text-muted">Total Duration</p>
                  <p className="mt-1 text-xs tabular-nums text-muted">{data.totalWords} words</p>
                </Card>
              </MotionItem>
            </div>
          </MotionSection>

          <MotionSection delayIndex={1} className="space-y-4">
            <LearnerSurfaceSectionHeader title="Segment Timeline" />
            <ol className="space-y-2">
              {data.timeline.map((seg, index) => (
                <li key={seg.index}>
                  <MotionItem delayIndex={Math.min(index, 5)}>
                    <Card padding="sm" className="flex items-start gap-3">
                      <div className="w-16 shrink-0 text-center">
                        <p className="text-xs tabular-nums text-muted">{seg.startTime.toFixed(1)}s</p>
                        <Badge variant={RATING_BADGE[seg.fluencyRating]} className="mt-1">{seg.fluencyRating}</Badge>
                      </div>
                      <div className="min-w-0 flex-1">
                        <p className="text-sm text-navy">{seg.text}</p>
                        <div className="mt-1 flex flex-wrap gap-x-3 gap-y-1">
                          <span className="text-xs tabular-nums text-muted">{seg.wordsPerMinute} WPM</span>
                          {seg.fillerCount > 0 && <span className="text-xs tabular-nums text-warning-strong">{seg.fillerCount} filler{seg.fillerCount > 1 ? 's' : ''}</span>}
                          {seg.isPause && <span className="text-xs tabular-nums text-danger-strong">{seg.pauseBefore}s pause</span>}
                        </div>
                      </div>
                      <div className="w-20 shrink-0 pt-1" aria-hidden="true">
                        <div className="h-2 overflow-hidden rounded-full bg-border">
                          <div className={`h-full rounded-full ${RATING_BAR[seg.fluencyRating]}`} style={{ width: `${Math.min(100, seg.wordsPerMinute / 2)}%` }} />
                        </div>
                      </div>
                    </Card>
                  </MotionItem>
                </li>
              ))}
            </ol>
          </MotionSection>
        </>
      ) : null}
    </>
  );
}
