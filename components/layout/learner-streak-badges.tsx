'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { Flame, Zap } from 'lucide-react';
import { fetchStreak, fetchXP } from '@/lib/api';
import { cn } from '@/lib/utils';

interface LearnerStreakBadgesProps {
  className?: string;
}

interface XpSummary {
  level: number;
  totalXP: number;
  currentLevelXP: number;
  nextLevelXP: number;
}

/** Derived client-side — the XP endpoint returns a numeric level only. */
function tierForLevel(level: number): string {
  if (level >= 15) return 'Expert';
  if (level >= 10) return 'Advanced';
  if (level >= 5) return 'Intermediate';
  return 'Beginner';
}

/**
 * Streak + level cards in the learner top bar. Both link to /achievements.
 * Hidden until at least one value resolves so the header does not jump.
 */
export function LearnerStreakBadges({ className }: LearnerStreakBadgesProps) {
  const [streak, setStreak] = useState<number | null>(null);
  const [xp, setXp] = useState<XpSummary | null>(null);

  useEffect(() => {
    let cancelled = false;
    Promise.allSettled([fetchStreak(), fetchXP()]).then(([streakR, xpR]) => {
      if (cancelled) return;
      if (streakR.status === 'fulfilled') {
        setStreak((streakR.value as { currentStreak: number }).currentStreak);
      }
      if (xpR.status === 'fulfilled') {
        setXp(xpR.value as XpSummary);
      }
    });
    return () => {
      cancelled = true;
    };
  }, []);

  if (streak === null && xp === null) return null;

  const span = xp ? Math.max(1, xp.nextLevelXP - xp.currentLevelXP) : 1;
  const gained = xp ? Math.max(0, xp.totalXP - xp.currentLevelXP) : 0;
  const progress = xp ? Math.min(100, Math.round((gained / span) * 100)) : 0;
  const tier = xp ? tierForLevel(xp.level) : '';

  return (
    <div className={cn('flex items-center gap-2', className)}>
      {streak !== null ? (
        <Link
          href="/achievements"
          aria-label={`Current streak: ${streak} days`}
          className="flex items-center gap-2 rounded-full border border-border bg-surface py-1 pl-1 pr-3 shadow-sm transition-colors hover:border-border-hover"
        >
          <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-orange-100 text-orange-600 dark:bg-orange-950/60 dark:text-orange-400">
            <Flame className="h-3.5 w-3.5" aria-hidden="true" />
          </span>
          <span className="leading-none">
            <span className="block text-[13px] font-bold text-navy">{streak}</span>
            <span className="mt-0.5 hidden text-[10.5px] text-muted xl:block">day streak</span>
          </span>
        </Link>
      ) : null}

      {xp ? (
        <Link
          href="/achievements"
          aria-label={`Level ${xp.level}, ${tier}, ${progress}% to next level`}
          className="flex items-center gap-2 rounded-full border border-border bg-surface py-1 pl-1 pr-3 shadow-sm transition-colors hover:border-border-hover"
        >
          <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
            <Zap className="h-3.5 w-3.5" aria-hidden="true" />
          </span>
          <span className="leading-none">
            <span className="flex items-center gap-2">
              <span className="text-[13px] font-bold text-navy">Level {xp.level}</span>
              <span className="hidden text-[10.5px] text-muted xl:inline">{tier}</span>
            </span>
            <span className="mt-1 hidden h-1 w-24 overflow-hidden rounded-full bg-background-light xl:block">
              <span
                className="block h-full rounded-full bg-primary transition-[width] duration-500"
                style={{ width: `${progress}%` }}
              />
            </span>
          </span>
        </Link>
      ) : null}
    </div>
  );
}
