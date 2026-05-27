'use client';

import type { DailyPlanDto, DailyPlanItemDto } from '@/lib/reading-pathway-api';

const ITEM_TYPE_ICONS: Record<string, string> = {
  drill: '📚',
  vocab_review: '🔤',
  wrong_review: '🔁',
  strategy: '💡',
  strategy_read: '💡',
  lesson: '🎓',
  mock: '📝',
};

const SKILL_BADGE_COLORS: Record<string, string> = {
  default: 'bg-violet-100 text-violet-700 dark:bg-violet-900/30 dark:text-violet-300',
};

interface TodayPlanProps {
  plan: DailyPlanDto;
  onStartItem: (item: DailyPlanItemDto) => void;
}

export function TodayPlan({ plan, onStartItem }: TodayPlanProps) {
  const visibleItems = plan.items.slice(0, 4);
  const total = plan.items.length;
  const completed = plan.completedCount;

  return (
    <div className="rounded-xl border border-border bg-surface">
      {/* Header */}
      <div className="flex items-center justify-between px-5 py-4 border-b border-border">
        <div>
          <h2 className="text-sm font-semibold text-gray-900 dark:text-white">Today&apos;s Plan</h2>
          <p className="text-xs text-gray-600 dark:text-gray-400 mt-0.5">{plan.totalMinutes} min total</p>
        </div>
        <span className="inline-flex items-center gap-1.5 rounded-full bg-blue-100 dark:bg-blue-900/30 px-2.5 py-0.5 text-xs font-semibold text-blue-700 dark:text-blue-300">
          {completed}/{total} done
        </span>
      </div>

      {/* Items */}
      {visibleItems.length > 0 ? (
        <ul className="divide-y divide-border">
          {visibleItems.map((item) => {
            const icon = ITEM_TYPE_ICONS[item.itemType] ?? '📌';
            const isDone = item.status === 'complete' || item.status === 'completed' || item.status === 'skipped';
            return (
              <li key={item.id} className="flex items-center gap-3 px-5 py-3">
                <span className="text-lg flex-shrink-0" aria-hidden="true">{icon}</span>
                <div className="flex-1 min-w-0">
                  <p className={`text-sm font-medium truncate ${isDone ? 'line-through text-gray-600 dark:text-gray-400' : 'text-gray-900 dark:text-white'}`}>
                    {item.title}
                  </p>
                  <div className="flex items-center gap-2 mt-0.5">
                    {item.skillCode ? (
                      <span className={`inline-block rounded px-1.5 py-0.5 text-xs font-medium ${SKILL_BADGE_COLORS.default}`}>
                        {item.skillCode}
                      </span>
                    ) : null}
                    <span className="text-xs text-gray-600 dark:text-gray-400">{item.estimatedMinutes} min</span>
                  </div>
                </div>
                {!isDone ? (
                  <button
                    type="button"
                    onClick={() => onStartItem(item)}
                    className="flex-shrink-0 rounded-lg border border-blue-200 dark:border-blue-700 bg-blue-50 dark:bg-blue-900/30 px-3 py-1.5 text-xs font-semibold text-blue-700 dark:text-blue-300 hover:bg-blue-100 dark:hover:bg-blue-800/50 transition-colors"
                  >
                    Start
                  </button>
                ) : (
                  <span className="flex-shrink-0 text-xs text-gray-600 dark:text-gray-400 capitalize">{item.status}</span>
                )}
              </li>
            );
          })}
        </ul>
      ) : (
        <div className="px-5 py-8 text-center">
          <p className="text-sm text-gray-600 dark:text-gray-400">No items planned for today.</p>
          <p className="text-xs text-gray-600 dark:text-gray-400 mt-1">Check back later or continue with a Reading paper.</p>
        </div>
      )}
    </div>
  );
}
