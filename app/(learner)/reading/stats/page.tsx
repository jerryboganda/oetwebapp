'use client';

import { useEffect, useState } from 'react';
import { CalendarDays, History, Radar, TrendingUp } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { DashboardHero } from '@/components/reading/DashboardHero';
import { SkillRadarChart } from '@/components/reading/SkillRadarChart';
import { ActivityHeatmap } from '@/components/reading/ActivityHeatmap';
import {
  getReadingDashboard,
  getSkillRadar,
  getScoreHistory,
  getActivityCalendar,
  getVocabStats,
  type ReadingDashboardDto,
  type SkillRadarDto,
  type ScoreHistoryDto,
  type ActivityCalendarDto,
  type VocabStatsDto,
} from '@/lib/reading-pathway-api';

export default function ReadingStatsPage() {
  const [dashboard, setDashboard] = useState<ReadingDashboardDto | null>(null);
  const [skillRadar, setSkillRadar] = useState<SkillRadarDto | null>(null);
  const [scoreHistory, setScoreHistory] = useState<ScoreHistoryDto | null>(null);
  const [activityCalendar, setActivityCalendar] = useState<ActivityCalendarDto | null>(null);
  const [vocabStats, setVocabStats] = useState<VocabStatsDto | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const [dash, radar, history, calendar, vocab] = await Promise.allSettled([
          getReadingDashboard(),
          getSkillRadar(),
          getScoreHistory(),
          getActivityCalendar(),
          getVocabStats(),
        ]);
        if (cancelled) return;
        if (dash.status === 'fulfilled') setDashboard(dash.value);
        if (radar.status === 'fulfilled') setSkillRadar(radar.value);
        if (history.status === 'fulfilled') setScoreHistory(history.value);
        if (calendar.status === 'fulfilled') setActivityCalendar(calendar.value);
        if (vocab.status === 'fulfilled') setVocabStats(vocab.value);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, []);

  const daysToExam: number | null = null; // derived from profile exam date if available

  return (
    <>
      <LearnerPageHero
        eyebrow="Reading"
        icon={TrendingUp}
        accent="blue"
        title="Reading Analytics"
        description="Track your progress, activity, and skill development."
      />

      {loading ? (
        <>
          {[...Array(3)].map((_, i) => (
            <Skeleton key={i} className="h-24 rounded-xl" />
          ))}
        </>
      ) : (
        <>
          {dashboard ? (
            <MotionSection delayIndex={0}>
              <DashboardHero
                readinessScore={dashboard.readinessScore}
                predictedScore={dashboard.predictedScore}
                daysToExam={daysToExam}
                streak={dashboard.streak}
              />
            </MotionSection>
          ) : null}

          <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
            <MotionItem delayIndex={0} className="h-full">
              <Card className="h-full">
                <LearnerSurfaceSectionHeader title="Skill Radar" className="mb-4" />
                {skillRadar ? (
                  <SkillRadarChart data={skillRadar} />
                ) : (
                  <EmptyState
                    className="py-8"
                    icon={<Radar className="h-7 w-7" aria-hidden />}
                    title="No skill data yet."
                    description="Complete more practice to unlock this chart."
                  />
                )}
              </Card>
            </MotionItem>

            <MotionItem delayIndex={1} className="h-full">
              <Card className="h-full">
                <LearnerSurfaceSectionHeader icon={TrendingUp} title="Score History" className="mb-4" />
                {scoreHistory?.history.length ? (
                  <ul className="divide-y divide-border">
                    {scoreHistory.history.slice(-5).reverse().map((entry, i) => (
                      <li key={i} className="flex items-center justify-between gap-3 py-2.5 text-sm first:pt-0 last:pb-0">
                        <div className="min-w-0">
                          <span className="font-medium tabular-nums text-navy">{entry.score}</span>
                          <span className="ms-2 text-xs capitalize text-muted">{entry.sessionType}</span>
                        </div>
                        <span className="shrink-0 text-xs tabular-nums text-muted">
                          {new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short' }).format(new Date(entry.date))}
                        </span>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <EmptyState className="py-8" icon={<History className="h-7 w-7" aria-hidden />} title="No score history yet." />
                )}
              </Card>
            </MotionItem>

            <MotionItem delayIndex={2} className="md:col-span-2">
              <Card>
                <LearnerSurfaceSectionHeader title="Activity (last 90 days)" className="mb-4" />
                {activityCalendar?.days.length ? (
                  <ActivityHeatmap days={activityCalendar.days} />
                ) : (
                  <EmptyState className="py-8" icon={<CalendarDays className="h-7 w-7" aria-hidden />} title="No activity data yet." />
                )}
              </Card>
            </MotionItem>

            {vocabStats ? (
              <MotionItem delayIndex={3} className="h-full">
                <Card className="h-full">
                  <LearnerSurfaceSectionHeader title="Vocabulary Summary" className="mb-4" />
                  <div className="grid grid-cols-3 gap-3">
                    {[
                      { label: 'Total', value: vocabStats.total },
                      { label: 'Mastered', value: vocabStats.mastered },
                      { label: 'Due Today', value: vocabStats.dueToday },
                    ].map(({ label, value }) => (
                      <div key={label} className="min-w-0 rounded-xl border border-border bg-background-light px-2 py-3 text-center">
                        <p className="text-lg font-bold tabular-nums text-navy"><CountUp value={value} /></p>
                        <p className="tile-label text-muted">{label}</p>
                      </div>
                    ))}
                  </div>
                </Card>
              </MotionItem>
            ) : null}
          </div>
        </>
      )}
    </>
  );
}
