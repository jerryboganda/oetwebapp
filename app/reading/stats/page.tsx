'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, TrendingUp } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
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
    <LearnerDashboardShell pageTitle="Reading Stats">
      <main className="space-y-5 sm:space-y-8">
        {/* Back link */}
        <Link
          href="/reading"
          className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-navy"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden />
          Back to Reading
        </Link>

        <div>
          <h1 className="text-2xl font-bold text-navy">Reading Analytics</h1>
          <p className="mt-1 text-sm text-muted">Track your progress, activity, and skill development.</p>
        </div>

        {loading ? (
          <div className="space-y-4">
            {[...Array(3)].map((_, i) => (
              <div key={i} className="h-24 animate-pulse rounded-xl bg-border/80 dark:bg-border/50" />
            ))}
          </div>
        ) : (
          <>
            {dashboard ? (
              <DashboardHero
                readinessScore={dashboard.readinessScore}
                predictedScore={dashboard.predictedScore}
                daysToExam={daysToExam}
                streak={dashboard.streak}
              />
            ) : null}

            <div className="grid grid-cols-1 gap-6 md:grid-cols-2">
              {/* Skill Radar */}
              <div className="rounded-xl border border-border bg-surface p-5">
                <h2 className="mb-4 text-sm font-semibold text-navy">Skill Radar</h2>
                {skillRadar ? (
                  <SkillRadarChart data={skillRadar} />
                ) : (
                  <p className="text-sm text-muted">No skill data yet. Complete more practice to unlock this chart.</p>
                )}
              </div>

              {/* Score History */}
              <div className="rounded-xl border border-border bg-surface p-5">
                <h2 className="mb-4 flex items-center gap-2 text-sm font-semibold text-navy">
                  <TrendingUp className="h-4 w-4" aria-hidden />
                  Score History
                </h2>
                {scoreHistory?.history.length ? (
                  <ul className="space-y-2">
                    {scoreHistory.history.slice(-5).reverse().map((entry, i) => (
                      <li key={i} className="flex items-center justify-between text-sm">
                        <div>
                          <span className="font-medium text-navy">{entry.score}</span>
                          <span className="ml-2 text-xs text-muted capitalize">{entry.sessionType}</span>
                        </div>
                        <span className="text-xs text-muted">
                          {new Intl.DateTimeFormat('en-GB', { day: '2-digit', month: 'short' }).format(new Date(entry.date))}
                        </span>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <p className="text-sm text-muted">No score history yet.</p>
                )}
              </div>

              {/* Activity Heatmap */}
              <div className="rounded-xl border border-border bg-surface p-5 md:col-span-2">
                <h2 className="mb-4 text-sm font-semibold text-navy">Activity (last 90 days)</h2>
                {activityCalendar?.days.length ? (
                  <ActivityHeatmap days={activityCalendar.days} />
                ) : (
                  <p className="text-sm text-muted">No activity data yet.</p>
                )}
              </div>

              {/* Vocab Summary */}
              {vocabStats ? (
                <div className="rounded-xl border border-border bg-surface p-5">
                  <h2 className="mb-4 text-sm font-semibold text-navy">Vocabulary Summary</h2>
                  <div className="grid grid-cols-3 gap-3">
                    {[
                      { label: 'Total', value: vocabStats.total },
                      { label: 'Mastered', value: vocabStats.mastered },
                      { label: 'Due Today', value: vocabStats.dueToday },
                    ].map(({ label, value }) => (
                      <div key={label} className="rounded-lg bg-background-light dark:bg-background-dark px-3 py-3 text-center">
                        <p className="text-lg font-bold text-navy tabular-nums">{value}</p>
                        <p className="text-xs text-muted">{label}</p>
                      </div>
                    ))}
                  </div>
                </div>
              ) : null}
            </div>
          </>
        )}
      </main>
    </LearnerDashboardShell>
  );
}
