'use client';

import { useEffect, useState } from 'react';
import dynamic from 'next/dynamic';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
<<<<<<< Updated upstream
  LineChart,
  Line,
  AreaChart,
  Area,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend
} from '@/components/charts/dynamic-recharts';
import {
=======
>>>>>>> Stashed changes
  TrendingUp,
  Activity,
  CheckCircle2,
  Send,
  Clock
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchTrendData, fetchCompletionData, fetchSubmissionVolume, fetchProgressEvidenceSummary } from '@/lib/api';
import type { ProgressEvidenceSummary, TrendPoint } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';

type CompletionPoint = { day: string; completed: number };
type VolumePoint = { week: string; submissions: number };

const chartSkeleton = <Skeleton className="h-full w-full rounded-2xl" />;

const TrendChart = dynamic(() => import('./_charts').then(m => m.TrendChart), {
  ssr: false,
  loading: () => chartSkeleton,
});
const CriterionChart = dynamic(() => import('./_charts').then(m => m.CriterionChart), {
  ssr: false,
  loading: () => chartSkeleton,
});
const CompletionChart = dynamic(() => import('./_charts').then(m => m.CompletionChart), {
  ssr: false,
  loading: () => chartSkeleton,
});
const VolumeChart = dynamic(() => import('./_charts').then(m => m.VolumeChart), {
  ssr: false,
  loading: () => chartSkeleton,
});

export default function ProgressDashboard() {
  const [criterionFilter, setCriterionFilter] = useState('Writing');
  const [trendData, setTrendData] = useState<TrendPoint[]>([]);
  const [completionData, setCompletionData] = useState<CompletionPoint[]>([]);
  const [volumeData, setVolumeData] = useState<VolumePoint[]>([]);
  const [progressSummary, setProgressSummary] = useState<ProgressEvidenceSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('progress_viewed');
    Promise.allSettled([
      fetchTrendData(),
      fetchCompletionData(),
      fetchSubmissionVolume(),
      fetchProgressEvidenceSummary(),
    ]).then((results) => {
      const [trendResult, completionResult, volumeResult, summaryResult] = results;
      if (trendResult.status === 'fulfilled') setTrendData(trendResult.value);
      if (completionResult.status === 'fulfilled') setCompletionData(completionResult.value as CompletionPoint[]);
      if (volumeResult.status === 'fulfilled') setVolumeData(volumeResult.value as VolumePoint[]);
      if (summaryResult.status === 'fulfilled') setProgressSummary(summaryResult.value);

      const anyFailed = results.some(r => r.status === 'rejected');
      const allFailed = results.every(r => r.status === 'rejected');

      if (allFailed) {
        setError('Failed to load progress data. Please try again.');
      } else if (anyFailed) {
        setError('Some progress data could not be loaded.');
      }
      setLoading(false);
    });
  }, []);

  const completedLast7 = completionData.reduce((sum, point) => sum + point.completed, 0);
  const averageTurnaroundLabel = progressSummary?.reviewUsage.averageTurnaroundHours
    ? `${progressSummary.reviewUsage.averageTurnaroundHours}h avg`
    : 'Pending';
  const hasTrendData = trendData.length > 0;

  const trendEmptyState = (
    <div className="flex h-full min-h-[240px] items-center justify-center rounded-3xl border border-dashed border-border bg-background-light/60 px-6 text-center">
      <div className="max-w-sm space-y-2">
        <p className="text-sm font-black uppercase tracking-widest text-muted">No trend data yet</p>
        <p className="text-sm text-muted">
          Complete a few scored submissions to unlock the movement chart across Reading, Listening, Writing, and Speaking.
        </p>
      </div>
    </div>
  );

  return (
    <LearnerDashboardShell
      pageTitle="Progress Dashboard"
      subtitle="Track your performance and activity over time"
      backHref="/"
    >
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Evidence Check"
          icon={TrendingUp}
          accent="primary"
          title="See whether recent effort is turning into better evidence"
          description="Use this page to connect score movement, completed work, and review throughput before you choose the next focus."
          highlights={[
            { icon: Activity, label: 'Trend coverage', value: trendData.length ? `${trendData.length} checkpoints` : 'Loading...' },
            { icon: CheckCircle2, label: 'Completed work', value: completionData.length ? `${completedLast7} tasks` : 'Loading...' },
            { icon: Clock, label: 'Review speed', value: averageTurnaroundLabel },
          ]}
        />

        {loading && (
          <div className="space-y-6">
            {[1, 2, 3].map(i => (
              <Skeleton key={i} className="h-64 rounded-2xl" />
            ))}
          </div>
        )}

        {!loading && error && (
          <InlineAlert variant="error">{error}</InlineAlert>
        )}

        {!loading && !error && (
          <>
            {/* 1. Sub-test Trend */}
            <MotionSection
              delayIndex={0}
              className="bg-surface rounded-2xl border border-border p-6 sm:p-8 shadow-sm"
            >
              <LearnerSurfaceSectionHeader
                eyebrow="Sub-test Performance Trend"
                title="See score movement across all skills"
                description="This chart should make the learner’s cross-subtest trajectory easy to read before they drill into detail."
                className="mb-6"
              />
              <div className="flex items-center gap-3 mb-6">
                <div className="w-10 h-10 rounded-xl bg-info/10 flex items-center justify-center shrink-0">
                  <TrendingUp className="w-5 h-5 text-info" />
                </div>
                <div>
                  <h2 className="text-base font-black text-navy">Sub-test Performance Trend</h2>
                  <p className="text-xs text-muted">Score progression across all skills</p>
                </div>
              </div>
              <div className="h-[240px] w-full sm:h-[280px] lg:h-[300px]" role="img" aria-label="Sub-test performance trend chart showing reading, listening, writing, and speaking scores over time">
                {hasTrendData ? <TrendChart data={trendData} /> : trendEmptyState}
              </div>
            </MotionSection>

            {/* 2. Criterion Trend */}
            <MotionSection
              delayIndex={1}
              className="bg-surface rounded-2xl border border-border p-6 sm:p-8 shadow-sm"
            >
              <LearnerSurfaceSectionHeader
                eyebrow="Criterion Trend"
                title="Filter deeper without losing the main story"
                description="Writing and speaking criterion filters should feel like a detailed continuation of the learner’s progress narrative."
                className="mb-6"
              />
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6">
                <div className="flex items-center gap-3">
                  <div className="w-10 h-10 rounded-xl bg-primary/10 flex items-center justify-center shrink-0">
                    <Activity className="w-5 h-5 text-primary" />
                  </div>
                  <div>
                    <h2 className="text-base font-black text-navy">Criterion Trend</h2>
                    <p className="text-xs text-muted">Deep dive into specific scoring criteria</p>
                  </div>
                </div>
                <div className="flex items-center gap-2 bg-background-light p-1 rounded-xl border border-border">
                  {['Writing', 'Speaking'].map(f => (
                    <button
                      key={f}
                      onClick={() => setCriterionFilter(f)}
                      className={`px-4 py-1.5 text-xs font-bold rounded-lg transition-colors ${criterionFilter === f ? 'bg-white text-navy shadow-sm' : 'text-muted hover:text-navy'}`}
                    >
                      {f}
                    </button>
                  ))}
                </div>
              </div>
              <div className="h-[240px] w-full sm:h-[280px] lg:h-[300px]" role="img" aria-label={`Criterion trend chart for ${criterionFilter} skills`}>
                {hasTrendData ? <CriterionChart data={trendData} criterionFilter={criterionFilter} /> : trendEmptyState}
              </div>
            </MotionSection>

            <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
              {/* 3. Completion Trend */}
              <MotionSection
                delayIndex={2}
                className="bg-surface rounded-2xl border border-border p-6 sm:p-8 shadow-sm"
              >
                <LearnerSurfaceSectionHeader
                  eyebrow="Completion Trend"
                  title="Keep task completion visible"
                  description="Progress is not only score movement; it is also whether the learner is actually completing planned work."
                  className="mb-6"
                />
                <div className="flex items-center gap-3 mb-6">
                  <div className="w-10 h-10 rounded-xl bg-success/10 flex items-center justify-center shrink-0">
                    <CheckCircle2 className="w-5 h-5 text-success" />
                  </div>
                  <div>
                    <h2 className="text-base font-black text-navy">Completion Trend</h2>
                    <p className="text-xs text-muted">Tasks completed over the last 7 days</p>
                  </div>
                </div>
                <div className="h-[220px] w-full sm:h-[240px] lg:h-[250px]">
                  <CompletionChart data={completionData} />
                </div>
              </MotionSection>

              {/* 4. Submission Volume */}
              <MotionSection
                delayIndex={3}
                className="bg-surface rounded-2xl border border-border p-6 sm:p-8 shadow-sm"
              >
                <LearnerSurfaceSectionHeader
                  eyebrow="Submission Volume"
                  title="Make writing and speaking effort visible"
                  description="Submission volume should reinforce how much evidence the learner has created, not sit as an isolated stat."
                  className="mb-6"
                />
                <div className="flex items-center gap-3 mb-6">
                  <div className="w-10 h-10 rounded-xl bg-warning/10 flex items-center justify-center shrink-0">
                    <Send className="w-5 h-5 text-warning" />
                  </div>
                  <div>
                    <h2 className="text-base font-black text-navy">Submission Volume</h2>
                    <p className="text-xs text-muted">Writing and Speaking tasks submitted</p>
                  </div>
                </div>
                <div className="h-[220px] w-full sm:h-[240px] lg:h-[250px]">
                  <VolumeChart data={volumeData} />
                </div>
              </MotionSection>
            </div>

            {/* 5. Review Usage */}
            <MotionSection
              delayIndex={4}
              className="bg-navy rounded-2xl p-6 sm:p-8 text-white shadow-lg relative overflow-hidden"
            >
              <div className="mb-6 relative z-10">
                <p className="text-xs font-black uppercase tracking-widest text-white/60 mb-2">Expert Review Turnaround</p>
                <h2 className="text-xl font-black text-white">Keep human-feedback timing visible</h2>
                <p className="text-sm text-white/70 mt-1">
                  Expert review should feel like part of the same learner system, with clear operational expectations.
                </p>
              </div>
              <div className="absolute top-0 right-0 w-64 h-64 bg-white opacity-5 rounded-full blur-3xl -translate-y-1/2 translate-x-1/3" />
              <div className="relative z-10 flex items-center gap-3">
                <div className="w-10 h-10 rounded-xl bg-white/10 flex items-center justify-center shrink-0">
                  <Clock className="w-5 h-5 text-white" />
                </div>
                <div>
                  <h2 className="text-base font-black">Expert Review Turnaround</h2>
                  <p className="text-xs text-white/70">Average time from submission to feedback</p>
                </div>
              </div>
              <div className="mt-6 bg-white/10 rounded-2xl p-5 border border-white/10 inline-block">
                <h3 className="text-xs font-bold text-white/70 uppercase tracking-widest mb-1">Avg Turnaround</h3>
                <div className="flex items-baseline gap-2">
                  <span className="text-4xl font-black">{progressSummary?.reviewUsage.averageTurnaroundHours ?? 'Pending'}</span>
                  <span className="text-sm font-bold text-white/70">hours</span>
                </div>
              </div>
              <p className="mt-4 text-xs text-white/60">
                {progressSummary?.freshness.usesFallbackSeries
                  ? 'Review timing is still based on limited data and will sharpen after more requests complete.'
                  : `Updated ${new Date(progressSummary?.freshness.generatedAt ?? new Date().toISOString()).toLocaleString()}.`}
              </p>
            </MotionSection>
          </>
        )}

      </div>
    </LearnerDashboardShell>
  );
}
