import { cn } from '@/lib/utils';
import type { MetricTone } from './types';

export function MetricGrid2x2({
  items,
}: { items: { label: string; value: string | number; tone?: MetricTone; sub?: string }[] }) {
  const valColor: Record<MetricTone, string> = {
    default: 'text-zinc-900 dark:text-zinc-100',
    success: 'text-emerald-600 dark:text-emerald-400',
    warning: 'text-amber-600  dark:text-amber-400',
    danger:  'text-rose-600   dark:text-rose-400',
    info:    'text-blue-600   dark:text-blue-400',
    purple:  'text-violet-600 dark:text-violet-400',
  };
  return (
    <div className="grid grid-cols-2">
      {items.map(({ label, value, tone = 'default', sub }, i) => (
        <div
          key={i}
          className={cn(
            'px-4 py-3.5',
            i % 2 === 0 ? 'border-r border-zinc-100 dark:border-zinc-800/50' : '',
            i < 2 ? 'border-b border-zinc-100 dark:border-zinc-800/50' : '',
          )}
        >
          <p className="text-[10px] font-extrabold uppercase tracking-[0.18em] text-zinc-500 dark:text-zinc-400 leading-none mb-1.5">{label}</p>
          <p className={cn('text-2xl font-black tabular-nums leading-none', valColor[tone])}>{value}</p>
          {sub && <p className="text-[10px] text-zinc-400 dark:text-zinc-500 mt-1.5 leading-none">{sub}</p>}
        </div>
      ))}
    </div>
  );
}
