'use client';

import { useContext, useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Trophy, Medal, Crown } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Select } from '@/components/ui/form-controls';
import { Tabs, TabPanel } from '@/components/ui/tabs';
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
const PERIOD_TABS = [
  { id: 'weekly', label: 'Weekly' },
  { id: 'monthly', label: 'Monthly' },
  { id: 'alltime', label: 'All Time' },
];
const EXAM_TYPE_OPTIONS = [{ value: 'oet', label: 'OET' }];

function boundedEntries(entries: LeaderboardEntry[]): LeaderboardEntry[] {
  const topEntries = entries.slice(0, LEADERBOARD_ROW_CAP);
  const currentLearner = entries.find((entry) => entry.isCurrentUser);
  if (!currentLearner || topEntries.some((entry) => entry.rank === currentLearner.rank)) {
    return topEntries;
  }
  return [...topEntries.slice(0, LEADERBOARD_ROW_CAP - 1), currentLearner];
}

function LeaderboardRow({ entry, index }: { entry: LeaderboardEntry; index: number }) {
  const className = `flex items-center gap-3 border-b border-border px-4 py-3.5 last:border-0 sm:gap-4 sm:px-5 ${entry.isCurrentUser ? 'bg-primary/10' : ''}`;
  const content = (
    <>
      <div className="w-8 shrink-0 text-center">
        {entry.rank <= 3 ? (
          <span className={MEDAL_COLORS[entry.rank - 1]} aria-label={`Rank ${entry.rank}`}>
            {entry.rank === 1 ? <Crown className="w-5 h-5 mx-auto" aria-hidden="true" /> : <Medal className="w-5 h-5 mx-auto" aria-hidden="true" />}
          </span>
        ) : (
          <span className="text-sm font-semibold tabular-nums text-muted">#{entry.rank}</span>
        )}
      </div>
      <div className="min-w-0 flex-1">
        <div className="truncate text-sm font-medium text-navy">
          {entry.displayName} {entry.isCurrentUser && <span className="text-xs text-primary">(you)</span>}
        </div>
        <div className="text-xs tabular-nums text-muted">Level {entry.level}</div>
      </div>
      <div className="shrink-0 text-sm font-semibold tabular-nums text-navy">{entry.totalXp.toLocaleString()} XP</div>
    </>
  );

  return index < ANIMATED_ROW_CAP ? (
    <MotionItem data-testid="leaderboard-entry" delayIndex={Math.min(index, 5)} className={className}>
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
      <div className="flex flex-wrap items-center gap-3">
        <Tabs
          tabs={PERIOD_TABS}
          activeTab={period}
          onChange={(id) => setPeriod(id as typeof period)}
          scrollable={false}
          className="w-auto"
        />
        <Select
          options={EXAM_TYPE_OPTIONS}
          value={examType}
          onChange={e => setExamType(e.target.value)}
          aria-label="Exam type"
          className="min-h-11 py-2"
        />
      </div>

      <TabPanel id={period} activeTab={period} className="learner-page-flow">
        {/* My position */}
        {myPos && (
          <Card className="flex flex-wrap items-center justify-between gap-3 border-primary/30 bg-primary/10">
            <div className="min-w-0">
              <div className="eyebrow text-primary">Your Position</div>
              <div className="mt-1 text-2xl font-bold tabular-nums text-primary">
                {myPos.optedIn ? (myPos.rank ? `#${myPos.rank}` : 'Unranked') : 'Not participating'}
              </div>
              <div className="text-sm tabular-nums text-primary">{myPos.totalXp.toLocaleString()} XP · Level {myPos.level}</div>
            </div>
            <Button
              onClick={toggleOptIn}
              loading={optInMutation.isPending}
              variant={myPos.optedIn ? 'outline' : 'primary'}
              className={myPos.optedIn ? 'shrink-0 bg-surface' : 'shrink-0'}
            >
              {myPos.optedIn ? 'Opt Out' : 'Join Rankings'}
            </Button>
          </Card>
        )}

        {/* Table */}
        {loading ? (
          <div className="space-y-3" aria-hidden="true">
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
          <Card padding="none" className="overflow-hidden">
            {entries.map((entry, i) => (
              <LeaderboardRow key={entry.rank} entry={entry} index={i} />
            ))}
          </Card>
        )}
      </TabPanel>
    </>
  );
}
