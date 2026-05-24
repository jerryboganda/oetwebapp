'use client';

import * as React from 'react';
import { Slot } from '@radix-ui/react-slot';
import { cva, type VariantProps } from 'class-variance-authority';

import { cn } from '@/lib/utils';

/**
 * Admin Card — the canonical surface primitive for the OET admin design system.
 *
 * Implements the shadcn-style composable Card with project-specific tokens
 * sourced from `app/admin/_design/admin-tokens.css`. Headers, titles, content,
 * footers, and a top-right action slot all compose with the same `Card` shell.
 *
 * See: docs/admin-redesign/axelit-study/04-COMPONENT_ARCHITECTURE.md
 */

const cardVariants = cva(
  [
    // Base surface — 12px rounded corner, 1px border, ambient halo shadow.
    'rounded-admin-lg border bg-admin-bg-surface text-admin-fg-default',
    'border-admin-border shadow-admin-md',
    // Narrow transition list (Hallmark §motion: never `all`).
    'transition-[border-color,box-shadow,transform]',
    'duration-200 ease-[cubic-bezier(0.16,1,0.3,1)]',
    'motion-reduce:transition-none',
  ],
  {
    variants: {
      surface: {
        default: '',
        'tinted-primary':
          'bg-[var(--admin-primary-tint)] border-[color:var(--admin-primary-tint-strong)]',
        'tinted-success':
          'bg-[var(--admin-success-tint)] border-[color:var(--admin-success-tint-strong)]',
        'tinted-warning':
          'bg-[var(--admin-warning-tint)] border-[color:var(--admin-warning-tint-strong)]',
        'tinted-danger':
          'bg-[var(--admin-danger-tint)] border-[color:var(--admin-danger-tint-strong)]',
        'tinted-info':
          'bg-[var(--admin-info-tint)] border-[color:var(--admin-info-tint-strong)]',
        elevated: 'bg-admin-bg-elevated shadow-admin-lg',
      },
      interactive: {
        true: [
          'cursor-pointer',
          '[@media(hover:hover)]:hover:shadow-admin-lg',
          '[@media(hover:hover)]:hover:border-admin-border-strong',
          'focus-visible:outline-none focus-visible:ring-2',
          'focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-2',
          'focus-visible:ring-offset-[var(--admin-bg-page)]',
        ],
        false: '',
      },
    },
    defaultVariants: {
      surface: 'default',
      interactive: false,
    },
  },
);

export interface CardProps
  extends React.HTMLAttributes<HTMLDivElement>,
    VariantProps<typeof cardVariants> {
  /**
   * Render as a different element (e.g. `Link`, `button`). Uses Radix Slot —
   * pass a single child element when `asChild`.
   */
  asChild?: boolean;
}

const Card = React.forwardRef<HTMLDivElement, CardProps>(
  ({ className, surface, interactive, asChild = false, ...props }, ref) => {
    const Comp = asChild ? Slot : 'div';
    return (
      <Comp
        ref={ref as React.Ref<HTMLDivElement>}
        data-slot="card"
        className={cn(cardVariants({ surface, interactive }), className)}
        {...props}
      />
    );
  },
);
Card.displayName = 'Card';

const CardHeader = React.forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(
  ({ className, ...props }, ref) => (
    <div
      ref={ref}
      data-slot="card-header"
      className={cn(
        'flex flex-row items-center justify-between space-y-0 p-4 sm:p-5',
        className,
      )}
      {...props}
    />
  ),
);
CardHeader.displayName = 'CardHeader';

const CardTitle = React.forwardRef<HTMLHeadingElement, React.HTMLAttributes<HTMLHeadingElement>>(
  ({ className, ...props }, ref) => (
    <h3
      ref={ref}
      data-slot="card-title"
      className={cn(
        'text-lg font-semibold leading-none tracking-tight text-admin-fg-strong',
        className,
      )}
      {...props}
    />
  ),
);
CardTitle.displayName = 'CardTitle';

const CardDescription = React.forwardRef<
  HTMLParagraphElement,
  React.HTMLAttributes<HTMLParagraphElement>
>(({ className, ...props }, ref) => (
  <p
    ref={ref}
    data-slot="card-description"
    className={cn('text-sm text-admin-fg-muted mt-1', className)}
    {...props}
  />
));
CardDescription.displayName = 'CardDescription';

const CardAction = React.forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(
  ({ className, ...props }, ref) => (
    <div
      ref={ref}
      data-slot="card-action"
      className={cn('ml-auto flex items-center gap-2', className)}
      {...props}
    />
  ),
);
CardAction.displayName = 'CardAction';

const CardContent = React.forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(
  ({ className, ...props }, ref) => (
    <div
      ref={ref}
      data-slot="card-content"
      className={cn('p-4 sm:p-5 pt-0', className)}
      {...props}
    />
  ),
);
CardContent.displayName = 'CardContent';

const CardFooter = React.forwardRef<HTMLDivElement, React.HTMLAttributes<HTMLDivElement>>(
  ({ className, ...props }, ref) => (
    <div
      ref={ref}
      data-slot="card-footer"
      className={cn(
        'flex items-center p-4 sm:p-5 pt-4 border-t border-admin-border mt-4',
        className,
      )}
      {...props}
    />
  ),
);
CardFooter.displayName = 'CardFooter';

export {
  Card,
  CardHeader,
  CardTitle,
  CardDescription,
  CardAction,
  CardContent,
  CardFooter,
  cardVariants,
};
