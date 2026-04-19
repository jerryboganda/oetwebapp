'use client';

import React from 'react';
import { Clock, CheckCircle2, Coins, Timer } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import type { ReviewLineage } from '@/lib/mock-data';

function formatDate(value: string | null | undefined) {
  if (!value) return '—';
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return value;
  return new Intl.DateTimeFormat(undefined, { year: 'numeric', month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }).format(d);
}

/**
 * Review lineage surface. Shown on the detail page whenever a review
 * request exists for the attempt. Displays state, turnaround, credits,
 * requested-at, and completed-at. Data comes from the server only —
 * nothing is derived on the client beyond labels.
 */
export function ReviewLineageCard({ lineage }: { lineage: ReviewLineage }) {
  const isCompleted = lineage.state === 'reviewed';
  return (
    <section className="rounded-[24px] border border-gray-200 bg-surface p-6 shadow-sm">
      <header className="mb-4">
        <p className="text-xs font-black uppercase tracking-widest text-muted">Expert review</p>
        <h3 className="mt-1 text-lg font-bold text-navy">Your review request stayed attached to this attempt</h3>
      </header>
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4 text-sm">
        <Stat
          icon={isCompleted ? CheckCircle2 : Clock}
          label="Status"
          valueNode={<Badge variant={isCompleted ? 'success' : 'warning'} size="sm">{lineage.stateLabel || lineage.state}</Badge>}
        />
        <Stat icon={Timer} label="Turnaround" value={lineage.turnaroundOption ?? '—'} />
        <Stat icon={Coins} label="Credits" value={String(lineage.creditsCharged)} />
        <Stat icon={Clock} label="Requested" value={formatDate(lineage.requestedAt)} />
      </div>
      {lineage.completedAt ? (
        <p className="mt-3 text-xs text-muted">Completed {formatDate(lineage.completedAt)}</p>
      ) : (
        <p className="mt-3 text-xs text-muted">Awaiting expert assignment or completion.</p>
      )}
    </section>
  );
}

function Stat({ icon: Icon, label, value, valueNode }: {
  icon: React.ElementType;
  label: string;
  value?: string;
  valueNode?: React.ReactNode;
}) {
  return (
    <div className="rounded-2xl border border-gray-100 bg-background-light p-3">
      <div className="flex items-center gap-2 text-muted">
        <Icon className="w-4 h-4" />
        <span className="text-[10px] font-black uppercase tracking-widest">{label}</span>
      </div>
      <div className="mt-1 font-bold text-navy">
        {valueNode ?? value}
      </div>
    </div>
  );
}
