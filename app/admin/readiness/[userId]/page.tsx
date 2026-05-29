'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { RefreshCcw, AlertTriangle, TrendingUp, Gauge } from 'lucide-react';
import { fetchAdminReadinessLearner, recomputeAdminReadiness } from '@/lib/api';
import { InlineAlert } from '@/components/ui/alert';
import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Button } from '@/components/admin/ui/button';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { ReadinessTrendChart } from '@/components/domain/readiness-trend-chart';
import type { ReadinessHistoryPoint } from '@/lib/mock-data';

export default function AdminReadinessLearnerDetailPage() {
  const params = useParams<{ userId: string }>();
  const userId = params?.userId ?? '';
  const [data, setData] = useState<Awaited<ReturnType<typeof fetchAdminReadinessLearner>> | null>(null);
  const [history, setHistory] = useState<ReadinessHistoryPoint[]>([]);
  const [recomputing, setRecomputing] = useState(false);
  const [error, setError] = useState('');

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Readiness', href: '/admin/readiness' },
    { label: userId },
  ];

  const load = useCallback(async () => {
    setError('');
    try {
      const result = await fetchAdminReadinessLearner(userId);
      setData(result);
      setHistory(result.history);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load learner readiness.');
    }
  }, [userId]);

  useEffect(() => { void load(); }, [load]);

  async function handleRecompute() {
    setRecomputing(true);
    try {
      await recomputeAdminReadiness(userId);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Recompute failed.');
    } finally {
      setRecomputing(false);
    }
  }

  if (!data) {
    return (
      <AdminOperationsLayout
        title="Learner readiness"
        breadcrumbs={breadcrumbs}
        eyebrow="Readiness"
        icon={<Gauge className="h-5 w-5" />}
        backHref="/admin/readiness"
      >
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <Skeleton className="h-12 rounded-admin-lg" />
        <Skeleton className="h-40 rounded-admin-lg" />
        <Skeleton className="h-56 rounded-admin-lg" />
      </AdminOperationsLayout>
    );
  }

  const snapshot = data.snapshot as Record<string, unknown>;
  const overall = Number(snapshot.overallReadiness ?? 0);
  const risk = String(snapshot.overallRisk ?? 'Unknown');
  const probability = typeof snapshot.targetDateProbability === 'number' ? snapshot.targetDateProbability : null;
  const weakest = typeof snapshot.weakestSubtest === 'string' ? snapshot.weakestSubtest : null;
  const confidence = String(snapshot.confidenceLevel ?? 'Low');
  const dataPoints = Number(snapshot.dataPointCount ?? 0);
  const subTests = Array.isArray(snapshot.subTests) ? snapshot.subTests as Record<string, unknown>[] : [];
  const blockers = Array.isArray(snapshot.blockers) ? snapshot.blockers as Record<string, unknown>[] : [];

  const riskTone: 'danger' | 'warning' | 'success' | 'default' =
    risk === 'High' ? 'danger' : risk === 'Moderate' ? 'warning' : risk === 'Low' ? 'success' : 'default';

  return (
    <AdminOperationsLayout
      title={data.displayName ?? data.userId}
      description={`Target date: ${data.targetExamDate ?? '-'}`}
      breadcrumbs={breadcrumbs}
      eyebrow="Readiness"
      icon={<Gauge className="h-5 w-5" />}
      backHref="/admin/readiness"
      actions={(
        <Button onClick={handleRecompute} disabled={recomputing} variant="outline">
          <RefreshCcw className={`mr-2 w-4 h-4 ${recomputing ? 'animate-spin' : ''}`} />
          {recomputing ? 'Recomputing…' : 'Force recompute'}
        </Button>
      )}
      kpis={(
        <KpiStrip>
          <KpiTile label="Overall" value={`${Math.round(overall)}`} tone="primary" />
          <KpiTile label="Risk" value={risk} tone={riskTone} />
          <KpiTile label="Target-date probability" value={probability != null ? `${Math.round(probability)}%` : '-'} />
          <KpiTile label="Confidence" value={`${confidence} · ${dataPoints} pts`} />
        </KpiStrip>
      )}
      primaryGrid={(
        <div className="space-y-6">
          {error && <InlineAlert variant="error">{error}</InlineAlert>}

          <Card>
            <CardHeader>
              <CardTitle className="text-sm flex items-center gap-2">
                <AlertTriangle className="w-4 h-4 text-admin-warning" /> Reasoning trace
              </CardTitle>
            </CardHeader>
            <CardContent>
              <p className="text-sm text-admin-fg-muted">{data.reasoningTrace}</p>
              {weakest && <p className="text-xs text-admin-fg-muted mt-2">Weakest sub-test: <strong>{weakest}</strong></p>}
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-sm flex items-center gap-2">
                <TrendingUp className="w-4 h-4 text-[var(--admin-primary)]" /> History (12 weeks)
              </CardTitle>
            </CardHeader>
            <CardContent>
              <ReadinessTrendChart data={history} series="overall" target={70} />
            </CardContent>
          </Card>
        </div>
      )}
      secondaryGrid={(
        <div className="grid gap-6 lg:grid-cols-2">
          <Card>
            <CardHeader>
              <CardTitle className="text-sm">Sub-test readiness</CardTitle>
            </CardHeader>
            <CardContent>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                {subTests.map((t) => (
                  <div key={String(t.code ?? t.id)} className="border border-admin-border rounded-admin p-3">
                    <div className="flex items-center justify-between mb-1">
                      <span className="text-sm font-bold text-admin-fg-strong">{String(t.name ?? t.code)}</span>
                      <span className="text-sm font-bold text-admin-fg-strong">{Math.round(Number(t.readiness ?? 0))}</span>
                    </div>
                    <p className="text-[11px] text-admin-fg-muted">{String(t.status ?? '')} · target {Number(t.target ?? 70)}</p>
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle className="text-sm">Blockers ({blockers.length})</CardTitle>
            </CardHeader>
            <CardContent>
              {blockers.length === 0 ? (
                <p className="text-sm text-admin-fg-muted">No active blockers.</p>
              ) : (
                <ul className="space-y-2 text-sm">
                  {blockers.map((b, i) => (
                    <li key={String(b.id ?? i)} className="border border-admin-border rounded-admin p-3">
                      <p className="font-semibold text-admin-fg-strong">{String(b.title ?? '')}</p>
                      <p className="text-xs text-admin-fg-muted">{String(b.description ?? '')}</p>
                    </li>
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
        </div>
      )}
    />
  );
}
