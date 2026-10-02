import type { ReactNode } from 'react';
import type { LucideIcon } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { CountUp } from '@/components/ui/count-up';
import { ResultGauge } from './gauge';

export type ScoreStatTone = 'default' | 'success' | 'warning' | 'danger' | 'info';

export interface ScoreStat {
  label: string;
  value: ReactNode;
  tone?: ScoreStatTone;
  icon?: ReactNode;
}

export interface ResultsScorePanelProps {
  eyebrow?: string;
  icon?: LucideIcon;
  title: string;
  subtitle?: string | null;
  /** 0–100 fill of the gauge ring. */
  gaugeValue: number;
  /** Custom centre node (band letter, x/500…). Falls back to the rounded %. */
  gaugeCenter?: ReactNode;
  gaugeLabel?: string;
  gaugeColor?: string;
  grade?: { label: string; tone: 'success' | 'warning' | 'danger' | 'info' | 'muted' } | null;
  stats?: ScoreStat[];
  aside?: ReactNode;
  /** Optional chart / breakdown rendered under the header (radar, bar…). */
  chartSlot?: ReactNode;
  className?: string;
}

const statToneClass: Record<ScoreStatTone, string> = {
  default: 'border-border bg-background-light text-navy dark:text-white',
  success: 'border-success/30 bg-success/10 text-success-strong',
  warning: 'border-warning/30 bg-warning/10 text-warning-strong',
  danger: 'border-danger/30 bg-danger/10 text-danger-strong',
  info: 'border-info/30 bg-info/10 text-info',
};

/**
 * The graphical score header shared by every results surface: a big gauge with
 * a free-form centre, a grade/band badge, a dense stat strip, and optional
 * aside + chart slot.
 *
 * Layout follows the card's own width (container queries), not the viewport:
 * the stat strip sits under the title as a full-width auto-fit grid, so a long
 * title wraps instead of squeezing the tiles, and the aside only takes a right
 * column when the card itself is wide enough.
 */
export function ResultsScorePanel({
  eyebrow,
  icon: Icon,
  title,
  subtitle,
  gaugeValue,
  gaugeCenter,
  gaugeLabel,
  gaugeColor = 'var(--color-primary)',
  grade,
  stats,
  aside,
  chartSlot,
  className,
}: ResultsScorePanelProps) {
  return (
    <Card padding="lg" data-testid="results-score-panel" className={cn('@container overflow-hidden', className)}>
      <div className={cn('grid gap-5', aside && '@3xl:grid-cols-[minmax(0,1fr)_16rem] @3xl:gap-x-8')}>
        <div className="flex min-w-0 items-center gap-4 sm:gap-5">
          <ResultGauge value={gaugeValue} color={gaugeColor}>
            <div data-testid="grade-value" className="flex flex-col items-center">
              {gaugeCenter ?? (
                <span className="text-2xl font-black text-navy dark:text-white">
                  <CountUp value={Math.round(gaugeValue)} />
                  <span className="text-sm">%</span>
                </span>
              )}
            </div>
            {gaugeLabel ? (
              <span className="mt-1 tile-label text-muted">{gaugeLabel}</span>
            ) : null}
          </ResultGauge>
          <div className="min-w-0">
            {eyebrow ? (
              <div className="eyebrow flex items-center gap-1.5 text-muted">
                {Icon ? <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden /> : null}
                {eyebrow}
              </div>
            ) : null}
            <h1 className="mt-1 text-balance text-xl font-black leading-tight text-navy dark:text-white sm:text-2xl">{title}</h1>
            {subtitle ? <p className="mt-1 max-w-prose text-sm text-muted">{subtitle}</p> : null}
            {grade ? (
              <span className="pop-in mt-2 inline-flex">
                <Badge variant={grade.tone}>{grade.label}</Badge>
              </span>
            ) : null}
          </div>
        </div>

        {stats?.length ? (
          <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,8.5rem),1fr))] gap-2">
            {stats.map((stat, index) => (
              <div key={index} data-testid="results-score-stat" className={cn('min-w-0 rounded-xl border p-3', statToneClass[stat.tone ?? 'default'])}>
                <div className="flex items-center gap-1.5">
                  {stat.icon ? <span className="shrink-0 [&>svg]:h-3.5 [&>svg]:w-3.5">{stat.icon}</span> : null}
                  <p className="tile-label min-w-0 opacity-80">{stat.label}</p>
                </div>
                <p className="mt-1 break-words text-lg font-black leading-tight tabular-nums">{stat.value}</p>
              </div>
            ))}
          </div>
        ) : null}

        {aside ? (
          <div className="min-w-0 @3xl:col-start-2 @3xl:row-span-2 @3xl:row-start-1 @3xl:self-center">{aside}</div>
        ) : null}
      </div>

      {chartSlot ? <div className="mt-5 border-t border-border pt-5">{chartSlot}</div> : null}
    </Card>
  );
}
