'use client';

import * as React from 'react';
import { Slot } from '@radix-ui/react-slot';
import { cva, type VariantProps } from 'class-variance-authority';
import { Loader2 } from 'lucide-react';

import { cn } from '@/lib/utils';

/**
 * Admin Button — primary interactive primitive for the OET admin design system.
 *
 * Implements the 8-state Hallmark contract: default, hover, focus-visible,
 * active, disabled, loading, error, success (error/success handled by parent
 * via toasts; per Hallmark, buttons never linger in success).
 *
 * Tokens referenced come from `app/admin/_design/admin-tokens.css`.
 */
export const buttonVariants = cva(
  // Base — applied to every variant.
  [
    'inline-flex items-center justify-center gap-2 whitespace-nowrap',
    'rounded-[var(--admin-radius-lg)] font-medium',
    'font-[var(--admin-font-body)]',
    'select-none',
    // Hover scoped to pointer-fine devices (prevents stuck-hover on touch).
    '[@media(hover:hover)]:hover:transition-colors',
    // Focus ring — brand primary, 4px wide, contrasts via ring-offset.
    'focus-visible:outline-none focus-visible:ring-2',
    'focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-2',
    'focus-visible:ring-offset-[var(--admin-bg-canvas)]',
    // Active — tactile press (75ms via global transition spec).
    'active:scale-[0.98]',
    // Disabled — pointer-events none + opacity + cursor.
    'disabled:opacity-50 disabled:cursor-not-allowed disabled:pointer-events-none',
    'aria-disabled:opacity-50 aria-disabled:cursor-not-allowed aria-disabled:pointer-events-none',
    // Transitions — narrow property list, never `all`.
    'transition-[background-color,border-color,color,box-shadow,transform]',
    'duration-150 ease-[cubic-bezier(0.4,0,0.6,1)]',
    // Reduced motion — collapse all transforms/transitions.
    'motion-reduce:transition-none motion-reduce:active:scale-100',
  ],
  {
    variants: {
      variant: {
        primary: [
          'bg-[var(--admin-primary)] text-white',
          'border border-[var(--admin-primary)]',
          '[@media(hover:hover)]:hover:bg-[var(--admin-primary-hover)]',
          '[@media(hover:hover)]:hover:border-[var(--admin-primary-hover)]',
          'active:bg-[var(--admin-primary-active)]',
          'shadow-sm',
        ],
        secondary: [
          'bg-[var(--admin-bg-subtle)] text-[var(--admin-fg-default)]',
          'border border-[var(--admin-border)]',
          '[@media(hover:hover)]:hover:bg-[var(--admin-state-hover)]',
          'active:bg-[var(--admin-state-active)]',
        ],
        outline: [
          'bg-transparent text-[var(--admin-primary)]',
          'border border-[var(--admin-primary)]',
          '[@media(hover:hover)]:hover:bg-[var(--admin-primary)]',
          '[@media(hover:hover)]:hover:text-white',
          'active:bg-[var(--admin-primary-active)]',
        ],
        ghost: [
          'bg-transparent text-[var(--admin-fg-default)]',
          'border border-transparent',
          '[@media(hover:hover)]:hover:bg-[var(--admin-state-hover)]',
          'active:bg-[var(--admin-state-active)]',
        ],
        destructive: [
          'bg-[var(--admin-danger)] text-white',
          'border border-[var(--admin-danger)]',
          '[@media(hover:hover)]:hover:bg-[var(--admin-danger-hover)]',
          '[@media(hover:hover)]:hover:border-[var(--admin-danger-hover)]',
          'active:bg-[var(--admin-danger-active)]',
          'focus-visible:ring-[var(--admin-danger)]',
          'shadow-sm',
        ],
        link: [
          'bg-transparent border-transparent text-[var(--admin-primary)]',
          'underline-offset-4',
          '[@media(hover:hover)]:hover:underline',
          'active:scale-100', // links don't press
          'h-auto p-0',
        ],
      },
      size: {
        sm: 'h-8 px-3 text-xs min-w-[2.5rem]',
        md: 'h-10 px-4 text-sm min-w-[5rem]',
        lg: 'h-11 px-6 text-base min-w-[6rem]',
        icon: 'h-10 w-10 p-0 min-w-0',
      },
      fullWidth: {
        true: 'w-full',
        false: '',
      },
    },
    compoundVariants: [
      // link variant ignores size width tokens
      { variant: 'link', size: 'sm', class: 'h-auto px-0 min-w-0' },
      { variant: 'link', size: 'md', class: 'h-auto px-0 min-w-0' },
      { variant: 'link', size: 'lg', class: 'h-auto px-0 min-w-0' },
    ],
    defaultVariants: {
      variant: 'primary',
      size: 'md',
      fullWidth: false,
    },
  },
);

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  /**
   * If true, renders the child element as the button (Radix Slot pattern).
   * Use for wrapping `<Link>` etc. with button styling.
   */
  asChild?: boolean;
  /**
   * If true, button shows inline spinner, preserves width via min-w,
   * sets `aria-busy="true"`, and disables click handlers.
   */
  loading?: boolean;
  /**
   * Optional text to show alongside the spinner while loading.
   * Falls back to original children if omitted (preserves layout).
   */
  loadingText?: React.ReactNode;
  /** Icon rendered before the label. Hidden when loading (spinner takes its place). */
  startIcon?: React.ReactNode;
  /** Icon rendered after the label. */
  endIcon?: React.ReactNode;
}

const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  (
    {
      className,
      variant,
      size,
      fullWidth,
      asChild = false,
      loading = false,
      loadingText,
      startIcon,
      endIcon,
      disabled,
      children,
      type,
      ...props
    },
    ref,
  ) => {
    const Comp = asChild ? Slot : 'button';
    const isDisabled = disabled || loading;

    // When asChild, we can't inject our own children-wrapping markup safely
    // (Slot expects a single child). For that case, render children verbatim.
    if (asChild) {
      return (
        <Comp
          ref={ref}
          className={cn(buttonVariants({ variant, size, fullWidth, className }))}
          aria-disabled={isDisabled || undefined}
          {...props}
        >
          {children}
        </Comp>
      );
    }

    return (
      <button
        ref={ref}
        type={type ?? 'button'}
        disabled={isDisabled}
        aria-busy={loading || undefined}
        data-loading={loading || undefined}
        className={cn(
          buttonVariants({ variant, size, fullWidth, className }),
          loading && 'cursor-progress',
        )}
        {...props}
      >
        {loading ? (
          <>
            <Loader2
              className="h-4 w-4 animate-spin motion-reduce:animate-none"
              aria-hidden="true"
            />
            <span>{loadingText ?? children}</span>
          </>
        ) : (
          <>
            {startIcon ? (
              <span className="inline-flex shrink-0" aria-hidden="true">
                {startIcon}
              </span>
            ) : null}
            <span>{children}</span>
            {endIcon ? (
              <span className="inline-flex shrink-0" aria-hidden="true">
                {endIcon}
              </span>
            ) : null}
          </>
        )}
      </button>
    );
  },
);
Button.displayName = 'Button';

export { Button };
