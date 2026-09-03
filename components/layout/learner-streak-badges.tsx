'use client';

import Link from 'next/link';
import { Flame, Zap } from 'lucide-react';
import { useAuth } from '@/contexts/auth-context';
import { useStreak, useXp } from '@/lib/query/hooks';
import { cn } from '@/lib/utils';
import { HEADER_CHIP, HEADER_CHIP_HOVER } from './header-chrome';

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
 *
 * Backed by the shared QueryClient (see lib/query/hooks.ts useStreak/useXp)
 * instead of a raw fetch-on-mount: TopNav remounts on every learner
 * navigation today (no persistent shell layout — see the global-nav
 * findings), so an uncached fetch here used to refire on every single tap.
 */
export function LearnerStreakBadges({ className }: LearnerStreakBadgesProps) {
  const { user } = useAuth();
  const userId = user?.userId ?? '';
  const { data: streakData } = useStreak(userId, { enabled: Boolean(userId) });
  const { data: xpData } = useXp(userId, { enabled: Boolean(userId) });

  const streak = (streakData as { currentStreak: number } | undefined)?.currentStreak ?? null;
  const xp = (xpData as XpSummary | undefined) ?? null;

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
          className={cn('flex items-center gap-2.5 rounded-xl p-1.5 lg:pr-3.5', HEADER_CHIP, HEADER_CHIP_HOVER)}
        >
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-orange-100 text-orange-600 dark:bg-orange-950/60 dark:text-orange-400">
            <Flame className="h-4 w-4" aria-hidden="true" />
          </span>
          <span className="leading-tight">
            <span className="block text-[14px] font-bold text-navy">{streak}</span>
            <span className="hidden text-[11px] text-muted xl:block">day streak</span>
          </span>
        </Link>
      ) : null}

      {xp ? (
        <Link
          href="/achievements"
          aria-label={`Level ${xp.level}, ${tier}, ${progress}% to next level`}
          className={cn('flex items-center gap-2.5 rounded-xl p-1.5 lg:pr-3.5', HEADER_CHIP, HEADER_CHIP_HOVER)}
        >
          <span className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
            <Zap className="h-4 w-4" aria-hidden="true" />
          </span>
          <span className="leading-tight">
            <span className="flex items-baseline justify-between gap-3">
              <span className="text-[14px] font-bold text-navy">Level {xp.level}</span>
              <span className="hidden text-[11px] text-muted xl:inline">{tier}</span>
            </span>
            <span className="mt-1 hidden items-center gap-2 xl:flex">
              <span className="block h-1.5 w-24 overflow-hidden rounded-full bg-background-light">
                <span
                  className="block h-full rounded-full bg-primary transition-[width] duration-500"
                  style={{ width: `${progress}%` }}
                />
              </span>
              <span className="text-[10.5px] text-muted">{tier}</span>
            </span>
          </span>
        </Link>
      ) : null}
    </div>
  );
}
