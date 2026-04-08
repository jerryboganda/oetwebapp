'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { Trophy, Flame, Star, Zap, Lock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { fetchXP, fetchStreak, fetchAchievements } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type XPData = { totalXp: number; level: number; xpToNextLevel: number; xpInCurrentLevel: number };
type StreakData = { currentStreak: number; longestStreak: number; lastActivityDate: string | null };
type Achievement = { achievementId: string; title: string; description: string; category: string; xpReward: number; unlockedAt: string | null; earnedAt?: string | null };

function XPBar({ xpInLevel, xpToNext }: { xpInLevel: number; xpToNext: number }) {
  const pct = xpToNext > 0 ? Math.min(100, (xpInLevel / xpToNext) * 100) : 100;
  return (
    <div className="w-full bg-gray-200 dark:bg-gray-700 rounded-full h-3">
      <motion.div
        className="bg-gradient-to-r from-yellow-400 to-orange-500 h-3 rounded-full"
        initial={{ width: 0 }}
        animate={{ width: `${pct}%` }}
        transition={{ duration: 0.8, ease: 'easeOut' }}
      />
    </div>
  );
}

const CATEGORY_ICONS: Record<string, React.ReactNode> = {
  practice: <Star className="w-5 h-5" />,
  streak: <Flame className="w-5 h-5" />,
  milestone: <Trophy className="w-5 h-5" />,
  mastery: <Zap className="w-5 h-5" />,
  social: <Star className="w-5 h-5" />,
  xp: <Zap className="w-5 h-5" />,
};

const CATEGORY_COLORS: Record<string, string> = {
  practice: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300',
  streak: 'bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300',
  milestone: 'bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-300',
  mastery: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300',
  social: 'bg-pink-100 text-pink-700 dark:bg-pink-900/30 dark:text-pink-300',
  xp: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300',
};

export default function AchievementsPage() {
  const [xp, setXp] = useState<XPData | null>(null);
  const [streak, setStreak] = useState<StreakData | null>(null);
  const [achievements, setAchievements] = useState<Achievement[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<string>('all');

  useEffect(() => {
    analytics.track('content_view', { page: 'achievements' });
    Promise.allSettled([fetchXP(), fetchStreak(), fetchAchievements()]).then(([xpR, streakR, achR]) => {
      if (xpR.status === 'fulfilled') setXp(xpR.value as XPData);
      if (streakR.status === 'fulfilled') setStreak(streakR.value as StreakData);
      if (achR.status === 'fulfilled') setAchievements(achR.value as Achievement[]);
      const anyFailed = [xpR, streakR, achR].some(r => r.status === 'rejected');
      if (anyFailed) setError('Some data could not be loaded.');
      setLoading(false);
    });
  }, []);

  const categories = ['all', ...Array.from(new Set(achievements.map(a => a.category)))];
  const filtered = filter === 'all' ? achievements : achievements.filter(a => a.category === filter);
  const unlocked = filtered.filter(a => a.unlockedAt || a.earnedAt);
  const locked = filtered.filter(a => !a.unlockedAt && !a.earnedAt);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Achievements"
        description="Track your progress, streaks, and milestones"
        icon={Trophy}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* XP + Streak summary */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-8">
        {loading ? (
          Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-28 rounded-xl" />)
        ) : (
          <>
            <motion.div
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              className="bg-white dark:bg-gray-800 rounded-xl p-5 border border-gray-200 dark:border-gray-700"
            >
              <div className="flex items-center gap-2 mb-1">
                <Zap className="w-5 h-5 text-yellow-500" />
                <span className="text-sm font-medium text-gray-500 dark:text-gray-400">Level</span>
              </div>
              <div className="text-3xl font-bold text-gray-900 dark:text-white">{xp?.level ?? '—'}</div>
              <div className="text-xs text-gray-500 mt-1">{xp?.totalXp.toLocaleString()} total XP</div>
            </motion.div>

            <motion.div
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.05 }}
              className="bg-white dark:bg-gray-800 rounded-xl p-5 border border-gray-200 dark:border-gray-700"
            >
              <div className="flex items-center gap-2 mb-2">
                <Zap className="w-4 h-4 text-yellow-400" />
                <span className="text-sm font-medium text-gray-500 dark:text-gray-400">XP Progress</span>
              </div>
              {xp && <XPBar xpInLevel={xp.xpInCurrentLevel} xpToNext={xp.xpToNextLevel} />}
              <div className="text-xs text-gray-500 mt-1">{xp?.xpInCurrentLevel} / {xp?.xpToNextLevel} to next level</div>
            </motion.div>

            <motion.div
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.1 }}
              className="bg-white dark:bg-gray-800 rounded-xl p-5 border border-gray-200 dark:border-gray-700"
            >
              <div className="flex items-center gap-2 mb-1">
                <Flame className="w-5 h-5 text-orange-500" />
                <span className="text-sm font-medium text-gray-500 dark:text-gray-400">Streak</span>
              </div>
              <div className="text-3xl font-bold text-gray-900 dark:text-white">{streak?.currentStreak ?? '—'} <span className="text-base font-normal text-gray-500">days</span></div>
              <div className="text-xs text-gray-500 mt-1">Best: {streak?.longestStreak ?? 0} days</div>
            </motion.div>
          </>
        )}
      </div>

      {/* Category filter */}
      <div className="flex flex-wrap gap-2 mb-6">
        {categories.map(cat => (
          <button
            key={cat}
            onClick={() => setFilter(cat)}
            className={`px-3 py-1.5 rounded-full text-sm font-medium capitalize transition-colors ${filter === cat ? 'bg-indigo-600 text-white' : 'bg-gray-100 text-gray-600 dark:bg-gray-700 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-600'}`}
          >
            {cat}
          </button>
        ))}
      </div>

      {loading ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-28 rounded-xl" />)}
        </div>
      ) : (
        <>
          {unlocked.length > 0 && (
            <>
              <LearnerSurfaceSectionHeader title={`Unlocked (${unlocked.length})`} />
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 mb-8">
                {unlocked.map((ach, i) => (
                  <motion.div
                    key={ach.achievementId}
                    initial={{ opacity: 0, scale: 0.95 }}
                    animate={{ opacity: 1, scale: 1 }}
                    transition={{ delay: i * 0.03 }}
                    className="bg-white dark:bg-gray-800 rounded-xl p-4 border border-gray-200 dark:border-gray-700 flex items-start gap-3"
                  >
                    <div className={`p-2 rounded-lg ${CATEGORY_COLORS[ach.category] ?? 'bg-gray-100 text-gray-600'}`}>
                      {CATEGORY_ICONS[ach.category] ?? <Trophy className="w-5 h-5" />}
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="font-semibold text-gray-900 dark:text-white text-sm">{ach.title}</div>
                      <div className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">{ach.description}</div>
                      <div className="flex items-center gap-1 mt-1.5">
                        <Zap className="w-3 h-3 text-yellow-500" />
                        <span className="text-xs font-medium text-yellow-600 dark:text-yellow-400">+{ach.xpReward} XP</span>
                      </div>
                    </div>
                  </motion.div>
                ))}
              </div>
            </>
          )}

          {locked.length > 0 && (
            <>
              <LearnerSurfaceSectionHeader title={`Locked (${locked.length})`} />
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                {locked.map((ach, i) => (
                  <motion.div
                    key={ach.achievementId}
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    transition={{ delay: i * 0.02 }}
                    className="bg-gray-50 dark:bg-gray-900 rounded-xl p-4 border border-gray-200 dark:border-gray-700 flex items-start gap-3 opacity-60"
                  >
                    <div className="p-2 rounded-lg bg-gray-200 dark:bg-gray-700 text-gray-400">
                      <Lock className="w-5 h-5" />
                    </div>
                    <div className="flex-1 min-w-0">
                      <div className="font-semibold text-gray-600 dark:text-gray-400 text-sm">{ach.title}</div>
                      <div className="text-xs text-gray-400 mt-0.5">{ach.description}</div>
                      <div className="flex items-center gap-1 mt-1.5">
                        <Zap className="w-3 h-3 text-gray-400" />
                        <span className="text-xs text-gray-400">+{ach.xpReward} XP</span>
                      </div>
                    </div>
                  </motion.div>
                ))}
              </div>
            </>
          )}
        </>
      )}
    </LearnerDashboardShell>
  );
}
