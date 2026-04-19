'use client';

import { Target, Calendar, Flag, ShieldCheck } from 'lucide-react';
import type { ProgressV2Payload } from '@/lib/api';

/**
 * Readiness + goal integration strip. Presents the distance to each target
 * score, the days-to-exam countdown, and (when enabled by admin policy) a
 * score-guarantee reminder.
 */
export function ProgressReadinessStrip({ payload }: { payload: ProgressV2Payload }) {
  const { goals, subtests, meta } = payload;

  const hasAnyTarget = [
    goals.targetWritingScore, goals.targetSpeakingScore, goals.targetReadingScore, goals.targetListeningScore,
  ].some((v) => v !== null);

  if (!hasAnyTarget && goals.daysToExam === null) {
    return null;
  }

  return (
    <div className="rounded-[28px] border border-gray-200 bg-surface p-5 sm:p-6 shadow-sm">
      <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-4 mb-4">
        <div>
          <p className="text-[11px] font-black uppercase tracking-widest text-muted">Readiness</p>
          <h3 className="text-lg font-black text-navy mt-1">How close are you to your target?</h3>
          <p className="text-xs text-muted mt-0.5">Distance in points between your latest scaled score and the target you set in your goals.</p>
        </div>
        {typeof goals.daysToExam === 'number' && (
          <div className="inline-flex items-center gap-2 rounded-xl bg-primary/10 px-3 py-2 text-primary">
            <Calendar className="w-4 h-4" />
            <span className="text-sm font-bold">
              {goals.daysToExam === 0 ? 'Exam today' : `${goals.daysToExam} day${goals.daysToExam === 1 ? '' : 's'} to exam`}
            </span>
          </div>
        )}
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        {subtests.map((s) => {
          const target = s.targetScaled;
          const latest = s.latestScaled;
          const gap = s.gapToTarget;
          if (target === null) {
            return (
              <div key={s.subtestCode} className="rounded-xl border border-dashed border-gray-200 p-3 text-xs text-muted">
                <p className="font-bold uppercase tracking-widest text-[10px]">{s.subtestCode}</p>
                <p className="mt-1">No target set</p>
              </div>
            );
          }
          const progress = latest === null ? 0 : Math.min(100, Math.max(0, Math.round(((latest ?? 0) / target) * 100)));
          const reached = typeof gap === 'number' && gap <= 0;
          return (
            <div key={s.subtestCode} className="rounded-xl border border-gray-200 p-3">
              <div className="flex items-center gap-1.5 text-[11px] font-black uppercase tracking-widest text-navy">
                <Target className="w-3 h-3" /> {s.subtestCode}
              </div>
              <p className="mt-1.5 text-sm">
                <span className="font-black text-navy">{latest ?? '—'}</span>
                <span className="text-muted"> / {target}</span>
              </p>
              <div className="mt-2 h-1.5 w-full rounded-full bg-gray-100 overflow-hidden">
                <div
                  className={`h-full rounded-full ${reached ? 'bg-emerald-500' : 'bg-primary'}`}
                  style={{ width: `${progress}%` }}
                />
              </div>
              <p className="mt-1 text-[11px]">
                {reached ? (
                  <span className="inline-flex items-center gap-1 text-emerald-700 font-bold"><Flag className="w-3 h-3" />Reached</span>
                ) : gap === null ? (
                  <span className="text-muted">—</span>
                ) : (
                  <span className="text-muted">{gap} pts to go</span>
                )}
              </p>
            </div>
          );
        })}
      </div>

      {meta.showScoreGuaranteeStrip && (
        <div className="mt-4 flex items-center gap-2 rounded-xl bg-emerald-50 border border-emerald-100 p-3 text-xs text-emerald-800">
          <ShieldCheck className="w-4 h-4" />
          <span>
            <strong>Score Guarantee eligible</strong> — consistent practice toward the target keeps your guarantee active. See Billing for details.
          </span>
        </div>
      )}
    </div>
  );
}
