'use client';

import { useCallback, useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { Trophy, Medal, Crown } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchLeaderboard, fetchMyLeaderboardPosition, setLeaderboardOptIn } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type LeaderboardEntry = { rank: number; displayName: string; totalXp: number; level: number; isCurrentUser?: boolean };
type MyPosition = { rank: number | null; totalXp: number; level: number; optedIn: boolean };

const MEDAL_COLORS = ['text-yellow-500', 'text-gray-400', 'text-orange-600'];

export default function LeaderboardPage() {
  const [entries, setEntries] = useState<LeaderboardEntry[]>([]);
  const [myPos, setMyPos] = useState<MyPosition | null>(null);
  const [period, setPeriod] = useState<'weekly' | 'monthly' | 'alltime'>('weekly');
  const [examType, setExamType] = useState<string>('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toggling, setToggling] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [lb, pos] = await Promise.all([
        fetchLeaderboard(examType || undefined, period),
        fetchMyLeaderboardPosition(examType || undefined, period),
      ]);
      setEntries((lb as { entries: LeaderboardEntry[] }).entries ?? []);
      setMyPos(pos as MyPosition);
    } catch {
      setError('Could not load leaderboard.');
    } finally {
      setLoading(false);
    }
  }, [examType, period]);

  useEffect(() => {
    analytics.track('leaderboard_viewed');
    void load();
  }, [load]);

  async function toggleOptIn() {
    if (!myPos) return;
    setToggling(true);
    try {
      await setLeaderboardOptIn(!myPos.optedIn);
      setMyPos(p => p ? { ...p, optedIn: !p.optedIn } : p);
    } catch {
      setError('Could not update preference.');
    } finally {
      setToggling(false);
    }
  }

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Leaderboard"
        description="See how you rank against other learners"
        icon={Trophy}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Controls */}
      <div className="flex flex-wrap gap-3 mb-6">
        <div className="flex rounded-lg border border-gray-200 dark:border-gray-700 overflow-hidden">
          {(['weekly', 'monthly', 'alltime'] as const).map(p => (
            <button
              key={p}
              onClick={() => setPeriod(p)}
              className={`px-4 py-2 text-sm font-medium capitalize ${period === p ? 'bg-indigo-600 text-white' : 'bg-white dark:bg-gray-800 text-gray-600 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700'}`}
            >
              {p === 'alltime' ? 'All Time' : p}
            </button>
          ))}
        </div>
        <select
          value={examType}
          onChange={e => setExamType(e.target.value)}
          className="px-3 py-2 text-sm border border-gray-200 dark:border-gray-700 rounded-lg bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300"
        >
          <option value="">All Exams</option>
          <option value="oet">OET</option>
          <option value="ielts">IELTS</option>
          <option value="pte">PTE</option>
        </select>
      </div>

      {/* My position */}
      {myPos && (
        <div className="bg-indigo-50 dark:bg-indigo-900/20 border border-indigo-200 dark:border-indigo-800 rounded-xl p-4 mb-6 flex items-center justify-between">
          <div>
            <div className="text-sm font-medium text-indigo-700 dark:text-indigo-300">Your Position</div>
            <div className="text-2xl font-bold text-indigo-900 dark:text-white">
              {myPos.optedIn ? (myPos.rank ? `#${myPos.rank}` : 'Unranked') : 'Not participating'}
            </div>
            <div className="text-sm text-indigo-600 dark:text-indigo-400">{myPos.totalXp.toLocaleString()} XP · Level {myPos.level}</div>
          </div>
          <button
            onClick={toggleOptIn}
            disabled={toggling}
            className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${myPos.optedIn ? 'bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300 border border-gray-300 dark:border-gray-600' : 'bg-indigo-600 text-white hover:bg-indigo-700'}`}
          >
            {myPos.optedIn ? 'Opt Out' : 'Join Rankings'}
          </button>
        </div>
      )}

      {/* Table */}
      {loading ? (
        <div className="space-y-3">
          {Array.from({ length: 10 }).map((_, i) => <Skeleton key={i} className="h-14 rounded-xl" />)}
        </div>
      ) : entries.length === 0 ? (
        <div className="text-center py-12 text-gray-400">No leaderboard data yet for this period.</div>
      ) : (
        <div className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 overflow-hidden">
          {entries.map((entry, i) => (
            <motion.div
              key={entry.rank}
              initial={{ opacity: 0, x: -10 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ delay: i * 0.03 }}
              className={`flex items-center gap-4 px-5 py-3.5 border-b border-gray-100 dark:border-gray-700 last:border-0 ${entry.isCurrentUser ? 'bg-indigo-50 dark:bg-indigo-900/20' : ''}`}
            >
              <div className="w-8 text-center">
                {entry.rank <= 3 ? (
                  <span className={MEDAL_COLORS[entry.rank - 1]}>
                    {entry.rank === 1 ? <Crown className="w-5 h-5 mx-auto" /> : <Medal className="w-5 h-5 mx-auto" />}
                  </span>
                ) : (
                  <span className="text-sm font-semibold text-gray-400">#{entry.rank}</span>
                )}
              </div>
              <div className="flex-1 min-w-0">
                <div className="font-medium text-gray-900 dark:text-white text-sm truncate">
                  {entry.displayName} {entry.isCurrentUser && <span className="text-indigo-500 text-xs">(you)</span>}
                </div>
                <div className="text-xs text-gray-400">Level {entry.level}</div>
              </div>
              <div className="text-sm font-semibold text-gray-700 dark:text-gray-300">{entry.totalXp.toLocaleString()} XP</div>
            </motion.div>
          ))}
        </div>
      )}
    </LearnerDashboardShell>
  );
}
