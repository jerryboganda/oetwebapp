'use client';

import { ArrowDown, ArrowUp, Minus, Gauge } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card, CardContent } from '@/components/ui/card';
import type { WritingReadinessSubScoreDto } from '@/lib/writing/types';

export interface ReadinessWidgetProps {
  score: number;
  subScores: WritingReadinessSubScoreDto;
  deltaVsLastWeek?: number | null;
  predictedBand?: string | null;
  className?: string;
}

interface SubBarSpec {
  key: keyof WritingReadinessSubScoreDto;
  label: string;
  weight: number;
}

const SUB_BARS: SubBarSpec[] = [
  { key: 'mockAverage', label: 'Mock average', weight: 50 },
  { key: 'trajectory', label: 'Trajectory', weight: 20 },
  { key: 'canonCleanRate', label: 'Canon clean rate', weight: 15 },
  { key: 'timeMgmt', label: 'Time management', weight: 10 },
  { key: 'typeConsistency', label: 'Type consistency', weight: 5 },
];

function scoreTone(score: number): { text: string; bg: string; ring: string; label: string } {
  if (score >= 85) {
    return {
      text: 'text-emerald-700 dark:text-emerald-300',
      bg: 'bg-emerald-50 dark:bg-emerald-950/40',
      ring: 'border-emerald-300/70 dark:border-emerald-800/60',
      label: 'Exam-ready',
    };
  }
  if (score >= 65) {
    return {
      text: 'text-amber-700 dark:text-amber-300',
      bg: 'bg-amber-50 dark:bg-amber-950/40',
      ring: 'border-amber-300/70 dark:border-amber-800/60',
      label: 'Building',
    };
  }
  return {
    text: 'text-red-700 dark:text-red-300',
    bg: 'bg-red-50 dark:bg-red-950/40',
    ring: 'border-red-300/70 dark:border-red-800/60',
    label: 'Foundation',
  };
}

/**
 * Headline readiness widget. Shows the 0-100 readiness score, a
 * delta-vs-last-week arrow, the predicted exam-day band, and a
 * breakdown of the five sub-scores per the formula in spec §9.4 /
 * §21.2.8.
 */
export function ReadinessWidget({ score, subScores, deltaVsLastWeek, predictedBand, className }: ReadinessWidgetProps) {
  const safeScore = Math.max(0, Math.min(100, Math.round(score)));
  const tone = scoreTone(safeScore);
  const delta = typeof deltaVsLastWeek === 'number' ? Math.round(deltaVsLastWeek) : null;
  const DeltaIcon = delta === null ? Minus : delta > 0 ? ArrowUp : delta < 0 ? ArrowDown : Minus;
  const deltaTone = delta === null
    ? 'text-muted'
    : delta > 0
      ? 'text-emerald-600 dark:text-emerald-300'
      : delta < 0
        ? 'text-red-600 dark:text-red-300'
        : 'text-muted';

  return (
    <Card padding="lg" className={cn('border', tone.ring, tone.bg, className)} aria-label="Readiness score widget">
      <CardContent>
        <div className="flex items-start justify-between gap-3 flex-wrap">
          <div className="flex items-center gap-3 min-w-0">
            <Gauge className={cn('w-7 h-7 shrink-0', tone.text)} aria-hidden="true" />
            <div className="min-w-0">
              <p className="text-xs uppercase tracking-wider font-bold text-muted">Readiness</p>
              <div className="flex items-baseline gap-2">
                <span className={cn('text-4xl font-extrabold tabular-nums', tone.text)}>{safeScore}</span>
                <span className="text-xs font-bold text-muted">/ 100</span>
              </div>
              <p className={cn('text-xs font-bold mt-0.5', tone.text)}>{tone.label}</p>
            </div>
          </div>
          <div className="text-right">
            {delta !== null ? (
              <div className={cn('inline-flex items-center gap-1 text-xs font-bold', deltaTone)}>
                <DeltaIcon className="w-3 h-3" aria-hidden="true" />
                <span>
                  {delta > 0 ? '+' : ''}
                  {delta} vs last week
                </span>
              </div>
            ) : null}
            {predictedBand ? (
              <p className="text-xs text-muted mt-1">
                Likely band on exam day:{' '}
                <span className="font-bold text-navy dark:text-white">{predictedBand}</span>
              </p>
            ) : null}
          </div>
        </div>

        <ul className="mt-4 space-y-1.5" aria-label="Readiness sub-score breakdown">
          {SUB_BARS.map(({ key, label, weight }) => {
            const value = Math.max(0, Math.min(100, Math.round(subScores[key])));
            return (
              <li key={key} className="flex items-center gap-2 text-xs">
                <span className="w-32 shrink-0 font-bold text-muted">{label}</span>
                <div
                  className="flex-1 h-1.5 rounded-full bg-slate-200/60 dark:bg-slate-800/60 overflow-hidden"
                  role="progressbar"
                  aria-valuemin={0}
                  aria-valuemax={100}
                  aria-valuenow={value}
                  aria-label={`${label}: ${value} percent`}
                >
                  <div
                    className={cn('h-full', tone.text.replace('text-', 'bg-'))}
                    style={{ width: `${value}%` }}
                  />
                </div>
                <span className="w-10 text-right tabular-nums font-bold">{value}</span>
                <span className="w-8 text-right text-[10px] text-muted">{weight}%</span>
              </li>
            );
          })}
        </ul>
      </CardContent>
    </Card>
  );
}
