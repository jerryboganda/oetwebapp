'use client';

import {
  LineChart,
  Line,
  AreaChart,
  Area,
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
} from 'recharts';
import type { TrendPoint } from '@/lib/mock-data';

type CompletionPoint = { day: string; completed: number };
type VolumePoint = { week: string; submissions: number };

const CHART_COLORS = {
  primary: '#7c3aed',
  info: '#2563eb',
  success: '#10b981',
  warning: '#d97706',
  danger: '#ef4444',
  navy: '#0f172a',
  muted: '#526072',
  border: '#d8e0e8',
} as const;

const CHART_TICK = { fontSize: 12, fill: CHART_COLORS.muted } as const;
const CHART_TOOLTIP_STYLE = { borderRadius: '16px', border: 'none', boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)' } as const;

export function TrendChart({ data }: { data: TrendPoint[] }) {
  return (
    <ResponsiveContainer width="100%" height="100%" minWidth={0}>
      <LineChart data={data} margin={{ top: 5, right: 20, bottom: 5, left: 0 }}>
        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={CHART_COLORS.border} />
        <XAxis dataKey="date" axisLine={false} tickLine={false} tick={CHART_TICK} dy={10} />
        <YAxis axisLine={false} tickLine={false} tick={CHART_TICK} />
        <Tooltip contentStyle={CHART_TOOLTIP_STYLE} />
        <Legend iconType="circle" wrapperStyle={{ fontSize: '12px', paddingTop: '20px' }} />
        <Line type="monotone" dataKey="reading" name="Reading" stroke={CHART_COLORS.info} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
        <Line type="monotone" dataKey="listening" name="Listening" stroke={CHART_COLORS.primary} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
        <Line type="monotone" dataKey="writing" name="Writing" stroke={CHART_COLORS.danger} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
        <Line type="monotone" dataKey="speaking" name="Speaking" stroke={CHART_COLORS.navy} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
      </LineChart>
    </ResponsiveContainer>
  );
}

export function CriterionChart({ data, criterionFilter }: { data: TrendPoint[]; criterionFilter: string }) {
  return (
    <ResponsiveContainer width="100%" height="100%" minWidth={0}>
      <LineChart data={data} margin={{ top: 5, right: 20, bottom: 5, left: 0 }}>
        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={CHART_COLORS.border} />
        <XAxis dataKey="date" axisLine={false} tickLine={false} tick={CHART_TICK} dy={10} />
        <YAxis axisLine={false} tickLine={false} tick={CHART_TICK} />
        <Tooltip contentStyle={CHART_TOOLTIP_STYLE} />
        <Legend iconType="circle" wrapperStyle={{ fontSize: '12px', paddingTop: '20px' }} />
        {criterionFilter === 'Writing' ? (
          <Line type="monotone" dataKey="writing" name="Writing Score" stroke={CHART_COLORS.danger} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
        ) : (
          <Line type="monotone" dataKey="speaking" name="Speaking Score" stroke={CHART_COLORS.primary} strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
        )}
      </LineChart>
    </ResponsiveContainer>
  );
}

export function CompletionChart({ data }: { data: CompletionPoint[] }) {
  return (
    <ResponsiveContainer width="100%" height="100%" minWidth={0}>
      <AreaChart data={data} margin={{ top: 5, right: 0, bottom: 5, left: -20 }}>
        <defs>
          <linearGradient id="colorCompleted" x1="0" y1="0" x2="0" y2="1">
            <stop offset="5%" stopColor={CHART_COLORS.success} stopOpacity={0.3} />
            <stop offset="95%" stopColor={CHART_COLORS.success} stopOpacity={0} />
          </linearGradient>
        </defs>
        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={CHART_COLORS.border} />
        <XAxis dataKey="day" axisLine={false} tickLine={false} tick={CHART_TICK} dy={10} />
        <YAxis axisLine={false} tickLine={false} tick={CHART_TICK} />
        <Tooltip contentStyle={CHART_TOOLTIP_STYLE} />
        <Area type="monotone" dataKey="completed" name="Tasks Completed" stroke={CHART_COLORS.success} strokeWidth={3} fillOpacity={1} fill="url(#colorCompleted)" />
      </AreaChart>
    </ResponsiveContainer>
  );
}

export function VolumeChart({ data }: { data: VolumePoint[] }) {
  return (
    <ResponsiveContainer width="100%" height="100%" minWidth={0}>
      <BarChart data={data} margin={{ top: 5, right: 0, bottom: 5, left: -20 }}>
        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={CHART_COLORS.border} />
        <XAxis dataKey="week" axisLine={false} tickLine={false} tick={CHART_TICK} dy={10} />
        <YAxis axisLine={false} tickLine={false} tick={CHART_TICK} />
        <Tooltip cursor={{ fill: CHART_COLORS.border }} contentStyle={CHART_TOOLTIP_STYLE} />
        <Bar dataKey="submissions" name="Submissions" fill={CHART_COLORS.warning} radius={[6, 6, 0, 0]} barSize={32} />
      </BarChart>
    </ResponsiveContainer>
  );
}
