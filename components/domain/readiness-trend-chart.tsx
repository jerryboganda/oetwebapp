'use client';

import { AreaChart, Area, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis, ReferenceLine } from 'recharts';
import type { ReadinessHistoryPoint } from '@/lib/mock-data';

interface ReadinessTrendChartProps {
  data: ReadinessHistoryPoint[];
  series?: 'overall' | 'writing' | 'speaking' | 'reading' | 'listening' | 'vocabulary';
  target?: number;
}

const SERIES_COLOR: Record<NonNullable<ReadinessTrendChartProps['series']>, string> = {
  overall: '#4f46e5',
  writing: '#e11d48',
  speaking: '#7c3aed',
  reading: '#2563eb',
  listening: '#4f46e5',
  vocabulary: '#0d9488',
};

export function ReadinessTrendChart({ data, series = 'overall', target = 70 }: ReadinessTrendChartProps) {
  if (!data || data.length === 0) {
    return (
      <div className="h-56 flex items-center justify-center text-sm text-muted">
        No history yet — keep practicing and your trend will appear here.
      </div>
    );
  }

  const color = SERIES_COLOR[series];
  const gradientId = `readiness-trend-${series}`;
  const formatted = data.map((row) => ({
    week: row.weekStartDate.slice(5),
    value: Number(row[series] ?? 0),
  }));

  return (
    <div className="h-56 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={formatted} margin={{ top: 8, right: 16, left: -8, bottom: 0 }}>
          <defs>
            <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor={color} stopOpacity={0.4} />
              <stop offset="95%" stopColor={color} stopOpacity={0} />
            </linearGradient>
          </defs>
          <CartesianGrid strokeDasharray="3 3" stroke="var(--color-border)" />
          <XAxis dataKey="week" stroke="var(--color-muted)" fontSize={11} />
          <YAxis domain={[0, 100]} stroke="var(--color-muted)" fontSize={11} />
          <Tooltip
            contentStyle={{ background: 'var(--color-surface)', border: '1px solid var(--color-border)', borderRadius: 8, fontSize: 12 }}
            labelStyle={{ color: 'var(--color-navy)' }}
          />
          {target > 0 && (
            <ReferenceLine y={target} stroke="var(--color-success)" strokeDasharray="5 5" label={{ value: `Target ${target}`, position: 'right', fill: 'var(--color-success)', fontSize: 11 }} />
          )}
          <Area type="monotone" dataKey="value" stroke={color} fill={`url(#${gradientId})`} strokeWidth={2} />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  );
}
