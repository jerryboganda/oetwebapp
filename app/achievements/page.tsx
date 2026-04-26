'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionItem } from '@/components/ui/motion-primitives';
import { ProgressBar } from '@/components/ui/progress';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { applyStreakFreeze, fetchAchievements, fetchStreak, fetchXP } from '@/lib/api';
import { Flame, Lock, Star, Trophy, Zap } from 'lucide-react';
import { useEffect, useState } from 'react';

type XPData = { totalXP: number; weeklyXP: number; monthlyXP: number; level: number; nextLevelXP: number; currentLevelXP: number };
type StreakData = { currentStreak: number; longestStreak: number; lastActiveDate: string | null; streakFreezesAvailable: number };
type Achievement = { id: string; code: string; label: string; description: string; category: string; iconUrl: string | null; xpReward: number; sortOrder: number; unlocked: boolean; unlockedAt: string | null };

const CATEGORY_ICONS: Record<string, React.ReactNode> = {
  practice: <Star className="w-5 h-5" />,
  streak: <Flame className="w-5 h-5" />,
  milestone: <Trophy className="w-5 h-5" />,
  mastery: <Zap className="w-5 h-5" />,
  social: <Star className="w-5 h-5" />,
  xp: <Zap className="w-5 h-5" />,
};

const CATEGORY_COLORS: Record<string, string> = {
  practice: 'bg-info/10 text-info',
  streak: 'bg-warning/10 text-warning',
  milestone: 'bg-primary/10 text-primary',
  mastery: 'bg-success/10 text-success',
  social: 'bg-danger/10 text-danger',
  xp: 'bg-primary/10 text-primary',
};

export default function AchievementsPage() {
  const [xp, setXp] = useState<XPData | null>(null);
  const [streak, setStreak] = useState<StreakData | null>(null);
  const [achievements, setAchievements] = useState<Achievement[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<string>('all');
  const [freezing, setFreezing] = useState(false);
  const [freezeMsg, setFreezeMsg] = useState<string | null>(null);

  const handleUseStreakFreeze = async () => {
    setFreezing(true);
    setFreezeMsg(null);
    try {
      const result = await applyStreakFreeze();
      setFreezeMsg(result.message);
      if (result.applied && streak) {
        setStreak({ ...streak, streakFreezesAvailable: Math.max(0, streak.streakFreezesAvailable - 1) });
      }
      analytics.track('streak_freeze_used', { applied: result.applied });
    } catch {
      setFreezeMsg('Failed to apply streak freeze.');
    } finally {
      setFreezing(false);
    }
  };

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
  const unlocked = filtered.filter(a => a.unlocked);
  const locked = filtered.filter(a => !a.unlocked);

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
          Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-28 rounded-2xl" />)
        ) : (
          <>
            <MotionItem delayIndex={0}>
              <Card>
                <div className="flex items-center gap-2 mb-1">
                  <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary/10">
                    <Zap className="w-4 h-4 text-primary" />
                  </div>
                  <span className="text-sm font-semibold text-muted">Level</span>
                </div>
                <div className="text-3xl font-bold text-navy">{xp?.level ?? '—'}</div>
                <div className="text-xs text-muted mt-1">{xp?.totalXP?.toLocaleString() ?? '0'} total XP</div>
              </Card>
            </MotionItem>

            <MotionItem delayIndex={1}>
              <Card>
                <div className="flex items-center gap-2 mb-2">
                  <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary/10">
                    <Zap className="w-4 h-4 text-primary" />
                  </div>
                  <span className="text-sm font-semibold text-muted">XP Progress</span>
                </div>
                {xp && (
                  <>
                    <ProgressBar
                      value={xp.totalXP - xp.currentLevelXP}
                      max={xp.nextLevelXP - xp.currentLevelXP}
                      size="md"
                      color="primary"
                    />
                    <div className="text-xs text-muted mt-1.5">{(xp.totalXP - xp.currentLevelXP).toLocaleString()} / {(xp.nextLevelXP - xp.currentLevelXP).toLocaleString()} to next level</div>
                  </>
                )}
              </Card>
            </MotionItem>

            <MotionItem delayIndex={2}>
              <Card>
                <div className="flex items-center gap-2 mb-1">
                  <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-warning/10">
                    <Flame className="w-4 h-4 text-warning" />
                  </div>
                  <span className="text-sm font-semibold text-muted">Streak</span>
                </div>
                <div className="text-3xl font-bold text-navy">{streak?.currentStreak ?? '—'} <span className="text-base font-normal text-muted">days</span></div>
                <div className="text-xs text-muted mt-1">Best: {streak?.longestStreak ?? 0} days</div>
                {streak && streak.streakFreezesAvailable > 0 && (
                  <div className="mt-2">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={handleUseStreakFreeze}
                      disabled={freezing}
                      className="text-xs"
                    >
                      {freezing ? 'Applying...' : `Use Streak Freeze (${streak.streakFreezesAvailable})`}
                    </Button>
                    {freezeMsg && <p className="text-xs text-muted mt-1">{freezeMsg}</p>}
                  </div>
                )}
              </Card>
            </MotionItem>
          </>
        )}
      </div>

      {/* Category filter */}
      <div className="flex flex-wrap items-center gap-2 bg-background-light p-1.5 rounded-xl border border-border mb-6">
        {categories.map(cat => (
          <button
            key={cat}
            onClick={() => setFilter(cat)}
            className={`px-4 py-1.5 text-xs font-bold rounded-lg capitalize transition-colors ${filter === cat ? 'bg-surface text-navy shadow-sm' : 'text-muted hover:text-navy'}`}
          >
            {cat}
          </button>
        ))}
      </div>

      {loading ? (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-28 rounded-2xl" />)}
        </div>
      ) : (
        <>
          {unlocked.length > 0 && (
            <>
              <LearnerSurfaceSectionHeader title={`Unlocked (${unlocked.length})`} />
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 mb-8">
                {unlocked.map((ach, i) => (
                  <MotionItem key={ach.id} delayIndex={i}>
                    <Card className="flex items-start gap-3">
                      <div className={`p-2 rounded-lg ${CATEGORY_COLORS[ach.category] ?? 'bg-lavender text-primary'}`}>
                        {CATEGORY_ICONS[ach.category] ?? <Trophy className="w-5 h-5" />}
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="font-semibold text-navy text-sm">{ach.label}</div>
                        <div className="text-xs text-muted mt-0.5">{ach.description}</div>
                        <div className="flex items-center gap-1 mt-1.5">
                          <Zap className="w-3 h-3 text-primary" />
                          <span className="text-xs font-medium text-primary">+{ach.xpReward} XP</span>
                        </div>
                      </div>
                    </Card>
                  </MotionItem>
                ))}
              </div>
            </>
          )}

          {locked.length > 0 && (
            <>
              <LearnerSurfaceSectionHeader title={`Locked (${locked.length})`} />
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                {locked.map((ach, i) => (
                  <MotionItem key={ach.id} delayIndex={i}>
                    <Card className="flex items-start gap-3 opacity-60">
                      <div className="p-2 rounded-lg bg-background-light text-muted">
                        <Lock className="w-5 h-5" />
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="font-semibold text-muted text-sm">{ach.label}</div>
                        <div className="text-xs text-muted/70 mt-0.5">{ach.description}</div>
                        <div className="flex items-center gap-1 mt-1.5">
                          <Zap className="w-3 h-3 text-muted/50" />
                          <span className="text-xs text-muted/50">+{ach.xpReward} XP</span>
                        </div>
                      </div>
                    </Card>
                  </MotionItem>
                ))}
              </div>
            </>
          )}
        </>
      )}
    </LearnerDashboardShell>
  );
}
