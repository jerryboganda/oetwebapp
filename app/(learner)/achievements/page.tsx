'use client';

import { useEffect, useState } from 'react';
import { Trophy, Flame, Star, Zap, Lock, ShieldCheck } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { ProgressBar } from '@/components/ui/progress';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { CountUp } from '@/components/ui/count-up';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerFreshnessIndicator } from '@/components/domain/learner-freshness-indicator';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { fetchXP, fetchStreak, fetchAchievements, applyStreakFreeze } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type XPData = { totalXP: number; weeklyXP: number; monthlyXP: number; level: number; nextLevelXP: number; currentLevelXP: number };
type StreakData = { currentStreak: number; longestStreak: number; lastActiveDate: string | null; streakFreezesAvailable: number };
type Achievement = { id: string; code: string; label: string; description: string; category: string; iconUrl: string | null; xpReward: number; sortOrder: number; unlocked: boolean; unlockedAt: string | null };

const CATEGORY_ICONS: Record<string, React.ReactNode> = {
  practice: <Star className="w-5 h-5" aria-hidden="true" />,
  streak: <Flame className="w-5 h-5" aria-hidden="true" />,
  milestone: <Trophy className="w-5 h-5" aria-hidden="true" />,
  mastery: <Zap className="w-5 h-5" aria-hidden="true" />,
  social: <Star className="w-5 h-5" aria-hidden="true" />,
  xp: <Zap className="w-5 h-5" aria-hidden="true" />,
};

const CATEGORY_COLORS: Record<string, string> = {
  practice: 'bg-info/10 text-info',
  streak: 'bg-warning/10 text-warning-strong',
  milestone: 'bg-primary/10 text-primary',
  mastery: 'bg-success/10 text-success-strong',
  social: 'bg-danger/10 text-danger-strong',
  xp: 'bg-primary/10 text-primary',
};

function categoryLabel(category: string) {
  return category.charAt(0).toUpperCase() + category.slice(1);
}

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
  const unlockedTotal = achievements.filter(a => a.unlocked).length;
  const latestUnlock = achievements
    .filter((achievement) => achievement.unlockedAt)
    .sort((first, second) => new Date(second.unlockedAt ?? '').getTime() - new Date(first.unlockedAt ?? '').getTime())[0]?.unlockedAt ?? null;

  return (
    <>
      <LearnerPageHero
        eyebrow="Momentum"
        title="Achievements, streaks, and XP in one place"
        description="Track the proof of consistent practice and see which milestones are still locked."
        icon={Trophy}
        accent="amber"
        highlights={[
          { icon: Zap, label: 'Level', value: xp ? String(xp.level) : loading ? 'Loading...' : 'Pending' },
          { icon: Flame, label: 'Current streak', value: streak ? `${streak.currentStreak} days` : loading ? 'Loading...' : 'Pending' },
          { icon: ShieldCheck, label: 'Unlocked', value: achievements.length ? `${unlockedTotal}/${achievements.length}` : loading ? 'Loading...' : 'No badges yet' },
        ]}
        aside={<LearnerFreshnessIndicator updatedAt={latestUnlock} staleAfterMinutes={10080} />}
      />

      {error && <InlineAlert variant="warning">{error}</InlineAlert>}

      {loading ? (
        <LearnerSkeleton variant="card-grid" />
      ) : (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
          <MotionItem delayIndex={0} className="h-full">
            <Card className="h-full">
              <div className="mb-1 flex items-center gap-2">
                <Zap className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                <span className="eyebrow text-muted">Level</span>
              </div>
              <div className="text-3xl font-bold tabular-nums text-navy">{xp ? <CountUp value={xp.level} /> : '–'}</div>
              <div className="mt-1 text-xs tabular-nums text-muted">{xp?.totalXP?.toLocaleString() ?? '0'} total XP</div>
            </Card>
          </MotionItem>

          <MotionItem delayIndex={1} className="h-full">
            <Card className="h-full">
              <div className="mb-2 flex items-center gap-2">
                <Zap className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                <span className="eyebrow text-muted">XP Progress</span>
              </div>
              {xp && (
                <>
                  <ProgressBar
                    value={xp.totalXP - xp.currentLevelXP}
                    max={xp.nextLevelXP - xp.currentLevelXP}
                    size="md"
                    color="primary"
                    ariaLabel="XP progress to next level"
                  />
                  <div className="mt-1.5 text-xs tabular-nums text-muted">{(xp.totalXP - xp.currentLevelXP).toLocaleString()} / {(xp.nextLevelXP - xp.currentLevelXP).toLocaleString()} to next level</div>
                </>
              )}
            </Card>
          </MotionItem>

          <MotionItem delayIndex={2} className="h-full">
            <Card className="h-full">
              <div className="mb-1 flex items-center gap-2">
                <Flame className="h-4 w-4 shrink-0 text-warning-strong" aria-hidden="true" />
                <span className="eyebrow text-muted">Streak</span>
              </div>
              <div className="text-3xl font-bold tabular-nums text-navy">{streak ? <CountUp value={streak.currentStreak} /> : '–'} <span className="text-base font-normal text-muted">days</span></div>
              <div className="mt-1 text-xs tabular-nums text-muted">Best: {streak?.longestStreak ?? 0} days</div>
              {streak && streak.streakFreezesAvailable > 0 && (
                <div className="mt-3">
                  <Button
                    variant="outline"
                    size="sm"
                    onClick={handleUseStreakFreeze}
                    disabled={freezing}
                  >
                    {freezing ? 'Applying...' : `Use Streak Freeze (${streak.streakFreezesAvailable})`}
                  </Button>
                  {freezeMsg && <p className="mt-1 text-xs text-muted" role="status">{freezeMsg}</p>}
                </div>
              )}
            </Card>
          </MotionItem>
        </div>
      )}

      <section>
        <LearnerSurfaceSectionHeader
          title="Achievement Library"
          description="Filter earned and locked badges without losing your current progress context."
          action={(
            <Tabs
              tabs={categories.map((cat) => ({ id: cat, label: categoryLabel(cat) }))}
              activeTab={filter}
              onChange={setFilter}
              scrollable={false}
              className="w-auto self-start sm:self-auto"
            />
          )}
          className="mb-5"
        />

        {loading ? (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3" aria-hidden="true">
            {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-28 rounded-2xl" />)}
          </div>
        ) : (
          <TabPanel id={filter} activeTab={filter} className="space-y-8">
            {filtered.length === 0 ? (
              <LearnerEmptyState
                icon={Trophy}
                title="No achievements in this category yet"
                description="Keep practicing or switch filters to review the full achievement library."
                primaryAction={{ label: 'Start Practice', href: '/study-plan' }}
                secondaryAction={{ label: 'Show All', onClick: () => setFilter('all') }}
              />
            ) : null}

            {unlocked.length > 0 && (
              <div>
                <h3 className="mb-3 text-base font-bold text-navy">Unlocked ({unlocked.length})</h3>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                  {unlocked.map((ach, i) => (
                    <MotionItem key={ach.id} delayIndex={Math.min(i, 5)} className="h-full">
                      <Card className="flex h-full items-start gap-3">
                        <div className={`shrink-0 rounded-lg p-2 ${CATEGORY_COLORS[ach.category] ?? 'bg-lavender text-primary'}`}>
                          {CATEGORY_ICONS[ach.category] ?? <Trophy className="w-5 h-5" aria-hidden="true" />}
                        </div>
                        <div className="min-w-0 flex-1">
                          <div className="text-sm font-semibold text-navy">{ach.label}</div>
                          <div className="mt-0.5 text-xs text-muted">{ach.description}</div>
                          <div className="mt-1.5 flex items-center gap-1">
                            <Zap className="h-3 w-3 text-primary" aria-hidden="true" />
                            <span className="text-xs font-medium tabular-nums text-primary">+{ach.xpReward} XP</span>
                          </div>
                        </div>
                      </Card>
                    </MotionItem>
                  ))}
                </div>
              </div>
            )}

            {locked.length > 0 && (
              <div>
                <h3 className="mb-3 text-base font-bold text-navy">Locked ({locked.length})</h3>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
                  {locked.map((ach, i) => (
                    <MotionItem key={ach.id} delayIndex={Math.min(i, 5)} className="h-full">
                      {/* Locked reads as a quieter surface, not as faded text: descriptions stay legible. */}
                      <Card className="flex h-full items-start gap-3 bg-background-light shadow-none">
                        <div className="shrink-0 rounded-lg bg-surface p-2 text-muted">
                          <Lock className="h-5 w-5" aria-hidden="true" />
                        </div>
                        <div className="min-w-0 flex-1">
                          <div className="text-sm font-semibold text-muted">{ach.label}</div>
                          <div className="mt-0.5 text-xs text-muted">{ach.description}</div>
                          <div className="mt-1.5 flex items-center gap-1 text-muted">
                            <Zap className="h-3 w-3" aria-hidden="true" />
                            <span className="text-xs tabular-nums">+{ach.xpReward} XP</span>
                          </div>
                        </div>
                      </Card>
                    </MotionItem>
                  ))}
                </div>
              </div>
            )}
          </TabPanel>
        )}
      </section>
    </>
  );
}
