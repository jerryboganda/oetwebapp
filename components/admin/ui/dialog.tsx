'use client';

/**
 * Admin Dialog — modal primitive for the OET admin design system.
 *
 * Wraps `@radix-ui/react-dialog` and applies admin design tokens
 * (`--admin-bg-elevated`, `--admin-border-default`, `--admin-fg-strong`,
 * `--admin-shadow-lg`, `--admin-radius-xl`).
 *
 * Behavior contract (per spec 17-MODAL-DRAWER-COMPLETE.md):
 *   - Focus trap, ARIA, body-scroll-lock, portal — Radix automatic
 *   - Esc closes (use `onEscapeKeyDown` to intercept for dirty-form warning)
 *   - Overlay click closes (use `onInteractOutside` to intercept)
 *   - Animations honor `prefers-reduced-motion` via `motion-reduce:` utilities
 *
 * Sizes:
 *   sm         320px   Confirms / simple alerts
 *   md (def)   480px   Standard forms (1-5 fields)
 *   lg         720px   Complex forms / tables
 *   xl         960px   Dashboards-in-modal
 *   fullscreen 100vw   Mobile / wizards
 */

import * as React from 'react';
import * as DialogPrimitive from '@radix-ui/react-dialog';
import { X } from 'lucide-react';
import { cva, type VariantProps } from 'class-variance-authority';

import { cn } from '@/lib/utils';

const Dialog = DialogPrimitive.Root;
const DialogTrigger = DialogPrimitive.Trigger;
const DialogPortal = DialogPrimitive.Portal;
const DialogClose = DialogPrimitive.Close;

const DialogOverlay = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Overlay>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Overlay>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Overlay
    ref={ref}
    className={cn(
      'fixed inset-0 z-[var(--admin-z-overlay)] bg-navy/50 backdrop-blur-sm',
      'data-[state=open]:animate-in data-[state=open]:fade-in-0',
      'data-[state=closed]:animate-out data-[state=closed]:fade-out-0',
      'data-[state=open]:duration-200 data-[state=closed]:duration-150',
      'motion-reduce:animate-none',
      className,
    )}
    {...props}
  />
));
DialogOverlay.displayName = DialogPrimitive.Overlay.displayName;

const dialogContentVariants = cva(
  [
    'fixed left-[50%] top-[50%] z-[var(--admin-z-modal)] grid w-full',
    'translate-x-[-50%] translate-y-[-50%] gap-4 p-6',
    'border border-[var(--admin-border-default)] bg-[var(--admin-bg-elevated)]',
    'shadow-[var(--admin-shadow-lg)] rounded-[var(--admin-radius-xl)]',
    'font-[var(--admin-font-body)] text-[var(--admin-fg-default)]',
    // Entrance / exit animations — 200ms ease-out per spec §2.6
    'data-[state=open]:animate-in data-[state=closed]:animate-out',
    'data-[state=open]:fade-in-0 data-[state=closed]:fade-out-0',
    'data-[state=open]:zoom-in-95 data-[state=closed]:zoom-out-95',
    'data-[state=open]:slide-in-from-bottom-2 data-[state=closed]:slide-out-to-bottom-2',
    'data-[state=open]:duration-200 data-[state=closed]:duration-150',
    'data-[state=open]:ease-[cubic-bezier(0.16,1,0.3,1)]',
    'motion-reduce:animate-none motion-reduce:zoom-in-100 motion-reduce:slide-in-from-bottom-0',
  ],
  {
    variants: {
      size: {
        sm: 'max-w-sm',
        md: 'max-w-lg',
        lg: 'max-w-2xl',
        xl: 'max-w-4xl',
        fullscreen: [
          'w-screen h-screen max-w-none',
          'left-0 top-0 translate-x-0 translate-y-0',
          'rounded-none border-0',
        ],
      },
    },
    defaultVariants: { size: 'md' },
  },
);

export interface DialogContentProps
  extends React.ComponentPropsWithoutRef<typeof DialogPrimitive.Content>,
    VariantProps<typeof dialogContentVariants> {
  /** Hide the built-in close button (use for AlertDialog or custom controls). */
  hideCloseButton?: boolean;
}

const DialogContent = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Content>,
  DialogContentProps
>(({ className, children, size, hideCloseButton = false, ...props }, ref) => (
  <DialogPortal>
    <DialogOverlay />
    <DialogPrimitive.Content
      ref={ref}
      className={cn(dialogContentVariants({ size }), className)}
      {...props}
    >
      {children}
      {!hideCloseButton && (
        <DialogPrimitive.Close
          className={cn(
            'absolute right-4 top-4 rounded-[var(--admin-radius-sm)]',
            'opacity-70 transition-opacity hover:opacity-100',
            'focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)]',
            'focus-visible:ring-offset-2 focus-visible:ring-offset-[var(--admin-bg-elevated)]',
            'disabled:pointer-events-none',
          )}
        >
          <X className="h-4 w-4" aria-hidden="true" />
          <span className="sr-only">Close</span>
        </DialogPrimitive.Close>
      )}
    </DialogPrimitive.Content>
  </DialogPortal>
));
DialogContent.displayName = DialogPrimitive.Content.displayName;

const DialogHeader = ({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) => (
  <div
    className={cn('flex flex-col space-y-1.5 text-left', className)}
    {...props}
  />
);
DialogHeader.displayName = 'DialogHeader';

const DialogFooter = ({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) => (
  <div
    className={cn(
      'flex flex-col-reverse gap-2 sm:flex-row sm:justify-end sm:space-x-2 sm:space-y-0',
      className,
    )}
    {...props}
  />
);
DialogFooter.displayName = 'DialogFooter';

const DialogTitle = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Title>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Title>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Title
    ref={ref}
    className={cn(
      'text-lg font-semibold leading-none tracking-tight text-[var(--admin-fg-strong)]',
      className,
    )}
    {...props}
  />
));
DialogTitle.displayName = DialogPrimitive.Title.displayName;

const DialogDescription = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Description>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Description>
>(({ className, ...props }, ref) => (
  <DialogPrimitive.Description
    ref={ref}
    className={cn('text-sm text-[var(--admin-fg-muted)]', className)}
    {...props}
  />
));
DialogDescription.displayName = DialogPrimitive.Description.displayName;

export {
  Dialog,
  DialogTrigger,
  DialogPortal,
  DialogClose,
  DialogOverlay,
  DialogContent,
  DialogHeader,
  DialogFooter,
  DialogTitle,
  DialogDescription,
};
