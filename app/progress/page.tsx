'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { TrendingUp, Download, BarChart3, Activity } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import {
  ChartTabularFallback,
  ProgressActivityPanel,
  ProgressComparativeTab,
  ProgressCriterionChart,
  ProgressRangePills,
  ProgressReadinessStrip,
  ProgressReviewStrip,
  ProgressSubtestMiniCards,
  ProgressTrendChart,
} from '@/components/domain/progress';
import { fetchProgressV2, progressPdfUrl, type ProgressRange, type ProgressV2Payload } from '@/lib/api';
import { analytics } from '@/lib/analytics';

const ALL_SUBTESTS = ['reading', 'listening', 'writing', 'speaking'] as const;

type Tab = 'trend' | 'criterion' | 'comparative';

export default function ProgressDashboard() {
  const [range, setRange] = useState<ProgressRange>('90d');
  const [payload, setPayload] = useState<ProgressV2Payload | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [visibleSubtests, setVisibleSubtests] = useState<Set<string>>(() => new Set(ALL_SUBTESTS));
  const [criterionSubtest, setCriterionSubtest] = useState<'writing' | 'speaking'>('writing');
  const [activeTab, setActiveTab] = useState<Tab>('trend');

  const load = useCallback(async (currentRange: ProgressRange) => {
    setLoading(true);
    setError(null);
    try {
      const next = await fetchProgressV2(currentRange);
      setPayload(next);
    } catch (e) {
      const err = e as { userMessage?: string; message?: string };
      setError(err.userMessage ?? err.message ?? 'Failed to load progress data.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    analytics.track('progress_viewed');
    queueMicrotask(() => { void load(range); });
  }, [load, range]);

  const onRangeChange = (next: ProgressRange) => {
    setRange(next);
    analytics.track('progress_range_changed', { range: next });
  };

  const onSubtestToggle = (subtest: string) => {
    setVisibleSubtests((prev) => {
      const next = new Set(prev);
      if (next.has(subtest)) next.delete(subtest);
      else next.add(subtest);
      analytics.track('progress_subtest_toggled', { subtest, visible: next.has(subtest) });
      return next;
    });
  };

  const onTabChange = (next: Tab) => {
    setActiveTab(next);
    analytics.track('progress_tab_switched', { tab: next });
  };

  const onCriterionChange = (next: 'writing' | 'speaking') => {
    setCriterionSubtest(next);
    analytics.track('progress_criterion_switched', { subtest: next });
  };

  const onExportPdf = () => {
    analytics.track('progress_pdf_exported');
    const a = document.createElement('a');
    a.href = progressPdfUrl();
    a.download = 'progress.pdf';
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
  };

  const heroHighlights = useMemo(() => {
    if (!payload) return [];
    const turnaround = payload.reviewUsage.averageTurnaroundHours;
    return [
      { icon: Activity, label: 'Evaluations', value: payload.totals.completedEvaluations.toString() },
      { icon: TrendingUp, label: 'Mock attempts', value: payload.totals.mockAttempts.toString() },
      {
        icon: BarChart3,
        label: 'Review turnaround',
        value: turnaround === null ? 'Pending' : `${turnaround.toFixed(1)} h`,
      },
    ];
  }, [payload]);

  return (
    <LearnerDashboardShell pageTitle="Progress Dashboard" subtitle="Track your performance and activity over time" backHref="/">
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Evidence Check"
          icon={TrendingUp}
          accent="primary"
          title="See whether recent effort is turning into better evidence"
          description="Use this page to connect score movement, completed work, review throughput, and where you stand against your goals before choosing the next focus."
          highlights={heroHighlights.length ? heroHighlights : [{ icon: Activity, label: 'Loading', value: '…' }]}
        />

        {/* Toolbar */}
        <div className="flex flex-wrap items-center justify-between gap-3">
          <ProgressRangePills value={range} onChange={onRangeChange} />
          {payload?.meta.showScoreGuaranteeStrip === false ? null : null}
          {payload && (
            <button
              type="button"
              onClick={onExportPdf}
              className="inline-flex items-center gap-1.5 rounded-xl border border-gray-200 bg-surface px-3 py-1.5 text-xs font-bold text-navy hover:bg-lavender/30 transition-colors"
              aria-label="Export progress as PDF"
            >
              <Download className="w-4 h-4" /> Export PDF
            </button>
          )}
        </div>

        {loading && (
          <div className="space-y-6">
            {[1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-64 rounded-2xl" />
            ))}
          </div>
        )}

        {!loading && error && <InlineAlert variant="error">{error}</InlineAlert>}

        {!loading && !error && payload && (
          <>
            <ProgressReviewStrip payload={payload} />

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Sub-test Performance Trend"
                title="See score movement on the canonical 0-500 scale"
                description="Tap any subtest card to hide or show its line. The dashed reference line is the Grade B pass mark (350)."
                className="mb-3"
              />
              <ProgressSubtestMiniCards subtests={payload.subtests} visible={visibleSubtests} onToggle={onSubtestToggle} />
            </section>

            <section className="rounded-[32px] border border-gray-200 bg-surface p-5 sm:p-6 shadow-sm">
              <div className="flex flex-wrap items-center justify-between gap-3 mb-4">
                <div className="inline-flex items-center gap-1 rounded-xl border border-gray-200 bg-background-light p-1" role="tablist" aria-label="Progress views">
                  {(['trend', 'criterion', 'comparative'] as const).map((t) => (
                    <button
                      key={t}
                      role="tab"
                      aria-selected={activeTab === t}
                      onClick={() => onTabChange(t)}
                      className={`px-4 py-1.5 text-xs font-bold rounded-lg transition-colors capitalize ${
                        activeTab === t ? 'bg-white text-navy shadow-sm' : 'text-muted hover:text-navy'
                      }`}
                    >
                      {t === 'trend' ? 'Trend' : t === 'criterion' ? 'Criterion' : 'Comparative'}
                    </button>
                  ))}
                </div>
                {activeTab === 'criterion' && (
                  <div className="inline-flex items-center gap-1 rounded-xl border border-gray-200 bg-background-light p-1" role="radiogroup" aria-label="Criterion subtest">
                    {(['writing', 'speaking'] as const).map((s) => (
                      <button
                        key={s}
                        role="radio"
                        aria-checked={criterionSubtest === s}
                        onClick={() => onCriterionChange(s)}
                        className={`px-3 py-1.5 text-xs font-bold rounded-lg transition-colors capitalize ${
                          criterionSubtest === s ? 'bg-white text-navy shadow-sm' : 'text-muted hover:text-navy'
                        }`}
                      >
                        {s}
                      </button>
                    ))}
                  </div>
                )}
              </div>

              {activeTab === 'trend' && <ProgressTrendChart payload={payload} visibleSubtests={visibleSubtests} />}
              {activeTab === 'criterion' && <ProgressCriterionChart payload={payload} subtest={criterionSubtest} />}
              {activeTab === 'comparative' && <ProgressComparativeTab comparative={payload.comparative} />}
            </section>

            <ProgressReadinessStrip payload={payload} />

            <ProgressActivityPanel payload={payload} />

            {/* Hidden screen-reader summary so the whole dashboard can be linearised */}
            <ChartTabularFallback
              caption="Progress dashboard text summary"
              headers={['Metric', 'Value']}
              rows={[
                ['Range', range],
                ['Target country', payload.meta.targetCountry ?? 'Not set'],
                ['Completed evaluations', payload.totals.completedEvaluations],
                ['Mock attempts', payload.totals.mockAttempts],
                ['Writing submissions', payload.totals.writingSubmissions],
                ['Speaking submissions', payload.totals.speakingSubmissions],
                ['Average review turnaround (hours)', payload.reviewUsage.averageTurnaroundHours ?? 'Pending'],
                ['Days to exam', payload.goals.daysToExam ?? 'Not set'],
              ]}
            />
          </>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
