'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { TrendingUp, RefreshCcw, AlertTriangle, ChevronRight } from 'lucide-react';
import { fetchAdminReadinessLearners, recomputeAdminReadiness, type AdminReadinessLearnerRow, type AdminReadinessLearnerList } from '@/lib/api';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

const RISK_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'All' },
  { value: 'High', label: 'High risk' },
  { value: 'Moderate', label: 'Moderate' },
  { value: 'Low', label: 'Low' },
  { value: 'Unknown', label: 'Unknown' },
];

const RISK_BADGE: Record<string, string> = {
  High: 'bg-danger/10 text-danger border-danger/20',
  Moderate: 'bg-warning/10 text-warning border-warning/20',
  Low: 'bg-success/10 text-success border-success/20',
  Unknown: 'bg-muted/10 text-muted border-border',
};

export default function AdminReadinessLearnersPage() {
  const [data, setData] = useState<AdminReadinessLearnerList | null>(null);
  const [risk, setRisk] = useState('');
  const [page, setPage] = useState(1);
  const [error, setError] = useState('');
  const [recomputing, setRecomputing] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError('');
    try {
      const result = await fetchAdminReadinessLearners({ risk: risk || undefined, page, pageSize: 25 });
      setData(result);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load learner readiness list.');
    }
  }, [risk, page]);

  useEffect(() => { void load(); }, [load]);

  async function handleRecompute(userId: string) {
    setRecomputing(userId);
    try {
      await recomputeAdminReadiness(userId);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Recompute failed.');
    } finally {
      setRecomputing(null);
    }
  }

  return (
    <div className="space-y-6">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-navy flex items-center gap-2">
            <TrendingUp className="w-6 h-6 text-primary" />
            Learner readiness oversight
          </h1>
          <p className="text-sm text-muted">Identify learners at risk of missing target dates and trigger intervention.</p>
        </div>
        <Link href="/admin/readiness/metrics" className="text-sm font-bold text-primary hover:underline">
          View platform metrics →
        </Link>
      </header>

      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      <div className="flex items-center gap-3 flex-wrap">
        <label className="text-xs font-bold uppercase tracking-widest text-muted">Filter by risk</label>
        {RISK_OPTIONS.map(opt => (
          <button
            key={opt.value}
            onClick={() => { setRisk(opt.value); setPage(1); }}
            className={`text-xs font-bold px-3 py-1.5 rounded-full border ${risk === opt.value ? 'bg-primary text-primary-foreground border-primary' : 'bg-surface text-navy border-border hover:bg-background-light'}`}
          >
            {opt.label}
          </button>
        ))}
      </div>

      {!data ? (
        <div className="space-y-3">
          {[1, 2, 3, 4, 5].map(i => <Skeleton key={i} className="h-14 rounded-xl" />)}
        </div>
      ) : (
        <div className="rounded-2xl border border-border bg-surface overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-background-light text-[11px] font-bold uppercase tracking-widest text-muted">
              <tr>
                <th className="text-left px-4 py-3">Learner</th>
                <th className="text-left px-4 py-3">Target date</th>
                <th className="text-left px-4 py-3">Overall</th>
                <th className="text-left px-4 py-3">Risk</th>
                <th className="text-left px-4 py-3">Weakest</th>
                <th className="text-left px-4 py-3">Probability</th>
                <th className="text-left px-4 py-3">Updated</th>
                <th className="text-right px-4 py-3">Actions</th>
              </tr>
            </thead>
            <tbody>
              {data.items.length === 0 ? (
                <tr><td colSpan={8} className="px-4 py-8 text-center text-muted">No learners matched the filter.</td></tr>
              ) : data.items.map((row: AdminReadinessLearnerRow) => (
                <tr key={row.userId} className="border-t border-border hover:bg-background-light/50">
                  <td className="px-4 py-3 font-semibold text-navy">{row.displayName}</td>
                  <td className="px-4 py-3 text-muted">{row.targetExamDate ?? '—'}</td>
                  <td className="px-4 py-3 font-bold">{Math.round(row.overallReadiness)}</td>
                  <td className="px-4 py-3">
                    <span className={`text-[10px] font-bold uppercase tracking-widest px-2 py-0.5 rounded-full border ${RISK_BADGE[row.overallRisk] ?? RISK_BADGE.Unknown}`}>
                      {row.overallRisk}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-muted">{row.weakestSubtest ?? '—'}</td>
                  <td className="px-4 py-3 text-muted">{row.targetDateProbability != null ? `${Math.round(row.targetDateProbability)}%` : '—'}</td>
                  <td className="px-4 py-3 text-muted text-xs">{new Date(row.computedAt).toLocaleDateString()}</td>
                  <td className="px-4 py-3 text-right">
                    <div className="flex items-center justify-end gap-2">
                      <button
                        onClick={() => handleRecompute(row.userId)}
                        disabled={recomputing === row.userId}
                        className="inline-flex items-center gap-1 text-xs font-bold text-muted hover:text-primary disabled:opacity-50"
                      >
                        <RefreshCcw className={`w-3.5 h-3.5 ${recomputing === row.userId ? 'animate-spin' : ''}`} />
                        {recomputing === row.userId ? 'Recomputing' : 'Recompute'}
                      </button>
                      <Link
                        href={`/admin/readiness/${encodeURIComponent(row.userId)}`}
                        className="inline-flex items-center gap-1 text-xs font-bold text-primary hover:underline"
                      >
                        Open <ChevronRight className="w-3.5 h-3.5" />
                      </Link>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {data.total > data.pageSize && (
            <div className="flex items-center justify-between px-4 py-3 border-t border-border text-xs text-muted">
              <span>Page {data.page} · {data.total} learners</span>
              <div className="flex gap-2">
                <button onClick={() => setPage(Math.max(1, page - 1))} disabled={page <= 1} className="font-bold text-primary disabled:opacity-50">Prev</button>
                <button onClick={() => setPage(page + 1)} disabled={data.items.length < data.pageSize} className="font-bold text-primary disabled:opacity-50">Next</button>
              </div>
            </div>
          )}
        </div>
      )}

      {data && data.items.some(r => r.overallRisk === 'High') && (
        <div className="rounded-2xl border border-danger/20 bg-danger/5 p-4 flex items-start gap-3">
          <AlertTriangle className="w-5 h-5 text-danger shrink-0 mt-0.5" />
          <div>
            <p className="text-sm font-bold text-navy">Intervention candidates detected</p>
            <p className="text-xs text-muted">High-risk learners benefit from outreach — consider a tutor check-in or revised study plan.</p>
          </div>
        </div>
      )}
    </div>
  );
}
