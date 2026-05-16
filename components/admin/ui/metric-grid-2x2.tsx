import { cn } from '@/lib/utils';
import type { MetricTone } from './types';

export function MetricGrid2x2({
  items,
}: { items: { label: string; value: string | number; tone?: MetricTone; sub?: string }[] }) {
  const valColor: Record<MetricTone, string> = {
    default: 'text-admin-text',
    success: 'text-emerald-400',
    warning: 'text-amber-400',
    danger:  'text-rose-400',
    info:    'text-blue-400',
    purple:  'text-violet-400',
  };
  return (
    <div className="grid grid-cols-2">
      {items.map(({ label, value, tone = 'default', sub }, i) => (
        <div
          key={i}
          className={cn(
            'px-4 py-3.5',
            i % 2 === 0 ? 'border-r border-admin-border/60' : '',
            i < 2 ? 'border-b border-admin-border/60' : '',
          )}
        >
          <p className="text-xs font-bold uppercase tracking-[0.18em] text-admin-text-muted leading-none mb-1.5">{label}</p>
          <p className={cn('text-2xl font-bold tabular-nums leading-none', valColor[tone])}>{value}</p>
          {sub && <p className="text-xs text-admin-text-muted mt-1.5 leading-none">{sub}</p>}
        </div>
      ))}
    </div>
  );
}
