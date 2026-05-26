'use client';

/**
 * AccentBarChart — horizontal accent-accuracy chart (§6.4, §27).
 *
 * Consumed by:
 *   - app/listening/results — diagnostic results page accent breakdown.
 *   - app/listening/dashboard — accent progress block on the dashboard.
 *
 * Pure-Tailwind implementation (no chart library) — each row is a label, a
 * gradient progress bar, and a numeric percentage. Rows under 60% accuracy
 * surface a "Needs work" warning chip.
 */

import { AlertTriangle } from 'lucide-react';
import type { AccentProgress } from '@/lib/listening-pathway-api';

export interface AccentBarChartProps {
  accents: AccentProgress[];
  /** Threshold below which a "Needs work" warning chip is shown. Default 60. */
  needsWorkThreshold?: number;
  className?: string;
}

interface AccentDisplay {
  flag: string;
  label: string;
}

function accentDisplay(code: string, fallbackLabel: string): AccentDisplay {
  switch (code) {
    case 'british':
    case 'en-GB':
      return { flag: '🇬🇧', label: 'British' };
    case 'australian':
    case 'en-AU':
      return { flag: '🇦🇺', label: 'Australian' };
    case 'us':
    case 'en-US':
      return { flag: '🇺🇸', label: 'North American' };
    case 'non_native':
    case 'en-XX':
      return { flag: '🌍', label: 'Non-native' };
    default:
      return { flag: '🎧', label: fallbackLabel || code };
  }
}

function clampPercentage(value: number): number {
  if (!Number.isFinite(value)) return 0;
  if (value < 0) return 0;
  if (value > 100) return 100;
  return Math.round(value);
}

export function AccentBarChart({
  accents,
  needsWorkThreshold = 60,
  className,
}: AccentBarChartProps) {
  if (!accents.length) {
    return (
      <div
        className={[
          'flex h-32 items-center justify-center rounded-xl border border-dashed border-border',
          'text-sm text-muted',
          className ?? '',
        ]
          .filter(Boolean)
          .join(' ')}
      >
        No accent data available yet.
      </div>
    );
  }

  return (
    <ul
      className={[
        'flex flex-col gap-3 rounded-xl border border-border bg-surface p-4',
        className ?? '',
      ]
        .filter(Boolean)
        .join(' ')}
      aria-label="Accent accuracy"
    >
      {accents.map((row) => {
        const pct = clampPercentage(row.accuracyPercentage);
        const needsWork = pct < needsWorkThreshold;
        const { flag, label } = accentDisplay(row.accent, row.label);

        return (
          <li key={row.accent} className="grid grid-cols-[minmax(0,1fr)_3rem] items-center gap-3">
            <div className="min-w-0">
              <div className="mb-1 flex items-center justify-between gap-2">
                <span className="inline-flex items-center gap-1.5 text-sm font-medium text-navy dark:text-slate-100">
                  <span aria-hidden="true">{flag}</span>
                  <span className="truncate">{label}</span>
                </span>
                {needsWork ? (
                  <span
                    className={[
                      'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-semibold',
                      'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300',
                    ].join(' ')}
                  >
                    <AlertTriangle aria-hidden="true" className="h-3 w-3" />
                    Needs work
                  </span>
                ) : null}
              </div>
              <div
                className="h-2.5 w-full overflow-hidden rounded-full bg-slate-200 dark:bg-slate-800"
                role="progressbar"
                aria-valuenow={pct}
                aria-valuemin={0}
                aria-valuemax={100}
                aria-label={`${label} accuracy`}
              >
                <div
                  className={[
                    'h-full rounded-full transition-[width] duration-500 ease-out',
                    needsWork
                      ? 'bg-gradient-to-r from-amber-400 to-orange-500'
                      : 'bg-gradient-to-r from-emerald-400 to-cyan-500',
                  ].join(' ')}
                  style={{ width: `${pct}%` }}
                />
              </div>
            </div>
            <span className="text-right text-sm font-semibold tabular-nums text-navy dark:text-slate-100">
              {pct}%
            </span>
          </li>
        );
      })}
    </ul>
  );
}

export default AccentBarChart;
