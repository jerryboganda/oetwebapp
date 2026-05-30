'use client';

import { ButtonHTMLAttributes } from 'react';

type IconButtonTone = 'neutral' | 'danger';

const TONE_CLASSES: Record<IconButtonTone, string> = {
  neutral: 'text-slate-400 hover:bg-slate-100 hover:text-slate-700',
  danger: 'text-rose-400 hover:bg-rose-50 hover:text-rose-600',
};

/**
 * Small square icon button used for reorder/remove affordances across the
 * Writing builder list editors. Keyboard-focusable; respects disabled state.
 * Motion: transitions only `color`/`background-color` at 150ms (no
 * transition-all), per project motion standards.
 */
export function IconButton({
  tone = 'neutral',
  className = '',
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { tone?: IconButtonTone }) {
  return (
    <button
      type="button"
      className={`rounded p-1 text-sm leading-none outline-none transition-colors duration-150 focus-visible:ring-2 focus-visible:ring-violet-200 disabled:cursor-not-allowed disabled:opacity-30 motion-reduce:transition-none ${TONE_CLASSES[tone]} ${className}`}
      {...props}
    />
  );
}
