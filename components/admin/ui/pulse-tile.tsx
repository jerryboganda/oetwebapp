import { cn } from '@/lib/utils';
import type { MetricTone } from './types';

export function PulseTile({
  label, value, tone, icon: Icon,
}: { label: string; value: string | number; tone: MetricTone; icon: React.ElementType }) {
  const bg: Record<MetricTone, string> = {
    default: 'bg-admin-surface border-admin-border',
    success: 'bg-emerald-500/10 border-emerald-500/20',
    warning: 'bg-amber-500/10 border-amber-500/20',
    danger:  'bg-rose-500/10 border-rose-500/20',
    info:    'bg-blue-500/10 border-blue-500/20',
    purple:  'bg-violet-500/10 border-violet-500/20',
  };
  const val: Record<MetricTone, string> = {
    default: 'text-admin-text',
    success: 'text-emerald-400',
    warning: 'text-amber-400',
    danger:  'text-rose-400',
    info:    'text-blue-400',
    purple:  'text-violet-400',
  };
  const ico: Record<MetricTone, string> = {
    default: 'text-admin-text-muted',
    success: 'text-emerald-500',
    warning: 'text-amber-500',
    danger:  'text-rose-500',
    info:    'text-blue-500',
    purple:  'text-violet-500',
  };
  return (
    <div className={cn('flex items-center gap-3 rounded-2xl border px-4 py-3 flex-1 min-w-0 shadow-sm', bg[tone])}>
      <Icon className={cn('h-5 w-5 shrink-0', ico[tone])} />
      <div className="min-w-0 flex-1">
        <p className="text-xs font-bold uppercase tracking-[0.18em] text-admin-text-muted truncate leading-none mb-1">{label}</p>
        <p className={cn('text-xl font-bold leading-none tabular-nums tracking-tight', val[tone])}>{value}</p>
      </div>
    </div>
  );
}
