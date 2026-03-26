'use client';

import { cn } from '@/lib/utils';
import { forwardRef, type ButtonHTMLAttributes } from 'react';
import { Loader2 } from 'lucide-react';

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
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
  sm: 'px-3 py-1.5 text-xs',
  md: 'px-5 py-2 text-sm',
  lg: 'px-6 py-3 text-base',
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = 'primary', size = 'md', loading, fullWidth, disabled, children, ...props }, ref) => (
    <button
      ref={ref}
      disabled={disabled || loading}
      className={cn(
        'inline-flex items-center justify-center gap-2 font-medium rounded-lg transition-all duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:opacity-50 disabled:pointer-events-none active:scale-[0.98]',
        variantStyles[variant],
        sizeStyles[size],
        fullWidth && 'w-full',
        className,
      )}
      {...props}
    >
      {loading && <Loader2 className="w-4 h-4 animate-spin" />}
      {children}
    </button>
  ),
);
Button.displayName = 'Button';
