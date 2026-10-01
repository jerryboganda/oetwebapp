'use client';

import Link from 'next/link';
import { Flame, Zap } from 'lucide-react';
import { motion } from 'motion/react';
import { useAuth } from '@/contexts/auth-context';
import { useStreakIncrease } from '@/hooks/use-streak-increase';
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
  // One flame pop when the real streak has grown since this device last saw it.
  const streakUp = useStreakIncrease(streak);

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
          <motion.span
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-warning/15 text-warning"
            initial={false}
            animate={streakUp ? { scale: [1, 1.35, 0.92, 1], rotate: [0, -10, 6, 0] } : undefined}
            transition={{ duration: 0.7, delay: 0.4 }}
          >
            <Flame className="h-4 w-4" aria-hidden="true" />
          </motion.span>
          <span className="leading-tight">
            <span className="block text-sm font-bold tabular-nums text-navy">{streak}</span>
            <span className="hidden text-2xs text-muted xl:block">day streak</span>
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
              <span className="text-sm font-bold text-navy">Level {xp.level}</span>
              <span className="hidden text-2xs text-muted xl:inline">{tier}</span>
            </span>
            <span className="mt-1 hidden items-center gap-2 xl:flex">
              <span className="block h-1.5 w-24 overflow-hidden rounded-full bg-background-light rtl:-scale-x-100">
                {/* Slides (transform) rather than animating width; fills in on first paint. */}
                <span
                  className="block h-full w-full rounded-full bg-primary transition-transform duration-700 ease-standard starting:[transform:translateX(-100%)]"
                  style={{ transform: `translateX(${progress - 100}%)` }}
                />
              </span>
              <span className="text-2xs tabular-nums text-muted">{progress}%</span>
            </span>
          </span>
        </Link>
      ) : null}
    </div>
  );
}
