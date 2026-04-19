'use client';

import { useMemo } from 'react';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend, ReferenceLine } from 'recharts';
import type { ProgressV2Payload, ProgressCriterionPoint } from '@/lib/api';
import { ChartTabularFallback } from './ChartTabularFallback';

/**
 * Criterion-level drilldown chart for Writing and Speaking. Uses the
 * `criterionTrend` payload emitted by the server, which rescales the 0-6
 * rubric into the canonical 0-500 OET axis so cross-subtest comparisons
 * stay meaningful.
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

  return (
    <div className="relative">
      <ChartTabularFallback
        caption={`${subtest[0].toUpperCase() + subtest.slice(1)} criterion trend on 0-500 scale`}
        headers={['Week', ...criterionCodes.map((c) => codeLabelMap.get(c) ?? c)]}
        rows={rows.map((r) => [String(r.weekLabel), ...criterionCodes.map((c) => (r[c] as number | null) ?? null)])}
      />
      <div className="h-[260px]" role="img" aria-label={`${subtest} criterion trend chart`}>
        <ResponsiveContainer width="100%" height="100%" minWidth={0}>
          <LineChart data={rows} margin={{ top: 8, right: 24, bottom: 8, left: 0 }}>
            <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
            <XAxis dataKey="weekLabel" axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} dy={10} />
            <YAxis domain={[0, 500]} ticks={[0, 100, 200, 300, 350, 400, 500]} axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} />
            <Tooltip contentStyle={{ borderRadius: '16px', border: 'none', boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)' }} />
            <Legend iconType="circle" wrapperStyle={{ fontSize: '12px', paddingTop: '20px' }} />
            <ReferenceLine y={350} stroke="#10b981" strokeDasharray="5 5" label={{ value: 'Grade B (350)', fill: '#047857', fontSize: 10, position: 'insideTopRight' }} />
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
          </LineChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}

function buildRows(points: ProgressCriterionPoint[], subtest: string) {
  const grouped = new Map<string, Record<string, string | number | null>>();
  for (const p of points) {
    if (p.subtestCode !== subtest) continue;
    const label = formatWeekLabel(p.weekKey);
    if (!grouped.has(p.weekKey)) {
      grouped.set(p.weekKey, { weekKey: p.weekKey, weekLabel: label });
    }
    grouped.get(p.weekKey)![p.criterionCode] = p.averageScaled;
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
