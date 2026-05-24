'use client';

import * as React from 'react';
import * as SwitchPrimitive from '@radix-ui/react-switch';

import { cn } from '@/lib/utils';

/**
 * Admin Switch — Radix Switch primitive styled as iOS-style toggle.
 *
 * Track: 36×20px (h-5 w-9), thumb: 16×16px (h-4 w-4).
 * Thumb slides 16px (translate-x-4) when checked.
 */
const Switch = React.forwardRef<
  React.ElementRef<typeof SwitchPrimitive.Root>,
  React.ComponentPropsWithoutRef<typeof SwitchPrimitive.Root>
>(({ className, ...props }, ref) => (
  <SwitchPrimitive.Root
    ref={ref}
    className={cn(
      // track size & shape
      'peer inline-flex h-5 w-9 shrink-0 cursor-pointer items-center',
      'rounded-full border-2 border-transparent',
      // colors
      'bg-[var(--admin-bg-subtle)]',
      'data-[state=checked]:bg-[var(--admin-primary)]',
      // focus ring
      'focus-visible:outline-none focus-visible:ring-2',
      'focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-2',
      'focus-visible:ring-offset-[var(--admin-bg-canvas)]',
      // disabled
      'disabled:cursor-not-allowed disabled:opacity-50',
      // transitions
      'transition-colors duration-150 ease-[cubic-bezier(0.4,0,0.6,1)]',
      'motion-reduce:transition-none',
      className,
    )}
    {...props}
  >
    <SwitchPrimitive.Thumb
      className={cn(
        'pointer-events-none block h-4 w-4 rounded-full',
        'bg-white shadow-sm ring-0',
        // slide animation — translate to right when checked
        'data-[state=unchecked]:translate-x-0',
        'data-[state=checked]:translate-x-4',
        'transition-transform duration-150 ease-[cubic-bezier(0.4,0,0.6,1)]',
        'motion-reduce:transition-none',
      )}
    />
  </SwitchPrimitive.Root>
));
Switch.displayName = SwitchPrimitive.Root.displayName;

export { Switch };
