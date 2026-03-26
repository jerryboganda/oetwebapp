'use client';

import { cn } from '@/lib/utils';
import { CircularProgress } from '@/components/ui/progress';
import { TrendingUp, TrendingDown, Minus } from 'lucide-react';

interface ReadinessMeterProps {
  value: number; // 0-100
  label?: string;
  change?: number; // positive = improvement
  sublabel?: string;
  size?: number;
  className?: string;
}

export function ReadinessMeter({ value, label = 'Test Readiness', change, sublabel, size = 120, className }: ReadinessMeterProps) {
  const color = value >= 70 ? 'var(--color-success)' : value >= 50 ? 'var(--color-primary)' : 'var(--color-danger)';

  return (
    <div className={cn('flex flex-col items-center text-center', className)}>
      <CircularProgress value={value} size={size} color={color} />
      <p className="text-sm font-bold text-navy mt-3">{label}</p>
      {change !== undefined && change !== 0 && (
        <p className={cn('text-xs font-semibold flex items-center gap-1 mt-1', change > 0 ? 'text-emerald-600' : 'text-red-600')}>
          {change > 0 ? <TrendingUp className="w-3.5 h-3.5" /> : <TrendingDown className="w-3.5 h-3.5" />}
          {change > 0 ? '+' : ''}{change}% since last week
        </p>
      )}
      {change === 0 && (
        <p className="text-xs font-semibold text-muted flex items-center gap-1 mt-1">
          <Minus className="w-3.5 h-3.5" /> No change
        </p>
      )}
      {sublabel && <p className="text-xs text-muted mt-1">{sublabel}</p>}
    </div>
  );
}
