'use client';

import React from 'react';
import { cn } from '@/lib/utils';
import { OET_SCALED_MAX, OET_SCALED_PASS_B } from '@/lib/scoring';

/**
 * Four-tile sparkline strip summarising recent scaled-score history per
 * sub-test. Tiles are buttons: clicking a tile filters the list to that
 * sub-test. The pass threshold line (350/500) is always rendered.
 */
export interface SparklineStripProps {
  data: Record<string, Array<{ at: string; scaled: number | null }>>;
  activeSubtest?: string;
  onTileClick: (subtest: string) => void;
}

const SUBTESTS: Array<{ key: string; label: string }> = [
  { key: 'writing', label: 'Writing' },
  { key: 'speaking', label: 'Speaking' },
  { key: 'reading', label: 'Reading' },
  { key: 'listening', label: 'Listening' },
];

export function SparklineStrip({ data, activeSubtest, onTileClick }: SparklineStripProps) {
  return (
    <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
      {SUBTESTS.map((st) => {
        const points = (data[st.key] ?? []).filter((p) => p.scaled !== null) as Array<{ at: string; scaled: number }>;
        const latest = points.length ? points[points.length - 1].scaled : null;
        const previous = points.length > 1 ? points[points.length - 2].scaled : null;
        const delta = latest !== null && previous !== null ? latest - previous : null;
        const isActive = activeSubtest === st.key;
        return (
          <button
            type="button"
            key={st.key}
            aria-pressed={isActive}
            onClick={() => onTileClick(st.key)}
            className={cn(
              'rounded-2xl border p-3 text-left transition-colors bg-surface',
              isActive ? 'border-primary ring-2 ring-primary/20' : 'border-gray-200 hover:border-primary/40',
            )}
          >
            <div className="flex items-center justify-between">
              <span className="text-xs font-black uppercase tracking-widest text-muted">{st.label}</span>
              {delta !== null ? (
                <span className={cn(
                  'text-xs font-bold',
                  delta > 0 ? 'text-emerald-600' : delta < 0 ? 'text-rose-600' : 'text-muted',
                )}>
                  {delta > 0 ? '+' : ''}{delta}
                </span>
              ) : null}
            </div>
            <div className="flex items-baseline gap-1 mt-1">
              <span className="text-xl font-black text-navy">{latest !== null ? latest : '—'}</span>
              <span className="text-xs text-muted">/ {OET_SCALED_MAX}</span>
            </div>
            <Sparkline points={points.map((p) => p.scaled)} />
          </button>
        );
      })}
    </div>
  );
}

function Sparkline({ points }: { points: number[] }) {
  if (points.length === 0) {
    return <div className="h-10 mt-2 rounded-lg bg-gray-50" aria-hidden />;
  }
  const width = 120;
  const height = 40;
  const min = 0;
  const max = OET_SCALED_MAX;
  const step = points.length > 1 ? width / (points.length - 1) : 0;
  const path = points
    .map((v, i) => {
      const x = i * step;
      const y = height - ((v - min) / (max - min)) * height;
      return `${i === 0 ? 'M' : 'L'} ${x.toFixed(1)} ${y.toFixed(1)}`;
    })
    .join(' ');
  const passY = height - ((OET_SCALED_PASS_B - min) / (max - min)) * height;
  return (
    <svg viewBox={`0 0 ${width} ${height}`} className="w-full h-10 mt-2" aria-label="Score trend">
      <line x1={0} x2={width} y1={passY} y2={passY} stroke="#10b981" strokeDasharray="3 2" strokeWidth={1} opacity={0.6} />
      <path d={path} fill="none" stroke="currentColor" strokeWidth={1.5} className="text-primary" />
      {points.map((v, i) => {
        const x = i * step;
        const y = height - ((v - min) / (max - min)) * height;
        return <circle key={i} cx={x} cy={y} r={1.5} className="fill-primary" />;
      })}
    </svg>
  );
}
