'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { AlertTriangle, Users, Activity } from 'lucide-react';
import { fetchAdminReadinessMetrics, type AdminReadinessMetrics } from '@/lib/api';
import { InlineAlert } from '@/components/ui/alert';
import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { KpiTile } from '@/components/admin/ui/kpi-tile';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Readiness', href: '/admin/readiness' },
  { label: 'Platform metrics' },
];

export default function AdminReadinessMetricsPage() {
  const [data, setData] = useState<AdminReadinessMetrics | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    let cancelled = false;
    fetchAdminReadinessMetrics()
      .then((m) => { if (!cancelled) setData(m); })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Could not load metrics.'); });
    return () => { cancelled = true; };
  }, []);

  if (!data) {
    return (
      <AdminOperationsLayout
        title="Platform readiness metrics"
        breadcrumbs={BREADCRUMBS}
        eyebrow="Readiness"
        icon={<Activity className="h-5 w-5" />}
        backHref="/admin/readiness"
      >
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
          {[1, 2, 3, 4, 5, 6, 7, 8].map(i => <Skeleton key={i} className="h-20 rounded-admin-lg" />)}
        </div>
      </AdminOperationsLayout>
    );
  }

  const total = data.learnersWithSnapshot;

  return (
    <AdminOperationsLayout
      title="Platform readiness metrics"
      description={`Snapshot generated at ${new Date(data.generatedAt).toLocaleString()} · ${total} learners with computed readiness.`}
      breadcrumbs={BREADCRUMBS}
      eyebrow="Readiness"
      icon={<Activity className="h-5 w-5" />}
      backHref="/admin/readiness"
      kpis={(
        <KpiStrip className="lg:grid-cols-4 xl:grid-cols-8">
          <KpiTile label="Learners" value={String(total)} icon={<Users className="h-4 w-4" />} />
          <KpiTile label="High risk" value={String(data.highRisk)} tone="danger" icon={<AlertTriangle className="h-4 w-4" />} />
          <KpiTile label="Moderate" value={String(data.moderateRisk)} tone="warning" />
          <KpiTile label="Low risk" value={String(data.lowRisk)} tone="success" />
          <KpiTile label="Unknown" value={String(data.unknownRisk)} />
          <KpiTile label="Intervention" value={String(data.interventionCandidates)} tone="danger" />
          <KpiTile label="Stale snapshots" value={String(data.staleSnapshots)} tone="warning" />
          <KpiTile label="Avg overall" value={data.avgOverall.toFixed(1)} tone="primary" />
        </KpiStrip>
      )}
      primaryGrid={(
        <Card>
          <CardHeader>
            <CardTitle className="text-sm">Average readiness by sub-test</CardTitle>
          </CardHeader>
          <CardContent>
            <div className="grid grid-cols-1 sm:grid-cols-5 gap-3">
              <SubAvg label="Writing" value={data.avgWriting} />
              <SubAvg label="Speaking" value={data.avgSpeaking} />
              <SubAvg label="Reading" value={data.avgReading} />
              <SubAvg label="Listening" value={data.avgListening} />
              <SubAvg label="Vocabulary" value={data.avgVocabulary} />
            </div>
          </CardContent>
        </Card>
      )}
      secondaryGrid={data.interventionCandidates > 0 ? (
        <Card surface="tinted-danger">
          <CardContent className="p-4">
            <div className="flex items-start gap-3">
              <AlertTriangle className="w-5 h-5 text-admin-danger shrink-0 mt-0.5" />
              <div>
                <p className="text-sm font-bold text-admin-fg-strong">{data.interventionCandidates} intervention candidates</p>
                <p className="text-xs text-admin-fg-muted">Learners with target-date probability below 50% need tutor or content team attention.</p>
                <Link href="/admin/readiness?risk=High" className="text-xs font-bold text-[var(--admin-primary)] hover:underline mt-2 inline-block">
                  View high-risk learners →
                </Link>
              </div>
            </div>
          </CardContent>
        </Card>
      ) : undefined}
    />
  );
}

function SubAvg({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-admin border border-admin-border p-3">
      <p className="text-[10px] uppercase tracking-widest font-bold text-admin-fg-muted">{label}</p>
      <p className="text-xl font-bold text-admin-fg-strong">{value.toFixed(1)}</p>
      <div className="h-1.5 w-full bg-admin-bg-subtle rounded-full overflow-hidden mt-1">
        <div className="h-full bg-[var(--admin-primary)]" style={{ width: `${Math.max(0, Math.min(100, value))}%` }} />
      </div>
    </div>
  );
}
