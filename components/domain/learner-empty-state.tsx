'use client';

import Link from 'next/link';
import { ArrowRight, type LucideIcon } from 'lucide-react';
import { type ReactNode } from 'react';
import { Button, type ButtonProps } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { cn } from '@/lib/utils';

type LearnerEmptyStateAction = {
  label: string;
  href?: string;
  onClick?: () => void;
  variant?: Extract<ButtonProps['variant'], 'primary' | 'secondary' | 'outline' | 'ghost'>;
};

type LearnerEmptyStateProps = {
  title: string;
  description: string;
  icon?: LucideIcon;
  primaryAction?: LearnerEmptyStateAction;
  secondaryAction?: LearnerEmptyStateAction;
  compact?: boolean;
  className?: string;
  children?: ReactNode;
};

function EmptyStateActionLink({ action, fullWidth = false }: { action: LearnerEmptyStateAction; fullWidth?: boolean }) {
  const variant = action.variant ?? 'primary';
  const sharedClassName = cn(
    'inline-flex min-h-11 items-center justify-center gap-2 rounded-lg px-4 py-2 text-sm font-semibold transition-[background-color,border-color,color,box-shadow] duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
    variant === 'primary' && 'bg-primary text-white shadow-sm hover:bg-primary/90',
    variant === 'secondary' && 'bg-navy text-white shadow-sm hover:bg-navy/90',
    variant === 'outline' && 'border border-border text-navy hover:border-border-hover hover:bg-surface',
    variant === 'ghost' && 'text-navy hover:bg-lavender/40 dark:hover:bg-white/5',
    fullWidth && 'w-full',
  );

  if (action.href) {
    return (
      <Link href={action.href} className={sharedClassName}>
        {action.label}
        <ArrowRight className="h-4 w-4" aria-hidden="true" />
      </Link>
    );
  }

  return (
    <Button variant={variant} onClick={action.onClick} fullWidth={fullWidth}>
      {action.label}
      <ArrowRight className="h-4 w-4" aria-hidden="true" />
    </Button>
  );
}

export function LearnerEmptyState({
  title,
  description,
  icon: Icon,
  primaryAction,
  secondaryAction,
  compact = false,
  className,
  children,
}: LearnerEmptyStateProps) {
  return (
    <Card
      className={cn(
        'border-dashed border-border bg-surface/95 shadow-sm',
        compact ? 'p-4' : 'p-5 sm:p-6',
        className,
      )}
      role="region"
      aria-label={title}
    >
      <div className={cn('flex gap-4', compact ? 'items-start' : 'flex-col items-start text-left sm:flex-row sm:items-center sm:justify-between')}>
        <div className="flex min-w-0 items-start gap-3">
          {Icon ? (
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary ring-1 ring-primary/10">
              <Icon className="h-5 w-5" aria-hidden="true" />
            </div>
          ) : null}
          <div className="min-w-0">
            <h3 className="text-sm font-bold text-navy sm:text-base">{title}</h3>
            <p className="mt-1 max-w-2xl text-sm leading-6 text-muted">{description}</p>
            {children ? <div className="mt-3">{children}</div> : null}
          </div>
        </div>
        {primaryAction || secondaryAction ? (
          <div className={cn('flex shrink-0 flex-col gap-2 sm:flex-row', compact && 'w-full sm:w-auto')}>
            {primaryAction ? <EmptyStateActionLink action={primaryAction} fullWidth={compact} /> : null}
            {secondaryAction ? <EmptyStateActionLink action={{ variant: 'outline', ...secondaryAction }} fullWidth={compact} /> : null}
          </div>
        ) : null}
      </div>
    </Card>
  );
}
