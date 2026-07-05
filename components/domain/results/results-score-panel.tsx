import type { ReactNode } from 'react';
import type { LucideIcon } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
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
  success: 'border-success/30 bg-success/10 text-success',
  warning: 'border-warning/30 bg-warning/10 text-warning',
  danger: 'border-danger/30 bg-danger/10 text-danger',
  info: 'border-info/30 bg-info/10 text-info',
};

/**
 * The graphical score header shared by every results surface: a big gauge with
 * a free-form centre, a grade/band badge, a dense stat strip, and optional
 * aside + chart slot.
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
    <Card padding="lg" className={cn('overflow-hidden', className)}>
      <div className="flex flex-col gap-5 lg:flex-row lg:items-center lg:gap-8">
        <div className="flex items-center gap-4 sm:gap-5">
          <ResultGauge value={gaugeValue} color={gaugeColor}>
            {gaugeCenter ?? (
              <span className="text-2xl font-black text-navy dark:text-white">
                {Math.round(gaugeValue)}
                <span className="text-sm">%</span>
              </span>
            )}
            {gaugeLabel ? (
              <span className="mt-1 text-[10px] font-bold uppercase tracking-widest text-muted">{gaugeLabel}</span>
            ) : null}
          </ResultGauge>
          <div className="min-w-0">
            {eyebrow ? (
              <div className="flex items-center gap-1.5 text-[11px] font-black uppercase tracking-[0.16em] text-muted">
                {Icon ? <Icon className="h-3.5 w-3.5" aria-hidden /> : null}
                {eyebrow}
              </div>
            ) : null}
            <h1 className="mt-1 text-xl font-black leading-tight text-navy dark:text-white sm:text-2xl">{title}</h1>
            {subtitle ? <p className="mt-1 text-sm text-muted">{subtitle}</p> : null}
            {grade ? (
              <Badge variant={grade.tone} className="mt-2">{grade.label}</Badge>
            ) : null}
          </div>
        </div>

        {stats?.length ? (
          <div className="grid flex-1 grid-cols-2 gap-2 sm:grid-cols-4 lg:max-w-2xl">
            {stats.map((stat, index) => (
              <div key={index} className={cn('rounded-xl border p-3', statToneClass[stat.tone ?? 'default'])}>
                <div className="flex items-center gap-1.5">
                  {stat.icon ? <span className="[&>svg]:h-3.5 [&>svg]:w-3.5">{stat.icon}</span> : null}
                  <p className="text-[10px] font-black uppercase tracking-widest opacity-80">{stat.label}</p>
                </div>
                <p className="mt-1 text-lg font-black leading-tight">{stat.value}</p>
              </div>
            ))}
          </div>
        ) : null}

        {aside ? <div className="lg:w-64 lg:shrink-0">{aside}</div> : null}
      </div>

      {chartSlot ? <div className="mt-5 border-t border-border pt-5">{chartSlot}</div> : null}
    </Card>
  );
}
