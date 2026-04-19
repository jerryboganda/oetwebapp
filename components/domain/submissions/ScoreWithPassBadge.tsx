'use client';

import React from 'react';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import type { SubmissionPassState } from '@/lib/scoring';

/**
 * Score + pass-state badge used on every Submission History card and the
 * detail hero. Routes display through the canonical scoring union so no
 * UI surface ever renders a raw percentage.
 */
export interface ScoreWithPassBadgeProps {
  scaledScore: number | null | undefined;
  scoreLabel: string;
  passState: SubmissionPassState | undefined;
  passLabel: string | undefined;
  grade?: string | null;
  className?: string;
  /** `"compact"` is for mobile rows; `"full"` is the default. */
  density?: 'compact' | 'full';
}

export function ScoreWithPassBadge({
  scaledScore,
  scoreLabel,
  passState,
  passLabel,
  grade,
  className,
  density = 'full',
}: ScoreWithPassBadgeProps) {
  const variant = passStateBadgeVariant(passState);
  const isPending = passState === 'pending' || scaledScore === null || scaledScore === undefined;
  return (
    <div className={cn('flex flex-col items-start gap-1', density === 'compact' && 'items-start', className)}>
      <span className="text-xs font-black uppercase tracking-widest text-muted">
        {isPending ? 'Score' : 'Scaled score'}
      </span>
      <div className="flex items-baseline gap-2">
        <span className={cn('text-xl font-black', isPending ? 'text-muted' : 'text-navy')}>
          {scoreLabel}
        </span>
        {grade && !isPending ? (
          <span className="text-sm font-bold text-muted">Grade {grade}</span>
        ) : null}
      </div>
      {passState ? (
        <Badge variant={variant} size="sm" aria-label={`Pass state: ${passLabel ?? passState}`}>
          {passLabel ?? passStateDefaultLabel(passState)}
        </Badge>
      ) : null}
    </div>
  );
}

function passStateBadgeVariant(state: SubmissionPassState | undefined):
  'success' | 'danger' | 'warning' | 'muted' {
  switch (state) {
    case 'pass': return 'success';
    case 'fail': return 'danger';
    case 'country_required':
    case 'country_unsupported':
      return 'warning';
    case 'pending':
    default:
      return 'muted';
  }
}

function passStateDefaultLabel(state: SubmissionPassState): string {
  switch (state) {
    case 'pass': return 'Pass';
    case 'fail': return 'Fail';
    case 'pending': return 'Pending';
    case 'country_required': return 'Country required';
    case 'country_unsupported': return 'Unsupported country';
    default: return 'Pending';
  }
}
