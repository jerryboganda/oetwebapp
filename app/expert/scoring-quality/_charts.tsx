'use client';

import { BarChart, Bar, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis, LineChart, Line } from 'recharts';

interface ScoringDistribution {
  criterion: string;
  mean: number;
  stdDev: number;
  min: number;
  max: number;
  count: number;
}

interface CalibrationPoint {
  date: string;
  averageScore: number;
}

export function ScoringDistributionChart({ data }: { data: ScoringDistribution[] }) {
  return (
    <ResponsiveContainer width="100%" height={280}>
      <BarChart data={data}>
        <CartesianGrid strokeDasharray="3 3" />
        <XAxis dataKey="criterion" fontSize={12} />
        <YAxis domain={[0, 6]} />
        <Tooltip />
        <Bar dataKey="mean" fill="#6366f1" name="Mean Score" radius={[4, 4, 0, 0]} />
      </BarChart>
    </ResponsiveContainer>
  );
}

export function CalibrationTrendChart({ data }: { data: CalibrationPoint[] }) {
  return (
    <ResponsiveContainer width="100%" height={220}>
      <LineChart data={data}>
        <CartesianGrid strokeDasharray="3 3" />
        <XAxis dataKey="date" tickFormatter={(v: string) => new Date(v).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} fontSize={11} />
        <YAxis domain={[0, 6]} />
        <Tooltip labelFormatter={(v) => new Date(String(v)).toLocaleDateString()} />
        <Line type="monotone" dataKey="averageScore" stroke="#6366f1" strokeWidth={2} dot={false} />
      </LineChart>
    </ResponsiveContainer>
  );
}
