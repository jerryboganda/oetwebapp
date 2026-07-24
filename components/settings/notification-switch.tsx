'use client';

import { cn } from '@/lib/utils';

interface NotificationSwitchProps {
  checked: boolean;
  onChange: () => void;
  disabled?: boolean;
  /** Accessible name — the visual label lives outside the control. */
  label: string;
  size?: 'sm' | 'md';
}

/**
 * Pill toggle used across the notification settings surface. Purple when on,
 * neutral when off, with a sliding knob. Rendered as a real switch role so
 * keyboard and screen-reader users get proper semantics.
 */
export function NotificationSwitch({
  checked,
  onChange,
  disabled = false,
  label,
  size = 'md',
}: NotificationSwitchProps) {
  const track = size === 'sm' ? 'h-5 w-9' : 'h-6 w-11';
  const knob = size === 'sm' ? 'h-3.5 w-3.5' : 'h-[1.125rem] w-[1.125rem]';
  // track width − knob − (2 × 2px inset)
  const travel = size === 'sm' ? 'translate-x-[1.125rem]' : 'translate-x-[1.375rem]';

  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      disabled={disabled}
      onClick={onChange}
      className={cn(
        'relative inline-flex shrink-0 items-center rounded-full transition-colors duration-200',
        'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
        'disabled:cursor-not-allowed disabled:opacity-50',
        track,
        checked ? 'bg-primary dark:bg-violet-600' : 'bg-border dark:bg-slate-700',
      )}
    >
      <span
        className={cn(
          'pointer-events-none ml-0.5 inline-block transform rounded-full bg-white shadow-sm transition-transform duration-200',
          knob,
          checked ? travel : 'translate-x-0',
        )}
      />
    </button>
  );
}
