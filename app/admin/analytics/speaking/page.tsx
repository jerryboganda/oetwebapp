'use client';

/**
 * OET Speaking — Phase 9 P9.2 — admin/expert analytics console.
 *
 * Aggregates the three analytics endpoints into one console:
 *  - GET /v1/expert/speaking/analytics/class
 *  - GET /v1/expert/speaking/analytics/tutor-consistency
 *  - GET /v1/admin/speaking/analytics/content-difficulty
 */
import { useCallback, useEffect, useState } from 'react';
import { Activity, BarChart3, Filter, Users } from 'lucide-react';
import { AdminOperationsLayout, KpiStrip, BentoGrid, BentoCell } from '@/components/admin/layout/admin-operations-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Input } from '@/components/ui/form-controls';
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
    return <div className="p-4 text-sm text-admin-fg-muted">{emptyLabel}</div>;
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
        <thead className="bg-admin-bg-subtle text-left text-xs uppercase tracking-wide text-admin-fg-muted">
          <tr>
            {columns.map((c) => (
              <th key={c} className="p-2">{c}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, idx) => (
            <tr key={idx} className="border-t border-admin-border">
              {columns.map((c) => {
                const v = row[c];
                let display: string;
                if (v === null || v === undefined) display = '—';
                else if (typeof v === 'object') display = JSON.stringify(v);
                else if (typeof v === 'number') display = Number.isInteger(v) ? String(v) : v.toFixed(2);
                else display = String(v);
                return (
                  <td key={c} className="p-2 align-top text-admin-fg-default">{display}</td>
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

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Analytics', href: '/admin' },
    { label: 'Speaking' },
  ];

  return (
    <AdminOperationsLayout
      title="Speaking · analytics console"
      description="Aggregated cohort, tutor-consistency, and content-difficulty metrics. All data is sourced from server-authoritative endpoints; no client-side scoring is performed."
      eyebrow="Analytics"
      breadcrumbs={breadcrumbs}
      actions={
        <Button onClick={reload} disabled={loading} size="sm">
          {loading ? 'Loading…' : 'Refresh'}
        </Button>
      }
      kpis={
        <KpiStrip>
          <KpiTile label="Cohort rows" value={toRows(classData).length} icon={<Users className="h-4 w-4" />} tone="primary" />
          <KpiTile label="Tutor rows" value={toRows(tutorData).length} icon={<Activity className="h-4 w-4" />} tone="info" />
          <KpiTile label="Content rows" value={toRows(contentData).length} icon={<BarChart3 className="h-4 w-4" />} tone="success" />
          <KpiTile label="Filters active" value={[cohortId, professionId, tutorId].filter(Boolean).length} icon={<Filter className="h-4 w-4" />} tone="default" />
        </KpiStrip>
      }
      primaryGrid={
        <div className="space-y-6">
          <Card>
            <CardHeader><CardTitle>Filters</CardTitle></CardHeader>
            <CardContent className="space-y-4">
              <div className="grid gap-3 sm:grid-cols-3">
                <Input placeholder="Cohort ID (optional)" value={cohortId} onChange={(e) => setCohortId(e.target.value)} />
                <Input placeholder="Profession (e.g. doctor)" value={professionId} onChange={(e) => setProfessionId(e.target.value)} />
                <Input placeholder="Tutor ID (optional)" value={tutorId} onChange={(e) => setTutorId(e.target.value)} />
              </div>
            </CardContent>
          </Card>

          {error && <InlineAlert variant="error">{error}</InlineAlert>}

          <BentoGrid>
            <BentoCell span={12}>
              <Card>
                <CardHeader>
                  <div className="flex items-center justify-between">
                    <CardTitle>Cohort performance</CardTitle>
                    <span className="text-xs text-admin-fg-muted">{summarise(classData)}</span>
                  </div>
                </CardHeader>
                <CardContent>
                  {loading && !classData ? (
                    <Skeleton className="h-32 w-full rounded-admin" />
                  ) : (
                    <RowsTable payload={classData} emptyLabel="No cohort rows for the current filter." />
                  )}
                </CardContent>
              </Card>
            </BentoCell>

            <BentoCell span={{ default: 12, xl: 6 }}>
              <Card>
                <CardHeader>
                  <div className="flex items-center justify-between">
                    <CardTitle>Tutor consistency</CardTitle>
                    <span className="text-xs text-admin-fg-muted">{summarise(tutorData)}</span>
                  </div>
                </CardHeader>
                <CardContent>
                  {loading && !tutorData ? (
                    <Skeleton className="h-32 w-full rounded-admin" />
                  ) : (
                    <RowsTable payload={tutorData} emptyLabel="No tutor drift detected for the filter." />
                  )}
                </CardContent>
              </Card>
            </BentoCell>

            <BentoCell span={{ default: 12, xl: 6 }}>
              <Card>
                <CardHeader>
                  <div className="flex items-center justify-between">
                    <CardTitle>Content difficulty</CardTitle>
                    <span className="text-xs text-admin-fg-muted">{summarise(contentData)}</span>
                  </div>
                </CardHeader>
                <CardContent>
                  {loading && !contentData ? (
                    <Skeleton className="h-32 w-full rounded-admin" />
                  ) : (
                    <RowsTable
                      payload={contentData}
                      emptyLabel="No content-difficulty signal yet — need more attempts per card."
                    />
                  )}
                </CardContent>
              </Card>
            </BentoCell>
          </BentoGrid>
        </div>
      }
    />
  );
}
