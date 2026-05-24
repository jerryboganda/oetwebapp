'use client';

import * as React from 'react';
import { AlertCircle, Loader2 } from 'lucide-react';

import { cn } from '@/lib/utils';
import { Label } from './label';

type TextareaSize = 'sm' | 'md' | 'lg';

const sizeClasses: Record<TextareaSize, string> = {
  sm: 'px-2.5 py-1.5 text-xs',
  md: 'px-3 py-2 text-sm',
  lg: 'px-3.5 py-2.5 text-base',
};

export interface TextareaProps
  extends Omit<React.TextareaHTMLAttributes<HTMLTextAreaElement>, 'size'> {
  /** Visible label rendered above the textarea. */
  label?: string;
  /** Helper text shown below. Hidden when error is present. */
  hint?: string;
  /** Error message — switches to error state (red border + aria-invalid). */
  error?: string;
  /** Marks the field as required (asterisk after label, sets required attr). */
  required?: boolean;
  /** Shows a small loading indicator in the corner. */
  loading?: boolean;
  /** Size variant — affects padding and font size, not rows. */
  size?: TextareaSize;
  /** Optional class for the outer wrapper. */
  wrapperClassName?: string;
}

/**
 * Admin Textarea — multi-line text input. Same API as Input minus the icon slots.
 *
 * The native `rows` and `resize` attributes pass through; defaults to 4 rows
 * and vertical-only resize.
 */
const Textarea = React.forwardRef<HTMLTextAreaElement, TextareaProps>(
  (
    {
      className,
      wrapperClassName,
      label,
      hint,
      error,
      required,
      loading,
      size = 'md',
      rows = 4,
      id,
      disabled,
      style,
      'aria-describedby': ariaDescribedBy,
      ...props
    },
    ref,
  ) => {
    const reactId = React.useId();
    const textareaId = id ?? `textarea-${reactId}`;
    const hintId = hint ? `${textareaId}-hint` : undefined;
    const errorId = error ? `${textareaId}-error` : undefined;

    const describedBy =
      [ariaDescribedBy, error ? errorId : hintId].filter(Boolean).join(' ') ||
      undefined;

    return (
      <div className={cn('flex w-full flex-col gap-1.5', wrapperClassName)}>
        {label ? (
          <Label htmlFor={textareaId}>
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
          <textarea
            ref={ref}
            id={textareaId}
            rows={rows}
            disabled={disabled}
            required={required}
            aria-invalid={error ? true : undefined}
            aria-describedby={describedBy}
            aria-busy={loading || undefined}
            data-loading={loading || undefined}
            style={{ resize: 'vertical', ...style }}
            className={cn(
              'block w-full rounded-[var(--admin-radius-lg)]',
              'bg-[var(--admin-bg-surface)] text-[var(--admin-fg-default)]',
              'placeholder:text-[var(--admin-fg-muted)]',
              'font-[var(--admin-font-body)]',
              'border border-[var(--admin-border)]',
              'leading-relaxed',
              sizeClasses[size],
              // focus ring
              'focus-visible:outline-none focus-visible:ring-2',
              'focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-1',
              'focus-visible:ring-offset-[var(--admin-bg-canvas)]',
              'focus-visible:border-[var(--admin-primary)]',
              // disabled
              'disabled:bg-[var(--admin-bg-subtle)] disabled:text-[var(--admin-fg-muted)]',
              'disabled:cursor-not-allowed disabled:opacity-100 disabled:resize-none',
              // error
              error && [
                'border-[var(--admin-danger)]',
                'focus-visible:ring-[var(--admin-danger)]',
                'focus-visible:border-[var(--admin-danger)]',
              ],
              'transition-[border-color,box-shadow] duration-150 ease-[cubic-bezier(0.4,0,0.6,1)]',
              'motion-reduce:transition-none',
              className,
            )}
            {...props}
          />

          {loading ? (
            <span
              className="pointer-events-none absolute right-2.5 top-2.5 text-[var(--admin-fg-muted)]"
              aria-hidden="true"
            >
              <Loader2 className="h-4 w-4 animate-spin motion-reduce:animate-none" />
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
Textarea.displayName = 'Textarea';

export { Textarea };
