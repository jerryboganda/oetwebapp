'use client';

import { getMotionPresenceMode, getSurfaceMotion, getSurfaceTransition, prefersReducedMotion } from '@/lib/motion';
import { triggerImpactHaptic } from '@/lib/mobile/haptics';
import { cn } from '@/lib/utils';
import { AnimatePresence, motion, useReducedMotion } from 'motion/react';
import { X } from 'lucide-react';
import { useCallback, useEffect, useMemo, useRef, type ReactNode } from 'react';

interface ModalProps {
  open: boolean;
  onClose: () => void;
  title?: string;
  children: ReactNode;
  className?: string;
  size?: 'sm' | 'md' | 'lg';
}

const sizeStyles: Record<string, string> = {
  sm: 'max-w-[calc(100vw-1.5rem)] sm:max-w-sm',
  md: 'max-w-[calc(100vw-1.5rem)] sm:max-w-lg',
  lg: 'max-w-[calc(100vw-1.5rem)] sm:max-w-2xl',
};

type FocusRestoreDescriptor = {
  id?: string;
  tagName: string;
  ariaLabel?: string;
  name?: string;
  text?: string;
};

function describeFocusTarget(element: HTMLElement | null): FocusRestoreDescriptor | null {
  if (!element) return null;
  return {
    id: element.id || undefined,
    tagName: element.tagName.toLowerCase(),
    ariaLabel: element.getAttribute('aria-label') || undefined,
    name: element.getAttribute('name') || undefined,
    text: element.textContent?.trim() || undefined,
  };
}

function resolveFocusTarget(element: HTMLElement | null, descriptor: FocusRestoreDescriptor | null) {
  if (element && element.isConnected) {
    return element;
  }

  if (!descriptor) {
    return null;
  }

  if (descriptor.id) {
    const byId = document.getElementById(descriptor.id);
    if (byId instanceof HTMLElement) {
      return byId;
    }
  }

  const candidates = Array.from(document.querySelectorAll<HTMLElement>(descriptor.tagName));

  if (descriptor.ariaLabel) {
    const byAria = candidates.find((candidate) => candidate.getAttribute('aria-label') === descriptor.ariaLabel);
    if (byAria) {
      return byAria;
    }
  }

  if (descriptor.name) {
    const byName = candidates.find((candidate) => candidate.getAttribute('name') === descriptor.name);
    if (byName) {
      return byName;
    }
  }

  if (descriptor.text) {
    const byText = candidates.find((candidate) => candidate.textContent?.trim() === descriptor.text);
    if (byText) {
      return byText;
    }
  }

  return null;
}

function queueFocusRestore(element: HTMLElement | null, descriptor: FocusRestoreDescriptor | null) {
  let attempts = 0;

  const tryRestoreFocus = () => {
    const restoreTarget = resolveFocusTarget(element, descriptor);
    if (!restoreTarget) {
      if (attempts < 6) {
        attempts += 1;
        window.setTimeout(tryRestoreFocus, 50);
      }
      return;
    }

    restoreTarget.focus();
    if (document.activeElement !== restoreTarget && attempts < 6) {
      attempts += 1;
      requestAnimationFrame(() => {
        restoreTarget.focus();
        if (document.activeElement !== restoreTarget) {
          window.setTimeout(tryRestoreFocus, 50);
        }
      });
    }
  };

  window.setTimeout(tryRestoreFocus, 50);
}

function getOverlayBackdropMotion(reducedMotion: boolean) {
  return reducedMotion
    ? { initial: false, animate: { opacity: 1 }, exit: { opacity: 0 }, transition: { duration: 0.12 } }
    : {
        initial: { opacity: 0 },
        animate: { opacity: 1 },
        exit: { opacity: 0 },
        transition: { duration: 0.18 },
      };
}

function OverlayCloseButton({ onClose }: { onClose: () => void }) {
  return (
    <button
      type="button"
      onClick={() => {
        void triggerImpactHaptic('LIGHT');
        onClose();
      }}
      className="touch-target rounded-xl p-2.5 text-muted transition-colors hover:bg-background-light hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
      aria-label="Close"
    >
      <X className="h-5 w-5" aria-hidden="true" />
    </button>
  );
}

export function Modal({ open, onClose, title, children, className, size = 'md' }: ModalProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const restoreFocusRef = useRef<HTMLElement | null>(null);
  const restoreFocusDescriptorRef = useRef<FocusRestoreDescriptor | null>(null);
  const wasOpenRef = useRef(false);
  const shouldRestoreFocusRef = useRef(false);
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const panelMotion = getSurfaceMotion('overlay', reducedMotion);
  const backdropMotion = getOverlayBackdropMotion(reducedMotion);
  const presenceMode = getMotionPresenceMode(reducedMotion);

  const trapFocus = useCallback(
    (event: KeyboardEvent) => {
      if (event.key !== 'Tab' || !dialogRef.current) return;
      const focusable = dialogRef.current.querySelectorAll<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
      );
      if (focusable.length === 0) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey) {
        if (document.activeElement === first) {
          event.preventDefault();
          last.focus();
        }
      } else if (document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    },
    [],
  );

  // Hold the latest onClose in a ref so the open/close effect below depends only
  // on `open`. Without this, parents that pass an inline `onClose={() => ...}`
  // create a new function reference on every render — every keystroke in a
  // child input would re-run the effect and steal focus back to the close button.
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    onCloseRef.current = onClose;
  }, [onClose]);

  useEffect(() => {
    if (!open) return;
    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    restoreFocusDescriptorRef.current = describeFocusTarget(restoreFocusRef.current);
    const handler = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onCloseRef.current();
      trapFocus(event);
    };
    document.addEventListener('keydown', handler);
    document.body.style.overflow = 'hidden';
    requestAnimationFrame(() => {
      const first = dialogRef.current?.querySelector<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
      );
      first?.focus();
    });
    return () => {
      document.removeEventListener('keydown', handler);
      document.body.style.overflow = '';
    };
  }, [open, trapFocus]);

  useEffect(() => {
    if (!open && wasOpenRef.current) {
      shouldRestoreFocusRef.current = true;
    }
    wasOpenRef.current = open;
  }, [open]);

  const handleExitComplete = useCallback(() => {
    if (!shouldRestoreFocusRef.current) {
      return;
    }

    shouldRestoreFocusRef.current = false;
    queueFocusRestore(restoreFocusRef.current, restoreFocusDescriptorRef.current);
  }, []);

  return (
    <AnimatePresence initial={false} mode={presenceMode} onExitComplete={handleExitComplete}>
      {open ? (
        <div
          className="fixed inset-0 z-50 flex items-start justify-center p-2 sm:items-center sm:p-4"
          role="dialog"
          aria-modal="true"
          aria-labelledby={title ? 'modal-title' : undefined}
          aria-label={title ? undefined : 'Dialog'}
        >
          <motion.div
            className="fixed inset-0 bg-navy/25 backdrop-blur-[2px]"
            onClick={() => {
              void triggerImpactHaptic('LIGHT');
              onClose();
            }}
            aria-hidden="true"
            {...backdropMotion}
          />
          <motion.div
            ref={dialogRef}
            className={cn(
              'relative flex max-h-[calc(100dvh-1rem)] w-full flex-col overflow-hidden rounded-[28px] border border-gray-200 bg-surface shadow-2xl sm:max-h-[calc(100dvh-1.5rem)]',
              sizeStyles[size],
              className,
            )}
            {...panelMotion}
          >
            {title && (
              <div className="flex items-center justify-between border-b border-gray-200 px-4 py-4 sm:px-6 sm:py-5">
                <h2 id="modal-title" className="text-lg font-bold text-navy">
                  {title}
                </h2>
                <OverlayCloseButton onClose={onClose} />
              </div>
            )}
            <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4 sm:px-6 sm:py-5">{children}</div>
          </motion.div>
        </div>
      ) : null}
    </AnimatePresence>
  );
}

interface DrawerProps {
  open: boolean;
  onClose: () => void;
  title?: string;
  children: ReactNode;
  side?: 'right' | 'left';
  className?: string;
}

export function Drawer({ open, onClose, title, children, side = 'right', className }: DrawerProps) {
  const drawerRef = useRef<HTMLDivElement>(null);
  const restoreFocusRef = useRef<HTMLElement | null>(null);
  const restoreFocusDescriptorRef = useRef<FocusRestoreDescriptor | null>(null);
  const wasOpenRef = useRef(false);
  const shouldRestoreFocusRef = useRef(false);
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const backdropMotion = getOverlayBackdropMotion(reducedMotion);
  const presenceMode = getMotionPresenceMode(reducedMotion);
  const drawerMotion = useMemo(
    () =>
      reducedMotion
        ? {
            initial: false,
            animate: { opacity: 1 },
            exit: { opacity: 0 },
            transition: { duration: 0.12 },
          }
        : {
            initial: { opacity: 0, x: side === 'right' ? 24 : -24 },
            animate: { opacity: 1, x: 0 },
            exit: { opacity: 0, x: side === 'right' ? 24 : -24 },
            transition: getSurfaceTransition('overlay', reducedMotion),
          },
    [reducedMotion, side],
  );

  const trapFocus = useCallback(
    (event: KeyboardEvent) => {
      if (event.key !== 'Tab' || !drawerRef.current) return;
      const focusable = drawerRef.current.querySelectorAll<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
      );
      if (focusable.length === 0) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey) {
        if (document.activeElement === first) {
          event.preventDefault();
          last.focus();
        }
      } else if (document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    },
    [],
  );

  useEffect(() => {
    if (!open) return;
    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    restoreFocusDescriptorRef.current = describeFocusTarget(restoreFocusRef.current);
    const handler = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
      trapFocus(event);
    };
    document.addEventListener('keydown', handler);
    document.body.style.overflow = 'hidden';
    requestAnimationFrame(() => {
      const first = drawerRef.current?.querySelector<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])',
      );
      first?.focus();
    });
    return () => {
      document.removeEventListener('keydown', handler);
      document.body.style.overflow = '';
    };
  }, [open, onClose, trapFocus]);

  useEffect(() => {
    if (!open && wasOpenRef.current) {
      shouldRestoreFocusRef.current = true;
    }
    wasOpenRef.current = open;
  }, [open]);

  const handleExitComplete = useCallback(() => {
    if (!shouldRestoreFocusRef.current) {
      return;
    }

    shouldRestoreFocusRef.current = false;
    queueFocusRestore(restoreFocusRef.current, restoreFocusDescriptorRef.current);
  }, []);

  return (
    <AnimatePresence initial={false} mode={presenceMode} onExitComplete={handleExitComplete}>
      {open ? (
        <div
          className="fixed inset-0 z-50"
          role="dialog"
          aria-modal="true"
          aria-labelledby={title ? 'drawer-title' : undefined}
          aria-label={title ? undefined : 'Drawer'}
        >
          <motion.div
            className="fixed inset-0 bg-navy/25 backdrop-blur-[2px]"
            onClick={() => {
              void triggerImpactHaptic('LIGHT');
              onClose();
            }}
            aria-hidden="true"
            {...backdropMotion}
          />
          <motion.div
            ref={drawerRef}
            className={cn(
              'fixed bottom-0 top-0 flex w-full max-w-md flex-col border-l border-gray-200 bg-surface pb-[env(safe-area-inset-bottom)] pt-[env(safe-area-inset-top)] shadow-2xl',
              side === 'right' ? 'right-0' : 'left-0 border-l-0 border-r',
              className,
            )}
            {...drawerMotion}
          >
            {title && (
              <div className="flex shrink-0 items-center justify-between border-b border-gray-200 px-4 py-4 sm:px-6 sm:py-5">
                <h2 id="drawer-title" className="text-lg font-bold text-navy">
                  {title}
                </h2>
                <OverlayCloseButton onClose={onClose} />
              </div>
            )}
            <div className="min-h-0 flex-1 overflow-y-auto px-4 py-4 sm:px-6 sm:py-5">{children}</div>
          </motion.div>
        </div>
      ) : null}
    </AnimatePresence>
  );
}
