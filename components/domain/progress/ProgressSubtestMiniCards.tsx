'use client';

import { TrendingUp, TrendingDown, Minus, BookOpen, Headphones, FilePenLine, Mic } from 'lucide-react';
import type { ProgressSubtestSummary } from '@/lib/api';

const SUBTEST_ICONS = {
  reading: BookOpen,
  listening: Headphones,
  writing: FilePenLine,
  speaking: Mic,
} as const;

const SUBTEST_LABELS = {
  reading: 'Reading',
  listening: 'Listening',
  writing: 'Writing',
  speaking: 'Speaking',
} as const;

const SUBTEST_ACCENT = {
  reading: 'bg-blue-50 text-blue-700 border-blue-100',
  listening: 'bg-purple-50 text-purple-700 border-purple-100',
  writing: 'bg-rose-50 text-rose-700 border-rose-100',
  speaking: 'bg-violet-50 text-violet-700 border-violet-100',
} as const;

export interface ProgressSubtestMiniCardsProps {
  subtests: ProgressSubtestSummary[];
  visible: Set<string>;
  onToggle: (subtest: string) => void;
}

/**
 * One card per subtest. Shows latest scaled, grade, delta vs 30d ago, gap to
 * target, threshold reason (e.g. country_required) and a visible eye toggle
 * that hides/shows that subtest in the trend chart.
 */
export function ProgressSubtestMiniCards({ subtests, visible, onToggle }: ProgressSubtestMiniCardsProps) {
  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
      {subtests.map((s) => {
        const Icon = SUBTEST_ICONS[s.subtestCode];
        const accent = SUBTEST_ACCENT[s.subtestCode];
        const isVisible = visible.has(s.subtestCode);

        let delta: React.ReactNode = <span className="inline-flex items-center gap-1 text-muted"><Minus className="w-3 h-3" />—</span>;
        if (typeof s.deltaLast30Days === 'number') {
          if (s.deltaLast30Days > 0) {
            delta = <span className="inline-flex items-center gap-1 text-emerald-700"><TrendingUp className="w-3 h-3" />+{s.deltaLast30Days}</span>;
          } else if (s.deltaLast30Days < 0) {
            delta = <span className="inline-flex items-center gap-1 text-rose-700"><TrendingDown className="w-3 h-3" />{s.deltaLast30Days}</span>;
          } else {
            delta = <span className="inline-flex items-center gap-1 text-muted"><Minus className="w-3 h-3" />0</span>;
          }
        }

        const needsCountry = s.thresholdReason === 'country_required' || s.thresholdReason === 'country_unsupported';

        return (
          <button
            type="button"
            key={s.subtestCode}
            onClick={() => onToggle(s.subtestCode)}
            aria-pressed={isVisible}
            aria-label={`Toggle ${SUBTEST_LABELS[s.subtestCode]} series`}
            className={`text-left rounded-2xl border p-4 transition-all ${accent} ${
              isVisible ? 'opacity-100 ring-1 ring-inset ring-black/5' : 'opacity-50 hover:opacity-75'
            }`}
          >
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Icon className="w-4 h-4" aria-hidden />
                <span className="text-xs font-black uppercase tracking-widest">{SUBTEST_LABELS[s.subtestCode]}</span>
              </div>
              {s.evaluationCount > 0 && <span className="text-[10px] text-muted">{s.evaluationCount} eval{s.evaluationCount === 1 ? '' : 's'}</span>}
            </div>

            <div className="mt-3 flex items-baseline gap-2">
              <span className="text-3xl font-black text-navy">{s.latestScaled ?? '—'}</span>
              {s.latestScaled !== null && <span className="text-xs font-bold text-muted">/500</span>}
              {s.latestGrade && (
                <span className="ml-auto text-xs font-bold text-navy">Grade {s.latestGrade}</span>
              )}
            </div>

            <div className="mt-2 grid grid-cols-2 gap-2 text-xs">
              <div>
                <p className="text-[10px] uppercase tracking-widest text-muted">30d Δ</p>
                <p className="font-bold mt-0.5">{delta}</p>
              </div>
              <div>
                <p className="text-[10px] uppercase tracking-widest text-muted">Target gap</p>
                <p className="font-bold text-navy mt-0.5">
                  {typeof s.gapToTarget === 'number' ? (s.gapToTarget <= 0 ? 'Reached' : `${s.gapToTarget} pts`) : '—'}
                </p>
              </div>
            </div>

            {needsCountry && s.subtestCode === 'writing' && (
              <p className="mt-2 text-[11px] text-amber-700">Set your target country in Settings to see Writing pass mark.</p>
            )}
          </button>
        );
      })}
    </div>
  );
}
