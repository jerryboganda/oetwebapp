'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { Flame, Zap } from 'lucide-react';
import { fetchStreak, fetchXP } from '@/lib/api';
import { cn } from '@/lib/utils';

interface LearnerStreakBadgesProps {
  className?: string;
}

/**
 * Compact streak + level badges shown in the learner top-nav.
 * Hidden until both values resolve to avoid layout flicker.
 */
export function LearnerStreakBadges({ className }: LearnerStreakBadgesProps) {
  const [streak, setStreak] = useState<number | null>(null);
  const [level, setLevel] = useState<number | null>(null);

  useEffect(() => {
    let cancelled = false;
    Promise.allSettled([fetchStreak(), fetchXP()]).then(([streakR, xpR]) => {
      if (cancelled) return;
      if (streakR.status === 'fulfilled') {
        setStreak((streakR.value as { currentStreak: number }).currentStreak);
      }
      if (xpR.status === 'fulfilled') {
        setLevel((xpR.value as { level: number }).level);
      }
    });
    return () => {
      cancelled = true;
    };
  }, []);

  if (streak === null && level === null) {
    return null;
  }

  return (
    <div className={cn('flex items-center gap-1', className)}>
      {streak !== null && (
        <Link
          href="/achievements"
          aria-label={`Current streak: ${streak} days`}
          className="flex items-center gap-1 rounded-full bg-amber-700 px-2 py-1 text-[11px] font-bold text-white shadow-sm transition-colors hover:bg-amber-800 dark:bg-amber-700 dark:text-white dark:hover:bg-amber-600 lg:px-2.5 lg:py-1.5 lg:text-xs"
        >
          <Flame className="h-3 w-3 lg:h-3.5 lg:w-3.5" aria-hidden="true" />
          {streak}d
        </Link>
      )}
      {level !== null && (
        <Link
          href="/achievements"
          aria-label={`Level ${level}`}
          className="flex items-center gap-1 rounded-full bg-navy px-2 py-1 text-[11px] font-bold text-surface shadow-sm ring-1 ring-border transition-colors hover:bg-navy/90 lg:px-2.5 lg:py-1.5 lg:text-xs"
        >
          <Zap className="h-3 w-3 lg:h-3.5 lg:w-3.5" aria-hidden="true" />
          Lv.{level}
        </Link>
      )}
    </div>
  );
}
