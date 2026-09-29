'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Sparkles, AlertTriangle, TrendingUp } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
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

  return (
    <>
      <LearnerPageHero
        icon={<Sparkles className="h-6 w-6" />}
        eyebrow="Insights"
        title="Your AI usage"
        description="How much AI you're using, what it's costing in credits, and what's coming up."
      />

      {error && (
        <InlineAlert
          variant="error"
          action={<Button size="sm" variant="outline" onClick={() => { setError(null); void load(); }}>Retry</Button>}
        >
          {error}
        </InlineAlert>
      )}

      {summary === null ? (
        error ? null : <Skeleton className="h-48 w-full" />
      ) : (
        <div className="space-y-6">
          {/* Headline cards — Credits / Attempts only. Raw provider token
              counts and platform cost data are operational data shown only in
              the admin AI/API Usage & Billing view. */}
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Card label="AI calls (30d)" value={summary.totalCalls.toLocaleString()} />
            <Card label="Credits used (30d)" value={`${summary.creditsUsed.toLocaleString()} credits`} />
            <Card label="Wallet balance" value={`${summary.walletBalance} credits`} />
          </div>

          {/* Forecast */}
          {forecast && (
            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex items-center gap-2 mb-3">
                <TrendingUp className="h-5 w-5" aria-hidden="true" />
                <h2 className="text-lg font-semibold">Forecast: next 30 days</h2>
              </div>
              <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                <Mini label="Predicted calls" value={forecast.forecastCalls.toLocaleString()} />
                <Mini label="Predicted credits" value={forecast.forecastCredits.toLocaleString()} />
              </div>
              {forecast.suggestedTopUpCredits > 0 && (
                <InlineAlert
                  variant="warning"
                  className="mt-4"
                  action={<Button variant="primary" size="sm" onClick={() => location.href = '/ai-packages'}>Top up</Button>}
                >
                  <strong>Suggested top-up:</strong> {forecast.suggestedTopUpCredits} credits to cover predicted usage.
                </InlineAlert>
              )}
            </section>
          )}

          {/* Churn risk (only show if medium/high) */}
          {churn && churn.riskBand !== 'low' && (
            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex items-center gap-2 mb-3">
                <AlertTriangle className="h-5 w-5" aria-hidden="true" />
                <h2 className="text-lg font-semibold">Account health</h2>
                <Badge variant={riskBadgeVariant(churn.riskBand)}>{churn.riskBand}</Badge>
              </div>
              <p className="text-sm text-muted">
                Risk score {(churn.riskScore * 100).toFixed(0)}%. {churn.recommendedAction ? `Recommended: ${churn.recommendedAction.replace(/_/g, ' ')}.` : ''}
              </p>
            </section>
          )}

          {/* Per-feature breakdown */}
          <section className="space-y-2">
            <h2 className="text-lg font-semibold">By feature</h2>
            {summary.byFeature.length === 0 ? (
              <p className="text-sm text-muted">No AI calls yet in this window.</p>
            ) : (
              <div className="overflow-hidden rounded-2xl border border-border bg-surface shadow-sm">
                <table className="w-full text-sm">
                  <thead className="bg-background-light text-left">
                    <tr>
                      <th className="px-4 py-2">Feature</th>
                      <th className="px-4 py-2 text-right">Calls</th>
                    </tr>
                  </thead>
                  <tbody>
                    {summary.byFeature.map((f) => (
                      <tr key={f.featureCode} className="border-t border-border">
                        <td className="px-4 py-2 font-mono text-xs">{f.featureCode}</td>
                        <td className="px-4 py-2 text-right">{f.calls.toLocaleString()}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          {/* Daily sparkline (text) */}
          <section className="space-y-2">
            <h2 className="text-lg font-semibold">Daily activity</h2>
            <div className="overflow-x-auto rounded-2xl border border-border bg-surface p-4 shadow-sm">
              <div
                className="flex h-32 items-end gap-1"
                role="img"
                aria-label={`Daily AI calls over ${summary.daily.length} days, peaking at ${Math.max(0, ...summary.daily.map((b) => b.calls))} calls`}
              >
                {summary.daily.map((d) => {
                  const max = Math.max(1, ...summary.daily.map((b) => b.calls));
                  const heightPct = Math.max(4, (d.calls / max) * 100);
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
              <p className="mt-2 text-xs text-muted">{summary.daily.length} day{summary.daily.length === 1 ? '' : 's'} of data.</p>
            </div>
          </section>
        </div>
      )}
    </>
  );
}

function Card({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
      <p className="text-sm text-muted">{label}</p>
      <p className="mt-1 text-2xl font-semibold">{value}</p>
    </div>
  );
}

function Mini({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg bg-background-light p-3">
      <p className="text-xs text-muted">{label}</p>
      <p className="text-lg font-semibold">{value}</p>
    </div>
  );
}
