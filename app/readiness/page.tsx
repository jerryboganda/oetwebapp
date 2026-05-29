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
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchReadiness, fetchReadinessHistory, fetchReadinessForecast, refreshReadiness } from '@/lib/api';
import type { ReadinessData, ReadinessHistoryPoint, ReadinessForecast } from '@/lib/mock-data';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
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

  if (error && !data) {
    return (
      <LearnerDashboardShell pageTitle="Readiness Center" backHref="/">
        <div><InlineAlert variant="error">{error}</InlineAlert></div>
      </LearnerDashboardShell>
    );
  }

  if (!data) {
    return (
      <LearnerDashboardShell pageTitle="Readiness Center" backHref="/">
        <div className="space-y-6">
          {[1, 2, 3].map(i => <Skeleton key={i} className="h-40 rounded-2xl" />)}
        </div>
      </LearnerDashboardShell>
    );
  }

  const riskIcon = data.overallRisk === 'High' ? ShieldAlert : data.overallRisk === 'Moderate' ? Shield : ShieldCheck;
  const riskAccent =
    data.overallRisk === 'High' ? { tile: 'bg-danger/10 text-danger', chip: 'bg-danger/10 text-danger border-danger/20' } :
    data.overallRisk === 'Moderate' ? { tile: 'bg-warning/10 text-warning', chip: 'bg-warning/10 text-warning border-warning/20' } :
    data.overallRisk === 'Low' ? { tile: 'bg-success/10 text-success', chip: 'bg-success/10 text-success border-success/20' } :
    { tile: 'bg-muted/10 text-muted', chip: 'bg-muted/10 text-muted border-border' };
  const RiskIconCmp = riskIcon;

  const riskFactors = (data as unknown as { riskFactors?: { label: string; severity: string; impact: number; description: string; actionHref?: string }[] }).riskFactors ?? [];

  return (
    <LearnerDashboardShell
      pageTitle="Readiness Center"
      subtitle={`Target Exam: ${data.targetDate}`}
      backHref="/"
    >
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Readiness Focus"
          icon={TrendingUp}
          accent="primary"
          title="Close the gap to exam day with evidence"
          description="Live signal from mocks, practice, tutor reviews, vocabulary mastery, and study-plan progress. Each panel links to the next best action."
          highlights={[
            { icon: Calendar, label: 'Target date', value: data.targetDate },
            { icon: riskIcon, label: 'Current risk', value: data.overallRisk },
            { icon: Clock, label: 'Recommended', value: `${data.recommendedStudyHours} hrs/week` },
          ]}
        />

        {/* Top row: Forecast gauge | Overall + actions | Risk factors */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          <section className="bg-surface border border-border rounded-[24px] p-6 shadow-sm">
            <div className="flex items-center justify-between mb-3">
              <div className="flex items-center gap-2">
                <span className={`inline-flex h-8 w-8 items-center justify-center rounded-xl ${riskAccent.tile}`}>
                  <RiskIconCmp className="w-4 h-4" />
                </span>
                <span className={`inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-[11px] font-bold uppercase tracking-wider ${riskAccent.chip}`}>
                  Forecast
                </span>
              </div>
              <button
                onClick={() => setSimulatorOpen(true)}
                className="inline-flex items-center gap-1.5 text-xs font-bold text-primary hover:underline"
              >
                <Sliders className="w-3.5 h-3.5" /> Run scenario
              </button>
            </div>
            <ReadinessForecastGauge
              probability={data.targetDateProbability ?? null}
              confidenceBand={data.confidenceLevel}
              targetDate={data.targetDate}
            />
          </section>

          <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm flex flex-col justify-between">
            <div>
              <div className="flex items-center gap-2 mb-4">
                <Clock className="w-5 h-5 text-primary" />
                <span className="text-xs font-bold uppercase tracking-widest text-muted">Overall readiness</span>
              </div>
              <div className="flex items-baseline gap-2 mb-1">
                <h2 className="text-5xl font-bold">{Math.round(data.overallReadiness ?? 0)}</h2>
                <span className="text-lg text-muted">/ 100</span>
              </div>
              <p className="text-xs text-muted leading-relaxed mb-3">
                {data.recommendedStudyHoursRationale ?? `Target ${data.targetDate} · ${data.weeksRemaining} weeks remaining.`}
              </p>
              <div className="text-xs font-bold text-navy">
                Recommended: {data.recommendedStudyHours} hrs/week
              </div>
            </div>
            <div className="mt-4 flex items-center justify-between">
              <ReadinessTargetDateEdit initialDate={data.targetDate} onSaved={() => void loadAll()} />
              <button
                onClick={handleRefresh}
                disabled={refreshing}
                className="inline-flex items-center gap-1.5 text-xs font-bold text-primary hover:underline disabled:opacity-50"
              >
                <RefreshCcw className={`w-3.5 h-3.5 ${refreshing ? 'animate-spin' : ''}`} />
                {refreshing ? 'Refreshing…' : 'Refresh'}
              </button>
            </div>
          </section>

          <section className="bg-surface rounded-[24px] border border-border p-6 shadow-sm">
            <div className="flex items-center gap-2 mb-4">
              <span className="inline-flex h-8 w-8 items-center justify-center rounded-xl bg-warning/10 text-warning">
                <AlertTriangle className="w-4 h-4" />
              </span>
              <span className="text-xs font-bold uppercase tracking-widest text-muted">Risk factors</span>
            </div>
            <div className="space-y-3">
              {riskFactors.length === 0 ? (
                <p className="text-sm text-muted">No critical risk factors detected.</p>
              ) : (
                riskFactors.slice(0, 5).map((f) => {
                  const sev = f.severity === 'high' ? { chip: 'bg-danger/10 text-danger', bar: 'bg-danger' }
                    : f.severity === 'medium' ? { chip: 'bg-warning/10 text-warning', bar: 'bg-warning' }
                    : { chip: 'bg-success/10 text-success', bar: 'bg-success' };
                  return (
                    <div key={f.label}>
                      <div className="flex items-center justify-between mb-1">
                        <span className="text-sm font-semibold text-navy">{f.label}</span>
                        <span className={`text-[10px] font-bold uppercase tracking-widest px-2 py-0.5 rounded-full ${sev.chip}`}>{f.severity}</span>
                      </div>
                      <div className="h-1.5 w-full bg-background-light rounded-full overflow-hidden">
                        <div className={`h-full ${sev.bar}`} style={{ width: `${Math.min(100, f.impact)}%` }} />
                      </div>
                      <p className="text-[11px] text-muted mt-1">{f.description}</p>
                    </div>
                  );
                })
              )}
            </div>
          </section>
        </div>

        {/* Sub-tests grid */}
        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Readiness by sub-test"
            title="See where the gap actually is"
            description="Each sub-test card deep-links to focused practice. Vocabulary mastery is tracked alongside as a multiplier across all sub-tests."
            className="mb-4"
          />
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
            {data.subTests.map((test) => (
              <ReadinessSubtestCard key={test.id} test={test} />
            ))}
            {data.vocabulary && (
              <ReadinessSubtestCard
                test={{
                  id: 'vocabulary',
                  name: 'Vocabulary' as ReadinessData['subTests'][number]['name'],
                  readiness: data.vocabulary.readiness,
                  target: data.vocabulary.target,
                  status: `${data.vocabulary.mastered}/${data.vocabulary.masteryTarget} mastered · ${Math.round(data.vocabulary.accuracy30d)}% accuracy 30d`,
                  color: 'text-teal-600',
                  bg: 'bg-teal-50',
                  barColor: 'bg-teal-500',
                  confidenceBand: undefined,
                  dataPoints: data.vocabulary.dataPoints,
                }}
                href="/vocabulary?filter=due"
              />
            )}
          </div>
        </section>

        {/* Trend chart */}
        <section className="bg-surface rounded-[24px] border border-border p-6 shadow-sm">
          <div className="flex items-center justify-between mb-4 flex-wrap gap-2">
            <div>
              <h3 className="text-lg font-bold text-navy">Readiness trend</h3>
              <p className="text-xs text-muted">Last 12 weeks of overall and per-sub-test readiness.</p>
            </div>
            <div className="flex flex-wrap gap-2">
              {TREND_SERIES_OPTIONS.map((opt) => (
                <button
                  key={opt.value}
                  type="button"
                  onClick={() => setTrendSeries(opt.value)}
                  className={`text-xs font-bold px-3 py-1.5 rounded-full border ${
                    trendSeries === opt.value
                      ? 'bg-primary text-primary-foreground border-primary'
                      : 'bg-surface text-navy border-border hover:bg-background-light'
                  }`}
                >
                  {opt.label}
                </button>
              ))}
            </div>
          </div>
          <ReadinessTrendChart data={history} series={trendSeries} target={70} />
        </section>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
          {/* Blockers */}
          <div className="lg:col-span-2 space-y-4">
            <LearnerSurfaceSectionHeader
              eyebrow="Actionable blockers"
              title="What is slowing you down right now"
              description="Each blocker has a one-click action that takes you to the right practice or task."
              className="mb-0"
            />
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              {data.blockers.length === 0 ? (
                <p className="text-sm text-muted col-span-full">No blockers detected. Keep going!</p>
              ) : (
                data.blockers.map((b) => <ReadinessBlockerCard key={String(b.id)} blocker={b} />)
              )}
            </div>
          </div>

          {/* Evidence panel */}
          <div>
            <LearnerSurfaceSectionHeader
              eyebrow="Evidence"
              title="What this is based on"
              description="Readiness is computed from real practice: mocks, tutor reviews, vocabulary, and study-plan progress."
              className="mb-4"
            />
            <section className="bg-surface rounded-[24px] border border-border p-6 shadow-sm">
              <div className="space-y-3">
                <EvidenceRow label="Full mocks (90d)" value={data.evidence.mocksCompleted} />
                <EvidenceRow label="Practice questions (90d)" value={data.evidence.practiceQuestions} />
                <EvidenceRow label="Tutor reviews (90d)" value={data.evidence.expertReviews} />
                <EvidenceRow label="Vocab reviewed (30d)" value={data.evidence.vocabReviewed30d ?? 0} icon={BookOpen} />
              </div>
              <div className="mt-5 bg-background-light rounded-xl p-4 border border-border">
                <div className="flex items-start gap-3">
                  <TrendingUp className="w-5 h-5 text-primary shrink-0 mt-0.5" />
                  <div>
                    <h4 className="text-xs font-bold text-navy uppercase tracking-widest mb-1">Recent trend</h4>
                    <p className="text-xs text-muted leading-relaxed">{data.evidence.recentTrend}</p>
                  </div>
                </div>
              </div>
              <div className="mt-4 flex items-center justify-between text-[10px] font-bold text-muted uppercase tracking-widest">
                <span className="inline-flex items-center gap-1"><Info className="w-3 h-3" /> Updated</span>
                <span>{new Date(data.evidence.lastUpdated).toLocaleDateString()}</span>
              </div>
              <div className="mt-3 flex items-center justify-between text-[10px] font-bold text-muted uppercase tracking-widest">
                <span>Confidence</span>
                <span className="text-navy">{data.confidenceLevel ?? 'Low'}</span>
              </div>
              <div className="mt-1 flex items-center justify-between text-[10px] font-bold text-muted uppercase tracking-widest">
                <span>Data points</span>
                <span className="text-navy">{data.dataPointCount ?? 0}</span>
              </div>
            </section>
          </div>
        </div>
      </div>

      <ReadinessForecastSimulator
        open={simulatorOpen}
        onClose={() => setSimulatorOpen(false)}
        initialForecast={forecast ?? undefined}
      />
    </LearnerDashboardShell>
  );
}

function EvidenceRow({ label, value, icon: Icon }: { label: string; value: number; icon?: React.ElementType }) {
  return (
    <div className="flex items-center justify-between py-2 border-b border-border last:border-b-0">
      <span className="text-sm font-medium text-muted inline-flex items-center gap-1.5">
        {Icon ? <Icon className="w-3.5 h-3.5" /> : null}
        {label}
      </span>
      <span className="text-base font-bold text-navy">{value}</span>
    </div>
  );
}
