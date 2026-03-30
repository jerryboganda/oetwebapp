import { cn } from '@/lib/utils';

/* ─── Linear Progress Bar ─── */
interface ProgressBarProps {
  value: number; // 0-100
  max?: number;
  label?: string;
  ariaLabel?: string;
  showValue?: boolean;
  size?: 'sm' | 'md';
  color?: 'primary' | 'success' | 'warning' | 'danger';
  className?: string;
}

const colorStyles: Record<string, string> = {
  primary: 'bg-primary',
  success: 'bg-emerald-500',
  warning: 'bg-amber-500',
  danger: 'bg-red-500',
};

export function ProgressBar({ value, max = 100, label, ariaLabel, showValue, size = 'sm', color = 'primary', className }: ProgressBarProps) {
  const pct = Math.min(100, Math.max(0, (value / max) * 100));
  return (
    <div className={cn('w-full', className)}>
      {(label || showValue) && (
        <div className="flex justify-between text-sm mb-1">
          {label && <span className="font-semibold text-navy">{label}</span>}
          {showValue && <span className="text-muted">{Math.round(pct)}%</span>}
        </div>
      )}
      <div
        className={cn('w-full bg-gray-100 rounded-full overflow-hidden shadow-inner', size === 'sm' ? 'h-2' : 'h-3')}
        role="progressbar"
        aria-valuenow={pct}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={ariaLabel ?? label}
      >
        <div className={cn('h-full rounded-full transition-all duration-700 ease-out', colorStyles[color])} style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

/* ─── Circular Progress ─── */
interface CircularProgressProps {
  value: number; // 0-100
  size?: number;
  strokeWidth?: number;
  label?: string;
  sublabel?: string;
  color?: string;
  className?: string;
}

export function CircularProgress({ value, size = 120, strokeWidth = 8, label, sublabel, color = 'var(--color-primary)', className }: CircularProgressProps) {
  const radius = (size - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference - (Math.min(100, Math.max(0, value)) / 100) * circumference;

  return (
    <div className={cn('flex flex-col items-center', className)}>
      <div className="relative" style={{ width: size, height: size }}>
        <svg className="w-full h-full transform -rotate-90" viewBox={`0 0 ${size} ${size}`}>
          <circle cx={size / 2} cy={size / 2} r={radius} fill="none" stroke="#E5E7EB" strokeWidth={strokeWidth} />
          <circle
            cx={size / 2} cy={size / 2} r={radius} fill="none" stroke={color} strokeWidth={strokeWidth}
            strokeDasharray={circumference} strokeDashoffset={offset} strokeLinecap="round"
            className="transition-all duration-700"
          />
        </svg>
        <div className="absolute inset-0 flex items-center justify-center flex-col">
          <span className="text-2xl font-bold text-navy leading-none">
            {Math.round(value)}<span className="text-sm">%</span>
          </span>
        </div>
      </div>
      {label && <p className="text-sm font-semibold text-navy mt-2">{label}</p>}
      {sublabel && <p className="text-xs text-muted mt-0.5">{sublabel}</p>}
    </div>
  );
}
