// Re-skinned 2026-05-24 for admin redesign — uses --admin-* token system
import { cn } from '@/lib/utils';
import type { MetricTone } from './types';

/**
 * PulseTile — KPI tile for dashboards.
 *
 * Public API preserved: `label`, `value`, `tone`, `icon` (props unchanged).
 * Visual treatment: tinted icon chip + strong numeric value, card chrome
 * matches the rest of the admin surface system.
 */
export function PulseTile({
  label, value, tone, icon: Icon,
}: { label: string; value: string | number; tone: MetricTone; icon: React.ElementType }) {
  // Tinted icon chip: bg uses CSS-var tint; foreground uses solid role color
  const iconChip: Record<MetricTone, string> = {
    default: 'bg-[var(--admin-state-hover)] text-admin-fg-muted',
    success: 'bg-[var(--admin-success-tint)] text-admin-success',
    warning: 'bg-[var(--admin-warning-tint)] text-admin-warning',
    danger:  'bg-[var(--admin-danger-tint)] text-admin-danger',
    info:    'bg-[var(--admin-info-tint)] text-admin-info',
    purple:  'bg-[var(--admin-primary-tint)] text-admin-primary',
  };

  return (
    <div
      className={cn(
        'flex min-w-0 flex-1 items-center gap-3 rounded-admin border border-admin-border',
        'bg-admin-bg-surface p-4 shadow-admin-sm transition-shadow hover:shadow-admin-md',
      )}
    >
      <div className={cn('flex h-10 w-10 shrink-0 items-center justify-center rounded-full p-2', iconChip[tone])}>
        <Icon className="h-5 w-5" />
      </div>
      <div className="min-w-0 flex-1">
        <p className="mb-1 truncate text-xs font-medium uppercase tracking-wide leading-none text-admin-fg-muted">
          {label}
        </p>
        <p className="text-2xl font-bold leading-none tracking-tight tabular-nums text-admin-fg-strong">
          {value}
        </p>
      </div>
    </div>
  );
}
