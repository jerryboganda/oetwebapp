'use client';

/**
 * Admin Tooltip — anchored hover/focus hint primitive.
 *
 * Wraps `@radix-ui/react-tooltip`. Visual style is inverted from the
 * surrounding UI (dark surface, light text in light mode) per Material spec
 * — this keeps the tip distinct from the underlying content.
 *
 * Usage:
 *   <TooltipProvider delayDuration={800}>  // wrap once at app root
 *     ...
 *     <Tooltip>
 *       <TooltipTrigger asChild><button aria-label="Save">…</button></TooltipTrigger>
 *       <TooltipContent>Save changes</TooltipContent>
 *     </Tooltip>
 *   </TooltipProvider>
 *
 * Accessibility:
 *   - Tooltips supplement, never replace, accessible labels.
 *     The trigger must have its own `aria-label` / visible text;
 *     tooltips are not announced reliably by all assistive tech.
 */

import * as React from 'react';
import * as TooltipPrimitive from '@radix-ui/react-tooltip';

import { cn } from '@/lib/utils';

const TooltipProvider = TooltipPrimitive.Provider;
const Tooltip = TooltipPrimitive.Root;
const TooltipTrigger = TooltipPrimitive.Trigger;

const TooltipContent = React.forwardRef<
  React.ElementRef<typeof TooltipPrimitive.Content>,
  React.ComponentPropsWithoutRef<typeof TooltipPrimitive.Content>
>(({ className, sideOffset = 4, ...props }, ref) => (
  <TooltipPrimitive.Portal>
    <TooltipPrimitive.Content
      ref={ref}
      sideOffset={sideOffset}
      className={cn(
        'z-[var(--admin-z-tooltip)] overflow-hidden',
        'rounded-[var(--admin-radius-md)] border border-[var(--admin-border-default)]',
        // Inverted surface — dark in light mode, light in dark mode.
        'bg-[var(--admin-fg-strong)] text-[var(--admin-bg-surface)]',
        'px-3 py-1.5 text-xs font-medium font-[var(--admin-font-body)]',
        'shadow-[var(--admin-shadow-md)]',
        'origin-[var(--radix-tooltip-content-transform-origin)]',
        'data-[state=delayed-open]:animate-in data-[state=closed]:animate-out',
        'data-[state=delayed-open]:fade-in-0 data-[state=closed]:fade-out-0',
        'data-[state=delayed-open]:zoom-in-95 data-[state=closed]:zoom-out-95',
        'data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2',
        'data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2',
        'motion-reduce:animate-none',
        className,
      )}
      {...props}
    />
  </TooltipPrimitive.Portal>
));
TooltipContent.displayName = TooltipPrimitive.Content.displayName;

export { Tooltip, TooltipTrigger, TooltipContent, TooltipProvider };
