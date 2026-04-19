'use client';

import { cn } from '@/lib/utils';
import { motion, useReducedMotion } from 'motion/react';
import { motionTokens } from '@/lib/motion';
import { forwardRef, useId, type ReactNode } from 'react';

// ─────────────────────────────────────────────────────────────────────────────
// Switch — accessible toggle for boolean settings.
//
// Design contract: DESIGN.md §5.13. Replaces raw <input type="checkbox"> and
// bespoke ToggleLeft/ToggleRight icon rows across admin pages.
// ─────────────────────────────────────────────────────────────────────────────

export interface SwitchProps {
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
  label?: string;
  description?: string;
  disabled?: boolean;
  size?: 'sm' | 'md';
  id?: string;
  className?: string;
  name?: string;
  /** When true, render a standalone toggle (no label / description / surrounding row). */
  standalone?: boolean;
  'aria-label'?: string;
}

const trackBase =
  'relative inline-flex shrink-0 cursor-pointer items-center rounded-full border transition-[background-color,border-color,box-shadow] duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2';

const thumbBase =
  'pointer-events-none inline-block rounded-full bg-surface shadow-sm transition-transform duration-200';

const sizeStyles = {
  sm: {
    track: 'h-5 w-9',
    thumb: 'h-4 w-4',
    translateOn: 'translate-x-[1.125rem]',
    translateOff: 'translate-x-0.5',
  },
  md: {
    track: 'h-6 w-11',
    thumb: 'h-5 w-5',
    translateOn: 'translate-x-[1.375rem]',
    translateOff: 'translate-x-0.5',
  },
} as const;

export const Switch = forwardRef<HTMLButtonElement, SwitchProps>(function Switch(
  {
    checked,
    onCheckedChange,
    label,
    description,
    disabled,
    size = 'md',
    id,
    className,
    name,
    standalone = false,
    'aria-label': ariaLabel,
  },
  ref,
) {
  const reactId = useId();
  const controlId = id ?? `switch-${reactId}`;
  const sz = sizeStyles[size];

  const track = (
    <button
      ref={ref}
      type="button"
      role="switch"
      id={controlId}
      name={name}
      aria-checked={checked}
      aria-label={ariaLabel ?? label}
      aria-describedby={description ? `${controlId}-desc` : undefined}
      disabled={disabled}
      onClick={() => onCheckedChange(!checked)}
      onKeyDown={(event) => {
        if (event.key === ' ' || event.key === 'Enter') {
          event.preventDefault();
          if (!disabled) onCheckedChange(!checked);
        }
      }}
      className={cn(
        trackBase,
        sz.track,
        checked
          ? 'border-primary bg-primary'
          : 'border-border bg-background-light',
        disabled && 'opacity-50 cursor-not-allowed',
      )}
    >
      <span
        className={cn(thumbBase, sz.thumb, checked ? sz.translateOn : sz.translateOff)}
      />
    </button>
  );

  if (standalone) {
    return <span className={className}>{track}</span>;
  }

  return (
    <label
      htmlFor={controlId}
      className={cn(
        'flex items-start gap-3 rounded-2xl border border-border bg-background-light px-4 py-3 shadow-sm transition-colors',
        !disabled && 'hover:border-border-hover cursor-pointer',
        disabled && 'opacity-60',
        className,
      )}
    >
      <span className="mt-0.5">{track}</span>
      {(label || description) && (
        <span className="flex-1 min-w-0">
          {label && (
            <span className="block text-sm font-semibold text-navy">{label}</span>
          )}
          {description && (
            <span id={`${controlId}-desc`} className="mt-0.5 block text-xs leading-5 text-muted">
              {description}
            </span>
          )}
        </span>
      )}
    </label>
  );
});

// ─────────────────────────────────────────────────────────────────────────────
// AnimatedSwitchIndicator — motion-enhanced track for listing layouts. Shown
// below as a separate export to preserve tree-shaking.
// ─────────────────────────────────────────────────────────────────────────────

export interface SwitchRowProps {
  checked: boolean;
  onCheckedChange: (checked: boolean) => void;
  label: string;
  description?: string;
  disabled?: boolean;
  badge?: ReactNode;
}

export function SwitchRow({ checked, onCheckedChange, label, description, disabled, badge }: SwitchRowProps) {
  const reducedMotion = useReducedMotion() ?? false;
  return (
    <motion.label
      whileHover={disabled || reducedMotion ? undefined : { scale: 1.005 }}
      whileTap={disabled || reducedMotion ? undefined : { scale: 0.995 }}
      transition={reducedMotion ? { duration: motionTokens.duration.instant } : motionTokens.spring.item}
      className={cn(
        'flex cursor-pointer items-start justify-between gap-4 rounded-2xl border border-border bg-background-light px-4 py-3 shadow-sm transition-colors',
        !disabled && 'hover:border-border-hover',
        disabled && 'cursor-not-allowed opacity-60',
      )}
    >
      <span className="flex-1 min-w-0">
        <span className="flex flex-wrap items-center gap-2">
          <span className="text-sm font-semibold text-navy">{label}</span>
          {badge}
        </span>
        {description && <span className="mt-1 block text-xs leading-5 text-muted">{description}</span>}
      </span>
      <Switch standalone checked={checked} onCheckedChange={onCheckedChange} disabled={disabled} aria-label={label} />
    </motion.label>
  );
}
