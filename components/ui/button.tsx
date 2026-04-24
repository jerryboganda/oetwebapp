'use client';

import { cn } from '@/lib/utils';
import { forwardRef, type ButtonHTMLAttributes } from 'react';
import { Loader2 } from 'lucide-react';
import { triggerImpactHaptic, type HapticImpactStyle } from '@/lib/mobile/haptics';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'ghost' | 'destructive' | 'outline';
  size?: 'sm' | 'md' | 'lg';
  loading?: boolean;
  fullWidth?: boolean;
}

const variantStyles: Record<string, string> = {
  primary: 'bg-primary text-white hover:bg-primary/90 shadow-sm',
  secondary: 'bg-navy text-white hover:bg-navy/90 shadow-sm',
  ghost: 'text-navy hover:bg-lavender/40 dark:hover:bg-white/5',
  destructive: 'bg-danger text-white hover:bg-danger/90 shadow-sm',
  outline: 'border border-border text-navy hover:bg-surface hover:border-border-hover',
};

const sizeStyles: Record<string, string> = {
  sm: 'min-h-11 px-3 py-2 text-xs',
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
    const isDisabled = disabled || loading;

    const handleClick: NonNullable<ButtonProps['onClick']> = (event) => {
      if (!isDisabled) {
        void triggerImpactHaptic(hapticStyles[variant]);
      }

      onClick?.(event);
    };

    return (
      <button
        ref={ref}
        disabled={isDisabled}
        aria-busy={loading || undefined}
        aria-disabled={isDisabled || undefined}
        className={cn(
          'inline-flex items-center justify-center gap-2 rounded-lg font-medium transition-[background-color,border-color,color,box-shadow,opacity,transform] duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50 motion-safe:hover:-translate-y-px motion-safe:active:translate-y-0 motion-safe:active:scale-[0.98]',
          variantStyles[variant],
          sizeStyles[size],
          fullWidth && 'w-full',
          className,
        )}
        onClick={handleClick}
        {...props}
      >
        {loading && (
          <Loader2 className="w-4 h-4 animate-spin motion-safe:animate-spin" aria-hidden="true" />
        )}
        {children}
      </button>
    );
  },
);
Button.displayName = 'Button';
