'use client';

import { cn } from '@/lib/utils';

export interface SliderProps {
  value: number;
  onChange: (value: number) => void;
  onCommit?: (value: number) => void;
  min?: number;
  max?: number;
  step?: number;
  label?: string;
  hint?: string;
  disabled?: boolean;
  className?: string;
  id?: string;
  'aria-label'?: string;
}

export function Slider({
  value,
  onChange,
  onCommit,
  min = 0,
  max = 100,
  step = 1,
  label,
  hint,
  disabled,
  className,
  id,
  'aria-label': ariaLabel,
}: SliderProps) {
  return (
    <div className={cn('space-y-2', className)}>
      {(label || hint) && (
        <div className="flex items-start justify-between gap-3">
          <div>
            {label ? <p className="text-sm font-semibold text-navy">{label}</p> : null}
            {hint ? <p className="text-xs text-muted">{hint}</p> : null}
          </div>
          <span className="text-sm font-semibold text-muted">{value}%</span>
        </div>
      )}
      <input
        id={id}
        type="range"
        min={min}
        max={max}
        step={step}
        value={value}
        disabled={disabled}
        aria-label={ariaLabel ?? label}
        onChange={(event) => onChange(Number(event.target.value))}
        onMouseUp={(event) => onCommit?.(Number((event.target as HTMLInputElement).value))}
        onTouchEnd={(event) => onCommit?.(Number((event.target as HTMLInputElement).value))}
        className="w-full accent-primary"
      />
    </div>
  );
}
