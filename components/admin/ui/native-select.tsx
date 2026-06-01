'use client';

import * as React from 'react';
import { AlertCircle } from 'lucide-react';

import { cn } from '@/lib/utils';
import { Label } from './label';

type NativeSelectOption = {
  value: string;
  label: string;
};

export interface NativeSelectProps
  extends Omit<React.SelectHTMLAttributes<HTMLSelectElement>, 'size'> {
  label?: string;
  hint?: string;
  error?: string;
  placeholder?: string;
  options?: NativeSelectOption[];
  wrapperClassName?: string;
}

const NativeSelect = React.forwardRef<HTMLSelectElement, NativeSelectProps>(
  (
    {
      className,
      wrapperClassName,
      label,
      hint,
      error,
      placeholder,
      options,
      id,
      required,
      children,
      'aria-describedby': ariaDescribedBy,
      ...props
    },
    ref,
  ) => {
    const reactId = React.useId();
    const selectId = id ?? `select-${reactId}`;
    const hintId = hint ? `${selectId}-hint` : undefined;
    const errorId = error ? `${selectId}-error` : undefined;
    const describedBy = [ariaDescribedBy, error ? errorId : hintId].filter(Boolean).join(' ') || undefined;

    return (
      <div className={cn('flex w-full flex-col gap-1.5', wrapperClassName)}>
        {label ? (
          <Label htmlFor={selectId}>
            {label}
            {required ? <span className="ml-0.5 text-[var(--admin-danger)]" aria-hidden="true">*</span> : null}
          </Label>
        ) : null}
        <select
          ref={ref}
          id={selectId}
          required={required}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy}
          className={cn(
            'block h-10 w-full rounded-[var(--admin-radius-lg)] border border-[var(--admin-border)]',
            'bg-[var(--admin-bg-surface)] px-3 py-2 text-sm text-[var(--admin-fg-default)]',
            'font-[var(--admin-font-body)] focus-visible:outline-none focus-visible:ring-2',
            'focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-1 focus-visible:ring-offset-[var(--admin-bg-canvas)]',
            'focus-visible:border-[var(--admin-primary)] disabled:cursor-not-allowed disabled:bg-[var(--admin-bg-subtle)]',
            'disabled:text-[var(--admin-fg-muted)] disabled:opacity-100',
            error && 'border-[var(--admin-danger)] focus-visible:ring-[var(--admin-danger)] focus-visible:border-[var(--admin-danger)]',
            'transition-[border-color,box-shadow] duration-150 motion-reduce:transition-none',
            className,
          )}
          {...props}
        >
          {placeholder ? <option value="">{placeholder}</option> : null}
          {options ? options.map((option) => (
            <option key={option.value} value={option.value}>{option.label}</option>
          )) : children}
        </select>
        {error ? (
          <p id={errorId} className="flex items-start gap-1.5 text-xs font-[var(--admin-font-body)] text-[var(--admin-danger)]" role="alert">
            <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            <span>{error}</span>
          </p>
        ) : hint ? (
          <p id={hintId} className="text-xs text-[var(--admin-fg-muted)] font-[var(--admin-font-body)]">{hint}</p>
        ) : null}
      </div>
    );
  },
);
NativeSelect.displayName = 'NativeSelect';

export { NativeSelect };