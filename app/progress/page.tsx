'use client';

import { useEffect, useState } from 'react';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
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
} from 'recharts';
import {
  TrendingUp,
  Activity,
  CheckCircle2,
  Clock
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { fetchTrendData, fetchCompletionData, fetchSubmissionVolume, fetchProgressEvidenceSummary } from '@/lib/api';
import type { ProgressEvidenceSummary, TrendPoint } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerFreshnessIndicator } from '@/components/domain/learner-freshness-indicator';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';

type CompletionPoint = { day: string; completed: number };
type VolumePoint = { week: string; submissions: number };

const CHART_COLORS = {
  primary: '#7c3aed',
  info: '#2563eb',
  success: '#10b981',
  warning: '#d97706',
  danger: '#ef4444',
  navy: '#0f172a',
  muted: '#526072',
  border: '#d8e0e8',
} as const;

const CHART_TICK = { fontSize: 12, fill: CHART_COLORS.muted } as const;
const CHART_TOOLTIP_STYLE = { borderRadius: '16px', border: 'none', boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)' } as const;

function ChartEmptyState({ title, description }: { title: string; description: string }) {
  return (
    <div className="flex h-full min-h-[240px] items-center justify-center">
      <LearnerEmptyState
        compact
        icon={TrendingUp}
        title={title}
        description={description}
        primaryAction={{ label: 'Start Practice', href: '/writing' }}
        secondaryAction={{ label: 'Open Study Plan', href: '/study-plan' }}
      />
    </div>
  );
}

function SrChartSummary({ children }: { children: string }) {
  return <p className="sr-only">{children}</p>;
}

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
  const hasCompletionData = completionData.length > 0;
  const hasVolumeData = volumeData.length > 0;
  const hasAnyProgressData = hasTrendData || hasCompletionData || hasVolumeData || Boolean(progressSummary);
  const generatedAt = progressSummary?.freshness.generatedAt ?? null;

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
          description="Track your score trends, completed work, and review activity to choose your next priority."
          highlights={[
            { icon: Activity, label: 'Trend coverage', value: trendData.length ? `${trendData.length} checkpoints` : 'Loading...' },
            { icon: CheckCircle2, label: 'Completed work', value: completionData.length ? `${completedLast7} tasks` : 'Loading...' },
            { icon: Clock, label: 'Review speed', value: averageTurnaroundLabel },
          ]}
          aside={<LearnerFreshnessIndicator updatedAt={generatedAt} staleAfterMinutes={1440} />}
        />

        {loading && (
          <LearnerSkeleton variant="dashboard" />
        )}

        {!loading && error && (
          <InlineAlert variant={hasAnyProgressData ? 'warning' : 'error'}>{error}</InlineAlert>
        )}

        {!loading && (
          <>
            {!hasAnyProgressData ? (
              <LearnerEmptyState
                icon={Activity}
                title="No progress evidence yet"
                description="Complete diagnostic work, practice submissions, or mock tests to unlock charts and review timing insights."
                primaryAction={{ label: 'Take Diagnostic', href: '/diagnostic' }}
                secondaryAction={{ label: 'Start Writing Practice', href: '/writing' }}
              />
            ) : null}

            {/* 1. Sub-test Trend */}
            <MotionSection
              delayIndex={0}
              className="bg-surface rounded-2xl border border-border p-6 sm:p-8 shadow-sm"
            >
              <LearnerSurfaceSectionHeader
                eyebrow="Sub-test Performance Trend"
                title="See score movement across all skills"
                description="Compare your trajectory across all four sub-tests at a glance."
                className="mb-6"
              />
              <div className="h-[240px] w-full sm:h-[280px] lg:h-[300px]" role="img" aria-label="Sub-test performance trend chart showing reading, listening, writing, and speaking scores over time">
                <SrChartSummary>
                  {hasTrendData
                    ? `Sub-test trend has ${trendData.length} checkpoints. Latest checkpoint is ${trendData[trendData.length - 1]?.date ?? 'unknown'}.`
                    : 'No score trend is available yet.'}
                </SrChartSummary>
                {hasTrendData ? (
                  <ResponsiveContainer width="100%" height="100%" minWidth={0}>
                    <LineChart data={trendData} margin={{ top: 5, right: 20, bottom: 5, left: 0 }}>
                      <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={CHART_COLORS.border} />
                      <XAxis dataKey="date" axisLine={false} tickLine={false} tick={CHART_TICK} dy={10} />
                      <YAxis axisLine={false} tickLine={false} tick={CHART_TICK} />
                      <Tooltip contentStyle={CHART_TOOLTIP_STYLE} />
                      <Legend iconType="circle" wrapperStyle={{ fontSize: '12px', paddingTop: '20px' }} />
                      <Line type="monotone" dataKey="reading" name="Reading" stroke={CHART_COLORS.info} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                      <Line type="monotone" dataKey="listening" name="Listening" stroke={CHART_COLORS.primary} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                      <Line type="monotone" dataKey="writing" name="Writing" stroke={CHART_COLORS.danger} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                      <Line type="monotone" dataKey="speaking" name="Speaking" stroke={CHART_COLORS.navy} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                    </LineChart>
                  </ResponsiveContainer>
                ) : (
                  <ChartEmptyState
                    title="No trend data yet"
                    description="Complete a few scored submissions to unlock movement across Reading, Listening, Writing, and Speaking."
                  />
                )}
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
                description="Filter Writing and Speaking by individual criterion to see exactly where you're improving."
                className="mb-6"
              />
              <div className="flex flex-col sm:flex-row sm:items-center justify-end gap-4 mb-6">
                <div className="flex items-center gap-2 bg-background-light p-1 rounded-xl border border-border">
                  {['Writing', 'Speaking'].map(f => (
                    <button
                      key={f}
                      type="button"
                      onClick={() => setCriterionFilter(f)}
                      aria-pressed={criterionFilter === f}
                      aria-label={`Show ${f} criterion trend`}
                      className={`px-4 py-1.5 text-xs font-bold rounded-lg transition-colors ${criterionFilter === f ? 'bg-white text-navy shadow-sm' : 'text-muted hover:text-navy'}`}
                    >
                      {f}
                    </button>
                  ))}
                </div>
              </div>
              <div className="h-[240px] w-full sm:h-[280px] lg:h-[300px]" role="img" aria-label={`Criterion trend chart for ${criterionFilter} skills`}>
                <SrChartSummary>
                  {hasTrendData
                    ? `${criterionFilter} criterion trend is displayed using the same ${trendData.length} score checkpoints.`
                    : `No ${criterionFilter} criterion trend is available yet.`}
                </SrChartSummary>
                {hasTrendData ? (
                  <ResponsiveContainer width="100%" height="100%" minWidth={0}>
                    <LineChart data={trendData} margin={{ top: 5, right: 20, bottom: 5, left: 0 }}>
                      <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={CHART_COLORS.border} />
                      <XAxis dataKey="date" axisLine={false} tickLine={false} tick={CHART_TICK} dy={10} />
                      <YAxis axisLine={false} tickLine={false} tick={CHART_TICK} />
                      <Tooltip contentStyle={CHART_TOOLTIP_STYLE} />
                      <Legend iconType="circle" wrapperStyle={{ fontSize: '12px', paddingTop: '20px' }} />
                      {criterionFilter === 'Writing' ? (
                        <Line type="monotone" dataKey="writing" name="Writing Score" stroke={CHART_COLORS.danger} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                      ) : (
                        <Line type="monotone" dataKey="speaking" name="Speaking Score" stroke={CHART_COLORS.primary} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                      )}
                    </LineChart>
                  </ResponsiveContainer>
                ) : (
                  <ChartEmptyState
                    title={`No ${criterionFilter.toLowerCase()} trend yet`}
                    description="Submit scored work in this skill to unlock criterion movement."
                  />
                )}
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
                  description="Progress isn't just score movement — it's also how consistently you're completing planned work."
                  className="mb-6"
                />
                <div className="h-[220px] w-full sm:h-[240px] lg:h-[250px]" role="img" aria-label="Completion trend chart showing tasks completed over the last 7 days">
                  <SrChartSummary>
                    {hasCompletionData ? `${completedLast7} tasks are represented across ${completionData.length} completion points.` : 'No task completion trend is available yet.'}
                  </SrChartSummary>
                  {hasCompletionData ? (
                    <ResponsiveContainer width="100%" height="100%" minWidth={0}>
                      <AreaChart data={completionData} margin={{ top: 5, right: 0, bottom: 5, left: -20 }}>
                      <defs>
                        <linearGradient id="colorCompleted" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="5%" stopColor={CHART_COLORS.success} stopOpacity={0.3} />
                          <stop offset="95%" stopColor={CHART_COLORS.success} stopOpacity={0} />
                        </linearGradient>
                      </defs>
                      <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={CHART_COLORS.border} />
                      <XAxis dataKey="day" axisLine={false} tickLine={false} tick={CHART_TICK} dy={10} />
                      <YAxis axisLine={false} tickLine={false} tick={CHART_TICK} />
                      <Tooltip contentStyle={CHART_TOOLTIP_STYLE} />
                      <Area type="monotone" dataKey="completed" name="Tasks Completed" stroke={CHART_COLORS.success} strokeWidth={3} fillOpacity={1} fill="url(#colorCompleted)" />
                      </AreaChart>
                    </ResponsiveContainer>
                  ) : (
                    <ChartEmptyState
                      title="No completion trend yet"
                      description="Complete planned tasks to make your weekly consistency visible."
                    />
                  )}
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
                  description="Your submission volume shows how much practice you've banked."
                  className="mb-6"
                />
                <div className="h-[220px] w-full sm:h-[240px] lg:h-[250px]" role="img" aria-label="Submission volume chart showing Writing and Speaking tasks submitted">
                  <SrChartSummary>
                    {hasVolumeData ? `Submission volume includes ${volumeData.length} weekly points.` : 'No submission-volume data is available yet.'}
                  </SrChartSummary>
                  {hasVolumeData ? (
                    <ResponsiveContainer width="100%" height="100%" minWidth={0}>
                      <BarChart data={volumeData} margin={{ top: 5, right: 0, bottom: 5, left: -20 }}>
                      <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={CHART_COLORS.border} />
                      <XAxis dataKey="week" axisLine={false} tickLine={false} tick={CHART_TICK} dy={10} />
                      <YAxis axisLine={false} tickLine={false} tick={CHART_TICK} />
                      <Tooltip cursor={{ fill: CHART_COLORS.border }} contentStyle={CHART_TOOLTIP_STYLE} />
                      <Bar dataKey="submissions" name="Submissions" fill={CHART_COLORS.warning} radius={[6, 6, 0, 0]} barSize={32} />
                      </BarChart>
                    </ResponsiveContainer>
                  ) : (
                    <ChartEmptyState
                      title="No submissions yet"
                      description="Submit Writing or Speaking work to see weekly volume and transfer practice."
                    />
                  )}
                </div>
              </MotionSection>
            </div>

            {/* 5. Review Usage */}
            <MotionSection
              delayIndex={4}
              className="rounded-2xl border border-slate-800 bg-slate-950 p-6 sm:p-8 text-white shadow-lg relative overflow-hidden dark:border-slate-700"
            >
              <div className="mb-6 relative z-10">
                <p className="text-xs font-black uppercase tracking-widest text-slate-300 mb-2">Tutor Review Turnaround</p>
                <h2 className="text-xl font-black text-white">Keep human-feedback timing visible</h2>
                <p className="text-sm text-slate-200 mt-1">
                  Tutor review should feel like part of the same learner system, with clear operational expectations.
                </p>
              </div>
              <div className="absolute top-0 right-0 w-64 h-64 bg-white opacity-5 rounded-full blur-3xl -translate-y-1/2 translate-x-1/3" />
              <div className="relative z-10 flex items-center gap-3">
                <div className="w-10 h-10 rounded-xl bg-white/10 flex items-center justify-center shrink-0">
                  <Clock className="w-5 h-5 text-white" />
                </div>
                <div>
                  <h2 className="text-base font-black">Tutor Review Turnaround</h2>
                  <p className="text-xs text-slate-200">Average time from submission to feedback</p>
                </div>
              </div>
              <div className="mt-6 bg-white/10 rounded-2xl p-5 border border-white/15 inline-block">
                <h3 className="text-xs font-bold text-slate-200 uppercase tracking-widest mb-1">Avg Turnaround</h3>
                <div className="flex items-baseline gap-2">
                  <span className="text-4xl font-black">{progressSummary?.reviewUsage.averageTurnaroundHours ?? 'Pending'}</span>
                  <span className="text-sm font-bold text-slate-200">hours</span>
                </div>
              </div>
              <p className="mt-4 text-xs text-slate-300">
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
