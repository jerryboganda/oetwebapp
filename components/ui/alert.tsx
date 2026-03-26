'use client';

import { cn } from '@/lib/utils';
import { AlertCircle, CheckCircle2, Info, AlertTriangle, X } from 'lucide-react';
import { useState, type ReactNode } from 'react';

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
      className={cn('flex items-start gap-3 px-4 py-3 rounded border', config.bgClass, config.borderClass, className)}
    >
      <Icon className={cn('w-5 h-5 shrink-0 mt-0.5', config.textClass)} />
      <div className="flex-1 min-w-0">
        {title && <p className={cn('font-semibold text-sm', config.textClass)}>{title}</p>}
        <div className={cn('text-sm', config.textClass, title && 'mt-0.5')}>{children}</div>
        {action && <div className="mt-2">{action}</div>}
      </div>
      {dismissible && (
        <button onClick={() => setVisible(false)} className={cn('p-0.5 rounded', config.textClass, 'hover:opacity-70')} aria-label="Dismiss">
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

export function Toast({ variant = 'info', message, onClose, className }: ToastProps) {
  const config = alertConfig[variant];
  const Icon = config.icon;

  return (
    <div className={cn('fixed bottom-6 right-6 z-[100] flex items-center gap-3 px-5 py-3 rounded-lg shadow-lg border', config.bgClass, config.borderClass, className)}>
      <Icon className={cn('w-5 h-5', config.textClass)} />
      <span className={cn('text-sm font-medium', config.textClass)}>{message}</span>
      {onClose && (
        <button onClick={onClose} className={cn('p-0.5 rounded', config.textClass, 'hover:opacity-70')} aria-label="Dismiss">
          <X className="w-4 h-4" />
        </button>
      )}
    </div>
  );
}
