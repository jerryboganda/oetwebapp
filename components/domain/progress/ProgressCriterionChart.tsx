'use client';

import { useMemo } from 'react';
import {
  ComposedChart,
  Line,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
  ReferenceLine,
} from 'recharts';
import type { ProgressV2Payload, ProgressCriterionPoint } from '@/lib/api';
import { ChartTabularFallback } from './ChartTabularFallback';

/**
 * Criterion-level drilldown chart for Writing and Speaking. Uses the
 * `criterionTrend` payload emitted by the server, which rescales the 0-6
 * rubric into the canonical 0-500 OET axis so cross-subtest comparisons
 * stay meaningful.
 *
 * <para>
 * When <c>meta.showCriterionConfidenceBand</c> is true AND any point has
 * <c>sampleCount ≥ 3</c>, the chart overlays a shaded 95% CI band behind
 * each criterion line. This tells learners to ignore single-evaluation
 * noise and focus on statistically-meaningful movement.
 * </para>
 */
export function ProgressCriterionChart({
  payload,
  subtest,
}: {
  payload: ProgressV2Payload;
  subtest: 'writing' | 'speaking';
}) {
  const rows = useMemo(() => buildRows(payload.criterionTrend, subtest), [payload.criterionTrend, subtest]);
  const criterionCodes = useMemo(
    () => Array.from(new Set(payload.criterionTrend.filter((p) => p.subtestCode === subtest).map((p) => p.criterionCode))),
    [payload.criterionTrend, subtest],
  );

  if (rows.length === 0 || criterionCodes.length === 0) {
    return (
      <div className="flex h-[220px] items-center justify-center rounded-3xl border border-dashed border-gray-200 bg-background-light/60 px-6 text-center">
        <p className="text-sm text-muted">No {subtest} criterion data yet. Evaluated submissions will unlock this chart.</p>
      </div>
    );
  }

  const colors = ['#2563eb', '#e11d48', '#9333ea', '#f59e0b', '#10b981', '#0ea5e9', '#f97316'];
  const codeLabelMap = buildCodeLabelMap(payload.criterionTrend, subtest);
  const showBand = Boolean(payload.meta.showCriterionConfidenceBand) && rows.some((r) =>
    criterionCodes.some((code) => typeof r[`${code}__n`] === 'number' && (r[`${code}__n`] as number) >= 3),
  );

  return (
    <div className="relative">
      <ChartTabularFallback
        caption={`${subtest[0].toUpperCase() + subtest.slice(1)} criterion trend on 0-500 scale${showBand ? ' with 95% confidence interval' : ''}`}
        headers={[
          'Week',
          ...criterionCodes.flatMap((c) =>
            showBand
              ? [codeLabelMap.get(c) ?? c, `${codeLabelMap.get(c) ?? c} CI low`, `${codeLabelMap.get(c) ?? c} CI high`]
              : [codeLabelMap.get(c) ?? c],
          ),
        ]}
        rows={rows.map((r) => [
          String(r.weekLabel),
          ...criterionCodes.flatMap((c) =>
            showBand
              ? [(r[c] as number | null) ?? null, (r[`${c}__lo`] as number | null) ?? null, (r[`${c}__hi`] as number | null) ?? null]
              : [(r[c] as number | null) ?? null],
          ),
        ])}
      />
      <div className="h-[260px]" role="img" aria-label={`${subtest} criterion trend chart`}>
        <ResponsiveContainer width="100%" height="100%" minWidth={0}>
          <ComposedChart data={rows} margin={{ top: 8, right: 24, bottom: 8, left: 0 }}>
            <defs>
              {criterionCodes.map((code, i) => (
                <linearGradient key={`grad-${code}`} id={`ciFill-${code}`} x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor={colors[i % colors.length]} stopOpacity={0.18} />
                  <stop offset="100%" stopColor={colors[i % colors.length]} stopOpacity={0.04} />
                </linearGradient>
              ))}
            </defs>
            <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
            <XAxis dataKey="weekLabel" axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} dy={10} />
            <YAxis domain={[0, 500]} ticks={[0, 100, 200, 300, 350, 400, 500]} axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} />
            <Tooltip contentStyle={{ borderRadius: '16px', border: 'none', boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)' }} />
            <Legend iconType="circle" wrapperStyle={{ fontSize: '12px', paddingTop: '20px' }} />
            <ReferenceLine y={350} stroke="#10b981" strokeDasharray="5 5" label={{ value: 'Grade B (350)', fill: '#047857', fontSize: 10, position: 'insideTopRight' }} />
            {showBand &&
              criterionCodes.map((code) => (
                <Area
                  key={`band-${code}`}
                  type="monotone"
                  dataKey={`${code}__range`}
                  stroke="none"
                  fill={`url(#ciFill-${code})`}
                  legendType="none"
                  isAnimationActive={false}
                  connectNulls
                  name="" /* hidden from legend */
                />
              ))}
            {criterionCodes.map((code, i) => (
              <Line
                key={code}
                type="monotone"
                dataKey={code}
                name={codeLabelMap.get(code) ?? code}
                stroke={colors[i % colors.length]}
                strokeWidth={2.5}
                dot={{ r: 3, strokeWidth: 2 }}
                activeDot={{ r: 5 }}
                connectNulls
              />
            ))}
          </ComposedChart>
        </ResponsiveContainer>
      </div>
      {showBand && (
        <p className="mt-2 text-[11px] text-muted">Shaded area: 95% confidence interval. Narrow band = consistent score across attempts; wide band = high variance.</p>
      )}
    </div>
  );
}

function buildRows(points: ProgressCriterionPoint[], subtest: string) {
  const grouped = new Map<string, Record<string, string | number | null | [number, number]>>();
  for (const p of points) {
    if (p.subtestCode !== subtest) continue;
    const label = formatWeekLabel(p.weekKey);
    if (!grouped.has(p.weekKey)) {
      grouped.set(p.weekKey, { weekKey: p.weekKey, weekLabel: label });
    }
    const row = grouped.get(p.weekKey)!;
    row[p.criterionCode] = p.averageScaled;
    row[`${p.criterionCode}__lo`] = p.lowerCi95;
    row[`${p.criterionCode}__hi`] = p.upperCi95;
    row[`${p.criterionCode}__n`] = p.sampleCount;
    // Recharts area needs a [low, high] tuple to shade the band between them.
    row[`${p.criterionCode}__range`] = [p.lowerCi95, p.upperCi95];
  }
  return [...grouped.values()].sort((a, b) => String(a.weekKey).localeCompare(String(b.weekKey)));
}

function buildCodeLabelMap(points: ProgressCriterionPoint[], subtest: string) {
  const map = new Map<string, string>();
  for (const p of points) {
    if (p.subtestCode === subtest) map.set(p.criterionCode, p.criterionLabel);
  }
  return map;
}

function formatWeekLabel(weekKey: string): string {
  const idx = weekKey.indexOf('W');
  return idx >= 0 ? weekKey.slice(idx) : weekKey;
}
