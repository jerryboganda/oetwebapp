'use client';

import * as React from 'react';
import Link from 'next/link';
import { cva } from 'class-variance-authority';

import { cn } from '@/lib/utils';
import { Button } from './button';

/* ─────────────────────────────────────────────────────────────────────
 * EmptyState — universal feedback container for the OET admin DS.
 *
 * Covers four operational flavors per spec 19-ERROR-EMPTY-STATES.md:
 *   • First-time (zero items)             → variant="default"
 *   • Filtered (no matching results)      → variant="default" + size="sm"
 *   • Error (fetch / boundary failure)    → variant="error"
 *   • Onboarding (welcome + step preview) → variant="onboarding" + steps
 *
 * Anatomy (centered column):
 *   illustration (icon-in-tinted-circle) → title → description →
 *   action row (primary + secondary)     → help / support links →
 *   (optional) onboarding step grid       → (optional) errorRef
 *
 * Accessibility:
 *   - Container exposes `role="status"` so SR announces the state on mount.
 *   - Heading hierarchy controlled by `headingLevel` (defaults to h2).
 *   - Illustration wrapper is `aria-hidden` (decorative).
 *   - Buttons receive a contextual `aria-label` combining label + title.
 * ───────────────────────────────────────────────────────────────────── */

type EmptyStateAction = {
  label: string;
  href?: string;
  onClick?: () => void;
  loading?: boolean;
};

type EmptyStateSecondaryAction = {
  label: string;
  href?: string;
  onClick?: () => void;
};

type EmptyStateLink = {
  label: string;
  href: string;
};

type EmptyStateStep = {
  icon: React.ReactNode;
  title: string;
  description: string;
};

export type EmptyStateProps = {
  variant?: 'default' | 'error' | 'onboarding';
  size?: 'sm' | 'md' | 'lg';
  illustration?: React.ReactNode;
  /** Backward-compat alias for `illustration`. */
  icon?: React.ReactNode;
  title: string;
  description?: string;
  primaryAction?: EmptyStateAction;
  secondaryAction?: EmptyStateSecondaryAction;
  supportLink?: EmptyStateLink;
  helpLink?: EmptyStateLink;
  errorRef?: string;
  steps?: EmptyStateStep[];
  /**
   * Override the heading level for proper page hierarchy.
   * Page-level (404/500/etc.) → 'h2'; section-level → 'h3'; inline → 'h4'.
   * Defaults: lg→h2, md→h3, sm→h3.
   */
  headingLevel?: 'h1' | 'h2' | 'h3' | 'h4';
  className?: string;
} & Omit<React.HTMLAttributes<HTMLDivElement>, 'title'>;

/* --- Container width + spacing per size ---------------------------------- */
const containerVariants = cva(
  [
    'mx-auto flex flex-col items-center text-center',
    'motion-safe:animate-in motion-safe:fade-in-0',
    'motion-safe:slide-in-from-bottom-1 motion-safe:duration-300',
  ],
  {
    variants: {
      size: {
        sm: 'max-w-xs py-6 px-4',
        md: 'max-w-md py-8 px-4',
        lg: 'max-w-lg py-12 px-4',
      },
    },
    defaultVariants: { size: 'md' },
  },
);

/* --- Illustration wrapper (tinted circle by variant) --------------------- */
const illustrationWrapperVariants = cva(
  [
    'inline-flex items-center justify-center rounded-full p-4',
    'motion-safe:animate-in motion-safe:zoom-in-95 motion-safe:duration-300',
  ],
  {
    variants: {
      variant: {
        default: 'bg-admin-primary/10 text-admin-primary',
        error: 'bg-admin-danger/10 text-admin-danger',
        onboarding: 'bg-admin-success/10 text-admin-success',
      },
      size: {
        sm: '[&_svg]:h-10 [&_svg]:w-10',
        md: '[&_svg]:h-12 [&_svg]:w-12',
        lg: '[&_svg]:h-14 [&_svg]:w-14',
      },
    },
    defaultVariants: { variant: 'default', size: 'md' },
  },
);

/* --- Title sizing -------------------------------------------------------- */
const titleVariants = cva(
  'font-semibold text-admin-fg-strong mt-5 tracking-tight',
  {
    variants: {
      size: {
        sm: 'text-base',
        md: 'text-lg',
        lg: 'text-xl',
      },
    },
    defaultVariants: { size: 'md' },
  },
);

/* --- Description sizing -------------------------------------------------- */
const descriptionVariants = cva(
  'text-admin-fg-muted mt-2 leading-relaxed max-w-[60ch]',
  {
    variants: {
      size: {
        sm: 'text-sm',
        md: 'text-sm',
        lg: 'text-base',
      },
    },
    defaultVariants: { size: 'md' },
  },
);

type ActionRenderOpts = {
  action: EmptyStateAction | EmptyStateSecondaryAction;
  variant: 'primary' | 'outline';
  size: 'sm' | 'md' | 'lg';
  titleContext: string;
};

function ActionButton({ action, variant, size, titleContext }: ActionRenderOpts) {
  const buttonSize = size === 'sm' ? 'sm' : 'md';
  const ariaLabel = `${action.label}: ${titleContext}`;
  // Only the primary action supports loading state.
  const loading =
    'loading' in action && variant === 'primary' ? Boolean(action.loading) : false;

  if (action.href) {
    return (
      <Button
        asChild
        variant={variant}
        size={buttonSize}
        aria-label={ariaLabel}
      >
        <Link href={action.href}>{action.label}</Link>
      </Button>
    );
  }

  return (
    <Button
      variant={variant}
      size={buttonSize}
      onClick={action.onClick}
      loading={loading}
      aria-label={ariaLabel}
    >
      {action.label}
    </Button>
  );
}

const EmptyState = React.forwardRef<HTMLDivElement, EmptyStateProps>(
  (
    {
      variant = 'default',
      size = 'md',
      illustration,
      icon,
      title,
      description,
      primaryAction,
      secondaryAction,
      supportLink,
      helpLink,
      errorRef,
      steps,
      headingLevel,
      className,
      ...rest
    },
    ref,
  ) => {
    // Default heading level based on size context.
    const resolvedHeading =
      headingLevel ?? (size === 'lg' ? 'h2' : 'h3');
    const Heading = resolvedHeading as React.ElementType;

    // Backward-compat: accept `icon` as an alias for `illustration`.
    const resolvedIllustration = illustration ?? icon;

    const hasActions = Boolean(primaryAction || secondaryAction);
    const hasFooterLinks = Boolean(helpLink || supportLink);
    const hasSteps = variant === 'onboarding' && (steps?.length ?? 0) > 0;

    return (
      <div
        ref={ref}
        role="status"
        aria-live="polite"
        className={cn(containerVariants({ size }), className)}
        data-variant={variant}
        data-size={size}
        {...rest}
      >
        {resolvedIllustration ? (
          <div
            aria-hidden="true"
            className={illustrationWrapperVariants({ variant, size })}
          >
            {resolvedIllustration}
          </div>
        ) : null}

        <Heading className={titleVariants({ size })}>{title}</Heading>

        {description ? (
          <p className={descriptionVariants({ size })}>{description}</p>
        ) : null}

        {hasActions ? (
          <div
            className={cn(
              'mt-6 flex w-full flex-col gap-2',
              'sm:w-auto sm:flex-row sm:justify-center sm:items-center',
            )}
          >
            {primaryAction ? (
              <ActionButton
                action={primaryAction}
                variant="primary"
                size={size}
                titleContext={title}
              />
            ) : null}
            {secondaryAction ? (
              <ActionButton
                action={secondaryAction}
                variant="outline"
                size={size}
                titleContext={title}
              />
            ) : null}
          </div>
        ) : null}

        {hasFooterLinks ? (
          <div className="mt-5 flex flex-wrap items-center justify-center gap-x-4 gap-y-2 text-xs">
            {helpLink ? (
              <Link
                href={helpLink.href}
                className={cn(
                  'text-admin-primary underline-offset-4 hover:underline',
                  'focus-visible:outline-none focus-visible:ring-2',
                  'focus-visible:ring-admin-primary focus-visible:ring-offset-2',
                  'focus-visible:ring-offset-admin-bg-page rounded-sm',
                )}
              >
                {helpLink.label}
              </Link>
            ) : null}
            {supportLink ? (
              <Link
                href={supportLink.href}
                className={cn(
                  'text-admin-fg-muted underline-offset-4 hover:underline hover:text-admin-fg-default',
                  'focus-visible:outline-none focus-visible:ring-2',
                  'focus-visible:ring-admin-primary focus-visible:ring-offset-2',
                  'focus-visible:ring-offset-admin-bg-page rounded-sm',
                )}
              >
                {supportLink.label}
              </Link>
            ) : null}
          </div>
        ) : null}

        {errorRef ? (
          <p className="mt-4 text-xs text-admin-fg-muted font-mono">
            Reference:{' '}
            <span className="text-admin-fg-default">{errorRef}</span>
          </p>
        ) : null}

        {hasSteps ? (
          <ul
            className={cn(
              'mt-8 grid w-full gap-4 text-left',
              'sm:grid-cols-2 lg:grid-cols-3',
            )}
            aria-label="Getting started"
          >
            {steps!.map((step, idx) => (
              <li
                key={`${step.title}-${idx}`}
                className={cn(
                  'rounded-admin border border-admin-border bg-admin-bg-surface p-4',
                  'shadow-admin-sm transition-shadow',
                  '[@media(hover:hover)]:hover:shadow-admin-md',
                )}
              >
                <div
                  aria-hidden="true"
                  className={cn(
                    'inline-flex h-9 w-9 items-center justify-center rounded-full',
                    'bg-admin-primary/10 text-admin-primary',
                    '[&_svg]:h-5 [&_svg]:w-5',
                  )}
                >
                  {step.icon}
                </div>
                <p className="mt-3 text-sm font-semibold text-admin-fg-strong">
                  {step.title}
                </p>
                <p className="mt-1 text-xs text-admin-fg-muted leading-relaxed">
                  {step.description}
                </p>
              </li>
            ))}
          </ul>
        ) : null}
      </div>
    );
  },
);
EmptyState.displayName = 'EmptyState';

export { EmptyState };
export type { EmptyStateAction, EmptyStateLink, EmptyStateStep };
