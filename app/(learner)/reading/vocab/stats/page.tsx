'use client';

import { useEffect, useState } from 'react';
import { Brain, CalendarCheck, PieChart as PieChartIcon, Sigma } from 'lucide-react';
import { PieChart, Pie, Cell, Tooltip, Legend, ResponsiveContainer } from '@/components/charts/dynamic-recharts';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { StatCard } from '@/components/ui/stat-card';
import { useAuth } from '@/contexts/auth-context';
import { getVocabStats, type VocabStatsDto } from '@/lib/reading-pathway-api';

// Chart series keep their meaning: mastered is good, struggling needs work.
const CHART_COLORS = {
  mastered:   'var(--color-success)',
  learning:   'var(--color-info)',
  struggling: 'var(--color-danger)',
};

export default function VocabStatsPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const [stats, setStats] = useState<VocabStatsDto | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) { setLoading(false); return; }

    let cancelled = false;
    (async () => {
      try {
        const s = await getVocabStats();
        if (!cancelled) setStats(s);
      } catch {
        // leave null
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [authLoading, isAuthenticated]);

  const chartData = stats
    ? [
        { name: 'Mastered',   value: stats.mastered,   color: CHART_COLORS.mastered },
        { name: 'Learning',   value: stats.learning,   color: CHART_COLORS.learning },
        { name: 'Struggling', value: stats.struggling, color: CHART_COLORS.struggling },
      ].filter((d) => d.value > 0)
    : [];

  // The breadcrumb's "Vocab" crumb is the way back, so no back link in the header.
  return (
    <>
      <LearnerPageHero
        eyebrow="Spaced Repetition"
        icon={PieChartIcon}
        title="Vocabulary Stats"
        description=""
      />

      {loading ? (
        <>
          <Skeleton className="h-72 w-full rounded-2xl" />
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-24 rounded-xl" />
            ))}
          </div>
        </>
      ) : !stats ? (
        // A failed load used to show a zero deck; say it failed instead.
        <ErrorState />
      ) : (
        <>
          {/* Donut chart */}
          <MotionSection>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader title="Word Breakdown" className="mb-4" />
              {chartData.length > 0 ? (
                <ResponsiveContainer width="100%" height={260}>
                  <PieChart>
                    <Pie
                      data={chartData}
                      cx="50%"
                      cy="50%"
                      innerRadius={70}
                      outerRadius={110}
                      paddingAngle={3}
                      dataKey="value"
                    >
                      {chartData.map((entry) => (
                        <Cell key={entry.name} fill={entry.color} stroke="none" />
                      ))}
                    </Pie>
                    <Tooltip
                      formatter={(value, name) => [value ?? 0, name ?? '']}
                      contentStyle={{
                        borderRadius: '0.75rem',
                        border: '1px solid var(--color-border)',
                        fontSize: '0.8rem',
                      }}
                    />
                    <Legend
                      iconType="circle"
                      iconSize={10}
                      formatter={(value) => (
                        <span className="text-sm text-muted">{value}</span>
                      )}
                    />
                  </PieChart>
                </ResponsiveContainer>
              ) : (
                <EmptyState
                  className="py-8"
                  icon={<PieChartIcon className="h-7 w-7" aria-hidden />}
                  title="No words in your deck yet."
                  description="Add some words to see the breakdown."
                />
              )}
            </Card>
          </MotionSection>

          {/* Stat cards: counts, not statuses, so one neutral tile style. */}
          <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <MotionItem delayIndex={0}>
              <StatCard label="Total Words" value={<CountUp value={stats.total} />} icon={<Sigma />} />
            </MotionItem>
            <MotionItem delayIndex={1}>
              <StatCard
                label="Average Retention"
                value={<CountUp value={Math.round(stats.averageRetention)} suffix="%" />}
                icon={<Brain />}
              />
            </MotionItem>
            <MotionItem delayIndex={2}>
              <StatCard label="Due Today" value={<CountUp value={stats.dueToday} />} icon={<CalendarCheck />} />
            </MotionItem>
          </section>
        </>
      )}
    </>
  );
}
