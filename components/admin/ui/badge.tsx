'use client';

import { forwardRef, type HTMLAttributes, type ReactNode } from 'react';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from '@/lib/utils';

/* ─────────────────────────────────────────────────────────────────────
 * Variants
 * ───────────────────────────────────────────────────────────────────── */

const badgeVariants = cva(
  'inline-flex items-center gap-1 rounded-full font-medium whitespace-nowrap leading-none transition-colors',
  {
    variants: {
      variant: {
        default: '',
        primary: '',
        secondary: '',
        success: '',
        warning: '',
        danger: '',
        info: '',
      },
      intensity: {
        solid: '',
        tinted: '',
      },
      size: {
        sm: 'h-5 px-1.5 text-xs',
        md: 'h-6 px-2 text-xs',
        lg: 'h-7 px-2.5 text-sm',
      },
    },
    compoundVariants: [
      // ── Tinted (default intensity) ──
      {
        variant: 'default',
        intensity: 'tinted',
        class: 'bg-[var(--admin-bg-subtle)] text-[var(--admin-fg-default)]',
      },
      {
        variant: 'primary',
        intensity: 'tinted',
        class: 'bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]',
      },
      {
        variant: 'secondary',
        intensity: 'tinted',
        class: 'bg-[var(--admin-secondary-tint)] text-[var(--admin-secondary)]',
      },
      {
        variant: 'success',
        intensity: 'tinted',
        class: 'bg-[var(--admin-success-tint)] text-[var(--admin-success)]',
      },
      {
        variant: 'warning',
        intensity: 'tinted',
        class: 'bg-[var(--admin-warning-tint)] text-[var(--admin-warning)]',
      },
      {
        variant: 'danger',
        intensity: 'tinted',
        class: 'bg-[var(--admin-danger-tint)] text-[var(--admin-danger)]',
      },
      {
        variant: 'info',
        intensity: 'tinted',
        class: 'bg-[var(--admin-info-tint)] text-[var(--admin-info)]',
      },
      // ── Solid ──
      {
        variant: 'default',
        intensity: 'solid',
        class: 'bg-[var(--admin-fg-muted)] text-[var(--admin-bg-surface)]',
      },
      {
        variant: 'primary',
        intensity: 'solid',
        class: 'bg-[var(--admin-primary)] text-[var(--admin-primary-fg)]',
      },
      {
        variant: 'secondary',
        intensity: 'solid',
        class: 'bg-[var(--admin-secondary)] text-[var(--admin-secondary-fg)]',
      },
      {
        variant: 'success',
        intensity: 'solid',
        class: 'bg-[var(--admin-success)] text-[var(--admin-success-fg)]',
      },
      {
        variant: 'warning',
        intensity: 'solid',
        class: 'bg-[var(--admin-warning)] text-[var(--admin-warning-fg)]',
      },
      {
        variant: 'danger',
        intensity: 'solid',
        class: 'bg-[var(--admin-danger)] text-[var(--admin-danger-fg)]',
      },
      {
        variant: 'info',
        intensity: 'solid',
        class: 'bg-[var(--admin-info)] text-[var(--admin-info-fg)]',
      },
    ],
    defaultVariants: {
      variant: 'default',
      intensity: 'tinted',
      size: 'md',
    },
  },
);

/* ─────────────────────────────────────────────────────────────────────
 * Props & Component
 * ───────────────────────────────────────────────────────────────────── */

export interface BadgeProps
  extends Omit<HTMLAttributes<HTMLSpanElement>, 'children'>,
    VariantProps<typeof badgeVariants> {
  /** Optional leading icon — render a small element (e.g. dot, lucide icon) */
  startIcon?: ReactNode;
  children?: ReactNode;
}

const Badge = forwardRef<HTMLSpanElement, BadgeProps>(
  ({ className, variant, intensity, size, startIcon, children, ...props }, ref) => {
    return (
      <span
        ref={ref}
        className={cn(badgeVariants({ variant, intensity, size }), className)}
        {...props}
      >
        {startIcon ? <span className="inline-flex shrink-0">{startIcon}</span> : null}
        {children}
      </span>
    );
  },
);
Badge.displayName = 'Badge';

/* ─────────────────────────────────────────────────────────────────────
 * Semantic helper: OET status → tone
 * ───────────────────────────────────────────────────────────────────── */

export type BadgeTone = NonNullable<BadgeProps['variant']>;

const STATUS_TONE_MAP: Record<string, BadgeTone> = {
  published: 'success',
  draft: 'default',
  pending: 'warning',
  failed: 'danger',
  archived: 'secondary',
  active: 'primary',
};

/** Map a common OET content status to a badge tone. Unknown statuses fall back to `default`. */
export function statusToTone(status: string): BadgeTone {
  return STATUS_TONE_MAP[status.toLowerCase()] ?? 'default';
}

export { Badge, badgeVariants };
