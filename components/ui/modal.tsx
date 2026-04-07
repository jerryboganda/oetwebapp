'use client';

import { cn } from '@/lib/utils';
import { X } from 'lucide-react';
import { useEffect, useRef, useCallback, type ReactNode } from 'react';
import { triggerImpactHaptic } from '@/lib/mobile/haptics';

/* ─── Modal ─── */
interface ModalProps {
  open: boolean;
  onClose: () => void;
  title?: string;
  children: ReactNode;
  className?: string;
  size?: 'sm' | 'md' | 'lg';
}

const sizeStyles: Record<string, string> = {
  sm: 'max-w-sm',
  md: 'max-w-lg',
  lg: 'max-w-2xl',
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

export function Modal({ open, onClose, title, children, className, size = 'md' }: ModalProps) {
  const dialogRef = useRef<HTMLDivElement>(null);
  const restoreFocusRef = useRef<HTMLElement | null>(null);
  const restoreFocusDescriptorRef = useRef<FocusRestoreDescriptor | null>(null);
  const wasOpenRef = useRef(false);

  const trapFocus = useCallback((e: KeyboardEvent) => {
    if (e.key !== 'Tab' || !dialogRef.current) return;
    const focusable = dialogRef.current.querySelectorAll<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
    );
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (e.shiftKey) {
      if (document.activeElement === first) { e.preventDefault(); last.focus(); }
    } else {
      if (document.activeElement === last) { e.preventDefault(); first.focus(); }
    }
  }, []);

  useEffect(() => {
    if (!open) return;
    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    restoreFocusDescriptorRef.current = describeFocusTarget(restoreFocusRef.current);
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
      trapFocus(e);
    };
    document.addEventListener('keydown', handler);
    document.body.style.overflow = 'hidden';
    // Auto-focus first focusable element
    requestAnimationFrame(() => {
      const first = dialogRef.current?.querySelector<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
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
      queueFocusRestore(restoreFocusRef.current, restoreFocusDescriptorRef.current);
    }

    wasOpenRef.current = open;
  }, [open]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" role="dialog" aria-modal="true" aria-labelledby={title ? 'modal-title' : undefined} aria-label={title ? undefined : 'Dialog'}>
      <div
        className="fixed inset-0 bg-navy/25 backdrop-blur-[2px]"
        onClick={() => {
          void triggerImpactHaptic('LIGHT');
          onClose();
        }}
        aria-hidden="true"
      />
      <div
        ref={dialogRef}
        className={cn(
          'relative w-full animate-in fade-in zoom-in-95 rounded-[28px] border border-gray-200 bg-surface shadow-2xl duration-200',
          sizeStyles[size],
          className,
        )}
      >
        {title && (
          <div className="flex items-center justify-between border-b border-gray-200 px-6 py-5">
            <h2 id="modal-title" className="text-lg font-bold text-navy">{title}</h2>
            <button
              type="button"
              onClick={() => {
                void triggerImpactHaptic('LIGHT');
                onClose();
              }}
              className="rounded-xl p-2 text-muted transition-colors hover:bg-background-light hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              aria-label="Close"
            >
              <X className="w-5 h-5" aria-hidden="true" />
            </button>
          </div>
        )}
        <div className="px-6 py-5">{children}</div>
      </div>
    </div>
  );
}

/* ─── Drawer ─── */
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

  const trapFocus = useCallback((e: KeyboardEvent) => {
    if (e.key !== 'Tab' || !drawerRef.current) return;
    const focusable = drawerRef.current.querySelectorAll<HTMLElement>(
      'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
    );
    if (focusable.length === 0) return;
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (e.shiftKey) {
      if (document.activeElement === first) { e.preventDefault(); last.focus(); }
    } else {
      if (document.activeElement === last) { e.preventDefault(); first.focus(); }
    }
  }, []);

  useEffect(() => {
    if (!open) return;
    restoreFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    restoreFocusDescriptorRef.current = describeFocusTarget(restoreFocusRef.current);
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
      trapFocus(e);
    };
    document.addEventListener('keydown', handler);
    document.body.style.overflow = 'hidden';
    requestAnimationFrame(() => {
      const first = drawerRef.current?.querySelector<HTMLElement>(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
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
      queueFocusRestore(restoreFocusRef.current, restoreFocusDescriptorRef.current);
    }

    wasOpenRef.current = open;
  }, [open]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50" role="dialog" aria-modal="true" aria-labelledby={title ? 'drawer-title' : undefined} aria-label={title ? undefined : 'Drawer'}>
      <div
        className="fixed inset-0 bg-navy/25 backdrop-blur-[2px] animate-in fade-in duration-200"
        onClick={() => {
          void triggerImpactHaptic('LIGHT');
          onClose();
        }}
        aria-hidden="true"
      />
      <div
        ref={drawerRef}
        className={cn(
          'fixed top-0 bottom-0 flex w-full max-w-md flex-col border-l border-gray-200 bg-surface shadow-2xl animate-in duration-300',
          side === 'right' ? 'right-0 slide-in-from-right' : 'left-0 slide-in-from-left',
          className,
        )}
      >
        {title && (
          <div className="flex shrink-0 items-center justify-between border-b border-gray-200 px-6 py-5">
            <h2 id="drawer-title" className="text-lg font-bold text-navy">{title}</h2>
            <button
              type="button"
              onClick={() => {
                void triggerImpactHaptic('LIGHT');
                onClose();
              }}
              className="rounded-xl p-2 text-muted transition-colors hover:bg-background-light hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              aria-label="Close"
            >
              <X className="w-5 h-5" aria-hidden="true" />
            </button>
          </div>
        )}
        <div className="flex-1 overflow-y-auto px-6 py-5">{children}</div>
      </div>
    </div>
  );
}
