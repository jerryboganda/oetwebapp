'use client';

import { AreaChart, Area, BarChart, Bar, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { CheckCircle2, Send } from 'lucide-react';
import type { ProgressV2Payload } from '@/lib/api';
import { ChartTabularFallback } from './ChartTabularFallback';

const DAY_SHORT_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];

/**
 * Real activity panel. Replaces the previously-hardcoded 7-day completion
 * stub and 5-week submission-volume stub. Both come straight from the
 * server-aggregated payload.
 */
export function ProgressActivityPanel({ payload }: { payload: ProgressV2Payload }) {
  const completionRows = payload.completion.map((c) => {
    const d = new Date(c.date);
    return {
      day: DAY_SHORT_NAMES[d.getUTCDay()],
      iso: c.date,
      completed: c.completed,
    };
  });

  const volumeRows = payload.submissionVolume.map((v, i) => ({
    week: `W${i + 1}`,
    weekKey: v.weekKey,
    writing: v.writing,
    speaking: v.speaking,
    total: v.writing + v.speaking,
  }));

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
      <div className="rounded-3xl border border-gray-200 bg-surface p-5 sm:p-6 shadow-sm">
        <div className="flex items-center gap-3 mb-4">
          <div className="w-10 h-10 rounded-xl bg-emerald-100 flex items-center justify-center">
            <CheckCircle2 className="w-5 h-5 text-emerald-700" />
          </div>
          <div>
            <h3 className="text-base font-black text-navy">Completion (last 7 days)</h3>
            <p className="text-xs text-muted">Real attempts, no placeholder data.</p>
          </div>
        </div>
        <div className="relative">
          <ChartTabularFallback
            caption="Daily completion count over the last 7 days"
            headers={['Day', 'Completed']}
            rows={completionRows.map((r) => [r.day, r.completed])}
          />
          <div className="h-[220px]" role="img" aria-label="Daily completion area chart">
            <ResponsiveContainer width="100%" height="100%" minWidth={0}>
              <AreaChart data={completionRows} margin={{ top: 5, right: 0, bottom: 5, left: -20 }}>
                <defs>
                  <linearGradient id="completionGradient" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#10b981" stopOpacity={0.35} />
                    <stop offset="95%" stopColor="#10b981" stopOpacity={0} />
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
                <XAxis dataKey="day" axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} dy={10} />
                <YAxis allowDecimals={false} axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} />
                <Tooltip contentStyle={{ borderRadius: '16px', border: 'none', boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)' }} />
                <Area type="monotone" dataKey="completed" stroke="#10b981" strokeWidth={3} fillOpacity={1} fill="url(#completionGradient)" />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>

      <div className="rounded-3xl border border-gray-200 bg-surface p-5 sm:p-6 shadow-sm">
        <div className="flex items-center gap-3 mb-4">
          <div className="w-10 h-10 rounded-xl bg-amber-100 flex items-center justify-center">
            <Send className="w-5 h-5 text-amber-700" />
          </div>
          <div>
            <h3 className="text-base font-black text-navy">Submission volume (last 5 weeks)</h3>
            <p className="text-xs text-muted">Writing and Speaking submissions per ISO week.</p>
          </div>
        </div>
        <div className="relative">
          <ChartTabularFallback
            caption="Weekly submission volume split by Writing and Speaking"
            headers={['Week', 'Writing', 'Speaking']}
            rows={volumeRows.map((r) => [r.week, r.writing, r.speaking])}
          />
          <div className="h-[220px]" role="img" aria-label="Weekly submission volume bar chart">
            <ResponsiveContainer width="100%" height="100%" minWidth={0}>
              <BarChart data={volumeRows} margin={{ top: 5, right: 0, bottom: 5, left: -20 }}>
                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
                <XAxis dataKey="week" axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} dy={10} />
                <YAxis allowDecimals={false} axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} />
                <Tooltip cursor={{ fill: '#f3f4f6' }} contentStyle={{ borderRadius: '16px', border: 'none', boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)' }} />
                <Bar dataKey="writing" name="Writing" stackId="vol" fill="#e11d48" radius={[6, 6, 0, 0]} barSize={32} />
                <Bar dataKey="speaking" name="Speaking" stackId="vol" fill="#9333ea" radius={[6, 6, 0, 0]} barSize={32} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>
    </div>
  );
}
