'use client';

/**
 * Admin Drawer — side drawer (desktop) + bottom sheet (mobile).
 *
 * Implementation:
 *  - Desktop (>=768px): Radix Dialog with `side` variants (left/right/top/bottom)
 *    for accessible focus trap, ARIA, body-scroll-lock, portal.
 *  - Mobile  (<768px) : Vaul `Drawer.Root` as an iOS-style bottom sheet with a
 *    drag handle, snap to max-h-[85vh], rounded top corners, drag-to-dismiss.
 *
 * Breakpoint detection is SSR-safe: defaults to desktop on first render and
 * hydrates to the actual viewport via `window.matchMedia('(max-width: 767px)')`.
 *
 * Public API (Drawer, DrawerTrigger, DrawerContent, DrawerHeader, DrawerFooter,
 * DrawerTitle, DrawerDescription, DrawerClose, DrawerOverlay, DrawerPortal)
 * is preserved so consumers don't need to change anything when the surface
 * swaps under the hood.
 *
 * Behavior contract:
 *   - Esc closes, overlay click closes (both surfaces)
 *   - Slide-in from edge per `side` prop on desktop, 300ms iOS easing
 *   - Mobile path always slides from bottom regardless of `side`
 *   - Honors `prefers-reduced-motion` via `motion-reduce:` utilities
 */

import * as React from 'react';
import * as DialogPrimitive from '@radix-ui/react-dialog';
import { Drawer as VaulDrawer } from 'vaul';
import { X } from 'lucide-react';
import { cva, type VariantProps } from 'class-variance-authority';

import { cn } from '@/lib/utils';

/* ─── Breakpoint detection (SSR-safe) ────────────────────────────────── */

const MOBILE_QUERY = '(max-width: 767px)';

function useIsMobile(): boolean {
  // Hydrate desktop-first to keep SSR markup deterministic. Mobile flips on
  // first effect tick, before paint, so no visible flash.
  const [isMobile, setIsMobile] = React.useState(false);

  React.useEffect(() => {
    if (typeof window === 'undefined' || !window.matchMedia) return;

    const mql = window.matchMedia(MOBILE_QUERY);
    const apply = () => setIsMobile(mql.matches);
    apply();

    // Safari < 14 only supports addListener / removeListener.
    if (typeof mql.addEventListener === 'function') {
      mql.addEventListener('change', apply);
      return () => mql.removeEventListener('change', apply);
    }
    mql.addListener(apply);
    return () => mql.removeListener(apply);
  }, []);

  return isMobile;
}

/* ─── Shared surface (passed to whichever primitive is active) ───────── */

type DrawerSurface = 'radix' | 'vaul';

const DrawerSurfaceContext = React.createContext<DrawerSurface>('radix');
const useDrawerSurface = () => React.useContext(DrawerSurfaceContext);

/* ─── Root + Trigger + Close (surface-aware) ─────────────────────────── */

type DrawerRootProps = React.ComponentProps<typeof DialogPrimitive.Root> & {
  /** Forwarded to Vaul on mobile. Ignored on desktop. */
  shouldScaleBackground?: boolean;
};

function Drawer({ shouldScaleBackground = true, ...props }: DrawerRootProps) {
  const isMobile = useIsMobile();
  const surface: DrawerSurface = isMobile ? 'vaul' : 'radix';

  return (
    <DrawerSurfaceContext.Provider value={surface}>
      {surface === 'vaul' ? (
        <VaulDrawer.Root
          shouldScaleBackground={shouldScaleBackground}
          {...(props as React.ComponentProps<typeof VaulDrawer.Root>)}
        />
      ) : (
        <DialogPrimitive.Root {...props} />
      )}
    </DrawerSurfaceContext.Provider>
  );
}
Drawer.displayName = 'Drawer';

const DrawerTrigger = React.forwardRef<
  HTMLButtonElement,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Trigger>
>((props, ref) => {
  const surface = useDrawerSurface();
  return surface === 'vaul' ? (
    <VaulDrawer.Trigger ref={ref} {...(props as React.ComponentPropsWithoutRef<typeof VaulDrawer.Trigger>)} />
  ) : (
    <DialogPrimitive.Trigger ref={ref} {...props} />
  );
});
DrawerTrigger.displayName = 'DrawerTrigger';

const DrawerClose = React.forwardRef<
  HTMLButtonElement,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Close>
>((props, ref) => {
  const surface = useDrawerSurface();
  return surface === 'vaul' ? (
    <VaulDrawer.Close ref={ref} {...(props as React.ComponentPropsWithoutRef<typeof VaulDrawer.Close>)} />
  ) : (
    <DialogPrimitive.Close ref={ref} {...props} />
  );
});
DrawerClose.displayName = 'DrawerClose';

const DrawerPortal = ({ children, ...props }: React.ComponentProps<typeof DialogPrimitive.Portal>) => {
  const surface = useDrawerSurface();
  return surface === 'vaul' ? (
    <VaulDrawer.Portal {...(props as React.ComponentProps<typeof VaulDrawer.Portal>)}>
      {children}
    </VaulDrawer.Portal>
  ) : (
    <DialogPrimitive.Portal {...props}>{children}</DialogPrimitive.Portal>
  );
};
DrawerPortal.displayName = 'DrawerPortal';

/* ─── Overlay ────────────────────────────────────────────────────────── */

const DrawerOverlay = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Overlay>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Overlay>
>(({ className, ...props }, ref) => {
  const surface = useDrawerSurface();
  const sharedClass = cn(
    'fixed inset-0 z-[var(--admin-z-overlay)] bg-navy/50 backdrop-blur-sm',
    'data-[state=open]:animate-in data-[state=open]:fade-in-0',
    'data-[state=closed]:animate-out data-[state=closed]:fade-out-0',
    'data-[state=open]:duration-200 data-[state=closed]:duration-150',
    'motion-reduce:animate-none',
    className,
  );

  if (surface === 'vaul') {
    return (
      <VaulDrawer.Overlay
        ref={ref as React.Ref<HTMLDivElement>}
        className={sharedClass}
        {...(props as React.ComponentPropsWithoutRef<typeof VaulDrawer.Overlay>)}
      />
    );
  }
  return <DialogPrimitive.Overlay ref={ref} className={sharedClass} {...props} />;
});
DrawerOverlay.displayName = 'DrawerOverlay';

/* ─── Side / size variants (desktop only) ────────────────────────────── */

const sideBase = {
  left: [
    'inset-y-0 left-0 h-full border-r',
    'data-[state=open]:slide-in-from-left',
    'data-[state=closed]:slide-out-to-left',
  ],
  right: [
    'inset-y-0 right-0 h-full border-l',
    'data-[state=open]:slide-in-from-right',
    'data-[state=closed]:slide-out-to-right',
  ],
  top: [
    'inset-x-0 top-0 w-full border-b',
    'data-[state=open]:slide-in-from-top',
    'data-[state=closed]:slide-out-to-top',
  ],
  bottom: [
    'inset-x-0 bottom-0 w-full border-t rounded-t-[var(--admin-radius-xl)]',
    'data-[state=open]:slide-in-from-bottom',
    'data-[state=closed]:slide-out-to-bottom',
  ],
} as const;

const drawerContentVariants = cva(
  [
    'fixed z-[var(--admin-z-modal)] flex flex-col gap-4 p-6',
    'border-[var(--admin-border-default)] bg-[var(--admin-bg-elevated)]',
    'shadow-[var(--admin-shadow-lg)]',
    'font-[var(--admin-font-body)] text-[var(--admin-fg-default)]',
    'data-[state=open]:animate-in data-[state=closed]:animate-out',
    'data-[state=open]:duration-300 data-[state=closed]:duration-250',
    'data-[state=open]:ease-[cubic-bezier(0.32,0.72,0,1)]',
    'motion-reduce:animate-none',
  ],
  {
    variants: {
      side: {
        left: sideBase.left,
        right: sideBase.right,
        top: sideBase.top,
        bottom: sideBase.bottom,
      },
      size: {
        sm: '',
        md: '',
        lg: '',
        xl: '',
      },
    },
    compoundVariants: [
      { side: 'left', size: 'sm', class: 'w-[320px] max-w-full' },
      { side: 'left', size: 'md', class: 'w-[400px] max-w-full' },
      { side: 'left', size: 'lg', class: 'w-[560px] max-w-full' },
      { side: 'left', size: 'xl', class: 'w-[720px] max-w-full' },
      { side: 'right', size: 'sm', class: 'w-[320px] max-w-full' },
      { side: 'right', size: 'md', class: 'w-[400px] max-w-full' },
      { side: 'right', size: 'lg', class: 'w-[560px] max-w-full' },
      { side: 'right', size: 'xl', class: 'w-[720px] max-w-full' },
      { side: 'top', size: 'sm', class: 'h-[240px] max-h-full' },
      { side: 'top', size: 'md', class: 'h-[320px] max-h-full' },
      { side: 'top', size: 'lg', class: 'h-[440px] max-h-full' },
      { side: 'top', size: 'xl', class: 'h-[560px] max-h-full' },
      { side: 'bottom', size: 'sm', class: 'h-[240px] max-h-[85vh]' },
      { side: 'bottom', size: 'md', class: 'h-[320px] max-h-[85vh]' },
      { side: 'bottom', size: 'lg', class: 'h-[440px] max-h-[85vh]' },
      { side: 'bottom', size: 'xl', class: 'h-[560px] max-h-[85vh]' },
    ],
    defaultVariants: { side: 'right', size: 'md' },
  },
);

export interface DrawerContentProps
  extends React.ComponentPropsWithoutRef<typeof DialogPrimitive.Content>,
    VariantProps<typeof drawerContentVariants> {
  /** Hide the built-in close button. */
  hideCloseButton?: boolean;
}

const DrawerContent = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Content>,
  DrawerContentProps
>(
  (
    { className, children, side = 'right', size = 'md', hideCloseButton = false, ...props },
    ref,
  ) => {
    const surface = useDrawerSurface();

    if (surface === 'vaul') {
      // Mobile: bottom sheet. `side` and `size` props are accepted for API
      // compatibility but mobile always renders as a draggable bottom sheet.
      return (
        <DrawerPortal>
          <DrawerOverlay />
          <VaulDrawer.Content
            ref={ref as React.Ref<HTMLDivElement>}
            className={cn(
              'fixed inset-x-0 bottom-0 z-[var(--admin-z-modal)] mt-24 flex flex-col',
              'max-h-[85vh] gap-4 p-6 pt-3',
              'rounded-t-[var(--admin-radius-xl)] border-t border-[var(--admin-border-default)]',
              'bg-[var(--admin-bg-elevated)] shadow-[var(--admin-shadow-lg)]',
              'font-[var(--admin-font-body)] text-[var(--admin-fg-default)]',
              className,
            )}
            {...(props as React.ComponentPropsWithoutRef<typeof VaulDrawer.Content>)}
          >
            {/* Vaul drag handle — interactive, not just decorative. */}
            <div
              aria-hidden="true"
              className="mx-auto mb-2 h-1.5 w-12 shrink-0 rounded-full bg-[var(--admin-border-strong)]"
            />
            {children}
            {!hideCloseButton && (
              <VaulDrawer.Close
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
              </VaulDrawer.Close>
            )}
          </VaulDrawer.Content>
        </DrawerPortal>
      );
    }

    // Desktop: Radix Dialog with side variants.
    return (
      <DrawerPortal>
        <DrawerOverlay />
        <DialogPrimitive.Content
          ref={ref}
          className={cn(drawerContentVariants({ side, size }), className)}
          {...props}
        >
          {side === 'bottom' && (
            <div
              aria-hidden="true"
              className="mx-auto -mt-2 mb-2 h-1.5 w-12 shrink-0 rounded-full bg-[var(--admin-border-strong)]"
            />
          )}
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
      </DrawerPortal>
    );
  },
);
DrawerContent.displayName = 'DrawerContent';

/* ─── Header / Footer (surface-agnostic, just layout) ────────────────── */

const DrawerHeader = ({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) => (
  <div
    className={cn('flex flex-col space-y-1.5 text-left', className)}
    {...props}
  />
);
DrawerHeader.displayName = 'DrawerHeader';

const DrawerFooter = ({
  className,
  ...props
}: React.HTMLAttributes<HTMLDivElement>) => (
  <div
    className={cn(
      'mt-auto flex flex-col-reverse gap-2 sm:flex-row sm:justify-end sm:space-x-2 sm:space-y-0',
      className,
    )}
    {...props}
  />
);
DrawerFooter.displayName = 'DrawerFooter';

/* ─── Title / Description (surface-aware so Vaul reads them as labels) ─ */

const DrawerTitle = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Title>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Title>
>(({ className, ...props }, ref) => {
  const surface = useDrawerSurface();
  const sharedClass = cn(
    'text-lg font-semibold leading-none tracking-tight text-[var(--admin-fg-strong)]',
    className,
  );
  if (surface === 'vaul') {
    return (
      <VaulDrawer.Title
        ref={ref as React.Ref<HTMLHeadingElement>}
        className={sharedClass}
        {...(props as React.ComponentPropsWithoutRef<typeof VaulDrawer.Title>)}
      />
    );
  }
  return <DialogPrimitive.Title ref={ref} className={sharedClass} {...props} />;
});
DrawerTitle.displayName = 'DrawerTitle';

const DrawerDescription = React.forwardRef<
  React.ElementRef<typeof DialogPrimitive.Description>,
  React.ComponentPropsWithoutRef<typeof DialogPrimitive.Description>
>(({ className, ...props }, ref) => {
  const surface = useDrawerSurface();
  const sharedClass = cn('text-sm text-[var(--admin-fg-muted)]', className);
  if (surface === 'vaul') {
    return (
      <VaulDrawer.Description
        ref={ref as React.Ref<HTMLParagraphElement>}
        className={sharedClass}
        {...(props as React.ComponentPropsWithoutRef<typeof VaulDrawer.Description>)}
      />
    );
  }
  return <DialogPrimitive.Description ref={ref} className={sharedClass} {...props} />;
});
DrawerDescription.displayName = 'DrawerDescription';

export {
  Drawer,
  DrawerTrigger,
  DrawerPortal,
  DrawerClose,
  DrawerOverlay,
  DrawerContent,
  DrawerHeader,
  DrawerFooter,
  DrawerTitle,
  DrawerDescription,
};
