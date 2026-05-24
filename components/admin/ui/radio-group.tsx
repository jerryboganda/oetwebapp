'use client';

import * as React from 'react';
import * as RadioGroupPrimitive from '@radix-ui/react-radio-group';
import { Circle } from 'lucide-react';

import { cn } from '@/lib/utils';

/**
 * Admin RadioGroup — Radix RadioGroup primitive with brand styling.
 *
 * Usage:
 * ```tsx
 * <RadioGroup defaultValue="a">
 *   <RadioGroupItem value="a" id="r1" />
 *   <Label htmlFor="r1">Option A</Label>
 * </RadioGroup>
 * ```
 */
const RadioGroup = React.forwardRef<
  React.ElementRef<typeof RadioGroupPrimitive.Root>,
  React.ComponentPropsWithoutRef<typeof RadioGroupPrimitive.Root>
>(({ className, ...props }, ref) => (
  <RadioGroupPrimitive.Root
    ref={ref}
    className={cn('grid gap-2', className)}
    {...props}
  />
));
RadioGroup.displayName = RadioGroupPrimitive.Root.displayName;

const RadioGroupItem = React.forwardRef<
  React.ElementRef<typeof RadioGroupPrimitive.Item>,
  React.ComponentPropsWithoutRef<typeof RadioGroupPrimitive.Item>
>(({ className, ...props }, ref) => (
  <RadioGroupPrimitive.Item
    ref={ref}
    className={cn(
      // size & shape
      'aspect-square h-4 w-4 rounded-full',
      // colors
      'bg-[var(--admin-bg-surface)] border border-[var(--admin-border)]',
      'text-[var(--admin-primary)]',
      // checked
      'data-[state=checked]:border-[var(--admin-primary)]',
      // hover (pointer-fine only)
      '[@media(hover:hover)]:hover:border-[var(--admin-primary)]',
      // focus ring
      'focus-visible:outline-none focus-visible:ring-2',
      'focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-2',
      'focus-visible:ring-offset-[var(--admin-bg-canvas)]',
      // disabled
      'disabled:cursor-not-allowed disabled:opacity-50',
      // transitions
      'transition-[border-color,box-shadow] duration-150 ease-[cubic-bezier(0.4,0,0.6,1)]',
      'motion-reduce:transition-none',
      'inline-flex items-center justify-center',
      className,
    )}
    {...props}
  >
    <RadioGroupPrimitive.Indicator className="flex items-center justify-center">
      <Circle
        className="h-2 w-2 fill-[var(--admin-primary)] text-[var(--admin-primary)]"
        strokeWidth={0}
      />
    </RadioGroupPrimitive.Indicator>
  </RadioGroupPrimitive.Item>
));
RadioGroupItem.displayName = RadioGroupPrimitive.Item.displayName;

export { RadioGroup, RadioGroupItem };
