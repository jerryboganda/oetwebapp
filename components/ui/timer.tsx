'use client';

import { cn } from '@/lib/utils';
import { useEffect, useRef, useState } from 'react';

type TimerMode = 'countdown' | 'elapsed';

interface TimerProps {
  mode?: TimerMode;
  initialSeconds?: number; // For countdown: total seconds. For elapsed: start at 0 by default.
  running?: boolean;
  onComplete?: () => void;
  onTick?: (seconds: number) => void;
  className?: string;
  size?: 'sm' | 'md' | 'lg';
  showWarning?: boolean; // Show red at <5min for countdown
}

function formatTime(totalSeconds: number): string {
  const h = Math.floor(totalSeconds / 3600);
  const m = Math.floor((totalSeconds % 3600) / 60);
  const s = totalSeconds % 60;
  if (h > 0) return `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

export function Timer({ mode = 'countdown', initialSeconds = 0, running = true, onComplete, onTick, className, size = 'md', showWarning = true }: TimerProps) {
  const [seconds, setSeconds] = useState(mode === 'countdown' ? initialSeconds : 0);
  const intervalRef = useRef<ReturnType<typeof setInterval> | undefined>(undefined);

  useEffect(() => {
    if (!running) {
      if (intervalRef.current) clearInterval(intervalRef.current);
      return;
    }
    intervalRef.current = setInterval(() => {
      setSeconds((prev) => {
        const next = mode === 'countdown' ? prev - 1 : prev + 1;
        if (mode === 'countdown' && next <= 0) {
          clearInterval(intervalRef.current);
          onComplete?.();
          return 0;
        }
        onTick?.(next);
        return next;
      });
    }, 1000);
    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, [running, mode, onComplete, onTick]);

  const isWarning = showWarning && mode === 'countdown' && seconds < 300 && seconds > 0;

  const sizeStyles = {
    sm: 'text-sm px-2 py-1',
    md: 'text-base px-3 py-1.5',
    lg: 'text-xl px-4 py-2',
  };

  return (
    <div
      className={cn(
        'font-mono font-bold rounded tabular-nums',
        sizeStyles[size],
        isWarning ? 'bg-red-50 text-red-700 border border-red-200' : 'bg-gray-100 text-navy border border-gray-200',
        className,
      )}
      role="timer"
      aria-label={mode === 'countdown' ? 'Time remaining' : 'Time elapsed'}
    >
      {formatTime(seconds)}
    </div>
  );
}
