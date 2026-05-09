import { cn } from '@/lib/utils';
import type { MetricTone } from './types';

export function StatusBadge({ tone, label }: { tone: MetricTone; label: string }) {
  const map: Record<MetricTone, string> = {
    default:  'bg-zinc-100 text-zinc-600 border border-zinc-200 dark:bg-zinc-800 dark:text-zinc-300 dark:border-zinc-700',
    success:  'bg-emerald-500 text-white',
    warning:  'bg-amber-500  text-white',
    danger:   'bg-rose-500   text-white',
    info:     'bg-blue-500   text-white',
    purple:   'bg-violet-500 text-white',
  };
  return (
    <span className={cn('inline-flex items-center px-2 py-0.5 rounded text-[10px] uppercase font-black tracking-widest leading-none whitespace-nowrap', map[tone])}>
      {label}
    </span>
  );
}
