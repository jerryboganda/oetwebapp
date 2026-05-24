'use client';

import * as React from 'react';
import * as LabelPrimitive from '@radix-ui/react-label';

import { cn } from '@/lib/utils';

/**
 * Admin Label — thin wrapper around Radix Label primitive.
 *
 * Pairs with our form controls via `htmlFor` or by wrapping the control.
 * Inherits `peer-disabled:opacity-50` cursor so labels dim alongside
 * disabled inputs when the input has `peer` class on a sibling.
 */
const Label = React.forwardRef<
  React.ElementRef<typeof LabelPrimitive.Root>,
  React.ComponentPropsWithoutRef<typeof LabelPrimitive.Root>
>(({ className, ...props }, ref) => (
  <LabelPrimitive.Root
    ref={ref}
    className={cn(
      'text-sm font-medium leading-none',
      'text-[var(--admin-fg-strong)]',
      'font-[var(--admin-font-body)]',
      'peer-disabled:cursor-not-allowed peer-disabled:opacity-50',
      className,
    )}
    {...props}
  />
));
Label.displayName = LabelPrimitive.Root.displayName;

export { Label };
