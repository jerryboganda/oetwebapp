'use client';

import { cn } from '@/lib/utils';
import { AlertCircle, CheckCircle2, Info, AlertTriangle, X } from 'lucide-react';
import { useState, useEffect, type ReactNode } from 'react';
import { AnimatePresence, motion, useReducedMotionConfig } from 'motion/react';
import { getCelebrateMotion, getSurfaceMotion, getMotionPresenceMode, prefersReducedMotion } from '@/lib/motion';
import { triggerImpactHaptic } from '@/lib/mobile/haptics';

/* ─── Inline Alert ─── */
type AlertVariant = 'info' | 'success' | 'warning' | 'error';

const alertConfig: Record<AlertVariant, { icon: typeof Info; bgClass: string; textClass: string; borderClass: string }> = {
  info: { icon: Info, bgClass: 'bg-info/10', textClass: 'text-info', borderClass: 'border-info/30' },
  success: { icon: CheckCircle2, bgClass: 'bg-success/10', textClass: 'text-success-strong', borderClass: 'border-success/30' },
  warning: { icon: AlertTriangle, bgClass: 'bg-warning/10', textClass: 'text-warning-strong', borderClass: 'border-warning/30' },
  error: { icon: AlertCircle, bgClass: 'bg-danger/10', textClass: 'text-danger-strong', borderClass: 'border-danger/30' },
};

interface InlineAlertProps extends Omit<React.HTMLAttributes<HTMLDivElement>, 'onDrag' | 'onDragEnd' | 'onDragStart' | 'onAnimationStart' | 'onAnimationEnd'> {
  variant?: AlertVariant;
  title?: string;
  children: ReactNode;
  dismissible?: boolean;
  className?: string;
  action?: ReactNode;
  /** `polite` announces as role="status" — use for non-urgent suggestions. Default `assertive` (role="alert"). */
  live?: 'assertive' | 'polite';
}

export function InlineAlert({ variant = 'info', title, children, dismissible, className, action, live = 'assertive', ...rest }: InlineAlertProps) {
  const [visible, setVisible] = useState(true);
  const reducedMotion = prefersReducedMotion(useReducedMotionConfig());
  const motionProps = getSurfaceMotion('item', reducedMotion);

  const config = alertConfig[variant];
  const Icon = config.icon;

  return (
    <AnimatePresence mode={getMotionPresenceMode(reducedMotion)}>
      {visible && (
        <motion.div
          role={live === 'polite' ? 'status' : 'alert'}
          {...rest}
          {...motionProps}
          className={cn('flex items-start gap-3 rounded-2xl border px-4 py-4 shadow-sm', config.bgClass, config.borderClass, className)}
        >
          <Icon className={cn('mt-0.5 h-5 w-5 shrink-0', config.textClass)} aria-hidden="true" />
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
              className={cn('rounded-xl p-2.5 -m-1 transition-colors', config.textClass, 'hover:bg-navy/5 dark:hover:bg-white/10')}
              aria-label="Dismiss"
            >
              <X className="w-4 h-4" aria-hidden="true" />
            </button>
          )}
        </motion.div>
      )}
    </AnimatePresence>
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
  const reducedMotion = prefersReducedMotion(useReducedMotionConfig());
  const celebrateProps = variant === 'success' ? getCelebrateMotion(reducedMotion) : getSurfaceMotion('overlay', reducedMotion);

  useEffect(() => {
    if (!onClose || duration <= 0) return;
    const timer = setTimeout(onClose, duration);
    return () => clearTimeout(timer);
  }, [onClose, duration]);

  return (
    <motion.div
      role="status"
      aria-live="polite"
      {...celebrateProps}
      className={cn('fixed bottom-[calc(var(--bottom-nav-height,6rem)+env(safe-area-inset-bottom)+0.75rem)] right-4 left-4 sm:left-auto sm:right-6 sm:bottom-6 sm:max-w-sm z-[100] flex items-center gap-3 rounded-2xl border px-5 py-3.5 shadow-xl', config.bgClass, config.borderClass, className)}
    >
      <Icon className={cn('h-5 w-5 shrink-0', config.textClass)} aria-hidden="true" />
      <span className={cn('text-sm font-medium', config.textClass)}>{message}</span>
      {onClose && (
        <button
          type="button"
          onClick={() => {
            void triggerImpactHaptic('LIGHT');
            onClose();
          }}
          className={cn('rounded-xl p-2.5 -m-1 transition-colors', config.textClass, 'hover:bg-navy/5 dark:hover:bg-white/10')}
          aria-label="Dismiss"
        >
          <X className="w-4 h-4" aria-hidden="true" />
        </button>
      )}
    </motion.div>
  );
}
