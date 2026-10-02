'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Sparkles, AlertTriangle, TrendingUp, BarChart3 } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import {
  fetchLearnerAiUsage,
  fetchMyChurnRisk,
  fetchMyForecast,
  type LearnerUsageSummaryDto,
  type ChurnRiskSnapshotDto,
  type UsageForecastSnapshotDto,
} from '@/lib/api';

function riskBadgeVariant(band: string) {
  if (band === 'high') return 'danger' as const;
  if (band === 'medium') return 'warning' as const;
  return 'success' as const;
}

// ponytail: CountUp prints digits without grouping, so 1,000+ stays locale-formatted text; a CountUp format prop would cover both.
function countValue(value: number, suffix = '') {
  return value < 1000 ? <CountUp value={value} suffix={suffix} /> : `${value.toLocaleString()}${suffix}`;
}

export default function LearnerAiUsagePage() {
  const today = useMemo(() => new Date().toISOString().slice(0, 10), []);
  const monthAgo = useMemo(() => {
    const d = new Date();
    d.setDate(d.getDate() - 30);
    return d.toISOString().slice(0, 10);
  }, []);

  const [summary, setSummary] = useState<LearnerUsageSummaryDto | null>(null);
  const [forecast, setForecast] = useState<UsageForecastSnapshotDto | null>(null);
  const [churn, setChurn] = useState<ChurnRiskSnapshotDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const [s, f, c] = await Promise.all([
        fetchLearnerAiUsage(monthAgo, today),
        fetchMyForecast(30),
        fetchMyChurnRisk(),
      ]);
      setSummary(s);
      setForecast(f);
      setChurn(c);
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load AI usage.');
    }
  }, [monthAgo, today]);

  useEffect(() => { void load(); }, [load]);

  const maxDailyCalls = summary ? Math.max(0, ...summary.daily.map((b) => b.calls)) : 0;

  return (
    <>
      <LearnerPageHero
        icon={Sparkles}
        eyebrow="Insights"
        title="Your AI usage"
        description="How much AI you're using, what it's costing in credits, and what's coming up."
      />

      {summary === null ? (
        error ? (
          <ErrorState message={error} onRetry={() => { setError(null); void load(); }} retryLabel="Retry" />
        ) : (
          <>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
              {[0, 1, 2].map((i) => <Skeleton key={i} className="h-24 rounded-2xl" />)}
            </div>
            <Skeleton className="h-48 w-full rounded-2xl" />
          </>
        )
      ) : (
        <>
          {error && (
            <InlineAlert
              variant="error"
              action={<Button size="sm" variant="outline" onClick={() => { setError(null); void load(); }}>Retry</Button>}
            >
              {error}
            </InlineAlert>
          )}

          {/* Headline cards — Credits / Attempts only. Raw provider token
              counts and platform cost data are operational data shown only in
              the admin AI/API Usage & Billing view. */}
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <MotionItem delayIndex={0}>
              <StatTile label="AI calls (30d)" value={countValue(summary.totalCalls)} />
            </MotionItem>
            <MotionItem delayIndex={1}>
              <StatTile label="Credits used (30d)" value={countValue(summary.creditsUsed, ' credits')} />
            </MotionItem>
            <MotionItem delayIndex={2}>
              <StatTile label="Wallet balance" value={countValue(summary.walletBalance, ' credits')} />
            </MotionItem>
          </div>

          {/* Forecast */}
          {forecast && (
            <MotionSection className="space-y-4">
              <LearnerSurfaceSectionHeader icon={TrendingUp} title="Forecast: next 30 days" />
              <Card>
                <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <Mini label="Predicted calls" value={forecast.forecastCalls.toLocaleString()} />
                  <Mini label="Predicted credits" value={forecast.forecastCredits.toLocaleString()} />
                </div>
                {forecast.suggestedTopUpCredits > 0 && (
                  <InlineAlert
                    variant="warning"
                    className="mt-4"
                    action={(
                      <Button asChild variant="primary" size="sm">
                        <Link href="/ai-packages">Top up</Link>
                      </Button>
                    )}
                  >
                    <strong>Suggested top-up:</strong> {forecast.suggestedTopUpCredits} credits to cover predicted usage.
                  </InlineAlert>
                )}
              </Card>
            </MotionSection>
          )}

          {/* Churn risk (only show if medium/high) */}
          {churn && churn.riskBand !== 'low' && (
            <MotionSection delayIndex={1} className="space-y-4">
              <LearnerSurfaceSectionHeader
                icon={AlertTriangle}
                title="Account health"
                action={<Badge variant={riskBadgeVariant(churn.riskBand)} className="self-start capitalize sm:self-auto">{churn.riskBand}</Badge>}
              />
              <Card>
                <p className="text-sm text-muted">
                  Risk score <span className="tabular-nums">{(churn.riskScore * 100).toFixed(0)}%</span>. {churn.recommendedAction ? `Recommended: ${churn.recommendedAction.replace(/_/g, ' ')}.` : ''}
                </p>
              </Card>
            </MotionSection>
          )}

          {/* Per-feature breakdown */}
          <MotionSection delayIndex={2} className="space-y-4">
            <LearnerSurfaceSectionHeader title="By feature" />
            {summary.byFeature.length === 0 ? (
              <EmptyState icon={<Sparkles className="h-8 w-8" />} title="No AI calls yet in this window." />
            ) : (
              <Card padding="none" className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead className="bg-background-light text-start">
                    <tr>
                      <th scope="col" className="px-4 py-2 text-start text-xs font-semibold text-muted">Feature</th>
                      <th scope="col" className="px-4 py-2 text-end text-xs font-semibold text-muted">Calls</th>
                    </tr>
                  </thead>
                  <tbody>
                    {summary.byFeature.map((f) => (
                      <tr key={f.featureCode} className="border-t border-border">
                        <td className="px-4 py-2 font-mono text-xs text-navy">{f.featureCode}</td>
                        <td className="px-4 py-2 text-end tabular-nums text-navy">{f.calls.toLocaleString()}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </Card>
            )}
          </MotionSection>

          {/* Daily sparkline (text) */}
          <MotionSection delayIndex={3} className="space-y-4">
            <LearnerSurfaceSectionHeader title="Daily activity" />
            {summary.daily.length === 0 ? (
              <EmptyState icon={<BarChart3 className="h-8 w-8" />} title="No AI calls yet in this window." />
            ) : (
              <Card className="overflow-x-auto">
                <div
                  className="flex h-32 items-end gap-1"
                  role="img"
                  aria-label={`Daily AI calls over ${summary.daily.length} days, peaking at ${maxDailyCalls} calls`}
                >
                  {summary.daily.map((d) => {
                    const heightPct = Math.max(4, (d.calls / Math.max(1, maxDailyCalls)) * 100);
                    return (
                      <div key={d.day} className="flex w-4 flex-col items-center" title={`${d.day}: ${d.calls} calls`}>
                        <div
                          className="w-3 rounded-t bg-primary"
                          style={{ height: `${heightPct}%` }}
                        />
                      </div>
                    );
                  })}
                </div>
                <p className="mt-2 text-xs tabular-nums text-muted">{summary.daily.length} day{summary.daily.length === 1 ? '' : 's'} of data.</p>
              </Card>
            )}
          </MotionSection>
        </>
      )}
    </>
  );
}

function StatTile({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <Card className="h-full">
      <p className="tile-label text-muted">{label}</p>
      <p className="mt-1 text-2xl font-semibold tabular-nums text-navy">{value}</p>
    </Card>
  );
}

function Mini({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl bg-background-light p-3">
      <p className="tile-label text-muted">{label}</p>
      <p className="text-lg font-semibold tabular-nums text-navy">{value}</p>
    </div>
  );
}
