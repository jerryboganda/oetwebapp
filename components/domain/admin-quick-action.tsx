'use client';

import Link from 'next/link';
import type { ComponentProps, ElementType } from 'react';
import { ArrowRight } from 'lucide-react';
import { cn } from '@/lib/utils';

type AdminQuickActionVariant = 'primary' | 'secondary';

export interface AdminQuickActionProps extends Omit<ComponentProps<typeof Link>, 'children'> {
  label: string;
  icon?: ElementType;
  variant?: AdminQuickActionVariant;
  description?: string;
}

export function AdminQuickAction({
  label,
  icon: Icon = ArrowRight,
  variant = 'secondary',
  description,
  className,
  ...rest
}: AdminQuickActionProps) {
  const base =
    'inline-flex w-full items-center justify-between gap-3 rounded-lg px-5 py-2 text-sm font-medium transition-[background-color,border-color,color,box-shadow,transform,opacity] duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2';
  const styles =
    variant === 'primary'
      ? 'bg-primary text-white shadow-sm hover:bg-primary/90'
      : 'border border-border text-navy hover:bg-surface hover:border-border-hover';
  return (
    <Link {...rest} className={cn(base, styles, className)}>
      <span className="flex min-w-0 flex-col items-start text-left">
        <span className="truncate">{label}</span>
        {description ? <span className="text-xs font-normal text-muted">{description}</span> : null}
      </span>
      <Icon className="h-4 w-4 shrink-0" aria-hidden="true" />
    </Link>
  );
}
