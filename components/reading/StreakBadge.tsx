'use client';

const MILESTONE_DAYS = [7, 30, 60, 100];

interface StreakBadgeProps {
  streak: number;
}

export function StreakBadge({ streak }: StreakBadgeProps) {
  const isMilestone = MILESTONE_DAYS.includes(streak);

  return (
    <span
      className={[
        'inline-flex items-center gap-1 rounded-full px-2.5 py-0.5 text-xs font-semibold',
        'bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300',
        isMilestone ? 'motion-safe:animate-pulse' : '',
      ].join(' ')}
    >
      <span aria-hidden="true">🔥</span>
      {streak} day{streak === 1 ? '' : 's'} streak
    </span>
  );
}
