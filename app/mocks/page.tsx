'use client';

import { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import {
  PlayCircle,
  FileText,
  Headphones,
  Mic,
  PenTool,
  Award,
  Clock,
  ArrowRight,
  CheckCircle2,
  Star,
  BarChart3
} from 'lucide-react';
import Link from 'next/link';
import { AppShell } from '@/components/layout/app-shell';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchMockReports } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { MockReport } from '@/lib/mock-data';

const recommendedMock = {
  id: 'full-mock-4',
  title: 'Full Mock Test 4',
  type: 'Full Mock',
  duration: '~3 hours',
  description: 'Based on your recent performance in Reading and Listening, taking a full mock test will help build your exam stamina.',
};

const subTestMocks = [
  { id: 'read-mock', title: 'Reading Mocks', icon: FileText, count: 12, color: 'text-blue-600', bg: 'bg-blue-100' },
  { id: 'list-mock', title: 'Listening Mocks', icon: Headphones, count: 10, color: 'text-indigo-600', bg: 'bg-indigo-100' },
  { id: 'speak-mock', title: 'Speaking Mocks', icon: Mic, count: 8, color: 'text-purple-600', bg: 'bg-purple-100' },
  { id: 'write-mock', title: 'Writing Mocks', icon: PenTool, count: 15, color: 'text-rose-600', bg: 'bg-rose-100' },
];

const fullMocks = [
  { id: 'fm-1', title: 'Full Mock Test 1', status: 'completed', score: 'A/B/B/B', date: 'Oct 12, 2023' },
  { id: 'fm-2', title: 'Full Mock Test 2', status: 'completed', score: 'B/B/B/B', date: 'Nov 05, 2023' },
  { id: 'fm-3', title: 'Full Mock Test 3', status: 'available', duration: '3h 15m' },
  { id: 'fm-4', title: 'Full Mock Test 4', status: 'available', duration: '3h 15m', isRecommended: true },
  { id: 'fm-5', title: 'Full Mock Test 5', status: 'locked', reason: 'Complete Mock 4 first' },
];

const purchasedReviews = [
  { id: 'pr-1', title: 'Writing Mock 2 - Expert Review', status: 'ready', date: 'Nov 10, 2023', reviewer: 'Sarah J.' },
  { id: 'pr-2', title: 'Speaking Mock 1 - Expert Review', status: 'pending', date: 'Nov 12, 2023', estimatedDelivery: 'Tomorrow' },
];

export default function MockCenter() {
  const [reports, setReports] = useState<MockReport[]>([]);
  const [loadingReports, setLoadingReports] = useState(true);
  const [reportsError, setReportsError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('module_entry', { module: 'mocks' });
    fetchMockReports()
      .then(setReports)
      .catch(() => setReportsError('Failed to load mock reports.'))
      .finally(() => setLoadingReports(false));
  }, []);

  return (
    <AppShell pageTitle="Mock Center" subtitle="Your hub for full exams, sub-test practice, and expert reviews.">
      <div className="max-w-5xl mx-auto px-4 sm:px-6 lg:px-8 py-8 space-y-10 pb-24">

        {/* 1. Recommended Next Mock */}
        <section>
          <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Recommended Next Step</h2>
          <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            className="bg-navy rounded-[32px] p-6 sm:p-8 text-white relative overflow-hidden shadow-lg"
          >
            <div className="absolute top-0 right-0 w-64 h-64 bg-white opacity-5 rounded-full blur-3xl -translate-y-1/2 translate-x-1/3 pointer-events-none" />
            <div className="relative z-10 flex flex-col sm:flex-row sm:items-center justify-between gap-6">
              <div className="flex-1">
                <div className="flex items-center gap-2 mb-3">
                  <Star className="w-5 h-5 text-amber-400 fill-amber-400" />
                  <span className="text-xs font-black text-amber-400 uppercase tracking-widest">Recommended</span>
                </div>
                <h3 className="text-2xl font-black mb-2">{recommendedMock.title}</h3>
                <p className="text-white/70 text-sm max-w-xl leading-relaxed mb-4">{recommendedMock.description}</p>
                <div className="flex items-center gap-4 text-sm font-medium text-white/60">
                  <span className="flex items-center gap-1.5"><Clock className="w-4 h-4" /> {recommendedMock.duration}</span>
                  <span className="flex items-center gap-1.5"><Award className="w-4 h-4" /> {recommendedMock.type}</span>
                </div>
              </div>
              <div className="shrink-0">
                <Link href={`/mocks/setup?id=${recommendedMock.id}`}>
                  <Button className="w-full sm:w-auto gap-2 bg-white text-navy hover:bg-gray-100">
                    <PlayCircle className="w-5 h-5" /> Start Mock
                  </Button>
                </Link>
              </div>
            </div>
          </motion.div>
        </section>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
          <div className="lg:col-span-2 space-y-10">

            {/* 2. Sub-test Mocks */}
            <section>
              <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Sub-test Mocks</h2>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                {subTestMocks.map((mock, idx) => (
                  <motion.div
                    key={mock.id}
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: idx * 0.05 }}
                  >
                    <Link href={`/mocks/setup?subtest=${mock.id}`} className="flex items-center gap-4 bg-surface rounded-[24px] border border-gray-200 p-5 hover:shadow-md hover:border-gray-300 transition-all group">
                      <div className={`w-12 h-12 rounded-2xl ${mock.bg} flex items-center justify-center shrink-0 group-hover:scale-105 transition-transform`}>
                        <mock.icon className={`w-6 h-6 ${mock.color}`} />
                      </div>
                      <div className="flex-1">
                        <h3 className="text-base font-bold text-navy">{mock.title}</h3>
                        <p className="text-sm text-muted">{mock.count} available</p>
                      </div>
                      <ArrowRight className="w-5 h-5 text-gray-300 group-hover:text-navy transition-colors" />
                    </Link>
                  </motion.div>
                ))}
              </div>
            </section>

            {/* 3. Full Mocks */}
            <section>
              <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Full Mocks</h2>
              <div className="bg-surface rounded-[24px] border border-gray-200 overflow-hidden shadow-sm">
                <div className="divide-y divide-gray-100">
                  {fullMocks.map((mock, idx) => (
                    <motion.div
                      key={mock.id}
                      initial={{ opacity: 0, x: -10 }}
                      animate={{ opacity: 1, x: 0 }}
                      transition={{ delay: idx * 0.05 }}
                      className={`p-5 flex items-center justify-between gap-4 transition-colors ${
                        mock.status === 'locked' ? 'bg-gray-50 opacity-75' : 'hover:bg-gray-50 cursor-pointer'
                      }`}
                    >
                      <div className="flex items-center gap-4">
                        <div className="shrink-0">
                          {mock.status === 'completed' ? (
                            <CheckCircle2 className="w-6 h-6 text-green-500" />
                          ) : mock.status === 'locked' ? (
                            <div className="w-6 h-6 rounded-full border-2 border-gray-300 flex items-center justify-center">
                              <div className="w-2 h-2 bg-gray-300 rounded-full" />
                            </div>
                          ) : (
                            <div className="w-6 h-6 rounded-full border-2 border-primary flex items-center justify-center">
                              <PlayCircle className="w-4 h-4 text-primary ml-0.5" />
                            </div>
                          )}
                        </div>
                        <div>
                          <h3 className="text-base font-bold text-navy flex items-center gap-2">
                            {mock.title}
                            {mock.isRecommended && (
                              <span className="bg-amber-100 text-amber-700 text-[10px] uppercase tracking-widest px-2 py-0.5 rounded-md font-black">
                                Recommended
                              </span>
                            )}
                          </h3>
                          <div className="text-sm text-muted mt-0.5">
                            {mock.status === 'completed' ? (
                              <span>Completed {mock.date} • Score: <span className="font-bold text-navy">{mock.score}</span></span>
                            ) : mock.status === 'locked' ? (
                              <span className="flex items-center gap-1"><Clock className="w-3.5 h-3.5" /> {mock.reason}</span>
                            ) : (
                              <span className="flex items-center gap-1"><Clock className="w-3.5 h-3.5" /> {mock.duration}</span>
                            )}
                          </div>
                        </div>
                      </div>
                      {mock.status !== 'locked' && (
                        <ArrowRight className="w-5 h-5 text-gray-300 hidden sm:block" />
                      )}
                    </motion.div>
                  ))}
                </div>
              </div>
            </section>
          </div>

          {/* Right Column: Reviews & Reports */}
          <div className="space-y-10">

            {/* 4. Purchased Mock Reviews */}
            <section>
              <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Expert Reviews</h2>
              <div className="space-y-3">
                {purchasedReviews.map((review, idx) => (
                  <motion.div
                    key={review.id}
                    initial={{ opacity: 0, y: 10 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: idx * 0.1 }}
                    className="bg-surface rounded-2xl border border-gray-200 p-4 shadow-sm hover:shadow-md transition-shadow cursor-pointer"
                  >
                    <div className="flex items-start justify-between mb-2">
                      <h3 className="text-sm font-bold text-navy pr-4 leading-tight">{review.title}</h3>
                      <span className={`text-[10px] font-black uppercase tracking-widest px-2 py-1 rounded-md shrink-0 ${
                        review.status === 'ready' ? 'bg-green-100 text-green-700' : 'bg-amber-100 text-amber-700'
                      }`}>
                        {review.status}
                      </span>
                    </div>
                    {'reviewer' in review ? (
                      <p className="text-xs text-muted">Reviewed by {review.reviewer} on {review.date}</p>
                    ) : (
                      <p className="text-xs text-muted">Expected: {review.estimatedDelivery}</p>
                    )}
                  </motion.div>
                ))}
                <Button variant="outline" className="w-full border-dashed text-muted hover:text-primary hover:border-primary/30">
                  Purchase New Review
                </Button>
              </div>
            </section>

            {/* 5. Previous Mock Reports */}
            <section>
              <div className="flex items-center justify-between mb-4">
                <h2 className="text-sm font-black text-muted uppercase tracking-widest">Previous Reports</h2>
              </div>
              {loadingReports ? (
                <div className="space-y-3">
                  <Skeleton className="h-16 rounded-2xl" />
                  <Skeleton className="h-16 rounded-2xl" />
                </div>
              ) : reportsError ? (
                <div className="text-center py-8 text-sm text-red-600">{reportsError}</div>
              ) : reports.length === 0 ? (
                <div className="text-center py-8 text-muted text-sm">No reports yet. Complete a mock to see your results here.</div>
              ) : (
                <div className="bg-surface rounded-[24px] border border-gray-200 overflow-hidden shadow-sm">
                  <div className="divide-y divide-gray-100">
                    {reports.slice(0, 4).map((report, idx) => (
                      <motion.div
                        key={report.id}
                        initial={{ opacity: 0, x: 10 }}
                        animate={{ opacity: 1, x: 0 }}
                        transition={{ delay: idx * 0.1 }}
                      >
                        <Link
                          href={`/mocks/report/${report.id}`}
                          className="p-4 hover:bg-gray-50 transition-colors flex items-center justify-between group"
                        >
                          <div className="flex items-center gap-3">
                            <div className="w-8 h-8 rounded-full bg-gray-100 flex items-center justify-center shrink-0">
                              <BarChart3 className="w-4 h-4 text-muted" />
                            </div>
                            <div>
                              <h3 className="text-sm font-bold text-navy group-hover:text-primary transition-colors">{report.title}</h3>
                              <p className="text-xs text-muted">{report.date}</p>
                            </div>
                          </div>
                          <span className="text-sm font-black text-navy">{report.overallScore}</span>
                        </Link>
                      </motion.div>
                    ))}
                  </div>
                </div>
              )}
            </section>

          </div>
        </div>
      </div>
    </AppShell>
  );
}
