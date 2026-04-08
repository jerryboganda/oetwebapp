'use client';

import { cn } from '@/lib/utils';
import { forwardRef, type ButtonHTMLAttributes } from 'react';
import { Loader2 } from 'lucide-react';
import { motion, useReducedMotion, AnimatePresence } from 'motion/react';
import { getMicroHover, getMicroTap, motionTokens, prefersReducedMotion } from '@/lib/motion';
import { triggerImpactHaptic, type HapticImpactStyle } from '@/lib/mobile/haptics';

export interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'onDrag' | 'onDragEnd' | 'onDragStart' | 'onAnimationStart' | 'onAnimationEnd'> {
  variant?: 'primary' | 'secondary' | 'ghost' | 'destructive' | 'outline';
  size?: 'sm' | 'md' | 'lg';
  loading?: boolean;
  fullWidth?: boolean;
}

const variantStyles: Record<string, string> = {
  primary: 'bg-primary text-white hover:bg-primary/90 shadow-sm',
  secondary: 'bg-navy text-white hover:bg-navy/90 shadow-sm',
  ghost: 'text-navy hover:bg-gray-100',
  destructive: 'bg-red-600 text-white hover:bg-red-700 shadow-sm',
  outline: 'border border-gray-300 text-navy hover:bg-gray-50',
};

const sizeStyles: Record<string, string> = {
  sm: 'min-h-10 px-3 py-2 text-xs',
  md: 'min-h-11 px-5 py-2.5 text-sm',
  lg: 'min-h-12 px-6 py-3 text-base',
};

const hapticStyles: Record<NonNullable<ButtonProps['variant']>, HapticImpactStyle> = {
  primary: 'MEDIUM',
  secondary: 'MEDIUM',
  ghost: 'LIGHT',
  destructive: 'HEAVY',
  outline: 'LIGHT',
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = 'primary', size = 'md', loading, fullWidth, disabled, children, onClick, ...props }, ref) => {
    const reducedMotion = prefersReducedMotion(useReducedMotion());
    const isDisabled = disabled || loading;

    const handleClick: NonNullable<ButtonProps['onClick']> = (event) => {
      if (!isDisabled) {
        void triggerImpactHaptic(hapticStyles[variant]);
      }

      onClick?.(event);
    };

    return (
      <motion.button
        ref={ref}
        disabled={isDisabled}
        aria-busy={loading || undefined}
        aria-disabled={isDisabled || undefined}
        whileHover={isDisabled ? undefined : getMicroHover(reducedMotion)}
        whileTap={isDisabled ? undefined : getMicroTap(reducedMotion)}
        className={cn(
          'inline-flex items-center justify-center gap-2 rounded-lg font-medium transition-[background-color,border-color,color,box-shadow,opacity] duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50',
          variantStyles[variant],
          sizeStyles[size],
          fullWidth && 'w-full',
          className,
        )}
        onClick={handleClick}
        {...props}
      >
        <AnimatePresence mode="wait" initial={false}>
          {loading && (
            <motion.span
              key="loader"
              initial={{ opacity: 0, scale: 0.6, width: 0 }}
              animate={{ opacity: 1, scale: 1, width: 'auto' }}
              exit={{ opacity: 0, scale: 0.6, width: 0 }}
              transition={{ duration: motionTokens.duration.fast, ease: motionTokens.ease.standard }}
              className="inline-flex"
            >
              <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" />
            </motion.span>
          )}
        </AnimatePresence>
        {children}
      </motion.button>
    );
  },
);
Button.displayName = 'Button';
