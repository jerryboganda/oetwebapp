'use client';

import { useMemo } from 'react';
import { Activity } from 'lucide-react';
import {
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import type { ReviewRetentionResponse } from '@/lib/types/review';

interface RetentionTrendCardProps {
  data: ReviewRetentionResponse | null;
  loading: boolean;
}

export function RetentionTrendCard({ data, loading }: RetentionTrendCardProps) {
  const series = useMemo(() => {
    if (!data) return [];
    return data.series.map((p) => ({
      date: p.date.slice(5),
      reviewed: p.reviewed,
      accuracy: p.accuracy,
    }));
  }, [data]);

  const totalReviewed = useMemo(() => series.reduce((sum, p) => sum + p.reviewed, 0), [series]);
  const avgAccuracy = useMemo(() => {
    if (series.length === 0) return 0;
    const days = series.filter((p) => p.reviewed > 0);
    if (days.length === 0) return 0;
    return Math.round(days.reduce((s, p) => s + p.accuracy, 0) / days.length);
  }, [series]);

  return (
    <Card className="rounded-3xl border border-border bg-surface p-1 shadow-sm">
      <CardHeader className="flex flex-row items-start justify-between pb-2">
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-primary">
            Retention trend
          </p>
          <CardTitle className="mt-1 text-lg text-navy">Last {data?.days ?? 30} days</CardTitle>
          <p className="mt-1 text-xs text-muted">
            Daily reviews and recall accuracy — the higher the line, the stronger retention.
          </p>
        </div>
        <div className="text-right">
          <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted">Avg accuracy</p>
          <p className="text-2xl font-bold text-navy">{avgAccuracy}%</p>
        </div>
      </CardHeader>
      <CardContent className="pt-2">
        {loading ? (
          <Skeleton className="h-40 w-full rounded-2xl" />
        ) : totalReviewed === 0 ? (
          <div className="flex h-40 flex-col items-center justify-center gap-2 rounded-2xl border border-dashed border-border bg-background-light/70 text-center">
            <Activity className="h-6 w-6 text-muted" />
            <p className="text-sm font-semibold text-navy">No rated items yet</p>
            <p className="text-xs text-muted">
              Rate a few items to see your retention curve.
            </p>
          </div>
        ) : (
          <div className="h-40 w-full">
            <ResponsiveContainer>
              <LineChart data={series} margin={{ top: 10, right: 10, bottom: 4, left: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--border, #d8e0e8)" strokeOpacity={0.5} />
                <XAxis
                  dataKey="date"
                  tick={{ fontSize: 11, fill: 'var(--muted, #526072)' }}
                  tickLine={false}
                  axisLine={false}
                  interval="preserveStartEnd"
                />
                <YAxis
                  yAxisId="accuracy"
                  domain={[0, 100]}
                  hide
                />
                <Tooltip
                  contentStyle={{
                    borderRadius: 12,
                    border: '1px solid var(--border, #d8e0e8)',
                    fontSize: 12,
                  }}
                  labelClassName="text-navy"
                  formatter={(value, name) => {
                    if (name === 'accuracy') return [`${value}%`, 'Accuracy'];
                    return [String(value), 'Reviewed'];
                  }}
                />
                <Line
                  yAxisId="accuracy"
                  type="monotone"
                  dataKey="accuracy"
                  stroke="#7c3aed"
                  strokeWidth={2}
                  dot={false}
                  name="accuracy"
                />
              </LineChart>
            </ResponsiveContainer>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
