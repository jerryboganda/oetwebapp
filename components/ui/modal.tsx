'use client';

import { cn } from '@/lib/utils';
import { X } from 'lucide-react';
import { useEffect, type ReactNode } from 'react';

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

export function Modal({ open, onClose, title, children, className, size = 'md' }: ModalProps) {
  useEffect(() => {
    if (!open) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handler);
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', handler);
      document.body.style.overflow = '';
    };
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" role="dialog" aria-modal="true" aria-labelledby={title ? 'modal-title' : undefined} aria-label={title ? undefined : 'Dialog'}>
      <div className="fixed inset-0 bg-black/40" onClick={onClose} aria-hidden="true" />
      <div className={cn('relative bg-white rounded-lg shadow-xl w-full', sizeStyles[size], className)}>
        {title && (
          <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200">
            <h2 id="modal-title" className="text-lg font-bold text-navy">{title}</h2>
            <button onClick={onClose} className="p-1 text-muted hover:text-navy rounded transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary" aria-label="Close">
              <X className="w-5 h-5" />
            </button>
          </div>
        )}
        <div className="px-6 py-4">{children}</div>
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
  useEffect(() => {
    if (!open) return;
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handler);
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', handler);
      document.body.style.overflow = '';
    };
  }, [open, onClose]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50" role="dialog" aria-modal="true" aria-labelledby={title ? 'drawer-title' : undefined} aria-label={title ? undefined : 'Drawer'}>
      <div className="fixed inset-0 bg-black/40" onClick={onClose} aria-hidden="true" />
      <div
        className={cn(
          'fixed top-0 bottom-0 bg-white shadow-xl w-full max-w-md flex flex-col',
          side === 'right' ? 'right-0' : 'left-0',
          className,
        )}
      >
        {title && (
          <div className="flex items-center justify-between px-6 py-4 border-b border-gray-200 shrink-0">
            <h2 id="drawer-title" className="text-lg font-bold text-navy">{title}</h2>
            <button onClick={onClose} className="p-1 text-muted hover:text-navy rounded transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary" aria-label="Close">
              <X className="w-5 h-5" />
            </button>
          </div>
        )}
        <div className="flex-1 overflow-y-auto px-6 py-4">{children}</div>
      </div>
    </div>
  );
}
