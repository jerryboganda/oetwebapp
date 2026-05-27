'use client';

import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

import type { TutorEarningsLine } from '@/lib/api';

export interface EarningsChartProps {
  lines: TutorEarningsLine[];
}

interface MonthBucket {
  month: string; // YYYY-MM
  label: string; // 'Jan 26'
  gross: number;
  net: number;
}

function monthKey(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
}

function monthLabel(key: string): string {
  const [year, month] = key.split('-').map(Number);
  const d = new Date(year, (month ?? 1) - 1, 1);
  return new Intl.DateTimeFormat('en-AU', { month: 'short', year: '2-digit' }).format(d);
}

function bucketRecent(lines: TutorEarningsLine[], monthCount = 6): MonthBucket[] {
  const now = new Date();
  const buckets: MonthBucket[] = [];
  for (let i = monthCount - 1; i >= 0; i--) {
    const d = new Date(now.getFullYear(), now.getMonth() - i, 1);
    const key = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
    buckets.push({ month: key, label: monthLabel(key), gross: 0, net: 0 });
  }

  for (const line of lines) {
    const key = monthKey(line.scheduledStartAt);
    const bucket = buckets.find((b) => b.month === key);
    if (bucket) {
      bucket.gross += line.grossUsd;
      bucket.net += line.netUsd;
    }
  }
  return buckets;
}

export function EarningsChart({ lines }: EarningsChartProps) {
  const data = bucketRecent(lines, 6);
  const hasAny = data.some((d) => d.gross > 0 || d.net > 0);

  if (!hasAny) {
    return (
      <div className="flex h-64 items-center justify-center rounded-2xl border border-dashed border-border bg-surface text-sm text-muted">
        No earnings recorded for the last 6 months.
      </div>
    );
  }

  return (
    <div className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
      <div style={{ width: '100%', height: 280 }}>
        <ResponsiveContainer>
          <BarChart data={data} margin={{ top: 8, right: 12, bottom: 8, left: 8 }}>
            <CartesianGrid strokeDasharray="3 3" stroke="rgba(148, 163, 184, 0.25)" />
            <XAxis dataKey="label" tick={{ fontSize: 12 }} stroke="rgba(100, 116, 139, 0.7)" />
            <YAxis tick={{ fontSize: 12 }} stroke="rgba(100, 116, 139, 0.7)" tickFormatter={(v) => `$${v}`} />
            <Tooltip
              formatter={(value: number) => `$${value.toFixed(2)}`}
              contentStyle={{ borderRadius: 12, borderColor: 'rgba(148, 163, 184, 0.3)' }}
            />
            <Bar dataKey="gross" fill="#a78bfa" name="Gross (USD)" radius={[4, 4, 0, 0]} />
            <Bar dataKey="net" fill="#7c3aed" name="Net (USD)" radius={[4, 4, 0, 0]} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}
