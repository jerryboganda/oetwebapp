'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, RefreshCcw, AlertTriangle, TrendingUp } from 'lucide-react';
import { fetchAdminReadinessLearner, recomputeAdminReadiness } from '@/lib/api';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { ReadinessTrendChart } from '@/components/domain/readiness-trend-chart';
import type { ReadinessHistoryPoint } from '@/lib/mock-data';

export default function AdminReadinessLearnerDetailPage() {
  const params = useParams<{ userId: string }>();
  const userId = params?.userId ?? '';
  const [data, setData] = useState<Awaited<ReturnType<typeof fetchAdminReadinessLearner>> | null>(null);
  const [history, setHistory] = useState<ReadinessHistoryPoint[]>([]);
  const [recomputing, setRecomputing] = useState(false);
  const [error, setError] = useState('');

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
      <div className="space-y-6">
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <Skeleton className="h-12 rounded-xl" />
        <Skeleton className="h-40 rounded-xl" />
        <Skeleton className="h-56 rounded-xl" />
      </div>
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

  return (
    <div className="space-y-6">
      <header className="flex items-center justify-between gap-3 flex-wrap">
        <div>
          <Link href="/admin/readiness" className="inline-flex items-center gap-1 text-xs font-bold text-primary hover:underline mb-2">
            <ArrowLeft className="w-3.5 h-3.5" /> Back to learners
          </Link>
          <h1 className="text-2xl font-bold text-navy">{data.displayName ?? data.userId}</h1>
          <p className="text-sm text-muted">Target date: {data.targetExamDate ?? '—'}</p>
        </div>
        <button
          onClick={handleRecompute}
          disabled={recomputing}
          className="inline-flex items-center gap-1.5 text-sm font-bold text-primary hover:underline disabled:opacity-50"
        >
          <RefreshCcw className={`w-4 h-4 ${recomputing ? 'animate-spin' : ''}`} />
          {recomputing ? 'Recomputing…' : 'Force recompute'}
        </button>
      </header>

      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      <section className="grid grid-cols-1 sm:grid-cols-4 gap-4">
        <Stat label="Overall" value={`${Math.round(overall)}`} accent="text-navy" />
        <Stat label="Risk" value={risk} accent={risk === 'High' ? 'text-danger' : risk === 'Moderate' ? 'text-warning' : risk === 'Low' ? 'text-success' : 'text-muted'} />
        <Stat label="Target-date probability" value={probability != null ? `${Math.round(probability)}%` : '—'} accent="text-navy" />
        <Stat label="Confidence" value={`${confidence} · ${dataPoints} pts`} accent="text-navy" />
      </section>

      <section className="rounded-2xl border border-border bg-surface p-4">
        <h2 className="text-sm font-bold text-navy mb-2 flex items-center gap-2">
          <AlertTriangle className="w-4 h-4 text-warning" /> Reasoning trace
        </h2>
        <p className="text-sm text-muted">{data.reasoningTrace}</p>
        {weakest && <p className="text-xs text-muted mt-2">Weakest sub-test: <strong>{weakest}</strong></p>}
      </section>

      <section className="rounded-2xl border border-border bg-surface p-4">
        <h2 className="text-sm font-bold text-navy mb-3 flex items-center gap-2">
          <TrendingUp className="w-4 h-4 text-primary" /> History (12 weeks)
        </h2>
        <ReadinessTrendChart data={history} series="overall" target={70} />
      </section>

      <section className="rounded-2xl border border-border bg-surface p-4">
        <h2 className="text-sm font-bold text-navy mb-3">Sub-test readiness</h2>
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
          {subTests.map((t) => (
            <div key={String(t.code ?? t.id)} className="border border-border rounded-xl p-3">
              <div className="flex items-center justify-between mb-1">
                <span className="text-sm font-bold text-navy">{String(t.name ?? t.code)}</span>
                <span className="text-sm font-bold text-navy">{Math.round(Number(t.readiness ?? 0))}</span>
              </div>
              <p className="text-[11px] text-muted">{String(t.status ?? '')} · target {Number(t.target ?? 70)}</p>
            </div>
          ))}
        </div>
      </section>

      <section className="rounded-2xl border border-border bg-surface p-4">
        <h2 className="text-sm font-bold text-navy mb-3">Blockers ({blockers.length})</h2>
        {blockers.length === 0 ? (
          <p className="text-sm text-muted">No active blockers.</p>
        ) : (
          <ul className="space-y-2 text-sm">
            {blockers.map((b, i) => (
              <li key={String(b.id ?? i)} className="border border-border rounded-xl p-3">
                <p className="font-semibold text-navy">{String(b.title ?? '')}</p>
                <p className="text-xs text-muted">{String(b.description ?? '')}</p>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}

function Stat({ label, value, accent }: { label: string; value: string; accent: string }) {
  return (
    <div className="rounded-2xl border border-border bg-surface p-4">
      <p className="text-[10px] uppercase tracking-widest font-bold text-muted">{label}</p>
      <p className={`text-2xl font-bold mt-1 ${accent}`}>{value}</p>
    </div>
  );
}
