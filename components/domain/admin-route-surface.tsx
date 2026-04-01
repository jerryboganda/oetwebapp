'use client';

import { cloneElement, isValidElement } from 'react';
import type { ComponentProps, ElementType, HTMLAttributes, ReactNode } from 'react';
import { Card } from '@/components/ui/card';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import type {
  LearnerPageHeroModel,
  LearnerSurfaceAccent,
  LearnerSurfaceAction,
  LearnerSurfaceCardModel,
} from '@/lib/learner-surface';
import { cn } from '@/lib/utils';

type LearnerSurfaceSectionHeaderProps = ComponentProps<typeof LearnerSurfaceSectionHeader>;

const toneAccentMap: Record<'default' | 'success' | 'warning' | 'danger', LearnerSurfaceAccent> = {
  default: 'navy',
  success: 'emerald',
  warning: 'amber',
  danger: 'rose',
};

export function AdminRouteWorkspace({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('space-y-6', className)} {...props}>
      {children}
    </div>
  );
}

export function AdminRouteHero(props: LearnerPageHeroModel) {
  return <LearnerPageHero {...props} />;
}

export function AdminRouteSectionHeader({
  action,
  actions,
  meta,
  className,
  ...props
}: LearnerSurfaceSectionHeaderProps & {
  actions?: ReactNode;
  meta?: string;
}) {
  return (
    <div className={cn('space-y-2', className)}>
      <LearnerSurfaceSectionHeader {...props} action={action ?? actions} />
      {meta ? <p className="text-xs font-medium uppercase tracking-[0.12em] text-muted">{meta}</p> : null}
    </div>
  );
}

export function AdminRouteFreshnessBadge({ value }: { value?: string | null }) {
  return (
    <span className="text-xs text-muted">
      {value ? `Updated ${new Date(value).toLocaleString()}` : 'Freshness unavailable'}
    </span>
  );
}

export function AdminRouteSummaryCard({
  label,
  value,
  hint,
  accent,
  icon,
  tone = 'default',
  statusLabel,
  primaryAction,
  secondaryAction,
  className,
}: {
  label: string;
  value: string | number;
  hint?: string;
  accent?: LearnerSurfaceAccent;
  icon?: ElementType | ReactNode;
  tone?: 'default' | 'success' | 'warning' | 'danger';
  statusLabel?: string;
  primaryAction?: LearnerSurfaceAction;
  secondaryAction?: LearnerSurfaceAction;
  className?: string;
}) {
  const normalizedIcon =
    !icon ? undefined
    : typeof icon === 'function' || typeof icon === 'string' ? (icon as ElementType)
    : isValidElement<{ className?: string }>(icon)
      ? ({ className }: { className?: string }) =>
          cloneElement(icon, {
            className: cn(icon.props.className, className),
          })
      : undefined;

  const card: LearnerSurfaceCardModel = {
    kind: 'status',
    sourceType: 'frontend_status',
    eyebrow: label,
    eyebrowIcon: normalizedIcon,
    title: String(value),
    description: hint ?? '',
    accent: accent ?? toneAccentMap[tone],
    statusLabel,
    primaryAction,
    secondaryAction,
  };

  return <LearnerSurfaceCard card={card} className={className} />;
}

export function AdminRoutePanel({
  title,
  description,
  actions,
  children,
  className,
  contentClassName,
}: {
  title: string;
  description?: string;
  actions?: ReactNode;
  children: ReactNode;
  className?: string;
  contentClassName?: string;
}) {
  return (
    <Card className={cn('overflow-hidden', className)} padding="lg">
      <div className="space-y-4">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div className="space-y-1">
            <h2 className="text-lg font-bold text-navy">{title}</h2>
            {description ? <p className="text-sm text-muted">{description}</p> : null}
          </div>
          {actions ? <div className="flex flex-wrap items-center gap-2">{actions}</div> : null}
        </div>
        <div className={contentClassName}>{children}</div>
      </div>
    </Card>
  );
}
