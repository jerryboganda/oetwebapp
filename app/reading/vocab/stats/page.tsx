'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { PieChart, Pie, Cell, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import { LearnerDashboardShell } from '@/components/layout';
import { useAuth } from '@/contexts/auth-context';
import { getVocabStats, type VocabStatsDto } from '@/lib/reading-pathway-api';

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

  return (
    <LearnerDashboardShell pageTitle="Vocab Stats">
      <main className="space-y-10">
        <div className="flex items-center justify-between">
          <div>
            <p className="mb-0.5 text-xs font-semibold uppercase tracking-widest text-violet-500">
              Spaced Repetition
            </p>
            <h1 className="text-2xl font-bold text-navy">
              Vocabulary Stats
            </h1>
          </div>
          <Link
            href="/reading/vocab"
            className="text-sm font-medium text-violet-600 hover:underline dark:text-violet-400"
          >
            ← Back
          </Link>
        </div>

        {loading ? (
          <div className="flex h-64 items-center justify-center">
            <div className="h-10 w-10 motion-safe:animate-spin rounded-full border-4 border-violet-200 border-t-violet-600" />
          </div>
        ) : (
          <>
            {/* Donut chart */}
            <section className="rounded-2xl border border-border bg-surface px-6 py-6">
              <h2 className="mb-4 text-base font-semibold text-navy">
                Word Breakdown
              </h2>
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
                <p className="py-12 text-center text-sm text-muted">
                  No words in your deck yet. Add some words to see the breakdown.
                </p>
              )}
            </section>

            {/* Stat cards */}
            <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              <StatCard
                label="Total Words"
                value={stats?.total ?? 0}
                colorClass="text-violet-700 dark:text-violet-300"
              />
              <StatCard
                label="Average Retention"
                value={stats ? `${Math.round(stats.averageRetention)}%` : '–'}
                colorClass="text-blue-700 dark:text-blue-300"
              />
              <StatCard
                label="Due Today"
                value={stats?.dueToday ?? 0}
                colorClass="text-amber-700 dark:text-amber-300"
              />
            </section>

            {/* Retention chart placeholder */}
            <section className="rounded-2xl border border-dashed border-border bg-background-light px-6 py-8 text-center">
              <p className="text-sm font-medium text-muted">
                Retention chart coming soon
              </p>
              <p className="mt-1 text-xs text-muted">
                Daily retention trends will appear here once you complete more review sessions.
              </p>
            </section>
          </>
        )}
      </main>
    </LearnerDashboardShell>
  );
}

function StatCard({ label, value, colorClass }: { label: string; value: number | string; colorClass: string }) {
  return (
    <div className="rounded-xl border border-border bg-surface px-5 py-4">
      <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-muted">
        {label}
      </p>
      <p className={`text-3xl font-bold ${colorClass}`}>{value}</p>
    </div>
  );
}