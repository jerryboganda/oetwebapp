// Re-skinned 2026-05-24 for admin redesign — uses --admin-* token system
import { cn } from '@/lib/utils';
import type { MetricTone } from './types';

/**
 * StatusBadge — small status chip.
 *
 * Backward-compatible API: still accepts `tone` + `label`.
 * Two new optional props:
 *   - `intensity`: 'solid' (default, brand-colored fill) | 'tinted' (low-emphasis)
 *   - `dot`: boolean — prepends a small filled-circle indicator
 *
 * Existing call sites that pass only { tone, label } render identically to
 * the previous solid pill — no migration required.
 */
export type StatusBadgeProps = {
  tone: MetricTone;
  label: string;
  intensity?: 'solid' | 'tinted';
  dot?: boolean;
};

export function StatusBadge({ tone, label, intensity = 'solid', dot = false }: StatusBadgeProps) {
  const solid: Record<MetricTone, string> = {
    default: 'bg-admin-bg-subtle text-admin-fg-default border border-admin-border',
    success: 'bg-admin-success text-admin-success-fg',
    warning: 'bg-admin-warning text-admin-warning-fg',
    danger:  'bg-admin-danger text-admin-danger-fg',
    info:    'bg-admin-info text-admin-info-fg',
    purple:  'bg-admin-primary text-admin-primary-fg',
  };

  const tinted: Record<MetricTone, string> = {
    default: 'bg-admin-bg-subtle text-admin-fg-muted',
    success: 'bg-[var(--admin-success-tint)] text-admin-success',
    warning: 'bg-[var(--admin-warning-tint)] text-admin-warning',
    danger:  'bg-[var(--admin-danger-tint)] text-admin-danger',
    info:    'bg-[var(--admin-info-tint)] text-admin-info',
    purple:  'bg-[var(--admin-primary-tint)] text-admin-primary',
  };

  const palette = intensity === 'tinted' ? tinted[tone] : solid[tone];

  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-medium leading-none',
        palette,
      )}
    >
      {dot && <span className="h-1.5 w-1.5 rounded-full bg-current opacity-80" aria-hidden="true" />}
      {label}
    </span>
  );
}
