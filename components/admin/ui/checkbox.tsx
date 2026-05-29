'use client';

import * as React from 'react';
import * as CheckboxPrimitive from '@radix-ui/react-checkbox';
import { Check, Minus } from 'lucide-react';

import { cn } from '@/lib/utils';

export interface CheckboxProps
  extends React.ComponentPropsWithoutRef<typeof CheckboxPrimitive.Root> {
  /** Optional label rendered to the right of the checkbox, paired via htmlFor. */
  label?: React.ReactNode;
  /** Optional helper text rendered below the label. */
  description?: React.ReactNode;
}

/**
 * Admin Checkbox — Radix Checkbox primitive with brand styling.
 *
 * Supports `checked={true | false | 'indeterminate'}` per Radix spec.
 * When indeterminate, renders a minus icon instead of the check.
 *
 * If `label` is provided, the component renders a flex row with paired
 * label/description block. Otherwise just the box.
 */
const Checkbox = React.forwardRef<
  React.ElementRef<typeof CheckboxPrimitive.Root>,
  CheckboxProps
>(({ className, label, description, id, disabled, ...props }, ref) => {
  const reactId = React.useId();
  const checkboxId = id ?? `checkbox-${reactId}`;
  const descriptionId = description ? `${checkboxId}-description` : undefined;

  const box = (
    <CheckboxPrimitive.Root
      ref={ref}
      id={checkboxId}
      disabled={disabled}
      aria-describedby={descriptionId}
      className={cn(
        // size & shape
        'peer h-4 w-4 shrink-0 rounded-[4px]',
        // colors — default
        'bg-[var(--admin-bg-surface)] border border-[var(--admin-border)]',
        // checked / indeterminate
        'data-[state=checked]:bg-[var(--admin-primary)] data-[state=checked]:border-[var(--admin-primary)]',
        'data-[state=checked]:text-[var(--admin-primary-fg)]',
        'data-[state=indeterminate]:bg-[var(--admin-primary)] data-[state=indeterminate]:border-[var(--admin-primary)]',
        'data-[state=indeterminate]:text-[var(--admin-primary-fg)]',
        // focus ring
        'focus-visible:outline-none focus-visible:ring-2',
        'focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-2',
        'focus-visible:ring-offset-[var(--admin-bg-canvas)]',
        // hover (pointer-fine only)
        '[@media(hover:hover)]:hover:border-[var(--admin-primary)]',
        // disabled
        'disabled:cursor-not-allowed disabled:opacity-50',
        // transitions
        'transition-[background-color,border-color,box-shadow] duration-150 ease-[cubic-bezier(0.4,0,0.6,1)]',
        'motion-reduce:transition-none',
        'inline-flex items-center justify-center',
        className,
      )}
      {...props}
    >
      <CheckboxPrimitive.Indicator className="flex items-center justify-center text-current">
        {props.checked === 'indeterminate' ? (
          <Minus className="h-3 w-3" strokeWidth={3} />
        ) : (
          <Check className="h-3 w-3" strokeWidth={3} />
        )}
      </CheckboxPrimitive.Indicator>
    </CheckboxPrimitive.Root>
  );

  if (!label && !description) {
    return box;
  }

  return (
    <div className="flex items-start gap-2.5">
      {box}
      <div className="flex flex-col gap-0.5">
        {label ? (
          <label
            htmlFor={checkboxId}
            className={cn(
              'text-sm font-medium leading-none',
              'text-[var(--admin-fg-strong)]',
              'font-[var(--admin-font-body)]',
              disabled ? 'cursor-not-allowed opacity-50' : 'cursor-pointer',
            )}
          >
            {label}
          </label>
        ) : null}
        {description ? (
          <p
            id={descriptionId}
            className={cn(
              'text-xs text-[var(--admin-fg-muted)]',
              'font-[var(--admin-font-body)]',
              disabled && 'opacity-50',
            )}
          >
            {description}
          </p>
        ) : null}
      </div>
    </div>
  );
});
Checkbox.displayName = CheckboxPrimitive.Root.displayName;

export { Checkbox };
