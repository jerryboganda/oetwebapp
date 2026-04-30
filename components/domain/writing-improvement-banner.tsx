'use client';

/**
 * ============================================================================
 * Writing Improvement Banner
 * ============================================================================
 *
 * Surfaces the deterministic improvement_score on the learner Revision page.
 * No AI calls — purely a presentational wrapper around `computeImprovementScore`.
 * ============================================================================
 */

import { TrendingUp, AlertTriangle, ArrowDownRight } from 'lucide-react';
import { Card } from '@/components/ui/card';
import type { ImprovementScore } from '@/lib/writing-revision/improvement-score';

interface WritingImprovementBannerProps {
  result: ImprovementScore;
  className?: string;
}

const BAND_STYLE: Record<
  ImprovementScore['band'],
  { ring: string; text: string; bar: string; bg: string; icon: typeof TrendingUp }
> = {
  major: {
    ring: 'border-success/40',
    text: 'text-success',
    bar: 'bg-success',
    bg: 'bg-success/10',
    icon: TrendingUp,
  },
  moderate: {
    ring: 'border-success/30',
    text: 'text-success',
    bar: 'bg-success/80',
    bg: 'bg-success/5',
    icon: TrendingUp,
  },
  minor: {
    ring: 'border-warning/30',
    text: 'text-warning',
    bar: 'bg-warning',
    bg: 'bg-warning/10',
    icon: TrendingUp,
  },
  minimal: {
    ring: 'border-border',
    text: 'text-muted',
    bar: 'bg-muted/50',
    bg: 'bg-background-light',
    icon: AlertTriangle,
  },
  regressed: {
    ring: 'border-error/40',
    text: 'text-error',
    bar: 'bg-error',
    bg: 'bg-error/10',
    icon: ArrowDownRight,
  },
};

export function WritingImprovementBanner({ result, className }: WritingImprovementBannerProps) {
  const style = BAND_STYLE[result.band];
  const Icon = style.icon;
  const widthPct = Math.max(0, Math.min(100, result.score));

  return (
    <Card
      className={`flex flex-col gap-4 border-2 p-6 sm:flex-row sm:items-center sm:justify-between ${style.ring} ${style.bg} ${className ?? ''}`}
      role="status"
      aria-label={`Improvement score ${result.score} out of 100 — ${result.headline}`}
    >
      <div className="flex items-center gap-4">
        <div className={`flex h-14 w-14 shrink-0 items-center justify-center rounded-2xl bg-surface ${style.text}`}>
          <Icon className="h-7 w-7" aria-hidden />
        </div>
        <div>
          <div className="flex items-baseline gap-2">
            <span className={`text-3xl font-bold tabular-nums ${style.text}`}>{result.score}</span>
            <span className="text-sm font-medium text-muted">/ 100</span>
          </div>
          <p className={`text-sm font-semibold ${style.text}`}>{result.headline}</p>
          <p className="mt-1 max-w-md text-sm text-muted">{result.summary}</p>
        </div>
      </div>
      <div className="w-full sm:max-w-xs">
        <div className="mb-1 flex items-center justify-between text-xs font-medium text-muted">
          <span>Improvement</span>
          <span>
            {result.criteriaImproved} improved
            {result.criteriaRegressed > 0 ? `, ${result.criteriaRegressed} regressed` : ''}
          </span>
        </div>
        <div className="h-2 overflow-hidden rounded-full bg-surface" aria-hidden>
          <div className={`h-full ${style.bar} transition-[width]`} style={{ width: `${widthPct}%` }} />
        </div>
      </div>
    </Card>
  );
}
