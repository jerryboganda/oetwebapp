import { cn } from '@/lib/utils';
import type { MetricTone } from './types';

export function StatusBadge({ tone, label }: { tone: MetricTone; label: string }) {
  const map: Record<MetricTone, string> = {
    default:  'bg-admin-surface-raised text-admin-text-muted border border-admin-border',
    success:  'bg-emerald-500 text-white',
    warning:  'bg-amber-500  text-white',
    danger:   'bg-rose-500   text-white',
    info:     'bg-blue-500   text-white',
    purple:   'bg-violet-500 text-white',
  };
  return (
    <span className={cn('inline-flex items-center px-2 py-0.5 rounded-full text-xs uppercase font-bold tracking-widest leading-none whitespace-nowrap', map[tone])}>
      {label}
    </span>
  );
}
