'use client';

/**
 * OET Speaking — Phase 9 P9.2 — admin/expert analytics console.
 *
 * Aggregates the three analytics endpoints into one console:
 *  - GET /v1/expert/speaking/analytics/class
 *  - GET /v1/expert/speaking/analytics/tutor-consistency
 *  - GET /v1/admin/speaking/analytics/content-difficulty
 *
 * Uses defensive typing (`unknown` → narrow on render) so the page
 * compiles even before the analytics contracts are finalised on the
 * backend; each row is rendered through a tolerant projector.
 */
import { useCallback, useEffect, useState } from 'react';
import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  fetchSpeakingAnalyticsClass,
  fetchSpeakingContentDifficulty,
  fetchSpeakingTutorConsistency,
} from '@/lib/api/speaking-compliance';

type Json = unknown;

function toRows(payload: Json): Record<string, unknown>[] {
  if (!payload) return [];
  if (Array.isArray(payload)) {
    return payload.filter((x) => x && typeof x === 'object') as Record<string, unknown>[];
  }
  if (typeof payload === 'object') {
    const obj = payload as Record<string, unknown>;
    for (const key of ['rows', 'items', 'tutors', 'cohorts', 'cards', 'data']) {
      const val = obj[key];
      if (Array.isArray(val)) {
        return val.filter((x) => x && typeof x === 'object') as Record<string, unknown>[];
      }
    }
  }
  return [];
}

function summarise(payload: Json, fallback = 'No data.'): string {
  const rows = toRows(payload);
  if (rows.length === 0) return fallback;
  return `${rows.length} row${rows.length === 1 ? '' : 's'}`;
}

function RowsTable({ payload, emptyLabel }: { payload: Json; emptyLabel: string }) {
  const rows = toRows(payload);
  if (rows.length === 0) {
    return <div className="p-4 text-sm text-slate-500">{emptyLabel}</div>;
  }
  const columns = Array.from(
    rows.reduce<Set<string>>((acc, row) => {
      Object.keys(row).forEach((k) => acc.add(k));
      return acc;
    }, new Set()),
  );
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <thead className="bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-500">
          <tr>
            {columns.map((c) => (
              <th key={c} className="p-2">
                {c}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, idx) => (
            <tr key={idx} className="border-t border-slate-100">
              {columns.map((c) => {
                const v = row[c];
                let display: string;
                if (v === null || v === undefined) display = '—';
                else if (typeof v === 'object') display = JSON.stringify(v);
                else if (typeof v === 'number')
                  display = Number.isInteger(v) ? String(v) : v.toFixed(2);
                else display = String(v);
                return (
                  <td key={c} className="p-2 align-top text-slate-700">
                    {display}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default function AdminSpeakingAnalyticsPage() {
  const [cohortId, setCohortId] = useState('');
  const [professionId, setProfessionId] = useState('');
  const [tutorId, setTutorId] = useState('');

  const [classData, setClassData] = useState<Json>(null);
  const [tutorData, setTutorData] = useState<Json>(null);
  const [contentData, setContentData] = useState<Json>(null);

  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [klass, tutor, content] = await Promise.all([
        fetchSpeakingAnalyticsClass(cohortId || undefined, professionId || undefined),
        fetchSpeakingTutorConsistency(tutorId || undefined),
        fetchSpeakingContentDifficulty(professionId || undefined),
      ]);
      setClassData(klass);
      setTutorData(tutor);
      setContentData(content);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load analytics.');
    } finally {
      setLoading(false);
    }
  }, [cohortId, professionId, tutorId]);

  useEffect(() => {
    reload();
  }, [reload]);

  return (
    <AdminRouteWorkspace role="main" aria-label="Speaking analytics">
      <div className="space-y-6">
        <header className="space-y-2">
          <h1 className="text-2xl font-semibold text-slate-900">Speaking · analytics console</h1>
          <p className="text-slate-600">
            Aggregated cohort, tutor-consistency, and content-difficulty metrics. All data is
            sourced from server-authoritative endpoints; no client-side scoring is performed.
          </p>
        </header>

        <Card className="space-y-4 p-4">
          <div className="grid gap-3 sm:grid-cols-3">
            <Input
              placeholder="Cohort ID (optional)"
              value={cohortId}
              onChange={(e) => setCohortId(e.target.value)}
            />
            <Input
              placeholder="Profession (e.g. doctor)"
              value={professionId}
              onChange={(e) => setProfessionId(e.target.value)}
            />
            <Input
              placeholder="Tutor ID (optional)"
              value={tutorId}
              onChange={(e) => setTutorId(e.target.value)}
            />
          </div>
          <div className="flex justify-end">
            <Button onClick={reload} disabled={loading}>
              {loading ? 'Loading…' : 'Refresh'}
            </Button>
          </div>
        </Card>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        <Card>
          <header className="flex items-center justify-between border-b border-slate-100 p-4">
            <h2 className="font-semibold text-slate-900">Cohort performance</h2>
            <span className="text-xs text-slate-500">{summarise(classData)}</span>
          </header>
          {loading && !classData ? (
            <Skeleton className="m-4 h-32 w-[calc(100%-2rem)]" />
          ) : (
            <RowsTable payload={classData} emptyLabel="No cohort rows for the current filter." />
          )}
        </Card>

        <Card>
          <header className="flex items-center justify-between border-b border-slate-100 p-4">
            <h2 className="font-semibold text-slate-900">Tutor consistency</h2>
            <span className="text-xs text-slate-500">{summarise(tutorData)}</span>
          </header>
          {loading && !tutorData ? (
            <Skeleton className="m-4 h-32 w-[calc(100%-2rem)]" />
          ) : (
            <RowsTable payload={tutorData} emptyLabel="No tutor drift detected for the filter." />
          )}
        </Card>

        <Card>
          <header className="flex items-center justify-between border-b border-slate-100 p-4">
            <h2 className="font-semibold text-slate-900">Content difficulty</h2>
            <span className="text-xs text-slate-500">{summarise(contentData)}</span>
          </header>
          {loading && !contentData ? (
            <Skeleton className="m-4 h-32 w-[calc(100%-2rem)]" />
          ) : (
            <RowsTable
              payload={contentData}
              emptyLabel="No content-difficulty signal yet — need more attempts per card."
            />
          )}
        </Card>
      </div>
    </AdminRouteWorkspace>
  );
}
