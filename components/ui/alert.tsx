'use client';

import { cn } from '@/lib/utils';
import { AlertCircle, CheckCircle2, Info, AlertTriangle, X } from 'lucide-react';
import { useState, useEffect, type ReactNode } from 'react';
import { triggerImpactHaptic } from '@/lib/mobile/haptics';

/* ─── Inline Alert ─── */
type AlertVariant = 'info' | 'success' | 'warning' | 'error';

const alertConfig: Record<AlertVariant, { icon: typeof Info; bgClass: string; textClass: string; borderClass: string }> = {
  info: { icon: Info, bgClass: 'bg-blue-50', textClass: 'text-blue-800', borderClass: 'border-blue-200' },
  success: { icon: CheckCircle2, bgClass: 'bg-emerald-50', textClass: 'text-emerald-800', borderClass: 'border-emerald-200' },
  warning: { icon: AlertTriangle, bgClass: 'bg-amber-50', textClass: 'text-amber-800', borderClass: 'border-amber-200' },
  error: { icon: AlertCircle, bgClass: 'bg-red-50', textClass: 'text-red-800', borderClass: 'border-red-200' },
};

interface InlineAlertProps {
  variant?: AlertVariant;
  title?: string;
  children: ReactNode;
  dismissible?: boolean;
  className?: string;
  action?: ReactNode;
}

export function InlineAlert({ variant = 'info', title, children, dismissible, className, action }: InlineAlertProps) {
  const [visible, setVisible] = useState(true);
  if (!visible) return null;

  const config = alertConfig[variant];
  const Icon = config.icon;

  return (
    <div
      role="alert"
      className={cn('flex items-start gap-3 rounded-[20px] border px-4 py-4 shadow-sm', config.bgClass, config.borderClass, className)}
    >
      <div className={cn('mt-0.5 flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-white/70', config.textClass)}>
        <Icon className="h-5 w-5" aria-hidden="true" />
      </div>
      <div className="flex-1 min-w-0">
        {title && <p className={cn('font-semibold text-sm', config.textClass)}>{title}</p>}
        <div className={cn('text-sm leading-6', config.textClass, title && 'mt-0.5')}>{children}</div>
        {action && <div className="mt-2">{action}</div>}
      </div>
      {dismissible && (
        <button
          type="button"
          onClick={() => {
            void triggerImpactHaptic('LIGHT');
            setVisible(false);
          }}
          className={cn('rounded-xl p-1', config.textClass, 'hover:bg-white/70')}
          aria-label="Dismiss"
        >
          <X className="w-4 h-4" />
        </button>
      )}
    </div>
  );
}

/* ─── Toast (simple notification) ─── */
interface ToastProps {
  variant?: AlertVariant;
  message: string;
  onClose?: () => void;
  className?: string;
}

export function Toast({ variant = 'info', message, onClose, className, duration = 5000 }: ToastProps & { duration?: number }) {
  const config = alertConfig[variant];
  const Icon = config.icon;

  useEffect(() => {
    if (!onClose || duration <= 0) return;
    const timer = setTimeout(onClose, duration);
    return () => clearTimeout(timer);
  }, [onClose, duration]);

  return (
    <div role="status" aria-live="polite" className={cn('fixed bottom-6 right-6 z-[100] flex items-center gap-3 rounded-[20px] border px-5 py-3.5 shadow-xl animate-in slide-in-from-bottom-4 fade-in duration-300', config.bgClass, config.borderClass, className)}>
      <div className={cn('flex h-10 w-10 items-center justify-center rounded-2xl bg-white/70', config.textClass)}>
        <Icon className="h-5 w-5" aria-hidden="true" />
      </div>
      <span className={cn('text-sm font-medium', config.textClass)}>{message}</span>
      {onClose && (
        <button
          type="button"
          onClick={() => {
            void triggerImpactHaptic('LIGHT');
            onClose();
          }}
          className={cn('rounded-xl p-1', config.textClass, 'hover:bg-white/70')}
          aria-label="Dismiss"
        >
          <X className="w-4 h-4" aria-hidden="true" />
        </button>
      )}
    </div>
  );
}
