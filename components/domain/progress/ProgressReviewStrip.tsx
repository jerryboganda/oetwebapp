'use client';

import { Activity, Clock } from 'lucide-react';
import type { ProgressV2Payload } from '@/lib/api';

/**
 * Header strip showing the expert review turnaround (only genuinely-real
 * stat surviving from the v1 page) plus headline counts.
 */
export function ProgressReviewStrip({ payload }: { payload: ProgressV2Payload }) {
  const { reviewUsage, totals } = payload;
  const turnaroundLabel = reviewUsage.averageTurnaroundHours === null
    ? 'No reviews yet'
    : `${reviewUsage.averageTurnaroundHours.toFixed(1)} h avg`;

  return (
    <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
      <Card icon={<Activity className="w-4 h-4" />} label="Completed evaluations" value={totals.completedEvaluations.toString()} />
      <Card icon={<Activity className="w-4 h-4" />} label="Mock attempts" value={totals.mockAttempts.toString()} />
      <Card
        icon={<Clock className="w-4 h-4" />}
        label="Expert review turnaround"
        value={turnaroundLabel}
        sub={reviewUsage.completedRequests > 0 ? `${reviewUsage.completedRequests}/${reviewUsage.totalRequests} reviews` : undefined}
      />
    </div>
  );
}

function Card({ icon, label, value, sub }: { icon: React.ReactNode; label: string; value: string; sub?: string }) {
  return (
    <div className="rounded-2xl border border-gray-200 bg-surface p-4 shadow-sm">
      <div className="flex items-center gap-2 text-muted">
        {icon}
        <p className="text-[11px] font-black uppercase tracking-widest">{label}</p>
      </div>
      <p className="mt-1 text-xl font-black text-navy">{value}</p>
      {sub && <p className="text-[11px] text-muted mt-0.5">{sub}</p>}
    </div>
  );
}
