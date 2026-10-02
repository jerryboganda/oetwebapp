'use client';

import { useContext, useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
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
} from '@/components/charts/dynamic-recharts';
import {
  TrendingUp,
  Activity,
  CheckCircle2,
  Clock
} from 'lucide-react';
import { AuthContext } from '@/contexts/auth-context';
import { fetchTrendData, fetchCompletionData, fetchSubmissionVolume, fetchProgressEvidenceSummary } from '@/lib/api';
import type { ProgressEvidenceSummary, TrendPoint } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';
import { queryKeys } from '@/lib/query/hooks';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerFreshnessIndicator } from '@/components/domain/learner-freshness-indicator';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { seriesColor } from '@/lib/domain/chart-palette';

type CompletionPoint = { day: string; completed: number };
type VolumePoint = { week: string; submissions: number };

// Non-series colours mirror globals.css tokens (SVG attributes can't read CSS vars);
// sub-test series use the shared chart palette so every chart agrees.
const CHART_COLORS = {
  success: '#10b981',
  warning: '#d97706',
  muted: '#526072',
  border: '#d8e0e8',
} as const;

const CHART_TICK = { fontSize: 12, fill: CHART_COLORS.muted } as const;
const CHART_TOOLTIP_STYLE = { borderRadius: '16px', border: 'none', boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)' } as const;
const CRITERION_TABS = [
  { id: 'Writing', label: 'Writing' },
  { id: 'Speaking', label: 'Speaking' },
];

function ChartEmptyState({ title, description }: { title: string; description: string }) {
  return (
    <div className="flex min-h-[240px] items-center justify-center">
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

function plural(count: number, noun: string) {
  return `${count} ${noun}${count === 1 ? '' : 's'}`;
}

export default function ProgressDashboard() {
  const [criterionFilter, setCriterionFilter] = useState('Writing');
  const authContext = useContext(AuthContext);
  const queryUserId = authContext?.user?.userId ?? 'current';
  const queriesEnabled = authContext ? !authContext.loading && authContext.isAuthenticated : true;
  const trendQuery = useQuery({
    queryKey: queryKeys.progress.trend(queryUserId),
    queryFn: fetchTrendData,
    staleTime: 45_000,
    enabled: queriesEnabled,
  });
  const completionQuery = useQuery({
    queryKey: queryKeys.progress.completion(queryUserId),
    queryFn: fetchCompletionData,
    staleTime: 45_000,
    enabled: queriesEnabled,
  });
  const volumeQuery = useQuery({
    queryKey: queryKeys.progress.submissionVolume(queryUserId),
    queryFn: fetchSubmissionVolume,
    staleTime: 45_000,
    enabled: queriesEnabled,
  });
  const summaryQuery = useQuery({
    queryKey: queryKeys.progress.evidence(queryUserId),
    queryFn: fetchProgressEvidenceSummary,
    staleTime: 45_000,
    enabled: queriesEnabled,
  });
  const progressQueries = [trendQuery, completionQuery, volumeQuery, summaryQuery];
  const trendData = (trendQuery.data ?? []) as TrendPoint[];
  const completionData = (completionQuery.data ?? []) as CompletionPoint[];
  const volumeData = (volumeQuery.data ?? []) as VolumePoint[];
  const progressSummary = (summaryQuery.data ?? null) as ProgressEvidenceSummary | null;
  const loading = queriesEnabled && progressQueries.some((query) => query.isPending);
  const failedQueries = progressQueries.filter((query) => query.error);
  const allFailed = failedQueries.length === progressQueries.length
    && progressQueries.every((query) => query.data === undefined);
  const error = failedQueries.length === 0
    ? null
    : allFailed
      ? 'Failed to load progress data. Please try again.'
      : 'Some progress data could not be loaded.';

  useEffect(() => {
    analytics.track('progress_viewed');
  }, []);

  const completedLast7 = completionData.reduce((sum, point) => sum + point.completed, 0);
  const averageTurnaroundHours = progressSummary?.reviewUsage.averageTurnaroundHours ?? null;
  const averageTurnaroundLabel = averageTurnaroundHours
    ? `${averageTurnaroundHours}h avg`
    : 'Pending';
  const hasTrendData = trendData.length > 0;
  const hasCompletionData = completionData.length > 0;
  const hasVolumeData = volumeData.length > 0;
  const hasAnyProgressData = hasTrendData || hasCompletionData || hasVolumeData || Boolean(progressSummary);
  const generatedAt = progressSummary?.freshness.generatedAt ?? null;
  // Real counts once loaded; a dash when that series failed, never a stuck "Loading...".
  const countLabel = (failed: boolean, label: string) => (loading ? 'Loading...' : failed ? '—' : label);

  return (
    <>
      <LearnerPageHero
        eyebrow="Evidence Check"
        icon={TrendingUp}
        accent="primary"
        title="See whether recent effort is turning into better evidence"
        description="Track your score trends, completed work, and review activity to choose your next priority."
        highlights={[
          { icon: Activity, label: 'Trend coverage', value: countLabel(Boolean(trendQuery.error), plural(trendData.length, 'checkpoint')) },
          { icon: CheckCircle2, label: 'Completed work', value: countLabel(Boolean(completionQuery.error), plural(completedLast7, 'task')) },
          { icon: Clock, label: 'Review speed', value: averageTurnaroundLabel },
        ]}
        aside={<LearnerFreshnessIndicator updatedAt={generatedAt} staleAfterMinutes={1440} />}
      />

      {loading && (
        <LearnerSkeleton variant="chart-panel" />
      )}

      {!loading && error && (
        <InlineAlert
          variant={hasAnyProgressData ? 'warning' : 'error'}
          action={<Button size="sm" variant="outline" onClick={() => failedQueries.forEach((query) => void query.refetch())}>Retry</Button>}
        >
          {error}
        </InlineAlert>
      )}

      {!loading && (
        <>
          {!hasAnyProgressData ? (
            <LearnerEmptyState
              icon={Activity}
              title="No progress evidence yet"
              description="Complete practice submissions or mock tests to unlock charts and review timing insights."
              primaryAction={{ label: 'Start Writing Practice', href: '/writing' }}
              secondaryAction={{ label: 'Open Study Plan', href: '/study-plan' }}
            />
          ) : null}

          {/* 1. Sub-test Trend */}
          <MotionSection delayIndex={0}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow="Sub-test Performance Trend"
                title="See score movement across all skills"
                description="Compare your trajectory across all four sub-tests at a glance."
                className="mb-6"
              />
              {hasTrendData ? (
                <>
                  <SrChartSummary>
                    {`Sub-test trend has ${trendData.length} checkpoints. Latest checkpoint is ${trendData[trendData.length - 1]?.date ?? 'unknown'}.`}
                  </SrChartSummary>
                  <div className="h-[240px] w-full sm:h-[280px] lg:h-[300px]" role="img" aria-label="Sub-test performance trend chart showing reading, listening, writing, and speaking scores over time">
                    <ResponsiveContainer width="100%" height="100%" minWidth={0}>
                      <LineChart data={trendData} margin={{ top: 5, right: 20, bottom: 5, left: 0 }}>
                        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={CHART_COLORS.border} />
                        <XAxis dataKey="date" axisLine={false} tickLine={false} tick={CHART_TICK} dy={10} />
                        <YAxis axisLine={false} tickLine={false} tick={CHART_TICK} />
                        <Tooltip contentStyle={CHART_TOOLTIP_STYLE} />
                        <Legend iconType="circle" wrapperStyle={{ fontSize: '12px', paddingTop: '20px' }} />
                        <Line type="monotone" dataKey="reading" name="Reading" stroke={seriesColor('reading')} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                        <Line type="monotone" dataKey="listening" name="Listening" stroke={seriesColor('listening')} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                        <Line type="monotone" dataKey="writing" name="Writing" stroke={seriesColor('writing')} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                        <Line type="monotone" dataKey="speaking" name="Speaking" stroke={seriesColor('speaking')} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                      </LineChart>
                    </ResponsiveContainer>
                  </div>
                </>
              ) : (
                <ChartEmptyState
                  title="No trend data yet"
                  description="Complete a few scored submissions to unlock movement across Reading, Listening, Writing, and Speaking."
                />
              )}
            </Card>
          </MotionSection>

          {/* 2. Criterion Trend */}
          <MotionSection delayIndex={1}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow="Criterion Trend"
                title="Filter deeper without losing the main story"
                description="Filter Writing and Speaking by individual criterion to see exactly where you're improving."
                action={(
                  <Tabs
                    tabs={CRITERION_TABS}
                    activeTab={criterionFilter}
                    onChange={setCriterionFilter}
                    scrollable={false}
                    className="w-auto shrink-0 self-start sm:self-auto"
                  />
                )}
                className="mb-6"
              />
              <TabPanel id={criterionFilter} activeTab={criterionFilter}>
                {hasTrendData ? (
                  <>
                    <SrChartSummary>
                      {`${criterionFilter} criterion trend is displayed using the same ${trendData.length} score checkpoints.`}
                    </SrChartSummary>
                    <div className="h-[240px] w-full sm:h-[280px] lg:h-[300px]" role="img" aria-label={`Criterion trend chart for ${criterionFilter} skills`}>
                      <ResponsiveContainer width="100%" height="100%" minWidth={0}>
                        <LineChart data={trendData} margin={{ top: 5, right: 20, bottom: 5, left: 0 }}>
                          <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={CHART_COLORS.border} />
                          <XAxis dataKey="date" axisLine={false} tickLine={false} tick={CHART_TICK} dy={10} />
                          <YAxis axisLine={false} tickLine={false} tick={CHART_TICK} />
                          <Tooltip contentStyle={CHART_TOOLTIP_STYLE} />
                          <Legend iconType="circle" wrapperStyle={{ fontSize: '12px', paddingTop: '20px' }} />
                          {criterionFilter === 'Writing' ? (
                            <Line type="monotone" dataKey="writing" name="Writing Score" stroke={seriesColor('writing')} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                          ) : (
                            <Line type="monotone" dataKey="speaking" name="Speaking Score" stroke={seriesColor('speaking')} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                          )}
                        </LineChart>
                      </ResponsiveContainer>
                    </div>
                  </>
                ) : (
                  <ChartEmptyState
                    title={`No ${criterionFilter.toLowerCase()} trend yet`}
                    description="Submit scored work in this skill to unlock criterion movement."
                  />
                )}
              </TabPanel>
            </Card>
          </MotionSection>

          <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            {/* 3. Completion Trend */}
            <MotionSection delayIndex={2}>
              <Card padding="lg" className="h-full">
                <LearnerSurfaceSectionHeader
                  eyebrow="Completion Trend"
                  title="Keep task completion visible"
                  description="Progress isn't just score movement; it's also how consistently you're completing planned work."
                  className="mb-6"
                />
                {hasCompletionData ? (
                  <>
                    <SrChartSummary>{`${completedLast7} tasks are represented across ${completionData.length} completion points.`}</SrChartSummary>
                    <div className="h-[220px] w-full sm:h-[240px] lg:h-[250px]" role="img" aria-label="Completion trend chart showing tasks completed over the last 7 days">
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
                    </div>
                  </>
                ) : (
                  <ChartEmptyState
                    title="No completion trend yet"
                    description="Complete planned tasks to make your weekly consistency visible."
                  />
                )}
              </Card>
            </MotionSection>

            {/* 4. Submission Volume */}
            <MotionSection delayIndex={3}>
              <Card padding="lg" className="h-full">
                <LearnerSurfaceSectionHeader
                  eyebrow="Submission Volume"
                  title="Make writing and speaking effort visible"
                  description="Your submission volume shows how much practice you've banked."
                  className="mb-6"
                />
                {hasVolumeData ? (
                  <>
                    <SrChartSummary>{`Submission volume includes ${volumeData.length} weekly points.`}</SrChartSummary>
                    <div className="h-[220px] w-full sm:h-[240px] lg:h-[250px]" role="img" aria-label="Submission volume chart showing Writing and Speaking tasks submitted">
                      <ResponsiveContainer width="100%" height="100%" minWidth={0}>
                        <BarChart data={volumeData} margin={{ top: 5, right: 0, bottom: 5, left: -20 }}>
                          <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={CHART_COLORS.border} />
                          <XAxis dataKey="week" axisLine={false} tickLine={false} tick={CHART_TICK} dy={10} />
                          <YAxis axisLine={false} tickLine={false} tick={CHART_TICK} />
                          <Tooltip cursor={{ fill: CHART_COLORS.border }} contentStyle={CHART_TOOLTIP_STYLE} />
                          <Bar dataKey="submissions" name="Submissions" fill={CHART_COLORS.warning} radius={[6, 6, 0, 0]} barSize={32} />
                        </BarChart>
                      </ResponsiveContainer>
                    </div>
                  </>
                ) : (
                  <ChartEmptyState
                    title="No submissions yet"
                    description="Submit Writing or Speaking work to see weekly volume and transfer practice."
                  />
                )}
              </Card>
            </MotionSection>
          </div>

          {/* 5. Review Usage */}
          <MotionSection delayIndex={4}>
            <Card padding="lg">
              <LearnerSurfaceSectionHeader
                eyebrow="Tutor Review Turnaround"
                icon={Clock}
                title="Keep human-feedback timing visible"
                description="Average time from submission to feedback"
                className="mb-5"
              />
              <div className="inline-flex flex-col rounded-2xl border border-border bg-background-light px-5 py-4">
                <p className="tile-label text-muted">Avg Turnaround</p>
                <p className="mt-1 flex items-baseline gap-2">
                  <span className="text-3xl font-bold tabular-nums text-navy">{averageTurnaroundHours ?? 'Pending'}</span>
                  {averageTurnaroundHours != null ? <span className="text-sm font-semibold text-muted">hours</span> : null}
                </p>
              </div>
              {progressSummary?.freshness.usesFallbackSeries ? (
                <p className="mt-4 text-xs text-muted">
                  Review timing is still based on limited data and will sharpen after more requests complete.
                </p>
              ) : null}
            </Card>
          </MotionSection>
        </>
      )}
    </>
  );
}
