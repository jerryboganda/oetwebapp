'use client';

import * as React from 'react';
import { AlertCircle, Loader2 } from 'lucide-react';

import { cn } from '@/lib/utils';
import { Label } from './label';

type InputSize = 'sm' | 'md' | 'lg';

const sizeClasses: Record<InputSize, string> = {
  sm: 'h-8 px-2.5 text-xs',
  md: 'h-10 px-3 text-sm',
  lg: 'h-11 px-3.5 text-base',
};

const iconSlotPadding: Record<InputSize, { start: string; end: string }> = {
  sm: { start: 'pl-8', end: 'pr-8' },
  md: { start: 'pl-10', end: 'pr-10' },
  lg: { start: 'pl-11', end: 'pr-11' },
};

export interface InputProps
  extends Omit<React.InputHTMLAttributes<HTMLInputElement>, 'size'> {
  /** Visible label rendered above the input. Required for accessibility. */
  label?: string;
  /** Helper text shown below the input. Hidden when an error is present. */
  hint?: string;
  /** Error message — when set, switches to error state (red border + icon + aria-invalid). */
  error?: string;
  /** Marks the field as required (asterisk after label, sets required attr). */
  required?: boolean;
  /** Shows inline spinner in endIcon slot. Sets aria-busy. */
  loading?: boolean;
  /** Icon rendered inside the input on the left. */
  startIcon?: React.ReactNode;
  /** Icon rendered inside the input on the right. Hidden when loading. */
  endIcon?: React.ReactNode;
  /** Height variant. Defaults to 'md' (40px). */
  size?: InputSize;
  /** Optional class applied to the outer wrapper. */
  wrapperClassName?: string;
}

/**
 * Admin Input — text input with label, hint, and error wiring.
 *
 * Implements the Hallmark 8-state contract: default, hover, focus-visible,
 * disabled, loading, error, success (via the absence of `error`). Native hover
 * is identical to default by Bootstrap convention; focus-visible owns the
 * visual change.
 */
const Input = React.forwardRef<HTMLInputElement, InputProps>(
  (
    {
      className,
      wrapperClassName,
      type = 'text',
      label,
      hint,
      error,
      required,
      loading,
      startIcon,
      endIcon,
      size = 'md',
      id,
      disabled,
      'aria-describedby': ariaDescribedBy,
      ...props
    },
    ref,
  ) => {
    const reactId = React.useId();
    const inputId = id ?? `input-${reactId}`;
    const hintId = hint ? `${inputId}-hint` : undefined;
    const errorId = error ? `${inputId}-error` : undefined;

    const describedBy =
      [ariaDescribedBy, error ? errorId : hintId].filter(Boolean).join(' ') ||
      undefined;

    const showStart = !!startIcon;
    const showEnd = loading || !!endIcon || !!error;

    return (
      <div className={cn('flex w-full flex-col gap-1.5', wrapperClassName)}>
        {label ? (
          <Label htmlFor={inputId}>
            {label}
            {required ? (
              <span
                className="ml-0.5 text-[var(--admin-danger)]"
                aria-hidden="true"
              >
                *
              </span>
            ) : null}
          </Label>
        ) : null}

        <div className="relative">
          {showStart ? (
            <span
              className={cn(
                'pointer-events-none absolute inset-y-0 left-0 flex items-center',
                'text-[var(--admin-fg-muted)]',
                size === 'sm' ? 'pl-2.5' : size === 'lg' ? 'pl-3.5' : 'pl-3',
              )}
              aria-hidden="true"
            >
              {startIcon}
            </span>
          ) : null}

          <input
            ref={ref}
            id={inputId}
            type={type}
            disabled={disabled}
            required={required}
            aria-invalid={error ? true : undefined}
            aria-describedby={describedBy}
            aria-busy={loading || undefined}
            data-loading={loading || undefined}
            className={cn(
              // base layout
              'block w-full rounded-[var(--admin-radius-lg)]',
              'bg-[var(--admin-bg-surface)] text-[var(--admin-fg-default)]',
              'placeholder:text-[var(--admin-fg-muted)]',
              'font-[var(--admin-font-body)]',
              'border border-[var(--admin-border)]',
              // sizing
              sizeClasses[size],
              // icon slot padding
              showStart && iconSlotPadding[size].start,
              showEnd && iconSlotPadding[size].end,
              // focus — 4px brand ring, override default outline
              'focus-visible:outline-none focus-visible:ring-2',
              'focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-1',
              'focus-visible:ring-offset-[var(--admin-bg-canvas)]',
              'focus-visible:border-[var(--admin-primary)]',
              // disabled
              'disabled:bg-[var(--admin-bg-subtle)] disabled:text-[var(--admin-fg-muted)]',
              'disabled:cursor-not-allowed disabled:opacity-100',
              // error
              error && [
                'border-[var(--admin-danger)]',
                'focus-visible:ring-[var(--admin-danger)]',
                'focus-visible:border-[var(--admin-danger)]',
              ],
              // transitions — narrow list
              'transition-[border-color,box-shadow] duration-150 ease-[cubic-bezier(0.4,0,0.6,1)]',
              'motion-reduce:transition-none',
              className,
            )}
            {...props}
          />

          {showEnd ? (
            <span
              className={cn(
                'pointer-events-none absolute inset-y-0 right-0 flex items-center',
                size === 'sm' ? 'pr-2.5' : size === 'lg' ? 'pr-3.5' : 'pr-3',
                error
                  ? 'text-[var(--admin-danger)]'
                  : 'text-[var(--admin-fg-muted)]',
              )}
              aria-hidden="true"
            >
              {loading ? (
                <Loader2 className="h-4 w-4 animate-spin motion-reduce:animate-none" />
              ) : error ? (
                <AlertCircle className="h-4 w-4" />
              ) : (
                endIcon
              )}
            </span>
          ) : null}
        </div>

        {error ? (
          <p
            id={errorId}
            className="flex items-start gap-1.5 text-xs text-[var(--admin-danger)] font-[var(--admin-font-body)]"
            role="alert"
          >
            <AlertCircle
              className="mt-0.5 h-3.5 w-3.5 shrink-0"
              aria-hidden="true"
            />
            <span>{error}</span>
          </p>
        ) : hint ? (
          <p
            id={hintId}
            className="text-xs text-[var(--admin-fg-muted)] font-[var(--admin-font-body)]"
          >
            {hint}
          </p>
        ) : null}
      </div>
    );
  },
);
Input.displayName = 'Input';

export { Input };
