'use client';

import type { ComponentProps, ElementType, HTMLAttributes } from 'react';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import type {
  LearnerPageHeroModel,
  LearnerSurfaceAccent,
  LearnerSurfaceAction,
  LearnerSurfaceCardModel,
} from '@/lib/learner-surface';
import { cn } from '@/lib/utils';

type LearnerSurfaceSectionHeaderProps = ComponentProps<typeof LearnerSurfaceSectionHeader>;

export function ExpertRouteWorkspace({ className, children, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div className={cn('space-y-6', className)} {...props}>
      {children}
    </div>
  );
}

export function ExpertRouteHero(props: LearnerPageHeroModel) {
  return <LearnerPageHero {...props} />;
}

export function ExpertRouteSectionHeader(props: LearnerSurfaceSectionHeaderProps) {
  return <LearnerSurfaceSectionHeader {...props} />;
}

export function ExpertRouteFreshnessBadge({ value }: { value?: string | null }) {
  return (
    <span className="text-xs text-muted">
      {value ? `Updated ${new Date(value).toLocaleString()}` : 'Freshness unavailable'}
    </span>
  );
}

export function ExpertRouteSummaryCard({
  label,
  value,
  hint,
  accent = 'primary',
  icon,
  statusLabel,
  primaryAction,
  secondaryAction,
  className,
}: {
  label: string;
  value: string | number;
  hint?: string;
  accent?: LearnerSurfaceAccent;
  icon?: ElementType;
  statusLabel?: string;
  primaryAction?: LearnerSurfaceAction;
  secondaryAction?: LearnerSurfaceAction;
  className?: string;
}) {
  const card: LearnerSurfaceCardModel = {
    kind: 'status',
    sourceType: 'frontend_status',
    eyebrow: label,
    eyebrowIcon: icon,
    title: String(value),
    description: hint ?? '',
    accent,
    statusLabel,
    primaryAction,
    secondaryAction,
  };

  return <LearnerSurfaceCard card={card} className={className} />;
}
