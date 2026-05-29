'use client';

import { useMemo } from 'react';
import {
  CartesianGrid,
  Line,
  LineChart,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  Legend,
} from 'recharts';
import { cn } from '@/lib/utils';

export interface BandHistoryDataPoint {
  date: string;
  rawTotal: number;
  estimatedBand: number;
  letterType?: string;
  isRevision?: boolean;
}

export interface BandHistoryChartProps {
  data: BandHistoryDataPoint[];
  targetBand?: number;
  className?: string;
}

/**
 * Linear regression — returns the trend line as predicted y values for
 * each x position so we can plot a `Line` against the same data array.
 * Stable + branchless; pure function.
 */
function linearTrend(points: { x: number; y: number }[]): number[] {
  const n = points.length;
  if (n < 2) return points.map((p) => p.y);
  let sumX = 0;
  let sumY = 0;
  let sumXY = 0;
  let sumXX = 0;
  for (const p of points) {
    sumX += p.x;
    sumY += p.y;
    sumXY += p.x * p.y;
    sumXX += p.x * p.x;
  }
  const denom = n * sumXX - sumX * sumX;
  if (denom === 0) {
    const mean = sumY / n;
    return points.map(() => mean);
  }
  const slope = (n * sumXY - sumX * sumY) / denom;
  const intercept = (sumY - slope * sumX) / n;
  return points.map((p) => slope * p.x + intercept);
}

function formatShortDate(iso: string): string {
  try {
    const d = new Date(iso);
    return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  } catch {
    return iso;
  }
}

/**
 * Line chart of raw writing score (0-38) over time, with optional
 * target band overlay and a least-squares linear trendline.
 *
 * Y axis is locked to 0-38 (max raw total for the 6 OET criteria;
 * C1 max 3 + 5 × C2-C6 max 7 = 38).
 */
export function BandHistoryChart({ data, targetBand, className }: BandHistoryChartProps) {
  const enriched = useMemo(() => {
    const points = data
      .filter((d) => d && typeof d.rawTotal === 'number')
      .slice()
      .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());
    const trend = linearTrend(points.map((p, i) => ({ x: i, y: p.rawTotal })));
    return points.map((p, i) => ({
      ...p,
      shortDate: formatShortDate(p.date),
      trend: trend[i],
    }));
  }, [data]);

  if (enriched.length === 0) {
    return (
      <div
        className={cn(
          'w-full h-72 rounded-2xl border border-dashed border-border bg-surface flex items-center justify-center text-sm text-muted',
          className,
        )}
        role="img"
        aria-label="Band history chart: no data yet"
      >
        No graded letters yet. Your band history will appear here.
      </div>
    );
  }

  return (
    <div
      className={cn('w-full h-72', className)}
      role="img"
      aria-label={`Band history chart with ${enriched.length} graded letter${enriched.length === 1 ? '' : 's'}`}
    >
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={enriched} margin={{ top: 16, right: 24, bottom: 8, left: 0 }}>
          <CartesianGrid strokeDasharray="3 3" stroke="rgba(100,116,139,0.2)" />
          <XAxis
            dataKey="shortDate"
            tick={{ fontSize: 11, fontWeight: 600 }}
            tickLine={false}
            axisLine={{ stroke: 'rgba(100,116,139,0.4)' }}
          />
          <YAxis
            domain={[0, 38]}
            tick={{ fontSize: 11, fontWeight: 600 }}
            tickLine={false}
            axisLine={{ stroke: 'rgba(100,116,139,0.4)' }}
            label={{
              value: 'Raw / 38',
              angle: -90,
              position: 'insideLeft',
              style: { fontSize: 10, fontWeight: 700, fill: '#64748b' },
            }}
          />
          <Tooltip
            contentStyle={{
              fontSize: 12,
              fontWeight: 600,
              borderRadius: 8,
              border: '1px solid rgba(100,116,139,0.3)',
            }}
            formatter={(value, name) => {
              const num = typeof value === 'number' ? value : Number(value ?? 0);
              const key = String(name ?? '');
              if (key === 'Raw') return [`${num} / 38`, 'Raw'];
              if (key === 'Trend') return [num.toFixed(1), 'Trend'];
              return [String(value ?? ''), key];
            }}
            labelFormatter={(label, payload) => {
              const point = payload?.[0]?.payload as BandHistoryDataPoint | undefined;
              if (!point) return String(label ?? '');
              const parts = [point.date];
              if (point.letterType) parts.push(point.letterType);
              if (point.isRevision) parts.push('revision');
              return parts.join(' · ');
            }}
          />
          {typeof targetBand === 'number' ? (
            <ReferenceLine
              y={targetBand}
              stroke="rgba(16,185,129,0.7)"
              strokeDasharray="4 4"
              label={{ value: `Target ${targetBand}`, position: 'right', fontSize: 10, fontWeight: 700 }}
            />
          ) : null}
          <Line
            type="monotone"
            dataKey="rawTotal"
            name="Raw"
            stroke="rgba(99,102,241,0.95)"
            strokeWidth={2.5}
            dot={{ r: 3, strokeWidth: 1.5 }}
            activeDot={{ r: 5 }}
          />
          <Line
            type="linear"
            dataKey="trend"
            name="Trend"
            stroke="rgba(244,114,182,0.85)"
            strokeWidth={1.5}
            strokeDasharray="6 4"
            dot={false}
          />
          <Legend wrapperStyle={{ fontSize: 11, fontWeight: 700, paddingTop: 8 }} iconSize={10} />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
