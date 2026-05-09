import { ReactNode } from 'react';
import { cn } from '@/lib/utils';
import { TrendingUp, TrendingDown, Minus } from 'lucide-react';
import { Card } from './card';

export interface StatTrend {
  value: string | number;
  label?: string;
  direction?: 'up' | 'down' | 'neutral';
}

export interface StatCardProps {
  label: string;
  value: ReactNode;
  icon?: ReactNode;
  trend?: StatTrend;
  hint?: string;
  sparklineData?: number[];
  tone?: 'default' | 'success' | 'warning' | 'danger' | 'info';
  className?: string;
}

function StatSparkline({ data, colorClass = 'text-primary' }: { data: number[]; colorClass?: string }) {
  if (!data || data.length < 2) return null;
  const min = Math.min(...data);
  const max = Math.max(...data);
  const range = max - min || 1;
  const width = 100;
  const height = 32;

  const points = data
    .map((val, i) => {
      const x = (i / (data.length - 1)) * width;
      const y = height - ((val - min) / range) * height;
      return `${x},${y}`;
    })
    .join(' ');

  return (
    <svg 
      viewBox={`-2 -2 ${width + 4} ${height + 4}`} 
      preserveAspectRatio="none" 
      className={cn('overflow-visible h-8 w-20', colorClass)}
    >
      <polyline
        fill="none"
        stroke="currentColor"
        strokeWidth="2.5"
        strokeLinecap="round"
        strokeLinejoin="round"
        points={points}
      />
    </svg>
  );
}

export function StatCard({
  label,
  value,
  icon,
  trend,
  sparklineData,
  tone = 'default',
  hint,
  className,
}: StatCardProps) {
  // Define tone colors aligned to the modern high-contrast design system
  const toneMap = {
    default: {
      border: 'border-l-4 border-l-primary border-y border-r border-border/80 dark:border-border/60',
      bg: 'bg-surface',
      text: 'text-navy dark:text-zinc-50',
      label: 'text-muted dark:text-zinc-400',
      iconFill: 'bg-primary/10 dark:bg-primary/20',
      iconText: 'text-primary dark:text-primary-light',
      trendUp: 'text-success dark:text-success',
      trendDown: 'text-danger dark:text-danger',
      trendNeutral: 'text-muted dark:text-zinc-500',
      sparkline: 'text-primary/40 dark:text-primary/60',
    },
    success: {
      border: 'border-l-4 border-l-success border-y border-r border-success/30 dark:border-success/30',
      bg: 'bg-success/5 dark:bg-success/10',
      text: 'text-success-dark dark:text-success',
      label: 'text-success-dark/70 dark:text-success/70',
      iconFill: 'bg-success/20 dark:bg-success/20',
      iconText: 'text-success-dark dark:text-success',
      trendUp: 'text-success-dark dark:text-success',
      trendDown: 'text-danger dark:text-danger',
      trendNeutral: 'text-success-dark/60 dark:text-success/60',
      sparkline: 'text-success/40 dark:text-success/50',
    },
    warning: {
      border: 'border-l-4 border-l-warning border-y border-r border-warning/30 dark:border-warning/30',
      bg: 'bg-warning/5 dark:bg-warning/10',
      text: 'text-amber-950 dark:text-amber-400',
      label: 'text-amber-900/70 dark:text-amber-400/70',
      iconFill: 'bg-warning/20 dark:bg-warning/20',
      iconText: 'text-amber-900 dark:text-amber-400',
      trendUp: 'text-danger dark:text-danger',
      trendDown: 'text-success dark:text-success',
      trendNeutral: 'text-amber-900/60 dark:text-amber-400/60',
      sparkline: 'text-warning/50 dark:text-warning/50',
    },
    danger: {
      border: 'border-l-4 border-l-danger border-y border-r border-danger/30 dark:border-danger/30',
      bg: 'bg-danger/5 dark:bg-danger/10',
      text: 'text-danger-dark dark:text-danger',
      label: 'text-danger-dark/70 dark:text-danger/70',
      iconFill: 'bg-danger/20 dark:bg-danger/20',
      iconText: 'text-danger-dark dark:text-danger',
      trendUp: 'text-danger-dark dark:text-danger',
      trendDown: 'text-success dark:text-success',
      trendNeutral: 'text-danger-dark/60 dark:text-danger/60',
      sparkline: 'text-danger/40 dark:text-danger/50',
    },
    info: {
      border: 'border-l-4 border-l-info border-y border-r border-info/30 dark:border-info/30',
      bg: 'bg-info/5 dark:bg-info/10',
      text: 'text-info-dark dark:text-info',
      label: 'text-info-dark/70 dark:text-info/70',
      iconFill: 'bg-info/20 dark:bg-info/20',
      iconText: 'text-info-dark dark:text-info',
      trendUp: 'text-success dark:text-success',
      trendDown: 'text-danger dark:text-danger',
      trendNeutral: 'text-info-dark/60 dark:text-info/60',
      sparkline: 'text-info/40 dark:text-info/50',
    },
  };

  const activeTone = toneMap[tone];

  return (
    <Card 
      className={cn(
        'relative overflow-hidden flex flex-col p-3 shadow-sm transition-shadow hover:shadow-md rounded-xl',
        activeTone.border,
        activeTone.bg,
        className
      )}
    >
      {/* Top Header: Label & Icon */}
      <div className="flex items-start justify-between gap-2">
        <h3 className={cn('text-[11px] font-bold uppercase tracking-[0.10em] line-clamp-1', activeTone.label)}>
          {label}
        </h3>
        {icon && (
          <div className={cn('flex h-5 w-5 shrink-0 items-center justify-center rounded-md', activeTone.iconFill, activeTone.iconText)}>
            {/* Clone icon to enforce smaller size */}
            <div className="[&>svg]:h-3.5 [&>svg]:w-3.5">{icon}</div>
          </div>
        )}
      </div>

      {/* Main Content: Value & Sparkline */}
      <div className="mt-2 flex items-end justify-between flex-1 gap-2">
        <div className="min-w-0">
          <div className={cn('text-xl font-extrabold tracking-tight truncate', activeTone.text)}>
            {value}
          </div>
          
          {/* Trend Data */}
          {trend && (
            <div className="mt-0.5 flex items-center gap-1 text-xs font-medium">
              {trend.direction === 'up' ? (
                <TrendingUp className={cn('h-3 w-3 shrink-0', activeTone.trendUp)} />
              ) : trend.direction === 'down' ? (
                <TrendingDown className={cn('h-3 w-3 shrink-0', activeTone.trendDown)} />
              ) : (
                <Minus className={cn('h-3 w-3 shrink-0', activeTone.trendNeutral)} />
              )}
              <span className={cn('truncate',
                trend.direction === 'up' ? activeTone.trendUp : 
                trend.direction === 'down' ? activeTone.trendDown : 
                activeTone.trendNeutral
              )}>
                {trend.value}
              </span>
              {trend.label && (
                <span className={cn('ml-0.5 text-[10px] uppercase truncate', activeTone.label)}>
                  {trend.label}
                </span>
              )}
            </div>
          )}
        </div>

        {/* Sparkline (Right-aligned) */}
        {sparklineData && sparklineData.length > 1 && (
          <div className="shrink-0 -mb-1 pb-1 flex flex-col justify-end w-12">
            <StatSparkline data={sparklineData} colorClass={activeTone.sparkline} />
          </div>
        )}
      </div>

      {/* Hint Text */}
      {hint && (
        <div className={cn('mt-1.5 text-[10px] font-medium leading-tight line-clamp-2', activeTone.label)}>
          {hint}
        </div>
      )}
    </Card>
  );
}
