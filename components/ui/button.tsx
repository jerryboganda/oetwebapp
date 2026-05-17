'use client';

import { cn } from '@/lib/utils';
import {
  Children,
  cloneElement,
  forwardRef,
  isValidElement,
  type ButtonHTMLAttributes,
  type MouseEvent,
  type ReactElement,
  type ReactNode,
} from 'react';
import { Loader2 } from 'lucide-react';
import { motion, useReducedMotion, AnimatePresence } from 'motion/react';
import { getMicroHover, getMicroTap, motionTokens, prefersReducedMotion } from '@/lib/motion';
import { triggerImpactHaptic, type HapticImpactStyle } from '@/lib/mobile/haptics';

export interface ButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'onDrag' | 'onDragEnd' | 'onDragStart' | 'onAnimationStart' | 'onAnimationEnd'> {
  variant?: 'primary' | 'secondary' | 'ghost' | 'destructive' | 'outline';
  size?: 'sm' | 'md' | 'lg';
  loading?: boolean;
  fullWidth?: boolean;
  asChild?: boolean;
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

type ButtonClassNameOptions = {
  variant?: NonNullable<ButtonProps['variant']>;
  size?: NonNullable<ButtonProps['size']>;
  fullWidth?: boolean;
  className?: string;
};

type AsChildProps = Record<string, unknown> & {
  className?: string;
  onClick?: (event: MouseEvent<HTMLElement>) => void;
  tabIndex?: number;
  children?: ReactNode;
  'aria-disabled'?: boolean;
};

export function buttonClassName({ variant = 'primary', size = 'md', fullWidth, className }: ButtonClassNameOptions = {}) {
  return cn(
    'inline-flex items-center justify-center gap-2 rounded-lg font-medium transition-[background-color,border-color,color,box-shadow,opacity] duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 disabled:pointer-events-none disabled:opacity-50',
    variantStyles[variant],
    sizeStyles[size],
    fullWidth && 'w-full',
    className,
  );
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = 'primary', size = 'md', loading, fullWidth, disabled, asChild, children, onClick, ...props }, ref) => {
    const reducedMotion = prefersReducedMotion(useReducedMotion());
    const isDisabled = disabled || loading;
    const classes = buttonClassName({ variant, size, fullWidth, className });

    const handleClick: NonNullable<ButtonProps['onClick']> = (event) => {
      if (!isDisabled) {
        void triggerImpactHaptic(hapticStyles[variant]);
      }

      onClick?.(event);
    };

    if (asChild) {
      const child = Children.only(children);

      if (!isValidElement<AsChildProps>(child)) {
        return null;
      }

      const typedChild = child as ReactElement<AsChildProps>;
      const { type: _buttonType, ...childPropsFromButton } = props;
      const handleChildClick = (event: MouseEvent<HTMLElement>) => {
        if (isDisabled) {
          event.preventDefault();
          event.stopPropagation();
          return;
        }

        void triggerImpactHaptic(hapticStyles[variant]);
        typedChild.props.onClick?.(event);
      };

      return cloneElement(typedChild, {
        ...childPropsFromButton,
        className: cn(classes, typedChild.props.className),
        onClick: handleChildClick,
        'aria-disabled': isDisabled || typedChild.props['aria-disabled'],
        tabIndex: isDisabled ? -1 : typedChild.props.tabIndex,
      });
    }

    return (
      <motion.button
        ref={ref}
        disabled={isDisabled}
        aria-busy={loading || undefined}
        aria-disabled={isDisabled || undefined}
        whileHover={isDisabled ? undefined : getMicroHover(reducedMotion)}
        whileTap={isDisabled ? undefined : getMicroTap(reducedMotion)}
        className={classes}
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
