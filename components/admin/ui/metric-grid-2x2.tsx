// Re-skinned 2026-05-24 for admin redesign — uses --admin-* token system
import { cn } from '@/lib/utils';
import type { MetricTone } from './types';

/**
 * MetricGrid2x2 — four metric pairs in a 2×2 grid.
 *
 * Public API preserved: `items` array with `label`, `value`, optional
 * `tone` (default tone) and optional `sub` (helper line).
 */
export function MetricGrid2x2({
  items,
}: { items: { label: string; value: string | number; tone?: MetricTone; sub?: string }[] }) {
  // Tone → text color for the metric value (tabular numeric display)
  const valColor: Record<MetricTone, string> = {
    default: 'text-admin-fg-strong',
    success: 'text-admin-success',
    warning: 'text-admin-warning',
    danger:  'text-admin-danger',
    info:    'text-admin-info',
    purple:  'text-admin-primary',
  };

  return (
    <div className="grid grid-cols-2 gap-2">
      {items.map(({ label, value, tone = 'default', sub }, i) => (
        <div key={i} className="rounded-admin-md bg-admin-bg-subtle p-3">
          <p className="mb-1.5 text-xs leading-none text-admin-fg-muted">{label}</p>
          <p className={cn('text-lg font-semibold leading-none tabular-nums', valColor[tone])}>{value}</p>
          {sub && <p className="mt-1.5 text-xs leading-none text-admin-fg-muted">{sub}</p>}
        </div>
      ))}
    </div>
  );
}
