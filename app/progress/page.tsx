'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
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
  Legend
} from 'recharts';
import {
  TrendingUp,
  Activity,
  CheckCircle2,
  Send,
  Clock
} from 'lucide-react';
import { AppShell } from '@/components/layout/app-shell';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchTrendData, fetchCompletionData, fetchSubmissionVolume } from '@/lib/api';
import type { TrendPoint } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';
import { InlineAlert } from '@/components/ui/alert';

type CompletionPoint = { day: string; completed: number };
type VolumePoint = { week: string; submissions: number };

export default function ProgressDashboard() {
  const [criterionFilter, setCriterionFilter] = useState('Writing');
  const [trendData, setTrendData] = useState<TrendPoint[]>([]);
  const [completionData, setCompletionData] = useState<CompletionPoint[]>([]);
  const [volumeData, setVolumeData] = useState<VolumePoint[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('progress_viewed');
    Promise.allSettled([
      fetchTrendData(),
      fetchCompletionData(),
      fetchSubmissionVolume(),
    ]).then((results) => {
      const [trendResult, completionResult, volumeResult] = results;
      if (trendResult.status === 'fulfilled') setTrendData(trendResult.value);
      if (completionResult.status === 'fulfilled') setCompletionData(completionResult.value as CompletionPoint[]);
      if (volumeResult.status === 'fulfilled') setVolumeData(volumeResult.value as VolumePoint[]);

      const anyFailed = results.some(r => r.status === 'rejected');
      const allFailed = results.every(r => r.status === 'rejected');

      if (allFailed) {
        setError('Failed to load progress data. Please try again.');
      } else if (anyFailed) {
        setError('Some progress data could not be loaded.');
      }
      setLoading(false);
    });
  }, []);

  return (
    <AppShell
      pageTitle="Progress Dashboard"
      subtitle="Track your performance and activity over time"
      backHref="/"
    >
      <div className="max-w-5xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-8">

        {loading && (
          <div className="space-y-6">
            {[1, 2, 3].map(i => (
              <Skeleton key={i} className="h-64 rounded-2xl" />
            ))}
          </div>
        )}

        {!loading && error && (
          <InlineAlert variant="error">{error}</InlineAlert>
        )}

        {!loading && !error && (
          <>
            {/* 1. Sub-test Trend */}
            <motion.section
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              className="bg-surface rounded-[32px] border border-gray-200 p-6 sm:p-8 shadow-sm"
            >
              <div className="flex items-center gap-3 mb-6">
                <div className="w-10 h-10 rounded-xl bg-blue-100 flex items-center justify-center shrink-0">
                  <TrendingUp className="w-5 h-5 text-blue-600" />
                </div>
                <div>
                  <h2 className="text-base font-black text-navy">Sub-test Performance Trend</h2>
                  <p className="text-xs text-muted">Score progression across all skills</p>
                </div>
              </div>
              <div className="h-[300px] w-full" role="img" aria-label="Sub-test performance trend chart showing reading, listening, writing, and speaking scores over time">
                <ResponsiveContainer width="100%" height="100%">
                  <LineChart data={trendData} margin={{ top: 5, right: 20, bottom: 5, left: 0 }}>
                    <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
                    <XAxis dataKey="date" axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} dy={10} />
                    <YAxis axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} />
                    <Tooltip contentStyle={{ borderRadius: '16px', border: 'none', boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)' }} />
                    <Legend iconType="circle" wrapperStyle={{ fontSize: '12px', paddingTop: '20px' }} />
                    <Line type="monotone" dataKey="reading"   name="Reading"   stroke="#2563eb" strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                    <Line type="monotone" dataKey="listening" name="Listening" stroke="#4f46e5" strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                    <Line type="monotone" dataKey="writing"   name="Writing"   stroke="#e11d48" strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                    <Line type="monotone" dataKey="speaking"  name="Speaking"  stroke="#9333ea" strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                  </LineChart>
                </ResponsiveContainer>
              </div>
            </motion.section>

            {/* 2. Criterion Trend */}
            <motion.section
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.1 }}
              className="bg-surface rounded-[32px] border border-gray-200 p-6 sm:p-8 shadow-sm"
            >
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 mb-6">
                <div className="flex items-center gap-3">
                  <div className="w-10 h-10 rounded-xl bg-purple-100 flex items-center justify-center shrink-0">
                    <Activity className="w-5 h-5 text-purple-600" />
                  </div>
                  <div>
                    <h2 className="text-base font-black text-navy">Criterion Trend</h2>
                    <p className="text-xs text-muted">Deep dive into specific scoring criteria</p>
                  </div>
                </div>
                <div className="flex items-center gap-2 bg-background-light p-1 rounded-xl border border-gray-200">
                  {['Writing', 'Speaking'].map(f => (
                    <button
                      key={f}
                      onClick={() => setCriterionFilter(f)}
                      className={`px-4 py-1.5 text-xs font-bold rounded-lg transition-colors ${criterionFilter === f ? 'bg-white text-navy shadow-sm' : 'text-muted hover:text-navy'}`}
                    >
                      {f}
                    </button>
                  ))}
                </div>
              </div>
              <div className="h-[300px] w-full" role="img" aria-label={`Criterion trend chart for ${criterionFilter} skills`}>
                <ResponsiveContainer width="100%" height="100%">
                  <LineChart data={trendData} margin={{ top: 5, right: 20, bottom: 5, left: 0 }}>
                    <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
                    <XAxis dataKey="date" axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} dy={10} />
                    <YAxis axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} />
                    <Tooltip contentStyle={{ borderRadius: '16px', border: 'none', boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)' }} />
                    <Legend iconType="circle" wrapperStyle={{ fontSize: '12px', paddingTop: '20px' }} />
                    {criterionFilter === 'Writing' ? (
                      <>
                        <Line type="monotone" dataKey="writing" name="Writing Score" stroke="#e11d48" strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                      </>
                    ) : (
                      <>
                        <Line type="monotone" dataKey="speaking" name="Speaking Score" stroke="#9333ea" strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
                      </>
                    )}
                  </LineChart>
                </ResponsiveContainer>
              </div>
            </motion.section>

            <div className="grid grid-cols-1 lg:grid-cols-2 gap-8">
              {/* 3. Completion Trend */}
              <motion.section
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.2 }}
                className="bg-surface rounded-[32px] border border-gray-200 p-6 sm:p-8 shadow-sm"
              >
                <div className="flex items-center gap-3 mb-6">
                  <div className="w-10 h-10 rounded-xl bg-green-100 flex items-center justify-center shrink-0">
                    <CheckCircle2 className="w-5 h-5 text-green-600" />
                  </div>
                  <div>
                    <h2 className="text-base font-black text-navy">Completion Trend</h2>
                    <p className="text-xs text-muted">Tasks completed over the last 7 days</p>
                  </div>
                </div>
                <div className="h-[250px] w-full">
                  <ResponsiveContainer width="100%" height="100%">
                    <AreaChart data={completionData} margin={{ top: 5, right: 0, bottom: 5, left: -20 }}>
                      <defs>
                        <linearGradient id="colorCompleted" x1="0" y1="0" x2="0" y2="1">
                          <stop offset="5%" stopColor="#10b981" stopOpacity={0.3} />
                          <stop offset="95%" stopColor="#10b981" stopOpacity={0} />
                        </linearGradient>
                      </defs>
                      <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
                      <XAxis dataKey="day" axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} dy={10} />
                      <YAxis axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} />
                      <Tooltip contentStyle={{ borderRadius: '16px', border: 'none', boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)' }} />
                      <Area type="monotone" dataKey="completed" name="Tasks Completed" stroke="#10b981" strokeWidth={3} fillOpacity={1} fill="url(#colorCompleted)" />
                    </AreaChart>
                  </ResponsiveContainer>
                </div>
              </motion.section>

              {/* 4. Submission Volume */}
              <motion.section
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.3 }}
                className="bg-surface rounded-[32px] border border-gray-200 p-6 sm:p-8 shadow-sm"
              >
                <div className="flex items-center gap-3 mb-6">
                  <div className="w-10 h-10 rounded-xl bg-amber-100 flex items-center justify-center shrink-0">
                    <Send className="w-5 h-5 text-amber-600" />
                  </div>
                  <div>
                    <h2 className="text-base font-black text-navy">Submission Volume</h2>
                    <p className="text-xs text-muted">Writing and Speaking tasks submitted</p>
                  </div>
                </div>
                <div className="h-[250px] w-full">
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={volumeData} margin={{ top: 5, right: 0, bottom: 5, left: -20 }}>
                      <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#f3f4f6" />
                      <XAxis dataKey="week" axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} dy={10} />
                      <YAxis axisLine={false} tickLine={false} tick={{ fontSize: 12, fill: '#6b7280' }} />
                      <Tooltip cursor={{ fill: '#f3f4f6' }} contentStyle={{ borderRadius: '16px', border: 'none', boxShadow: '0 10px 15px -3px rgba(0,0,0,0.1)' }} />
                      <Bar dataKey="submissions" name="Submissions" fill="#f59e0b" radius={[6, 6, 0, 0]} barSize={32} />
                    </BarChart>
                  </ResponsiveContainer>
                </div>
              </motion.section>
            </div>

            {/* 5. Review Usage */}
            <motion.section
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.4 }}
              className="bg-navy rounded-[32px] p-6 sm:p-8 text-white shadow-lg relative overflow-hidden"
            >
              <div className="absolute top-0 right-0 w-64 h-64 bg-white opacity-5 rounded-full blur-3xl -translate-y-1/2 translate-x-1/3" />
              <div className="relative z-10 flex items-center gap-3">
                <div className="w-10 h-10 rounded-xl bg-white/10 flex items-center justify-center shrink-0">
                  <Clock className="w-5 h-5 text-white" />
                </div>
                <div>
                  <h2 className="text-base font-black">Expert Review Turnaround</h2>
                  <p className="text-xs text-white/70">Average time from submission to feedback</p>
                </div>
              </div>
              <div className="mt-6 bg-white/10 rounded-2xl p-5 border border-white/10 inline-block">
                <h3 className="text-xs font-bold text-white/70 uppercase tracking-widest mb-1">Avg Turnaround</h3>
                <div className="flex items-baseline gap-2">
                  <span className="text-4xl font-black">24.5</span>
                  <span className="text-sm font-bold text-white/70">hours</span>
                </div>
              </div>
            </motion.section>
          </>
        )}

      </div>
    </AppShell>
  );
}

// --- Mock Data ---
const subTestTrendData = [
  { date: 'Week 1', reading: 65, listening: 60, writing: 50, speaking: 55 },
  { date: 'Week 2', reading: 70, listening: 65, writing: 55, speaking: 60 },
  { date: 'Week 3', reading: 75, listening: 70, writing: 55, speaking: 65 },
  { date: 'Week 4', reading: 80, listening: 75, writing: 60, speaking: 70 },
  { date: 'Week 5', reading: 85, listening: 80, writing: 65, speaking: 70 },
];

const criterionTrendData = [
  { date: 'Week 1', conciseness: 40, grammar: 60, vocabulary: 55 },
  { date: 'Week 2', conciseness: 45, grammar: 65, vocabulary: 60 },
  { date: 'Week 3', conciseness: 45, grammar: 70, vocabulary: 65 },
  { date: 'Week 4', conciseness: 55, grammar: 75, vocabulary: 70 },
  { date: 'Week 5', conciseness: 60, grammar: 75, vocabulary: 75 },
];

const completionTrendData = [
  { date: 'Mon', completed: 2 },
  { date: 'Tue', completed: 5 },
  { date: 'Wed', completed: 3 },
  { date: 'Thu', completed: 6 },
  { date: 'Fri', completed: 4 },
  { date: 'Sat', completed: 8 },
  { date: 'Sun', completed: 7 },
];

const submissionVolumeData = [
  { week: 'Week 1', submissions: 3 },
  { week: 'Week 2', submissions: 5 },
  { week: 'Week 3', submissions: 4 },
  { week: 'Week 4', submissions: 8 },
  { week: 'Week 5', submissions: 6 },
];

const reviewData = {
  hasReviews: true,
  usage: [
    { month: 'Aug', used: 1, available: 3 },
    { month: 'Sep', used: 2, available: 2 },
    { month: 'Oct', used: 3, available: 1 },
  ],
  avgTurnaroundHours: 24.5
};

