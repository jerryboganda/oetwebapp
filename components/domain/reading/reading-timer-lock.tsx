'use client';

import { useEffect, useRef, useState, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { Clock, Lock, AlertTriangle } from 'lucide-react';
import { cn } from '@/lib/utils';

interface ReadingTimerLockProps {
  partADeadlineAt: string;
  overallDeadlineAt: string;
  currentPart: 'A' | 'B' | 'C';
  onPartALocked: () => void;
  onExamExpired: () => void;
  mode: 'exam' | 'learning';
}

function formatTime(totalSeconds: number): string {
  if (totalSeconds <= 0) return '00:00';
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
}

function getRemainingSeconds(deadline: string): number {
  const remaining = Math.floor((new Date(deadline).getTime() - Date.now()) / 1000);
  return Math.max(0, remaining);
}

export function ReadingTimerLock({
  partADeadlineAt,
  overallDeadlineAt,
  currentPart,
  onPartALocked,
  onExamExpired,
  mode,
}: ReadingTimerLockProps) {
  const [partARemaining, setPartARemaining] = useState(() => getRemainingSeconds(partADeadlineAt));
  const [overallRemaining, setOverallRemaining] = useState(() => getRemainingSeconds(overallDeadlineAt));
  const [partALocked, setPartALocked] = useState(() => getRemainingSeconds(partADeadlineAt) <= 0);

  const partAFiredRef = useRef(false);
  const examFiredRef = useRef(false);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const handlePartAExpiry = useCallback(() => {
    if (!partAFiredRef.current) {
      partAFiredRef.current = true;
      setPartALocked(true);
      onPartALocked();
    }
  }, [onPartALocked]);

  const handleExamExpiry = useCallback(() => {
    if (!examFiredRef.current) {
      examFiredRef.current = true;
      onExamExpired();
    }
  }, [onExamExpired]);

  useEffect(() => {
    if (mode !== 'exam') return;

    intervalRef.current = setInterval(() => {
      const partA = getRemainingSeconds(partADeadlineAt);
      const overall = getRemainingSeconds(overallDeadlineAt);

      setPartARemaining(partA);
      setOverallRemaining(overall);

      if (partA <= 0) handlePartAExpiry();
      if (overall <= 0) handleExamExpiry();
    }, 1000);

    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, [mode, partADeadlineAt, overallDeadlineAt, handlePartAExpiry, handleExamExpiry]);

  if (mode === 'learning') {
    return (
      <div className="inline-flex items-center gap-1.5 rounded-full bg-emerald-50 px-3 py-1 text-xs font-medium text-emerald-700 dark:bg-emerald-950/40 dark:text-emerald-400">
        <Clock className="h-3.5 w-3.5" aria-hidden="true" />
        No time limit
      </div>
    );
  }

  const isPartAActive = currentPart === 'A' && !partALocked;
  const activeDeadlineSeconds = isPartAActive ? partARemaining : overallRemaining;
  const label = isPartAActive ? 'Part A' : 'Exam';

  const isAmber = activeDeadlineSeconds <= 300 && activeDeadlineSeconds > 60;
  const isRed = activeDeadlineSeconds <= 60;
  const shouldPulse = activeDeadlineSeconds <= 30 && activeDeadlineSeconds > 0;

  const colorClass = isRed
    ? 'text-red-600 dark:text-red-400'
    : isAmber
      ? 'text-amber-600 dark:text-amber-400'
      : 'text-foreground';

  const bgClass = isRed
    ? 'bg-red-50 border-red-200 dark:bg-red-950/30 dark:border-red-800'
    : isAmber
      ? 'bg-amber-50 border-amber-200 dark:bg-amber-950/30 dark:border-amber-800'
      : 'bg-background-light border-border';

  return (
    <div className="flex items-center gap-2">
      <AnimatePresence mode="wait">
        <motion.div
          key={`${label}-${shouldPulse}`}
          className={cn(
            'inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-semibold tabular-nums',
            bgClass,
            colorClass,
          )}
          animate={shouldPulse ? { scale: [1, 1.05, 1] } : undefined}
          transition={shouldPulse ? { repeat: Infinity, duration: 0.8 } : undefined}
        >
          {isRed ? (
            <AlertTriangle className="h-3.5 w-3.5" aria-hidden="true" />
          ) : (
            <Clock className="h-3.5 w-3.5" aria-hidden="true" />
          )}
          <span>{label}: {formatTime(activeDeadlineSeconds)}</span>
        </motion.div>
      </AnimatePresence>

      <AnimatePresence>
        {partALocked && (
          <motion.div
            initial={{ opacity: 0, scale: 0.8 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.8 }}
            className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-600 dark:bg-slate-800 dark:text-slate-400"
          >
            <Lock className="h-3 w-3" aria-hidden="true" />
            Part A locked
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
