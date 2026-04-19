'use client';

import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
  ReferenceLine,
} from 'recharts';
import type { ProgressV2Payload, ProgressWeeklyPoint } from '@/lib/api';
import { ChartTabularFallback } from './ChartTabularFallback';

const SUBTEST_COLORS: Record<string, string> = {
  reading: '#2563eb',
  listening: '#4f46e5',
  writing: '#e11d48',
  speaking: '#9333ea',
};

const SUBTEST_LABELS: Record<string, string> = {
  reading: 'Reading',
  listening: 'Listening',
  writing: 'Writing',
  speaking: 'Speaking',
};

const ALL_SUBTESTS = ['reading', 'listening', 'writing', 'speaking'] as const;

function formatWeekKey(weekKey: string): string {
  // "2026-W16" -> "W16"
  const idx = weekKey.indexOf('W');
  return idx >= 0 ? weekKey.slice(idx) : weekKey;
}

export interface ProgressTrendChartProps {
  payload: ProgressV2Payload;
  visibleSubtests: Set<string>;
  onPointClick?: (subtest: string, weekKey: string) => void;
}

/**
 * Canonical 0-500 trend chart. Grade-B reference line at 350 is ALWAYS
 * visible. For Writing with a US/QA target country the line additionally
 * shows the 300 threshold. Click any line dot to drill into that subtest
 * in the submissions history.
 */
export function ProgressTrendChart({ payload, visibleSubtests, onPointClick }: ProgressTrendChartProps) {
  const { trend, meta } = payload;
  const rows = trend.map((point) => buildRow(point));
  const hasData = trend.length >= meta.minEvaluationsForTrend;

  if (!hasData) {
    return <EmptyTrend minEvaluations={meta.minEvaluationsForTrend} have={trend.length} />;
  }

  // The Writing threshold is country-aware; only render the extra line when
  // it differs from the Grade-B line (300 for US/QA) to avoid stacking.
  const showWritingCPlus = meta.writingThreshold !== null && meta.writingThreshold !== meta.gradeBThreshold;

  const handleDotClick = (subtest: string) => (dotPayload: unknown) => {
    const key = (dotPayload as { payload?: { weekKey?: string } } | undefined)?.payload?.weekKey ?? 'latest';
    onPointClick?.(subtest, key);
  };

  return (
    <div className="relative">
      <ChartTabularFallback
        caption="Sub-test score trend over time (0-500 scale)"
        headers={['Week', ...ALL_SUBTESTS.filter((s) => visibleSubtests.has(s)).map((s) => SUBTEST_LABELS[s])]}
        rows={trend.map((point) => [
          formatWeekKey(point.weekKey),
          ...ALL_SUBTESTS.filter((s) => visibleSubtests.has(s)).map((s) => point.subtestScaled[s] ?? null),
        ])}
      />
      <div className="h-[280px] w-full sm:h-[320px]" role="img" aria-label="Sub-test score trend chart on 0 to 500 scale">
        <ResponsiveContainer width="100%" height="100%" minWidth={0}>
          <LineChart data={rows} margin={{ top: 8, right: 24, bottom: 8, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
            <XAxis dataKey="weekLabel" axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} dy={10} />
            <YAxis
              domain={[meta.scoreAxisMin, meta.scoreAxisMax]}
              ticks={[0, 100, 200, 300, 350, 400, 500]}
              axisLine={false}
              tickLine={false}
              tick={{ fontSize: 12, fill: '#6b7280' }}
            />
            <Tooltip
              contentStyle={{
                borderRadius: '16px',
                border: 'none',
                boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)',
              }}
              formatter={(value: unknown) => (value == null ? '—' : `${value}/500`)}
            />
            <Legend iconType="circle" wrapperStyle={{ fontSize: '12px', paddingTop: '20px' }} />
            <ReferenceLine
              y={meta.gradeBThreshold}
              stroke="#10b981"
              strokeDasharray="5 5"
              label={{ value: `Grade B (${meta.gradeBThreshold})`, fill: '#047857', fontSize: 11, position: 'insideTopRight' }}
            />
            {showWritingCPlus && (
              <ReferenceLine
                y={meta.writingThreshold!}
                stroke="#f59e0b"
                strokeDasharray="2 4"
                label={{ value: `Writing ${meta.writingThreshold} (${meta.writingThresholdGrade})`, fill: '#b45309', fontSize: 11, position: 'insideBottomRight' }}
              />
            )}
            {ALL_SUBTESTS.filter((s) => visibleSubtests.has(s)).map((subtest) => (
              <Line
                key={subtest}
                type="monotone"
                dataKey={subtest}
                name={SUBTEST_LABELS[subtest]}
                stroke={SUBTEST_COLORS[subtest]}
                strokeWidth={3}
                dot={{ r: 4, strokeWidth: 2 }}
                activeDot={{ r: 6 }}
                connectNulls={false}
                style={{ cursor: onPointClick ? 'pointer' : 'default' }}
                onClick={onPointClick ? handleDotClick(subtest) : undefined}
              />
            ))}
          </LineChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}

function buildRow(point: ProgressWeeklyPoint) {
  const out: Record<string, string | number | null> = {
    weekLabel: formatWeekKey(point.weekKey),
    weekKey: point.weekKey,
  };
  for (const s of ALL_SUBTESTS) {
    out[s] = point.subtestScaled[s] ?? null;
  }
  return out;
}

function EmptyTrend({ minEvaluations, have }: { minEvaluations: number; have: number }) {
  return (
    <div className="flex h-full min-h-[240px] items-center justify-center rounded-3xl border border-dashed border-gray-200 bg-background-light/60 px-6 text-center">
      <div className="max-w-sm space-y-2">
        <p className="text-sm font-black uppercase tracking-widest text-muted">Not enough data yet</p>
        <p className="text-sm text-muted">
          Complete {Math.max(0, minEvaluations - have)} more scored {minEvaluations - have === 1 ? 'attempt' : 'attempts'} to unlock the trend chart.
        </p>
      </div>
    </div>
  );
}
