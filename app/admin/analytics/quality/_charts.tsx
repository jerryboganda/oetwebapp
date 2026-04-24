'use client';

import { CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';

type RatePoint = { label: string; agreement?: number; appeals?: number };
type OperationsPoint = { label: string; reviewTime?: number; riskCases?: number };

export function RateTrendChart({ data }: { data: RatePoint[] }) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <LineChart data={data}>
        <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
        <XAxis dataKey="label" stroke="#64748b" fontSize={12} />
        <YAxis stroke="#64748b" fontSize={12} />
        <Tooltip />
        <Legend />
        <Line type="monotone" dataKey="agreement" name="Agreement" stroke="#2563eb" strokeWidth={2} dot={false} />
        <Line type="monotone" dataKey="appeals" name="Appeals" stroke="#dc2626" strokeWidth={2} dot={false} />
      </LineChart>
    </ResponsiveContainer>
  );
}

export function OperationsTrendChart({ data }: { data: OperationsPoint[] }) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <LineChart data={data}>
        <CartesianGrid strokeDasharray="3 3" stroke="#e2e8f0" />
        <XAxis dataKey="label" stroke="#64748b" fontSize={12} />
        <YAxis stroke="#64748b" fontSize={12} />
        <Tooltip />
        <Legend />
        <Line type="monotone" dataKey="reviewTime" name="Review Time" stroke="#f59e0b" strokeWidth={2} dot={false} />
        <Line type="monotone" dataKey="riskCases" name="Risk Cases" stroke="#7c3aed" strokeWidth={2} dot={false} />
      </LineChart>
    </ResponsiveContainer>
  );
}
