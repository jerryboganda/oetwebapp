'use client';

import React, { useEffect, useState, useCallback } from 'react';
import {
  Calendar,
  Clock,
  AlertTriangle,
  ShieldAlert,
  ShieldCheck,
  Shield,
  TrendingUp,
  Info,
  Sliders,
  RefreshCcw,
  BookOpen,
} from 'lucide-react';
import { Skeleton } from '@/components/ui/skeleton';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { ErrorState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { ProgressBar } from '@/components/ui/progress';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { fetchReadiness, fetchReadinessHistory, fetchReadinessForecast, refreshReadiness } from '@/lib/api';
import type { ReadinessData, ReadinessHistoryPoint, ReadinessForecast } from '@/lib/mock-data';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { ReadinessTrendChart } from '@/components/domain/readiness-trend-chart';
import { ReadinessForecastGauge } from '@/components/domain/readiness-forecast-gauge';
import { ReadinessForecastSimulator } from '@/components/domain/readiness-forecast-simulator';
import { ReadinessBlockerCard } from '@/components/domain/readiness-blocker-card';
import { ReadinessSubtestCard } from '@/components/domain/readiness-subtest-card';
import { ReadinessTargetDateEdit } from '@/components/domain/readiness-target-date-edit';
import { analytics } from '@/lib/analytics';

const TREND_SERIES_OPTIONS: { value: 'overall' | 'writing' | 'speaking' | 'reading' | 'listening' | 'vocabulary'; label: string }[] = [
  { value: 'overall', label: 'Overall' },
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
  { value: 'reading', label: 'Reading' },
  { value: 'listening', label: 'Listening' },
  { value: 'vocabulary', label: 'Vocabulary' },
];

export default function ReadinessCenter() {
  const [data, setData] = useState<ReadinessData | null>(null);
  const [history, setHistory] = useState<ReadinessHistoryPoint[]>([]);
  const [forecast, setForecast] = useState<ReadinessForecast | null>(null);
  const [error, setError] = useState('');
  const [refreshing, setRefreshing] = useState(false);
  const [simulatorOpen, setSimulatorOpen] = useState(false);
  const [trendSeries, setTrendSeries] = useState<typeof TREND_SERIES_OPTIONS[number]['value']>('overall');

  const loadAll = useCallback(async () => {
    setError('');
    const [readinessResult, historyResult, forecastResult] = await Promise.allSettled([
      fetchReadiness(),
      fetchReadinessHistory(12),
      fetchReadinessForecast(),
    ]);
    if (readinessResult.status === 'fulfilled') setData(readinessResult.value);
    else setError('Could not load readiness data.');
    if (historyResult.status === 'fulfilled') setHistory(historyResult.value);
    if (forecastResult.status === 'fulfilled') setForecast(forecastResult.value);
  }, []);

  useEffect(() => {
    analytics.track('readiness_viewed');
    void loadAll();
  }, [loadAll]);

  async function handleRefresh() {
    setRefreshing(true);
    try {
      const fresh = await refreshReadiness();
      setData(fresh);
      // refresh history + forecast too
      const [h, f] = await Promise.all([
        fetchReadinessHistory(12).catch(() => history),
        fetchReadinessForecast().catch(() => forecast),
      ]);
      setHistory(h);
      setForecast(f);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not refresh readiness.');
    } finally {
      setRefreshing(false);
    }
  }

  // The hero stays up in every state (one h1); its chips hold their place until real data arrives.
  const pending = error ? '—' : 'Loading...';
  const hero = (
    <LearnerPageHero
      eyebrow="Readiness Focus"
      icon={TrendingUp}
      accent="primary"
      title="Close the gap to exam day with evidence"
      description="Live signal from mocks, practice, tutor reviews, vocabulary mastery, and study-plan progress. Each panel links to the next best action."
      highlights={[
        { icon: Calendar, label: 'Target date', value: data ? data.targetDate : pending },
        { icon: data?.overallRisk === 'High' ? ShieldAlert : data?.overallRisk === 'Moderate' ? Shield : ShieldCheck, label: 'Current risk', value: data ? data.overallRisk : pending },
        { icon: Clock, label: 'Recommended', value: data ? `${data.recommendedStudyHours} hrs/week` : pending },
      ]}
    />
  );

  if (error && !data) {
    return (
      <>
        {hero}
        <ErrorState message={error} onRetry={() => void loadAll()} />
      </>
    );
  }

  if (!data) {
    return (
      <>
        {hero}
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-3" aria-hidden="true">
          {[1, 2, 3].map(i => <Skeleton key={i} className="h-40 rounded-2xl" />)}
        </div>
      </>
    );
  }

  const riskIcon = data.overallRisk === 'High' ? ShieldAlert : data.overallRisk === 'Moderate' ? Shield : ShieldCheck;
  const riskAccent =
    data.overallRisk === 'High' ? { tile: 'bg-danger/10 text-danger-strong', chip: 'bg-danger/10 text-danger-strong border-danger/20' } :
    data.overallRisk === 'Moderate' ? { tile: 'bg-warning/10 text-warning-strong', chip: 'bg-warning/10 text-warning-strong border-warning/20' } :
    data.overallRisk === 'Low' ? { tile: 'bg-success/10 text-success-strong', chip: 'bg-success/10 text-success-strong border-success/20' } :
    { tile: 'bg-muted/10 text-muted', chip: 'bg-muted/10 text-muted border-border' };
  const RiskIconCmp = riskIcon;

  const riskFactors = (data as unknown as { riskFactors?: { label: string; severity: string; impact: number; description: string; actionHref?: string }[] }).riskFactors ?? [];

  return (
    <>
      {hero}

      {/* Top row: Forecast gauge | Overall + actions | Risk factors */}
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <MotionSection delayIndex={0}>
          <Card padding="lg" className="h-full">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2">
                <span className={`inline-flex h-8 w-8 items-center justify-center rounded-xl ${riskAccent.tile}`}>
                  <RiskIconCmp className="h-4 w-4" aria-hidden="true" />
                </span>
                <h2 className={`tile-label inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 ${riskAccent.chip}`}>
                  Forecast
                </h2>
              </div>
              <button
                type="button"
                onClick={() => setSimulatorOpen(true)}
                className="-me-2 inline-flex min-h-11 items-center gap-1.5 rounded-control px-2 text-xs font-bold text-primary transition-colors hover-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              >
                <Sliders className="h-3.5 w-3.5" aria-hidden="true" /> Run scenario
              </button>
            </div>
            <ReadinessForecastGauge
              probability={data.targetDateProbability ?? null}
              confidenceBand={data.confidenceLevel}
              targetDate={data.targetDate}
            />
          </Card>
        </MotionSection>

        <MotionSection delayIndex={1}>
          <Card padding="lg" className="flex h-full flex-col justify-between">
            <div>
              <div className="mb-4 flex items-center gap-2">
                <Clock className="h-5 w-5 text-primary" aria-hidden="true" />
                <h2 className="eyebrow text-muted">Overall readiness</h2>
              </div>
              <div className="mb-1 flex items-baseline gap-2">
                <p className="text-5xl font-bold tabular-nums text-navy"><CountUp value={Math.round(data.overallReadiness ?? 0)} /></p>
                <span className="text-lg text-muted">/ 100</span>
              </div>
              <p className="mb-3 text-xs leading-relaxed text-muted">
                {data.recommendedStudyHoursRationale ?? `Target ${data.targetDate} · ${data.weeksRemaining} weeks remaining.`}
              </p>
              <div className="text-xs font-bold text-navy">
                Recommended: {data.recommendedStudyHours} hrs/week
              </div>
            </div>
            <div className="mt-4 flex flex-wrap items-center justify-between gap-2">
              <ReadinessTargetDateEdit initialDate={data.targetDate} onSaved={() => void loadAll()} />
              <button
                type="button"
                onClick={handleRefresh}
                disabled={refreshing}
                className="-me-2 inline-flex min-h-11 items-center gap-1.5 rounded-control px-2 text-xs font-bold text-primary transition-colors hover-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-50"
              >
                <RefreshCcw className={`h-3.5 w-3.5 ${refreshing ? 'animate-spin' : ''}`} aria-hidden="true" />
                {refreshing ? 'Refreshing…' : 'Refresh'}
              </button>
            </div>
          </Card>
        </MotionSection>

        <MotionSection delayIndex={2}>
          <Card padding="lg" className="h-full">
            <div className="mb-4 flex items-center gap-2">
              <span className="inline-flex h-8 w-8 items-center justify-center rounded-xl bg-warning/10 text-warning-strong">
                <AlertTriangle className="h-4 w-4" aria-hidden="true" />
              </span>
              <h2 className="eyebrow text-muted">Risk factors</h2>
            </div>
            <div className="space-y-3">
              {riskFactors.length === 0 ? (
                <p className="text-sm text-muted">No critical risk factors detected.</p>
              ) : (
                riskFactors.slice(0, 5).map((f) => {
                  const sev = f.severity === 'high' ? { chip: 'bg-danger/10 text-danger-strong', bar: 'danger' as const }
                    : f.severity === 'medium' ? { chip: 'bg-warning/10 text-warning-strong', bar: 'warning' as const }
                    : { chip: 'bg-success/10 text-success-strong', bar: 'success' as const };
                  return (
                    <div key={f.label}>
                      <div className="mb-1 flex items-center justify-between gap-2">
                        <span className="min-w-0 text-sm font-semibold text-navy">{f.label}</span>
                        <span className={`tile-label shrink-0 rounded-full px-2 py-0.5 ${sev.chip}`}>{f.severity}</span>
                      </div>
                      <ProgressBar value={Math.min(100, f.impact)} color={sev.bar} ariaLabel={`${f.label} impact`} />
                      <p className="mt-1 text-2xs text-muted">{f.description}</p>
                    </div>
                  );
                })
              )}
            </div>
          </Card>
        </MotionSection>
      </div>

      {/* Sub-tests grid */}
      <section>
        <LearnerSurfaceSectionHeader
          eyebrow="Readiness by sub-test"
          title="See where the gap actually is"
          description="Each sub-test card deep-links to focused practice. Vocabulary mastery is tracked alongside as a multiplier across all sub-tests."
          className="mb-4"
        />
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {data.subTests.map((test, index) => (
            <MotionItem key={test.id} delayIndex={Math.min(index, 5)} className="h-full">
              <ReadinessSubtestCard test={test} />
            </MotionItem>
          ))}
          {data.vocabulary && (
            <MotionItem delayIndex={Math.min(data.subTests.length, 5)} className="h-full">
              <ReadinessSubtestCard
                test={{
                  id: 'vocabulary',
                  name: 'Vocabulary' as ReadinessData['subTests'][number]['name'],
                  readiness: data.vocabulary.readiness,
                  target: data.vocabulary.target,
                  status: `${data.vocabulary.mastered}/${data.vocabulary.masteryTarget} mastered · ${Math.round(data.vocabulary.accuracy30d)}% accuracy 30d`,
                  // Not a sub-test, so no skill token: the neutral anchor keeps it apart from the four skills.
                  color: 'text-navy',
                  bg: 'bg-navy/10',
                  barColor: 'bg-navy',
                  confidenceBand: undefined,
                  dataPoints: data.vocabulary.dataPoints,
                }}
                href="/vocabulary?filter=due"
              />
            </MotionItem>
          )}
        </div>
      </section>

      {/* Trend chart */}
      <MotionSection>
        <Card padding="lg">
          <LearnerSurfaceSectionHeader
            title="Readiness trend"
            description="Last 12 weeks of overall and per-sub-test readiness."
            className="mb-4"
          />
          <Tabs
            tabs={TREND_SERIES_OPTIONS.map((opt) => ({ id: opt.value, label: opt.label }))}
            activeTab={trendSeries}
            onChange={(id) => setTrendSeries(id as typeof trendSeries)}
            scrollable={false}
            className="mb-4 w-auto"
          />
          <TabPanel id={trendSeries} activeTab={trendSeries}>
            <ReadinessTrendChart data={history} series={trendSeries} target={70} />
          </TabPanel>
        </Card>
      </MotionSection>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        {/* Blockers */}
        <section className="lg:col-span-2">
          <LearnerSurfaceSectionHeader
            eyebrow="Blockers"
            title="What is slowing you down right now"
            className="mb-4"
          />
          {data.blockers.length === 0 ? (
            <LearnerEmptyState
              compact
              icon={ShieldCheck}
              title="No blockers detected"
              description="Keep going!"
            />
          ) : (
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
              {data.blockers.map((b, index) => (
                <MotionItem key={String(b.id)} delayIndex={Math.min(index, 5)} className="h-full">
                  <ReadinessBlockerCard blocker={b} />
                </MotionItem>
              ))}
            </div>
          )}
        </section>

        {/* Evidence panel */}
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Evidence"
            title="What this is based on"
            description="Readiness is computed from real practice: mocks, tutor reviews, vocabulary, and study-plan progress."
            className="mb-4"
          />
          <MotionSection>
            <Card padding="lg">
              <div className="space-y-3">
                <EvidenceRow label="Full mocks (90d)" value={data.evidence.mocksCompleted} />
                <EvidenceRow label="Practice questions (90d)" value={data.evidence.practiceQuestions} />
                <EvidenceRow label="Tutor reviews (90d)" value={data.evidence.expertReviews} />
                <EvidenceRow label="Vocab reviewed (30d)" value={data.evidence.vocabReviewed30d ?? 0} icon={BookOpen} />
              </div>
              <div className="mt-5 rounded-xl border border-border bg-background-light p-4">
                <div className="flex items-start gap-3">
                  <TrendingUp className="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden="true" />
                  <div className="min-w-0">
                    <p className="eyebrow mb-1 text-navy">Recent trend</p>
                    <p className="text-xs leading-relaxed text-muted">{data.evidence.recentTrend}</p>
                  </div>
                </div>
              </div>
              <div className="tile-label mt-4 flex items-center justify-between gap-2 text-muted">
                <span className="inline-flex items-center gap-1"><Info className="h-3 w-3" aria-hidden="true" /> Updated</span>
                <span className="tabular-nums">{new Date(data.evidence.lastUpdated).toLocaleDateString()}</span>
              </div>
              <div className="tile-label mt-3 flex items-center justify-between gap-2 text-muted">
                <span>Confidence</span>
                <span className="text-navy">{data.confidenceLevel ?? 'Low'}</span>
              </div>
              <div className="tile-label mt-1 flex items-center justify-between gap-2 text-muted">
                <span>Data points</span>
                <span className="tabular-nums text-navy">{data.dataPointCount ?? 0}</span>
              </div>
            </Card>
          </MotionSection>
        </section>
      </div>

      <ReadinessForecastSimulator
        open={simulatorOpen}
        onClose={() => setSimulatorOpen(false)}
        initialForecast={forecast ?? undefined}
      />
    </>
  );
}

function EvidenceRow({ label, value, icon: Icon }: { label: string; value: number; icon?: React.ElementType }) {
  return (
    <div className="flex items-center justify-between gap-3 border-b border-border py-2 last:border-b-0">
      <span className="inline-flex min-w-0 items-center gap-1.5 text-sm font-medium text-muted">
        {Icon ? <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> : null}
        {label}
      </span>
      <span className="text-base font-bold tabular-nums text-navy"><CountUp value={value} /></span>
    </div>
  );
}
