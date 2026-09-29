'use client';

import { useContext, useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Trophy, Medal, Crown } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { AuthContext } from '@/contexts/auth-context';
import { fetchLeaderboard, fetchMyLeaderboardPosition, setLeaderboardOptIn } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { queryKeys } from '@/lib/query/hooks';

type LeaderboardEntry = { rank: number; displayName: string; totalXp: number; level: number; isCurrentUser?: boolean };
type MyPosition = { rank: number | null; totalXp: number; level: number; optedIn: boolean };

const MEDAL_COLORS = ['text-gold', 'text-muted', 'text-orange-600'];
const LEADERBOARD_ROW_CAP = 50;
const ANIMATED_ROW_CAP = 20;

function boundedEntries(entries: LeaderboardEntry[]): LeaderboardEntry[] {
  const topEntries = entries.slice(0, LEADERBOARD_ROW_CAP);
  const currentLearner = entries.find((entry) => entry.isCurrentUser);
  if (!currentLearner || topEntries.some((entry) => entry.rank === currentLearner.rank)) {
    return topEntries;
  }
  return [...topEntries.slice(0, LEADERBOARD_ROW_CAP - 1), currentLearner];
}

function LeaderboardRow({ entry, index }: { entry: LeaderboardEntry; index: number }) {
  const className = `flex items-center gap-4 px-5 py-3.5 border-b border-border last:border-0 ${entry.isCurrentUser ? 'bg-primary/10' : ''}`;
  const content = (
    <>
      <div className="w-8 text-center">
        {entry.rank <= 3 ? (
          <span className={MEDAL_COLORS[entry.rank - 1]} aria-label={`Rank ${entry.rank}`}>
            {entry.rank === 1 ? <Crown className="w-5 h-5 mx-auto" aria-hidden="true" /> : <Medal className="w-5 h-5 mx-auto" aria-hidden="true" />}
          </span>
        ) : (
          <span className="text-sm font-semibold text-muted/60">#{entry.rank}</span>
        )}
      </div>
      <div className="flex-1 min-w-0">
        <div className="font-medium text-navy text-sm truncate">
          {entry.displayName} {entry.isCurrentUser && <span className="text-primary text-xs">(you)</span>}
        </div>
        <div className="text-xs text-muted/60">Level {entry.level}</div>
      </div>
      <div className="text-sm font-semibold text-navy">{entry.totalXp.toLocaleString()} XP</div>
    </>
  );

  return index < ANIMATED_ROW_CAP ? (
    <MotionItem data-testid="leaderboard-entry" delayIndex={index} className={className}>
      {content}
    </MotionItem>
  ) : (
    <div data-testid="leaderboard-entry" className={className}>
      {content}
    </div>
  );
}

export default function LeaderboardPage() {
  const [period, setPeriod] = useState<'weekly' | 'monthly' | 'alltime'>('weekly');
  const [examType, setExamType] = useState<string>('oet');
  const [mutationError, setMutationError] = useState<string | null>(null);
  const authContext = useContext(AuthContext);
  const queryClient = useQueryClient();
  const queryUserId = authContext?.user?.userId ?? 'current';
  const queriesEnabled = authContext ? !authContext.loading && authContext.isAuthenticated : true;
  const normalizedExamType = examType || 'all';
  const leaderboardQuery = useQuery({
    queryKey: queryKeys.leaderboard.list(queryUserId, normalizedExamType, period),
    queryFn: () => fetchLeaderboard(examType || undefined, period),
    staleTime: 30_000,
    enabled: queriesEnabled,
  });
  const positionQuery = useQuery({
    queryKey: queryKeys.leaderboard.position(queryUserId, normalizedExamType, period),
    queryFn: () => fetchMyLeaderboardPosition(examType || undefined, period),
    staleTime: 30_000,
    enabled: queriesEnabled,
  });
  // The backend returns a bare JSON array (it never wrapped in { entries }),
  // so reading `.entries` here left the board permanently empty. Accept the
  // array, tolerating a wrapped shape if the contract ever changes.
  const rawLeaderboard = leaderboardQuery.data as LeaderboardEntry[] | { entries?: LeaderboardEntry[] } | undefined;
  const entries = boundedEntries(
    Array.isArray(rawLeaderboard) ? rawLeaderboard : (rawLeaderboard?.entries ?? []),
  );
  const myPos = (positionQuery.data ?? null) as MyPosition | null;
  const loading = queriesEnabled && (leaderboardQuery.isPending || positionQuery.isPending);
  const error = mutationError || (leaderboardQuery.error || positionQuery.error ? 'Could not load leaderboard.' : null);
  const optInMutation = useMutation({
    mutationFn: setLeaderboardOptIn,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: queryKeys.leaderboard.list(queryUserId, normalizedExamType, period),
        }),
        queryClient.invalidateQueries({
          queryKey: queryKeys.leaderboard.position(queryUserId, normalizedExamType, period),
        }),
      ]);
    },
  });

  useEffect(() => {
    analytics.track('leaderboard_viewed');
  }, []);

  async function toggleOptIn() {
    if (!myPos) return;
    setMutationError(null);
    try {
      await optInMutation.mutateAsync(!myPos.optedIn);
    } catch {
      setMutationError('Could not update preference.');
    }
  }

  return (
    <>
      <LearnerPageHero
        title="Leaderboard"
        description="See how you rank against other learners"
        icon={Trophy}
      />

      {error && (
        <InlineAlert
          variant="warning"
          className="mb-4"
          action={!mutationError ? (
            <Button size="sm" variant="outline" onClick={() => { void leaderboardQuery.refetch(); void positionQuery.refetch(); }}>
              Retry
            </Button>
          ) : undefined}
        >
          {error}
        </InlineAlert>
      )}

      {/* Controls */}
      <div className="flex flex-wrap gap-3 mb-6">
        <div className="flex rounded-lg border border-border overflow-hidden">
          {(['weekly', 'monthly', 'alltime'] as const).map(p => (
            <button
              key={p}
              type="button"
              onClick={() => setPeriod(p)}
              aria-pressed={period === p}
              className={`min-h-11 px-4 py-2 text-sm font-medium capitalize focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary ${period === p ? 'bg-primary text-white dark:bg-violet-700' : 'bg-surface text-muted hover:bg-background-light'}`}
            >
              {p === 'alltime' ? 'All Time' : p}
            </button>
          ))}
        </div>
        <select
          value={examType}
          onChange={e => setExamType(e.target.value)}
          aria-label="Exam type"
          className="min-h-11 px-3 py-2 text-sm border border-border rounded-lg bg-surface text-navy"
        >
          <option value="oet">OET</option>
        </select>
      </div>

      {/* My position */}
      {myPos && (
        <div className="bg-primary/10 border border-primary/30 rounded-2xl p-4 mb-6 flex flex-wrap items-center justify-between gap-3">
          <div>
            <div className="text-sm font-medium text-primary">Your Position</div>
            <div className="text-2xl font-bold text-primary">
              {myPos.optedIn ? (myPos.rank ? `#${myPos.rank}` : 'Unranked') : 'Not participating'}
            </div>
            <div className="text-sm text-primary">{myPos.totalXp.toLocaleString()} XP · Level {myPos.level}</div>
          </div>
          <Button
            onClick={toggleOptIn}
            loading={optInMutation.isPending}
            variant={myPos.optedIn ? 'outline' : 'primary'}
            className={myPos.optedIn ? 'shrink-0 bg-surface' : 'shrink-0'}
          >
            {myPos.optedIn ? 'Opt Out' : 'Join Rankings'}
          </Button>
        </div>
      )}

      {/* Table */}
      {loading ? (
        <div className="space-y-3">
          {Array.from({ length: 10 }).map((_, i) => <Skeleton key={i} className="h-14 rounded-xl" />)}
        </div>
      ) : entries.length === 0 ? (
        <LearnerEmptyState
          icon={Trophy}
          title="No leaderboard data yet for this period."
          description="Complete practice to earn XP and appear in the rankings."
          primaryAction={{ label: 'Open Study Plan', href: '/study-plan' }}
        />
      ) : (
        <div className="bg-surface rounded-2xl border border-border overflow-hidden">
          {entries.map((entry, i) => (
            <LeaderboardRow key={entry.rank} entry={entry} index={i} />
          ))}
        </div>
      )}
    </>
  );
}
