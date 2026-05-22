'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, AlertTriangle, Users, Activity } from 'lucide-react';
import { fetchAdminReadinessMetrics, type AdminReadinessMetrics } from '@/lib/api';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';

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
      <div className="space-y-6">
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <Skeleton className="h-12 rounded-xl" />
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
          {[1, 2, 3, 4, 5, 6, 7, 8].map(i => <Skeleton key={i} className="h-20 rounded-xl" />)}
        </div>
      </div>
    );
  }

  const total = data.learnersWithSnapshot;

  return (
    <div className="space-y-6">
      <header>
        <Link href="/admin/readiness" className="inline-flex items-center gap-1 text-xs font-bold text-primary hover:underline mb-2">
          <ArrowLeft className="w-3.5 h-3.5" /> Back to learners
        </Link>
        <h1 className="text-2xl font-bold text-navy flex items-center gap-2">
          <Activity className="w-6 h-6 text-primary" /> Platform readiness metrics
        </h1>
        <p className="text-sm text-muted">
          Snapshot generated at {new Date(data.generatedAt).toLocaleString()} · {total} learners with computed readiness.
        </p>
      </header>

      <section className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <Stat label="Learners" value={String(total)} icon={Users} accent="text-navy" />
        <Stat label="High risk" value={String(data.highRisk)} icon={AlertTriangle} accent="text-danger" />
        <Stat label="Moderate" value={String(data.moderateRisk)} accent="text-warning" />
        <Stat label="Low risk" value={String(data.lowRisk)} accent="text-success" />
        <Stat label="Unknown" value={String(data.unknownRisk)} accent="text-muted" />
        <Stat label="Intervention candidates" value={String(data.interventionCandidates)} accent="text-danger" />
        <Stat label="Stale snapshots" value={String(data.staleSnapshots)} accent="text-warning" />
        <Stat label="Avg overall" value={data.avgOverall.toFixed(1)} accent="text-navy" />
      </section>

      <section className="rounded-2xl border border-border bg-surface p-4">
        <h2 className="text-sm font-bold text-navy mb-3">Average readiness by sub-test</h2>
        <div className="grid grid-cols-1 sm:grid-cols-5 gap-3">
          <SubAvg label="Writing" value={data.avgWriting} />
          <SubAvg label="Speaking" value={data.avgSpeaking} />
          <SubAvg label="Reading" value={data.avgReading} />
          <SubAvg label="Listening" value={data.avgListening} />
          <SubAvg label="Vocabulary" value={data.avgVocabulary} />
        </div>
      </section>

      {data.interventionCandidates > 0 && (
        <div className="rounded-2xl border border-danger/20 bg-danger/5 p-4 flex items-start gap-3">
          <AlertTriangle className="w-5 h-5 text-danger shrink-0 mt-0.5" />
          <div>
            <p className="text-sm font-bold text-navy">{data.interventionCandidates} intervention candidates</p>
            <p className="text-xs text-muted">Learners with target-date probability below 50% need tutor or content team attention.</p>
            <Link href="/admin/readiness?risk=High" className="text-xs font-bold text-primary hover:underline mt-2 inline-block">
              View high-risk learners →
            </Link>
          </div>
        </div>
      )}
    </div>
  );
}

function Stat({ label, value, icon: Icon, accent }: { label: string; value: string; icon?: React.ElementType; accent: string }) {
  return (
    <div className="rounded-2xl border border-border bg-surface p-4">
      <p className="text-[10px] uppercase tracking-widest font-bold text-muted inline-flex items-center gap-1">
        {Icon ? <Icon className="w-3 h-3" /> : null}{label}
      </p>
      <p className={`text-2xl font-bold mt-1 ${accent}`}>{value}</p>
    </div>
  );
}

function SubAvg({ label, value }: { label: string; value: number }) {
  return (
    <div className="rounded-xl border border-border p-3">
      <p className="text-[10px] uppercase tracking-widest font-bold text-muted">{label}</p>
      <p className="text-xl font-bold text-navy">{value.toFixed(1)}</p>
      <div className="h-1.5 w-full bg-background-light rounded-full overflow-hidden mt-1">
        <div className="h-full bg-primary" style={{ width: `${Math.max(0, Math.min(100, value))}%` }} />
      </div>
    </div>
  );
}
