import { cn } from '@/lib/utils';
import type { MetricTone } from './types';

export function PulseTile({
  label, value, tone, icon: Icon,
}: { label: string; value: string | number; tone: MetricTone; icon: React.ElementType }) {
  const bg: Record<MetricTone, string> = {
    default: 'bg-white border-zinc-200 dark:bg-zinc-950 dark:border-zinc-800',
    success: 'bg-emerald-50 border-emerald-200/60 dark:bg-emerald-950/40 dark:border-emerald-800/40',
    warning: 'bg-amber-50  border-amber-200/60  dark:bg-amber-950/40  dark:border-amber-800/40',
    danger:  'bg-rose-50   border-rose-200/60   dark:bg-rose-950/40   dark:border-rose-800/40',
    info:    'bg-blue-50   border-blue-200/60   dark:bg-blue-950/40   dark:border-blue-800/40',
    purple:  'bg-violet-50 border-violet-200/60 dark:bg-violet-950/40 dark:border-violet-800/40',
  };
  const val: Record<MetricTone, string> = {
    default: 'text-zinc-900   dark:text-zinc-100',
    success: 'text-emerald-700 dark:text-emerald-400',
    warning: 'text-amber-700  dark:text-amber-400',
    danger:  'text-rose-700   dark:text-rose-400',
    info:    'text-blue-700   dark:text-blue-400',
    purple:  'text-violet-700 dark:text-violet-400',
  };
  const ico: Record<MetricTone, string> = {
    default: 'text-zinc-400',
    success: 'text-emerald-500',
    warning: 'text-amber-500',
    danger:  'text-rose-500',
    info:    'text-blue-500',
    purple:  'text-violet-500',
  };
  return (
    <div className={cn('flex items-center gap-3 rounded-xl border px-4 py-3 flex-1 min-w-0 shadow-sm', bg[tone])}>
      <Icon className={cn('h-5 w-5 shrink-0', ico[tone])} />
      <div className="min-w-0 flex-1">
        <p className="text-[10px] font-extrabold uppercase tracking-[0.18em] text-zinc-500 dark:text-zinc-400 truncate leading-none mb-1">{label}</p>
        <p className={cn('text-xl font-black leading-none tabular-nums tracking-tight', val[tone])}>{value}</p>
      </div>
    </div>
  );
}
