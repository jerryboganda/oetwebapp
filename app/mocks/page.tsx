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
  BarChart3,
  Layers,
} from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import type { MockReport } from '@/lib/mock-data';
import { fetchMocksHome } from '@/lib/api';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';

const fallbackSubTestMocks = [
  { id: 'read-mock', title: 'Reading Mocks', icon: FileText, count: 12, color: 'text-blue-600', bg: 'bg-blue-100', href: '/mocks/setup?subtest=reading' },
  { id: 'list-mock', title: 'Listening Mocks', icon: Headphones, count: 10, color: 'text-indigo-600', bg: 'bg-indigo-100', href: '/mocks/setup?subtest=listening' },
  { id: 'speak-mock', title: 'Speaking Mocks', icon: Mic, count: 8, color: 'text-purple-600', bg: 'bg-purple-100', href: '/mocks/setup?subtest=speaking' },
  { id: 'write-mock', title: 'Writing Mocks', icon: PenTool, count: 15, color: 'text-rose-600', bg: 'bg-rose-100', href: '/mocks/setup?subtest=writing' },
];

const fallbackFullMocks = [
  { id: 'fm-1', title: 'Full Mock Test 1', status: 'completed', score: 'A/B/B/B', date: 'Oct 12, 2023' },
  { id: 'fm-2', title: 'Full Mock Test 2', status: 'completed', score: 'B/B/B/B', date: 'Nov 05, 2023' },
  { id: 'fm-3', title: 'Full Mock Test 3', status: 'available', duration: '3h 15m' },
  { id: 'fm-4', title: 'Full Mock Test 4', status: 'available', duration: '3h 15m', isRecommended: true },
  { id: 'fm-5', title: 'Full Mock Test 5', status: 'locked', reason: 'Complete Mock 4 first' },
];

export default function MockCenter() {
  const [home, setHome] = useState<Record<string, any> | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('module_entry', { module: 'mocks' });
    fetchMocksHome()
      .then(setHome)
      .catch(() => setError('Failed to load mock center.'))
      .finally(() => setLoading(false));
  }, []);

  const reports = (home?.reports ?? []) as MockReport[];
  const recommended = home?.recommendedNextMock;
  const availableCredits = home?.purchasedMockReviews?.availableCredits ?? 0;
  const fullMocks = home?.collections?.fullMocks?.length ? home.collections.fullMocks : fallbackFullMocks;
  const subTestMocks = home?.collections?.subTestMocks?.length
    ? home.collections.subTestMocks.map((mock: Record<string, any>) => ({
        id: mock.id,
        title: mock.title,
        icon: mock.subtest === 'writing' ? PenTool : mock.subtest === 'speaking' ? Mic : mock.subtest === 'listening' ? Headphones : FileText,
        count: 'Ready',
        color: mock.subtest === 'writing' ? 'text-rose-600' : mock.subtest === 'speaking' ? 'text-purple-600' : mock.subtest === 'listening' ? 'text-indigo-600' : 'text-blue-600',
        bg: mock.subtest === 'writing' ? 'bg-rose-100' : mock.subtest === 'speaking' ? 'bg-purple-100' : mock.subtest === 'listening' ? 'bg-indigo-100' : 'bg-blue-100',
        href: mock.route ?? '/mocks/setup',
      }))
    : fallbackSubTestMocks;

  const recommendedCard: LearnerSurfaceCardModel = {
    kind: 'navigation',
    sourceType: recommended ? 'backend_summary' : 'frontend_navigation',
    accent: 'navy',
    eyebrow: 'Recommended Next Step',
    eyebrowIcon: Star,
    title: recommended?.title ?? 'Full OET Mock Test',
    description: recommended?.rationale ?? 'Start a full mock when you need evidence that your recent practice work is holding up under full-exam pressure.',
    metaItems: [
      { icon: Clock, label: '~3 hours' },
      { icon: Award, label: 'Full mock flow' },
      { icon: FileText, label: 'Report included' },
    ],
    primaryAction: {
      label: 'Start Mock',
      href: recommended?.route ?? '/mocks/setup',
    },
  };

  return (
    <LearnerDashboardShell pageTitle="Mock Center" subtitle="Your hub for full exams, sub-test practice, and expert reviews.">
      <div className="space-y-10 pb-24">
        <LearnerPageHero
          eyebrow="Module Focus"
          icon={Layers}
          accent="navy"
          title="Choose the mock that proves whether practice is transferring"
          description="Use this center to pick the right simulation depth, keep progression visible, and follow mock evidence into the next decision."
          highlights={[
            { icon: Award, label: 'Review credits', value: `${availableCredits} available` },
            { icon: Layers, label: 'Mock routes', value: `${fullMocks.length} full mocks` },
            { icon: BarChart3, label: 'Recent reports', value: `${reports.length} available` },
          ]}
        />

        {loading ? (
          <div className="space-y-4">
            <Skeleton className="h-72 rounded-2xl" />
            <Skeleton className="h-52 rounded-2xl" />
          </div>
        ) : error ? (
          <div className="text-center py-8 text-sm text-red-600">{error}</div>
        ) : (
          <>
            <motion.div initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }}>
              <LearnerSurfaceCard card={recommendedCard} />
            </motion.div>

            <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
              <div className="lg:col-span-2 space-y-10">
                <section>
                  <LearnerSurfaceSectionHeader
                    eyebrow="Sub-test Mocks"
                    title="Choose the simulation scope you need"
                    description="Each entry explains whether you are entering a sub-test path or a full mock route."
                    className="mb-4"
                  />
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                    {subTestMocks.map((mock: Record<string, any>, idx: number) => (
                      <motion.div
                        key={mock.id}
                        initial={{ opacity: 0, y: 20 }}
                        animate={{ opacity: 1, y: 0 }}
                        transition={{ delay: idx * 0.05 }}
                      >
                        <Link href={mock.href} className="flex items-center gap-4 bg-surface rounded-[24px] border border-gray-200 p-5 hover:shadow-md hover:border-gray-300 transition-all group">
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

                <section>
                  <LearnerSurfaceSectionHeader
                    eyebrow="Full Mocks"
                    title="Keep full-exam progression visible"
                    description="Learners should see what is completed, what is available next, and what is still locked."
                    className="mb-4"
                  />
                  <div className="bg-surface rounded-[24px] border border-gray-200 overflow-hidden shadow-sm">
                    <div className="divide-y divide-gray-100">
                      {fullMocks.map((mock: Record<string, any>, idx: number) => (
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
                                {mock.isRecommended ? (
                                  <span className="bg-amber-100 text-amber-700 text-[10px] uppercase tracking-widest px-2 py-0.5 rounded-md font-black">
                                    Recommended
                                  </span>
                                ) : null}
                              </h3>
                              <div className="text-sm text-muted mt-0.5">
                                {mock.status === 'completed' ? (
                                  <span>Completed {mock.date} · Score: <span className="font-bold text-navy">{mock.score}</span></span>
                                ) : mock.status === 'locked' ? (
                                  <span className="flex items-center gap-1"><Clock className="w-3.5 h-3.5" /> {mock.reason}</span>
                                ) : (
                                  <span className="flex items-center gap-1"><Clock className="w-3.5 h-3.5" /> {mock.duration}</span>
                                )}
                              </div>
                            </div>
                          </div>
                          {mock.status !== 'locked' ? (
                            <ArrowRight className="w-5 h-5 text-gray-300 hidden sm:block" />
                          ) : null}
                        </motion.div>
                      ))}
                    </div>
                  </div>
                </section>
              </div>

              <div className="space-y-10">
                <section>
                  <LearnerSurfaceSectionHeader
                    eyebrow="Expert Reviews"
                    title="Keep review readiness explicit"
                    description="Mock review status should be visible as part of the same learner system, not as a separate hidden flow."
                    className="mb-4"
                  />
                  <div className="space-y-3">
                    <LearnerSurfaceCard
                      card={{
                        kind: 'status',
                        sourceType: 'backend_summary',
                        accent: 'amber',
                        eyebrow: 'Review Capacity',
                        eyebrowIcon: Star,
                        title: 'Writing and speaking reviews are available',
                        description: 'Use review credits to add expert feedback after high-value mock attempts.',
                        metaItems: [
                          { label: `${availableCredits} credits available` },
                          { label: 'Writing + Speaking' },
                        ],
                        primaryAction: {
                          label: 'Purchase New Review',
                          href: home?.purchasedMockReviews?.route ?? '/writing/expert-request',
                          variant: 'outline',
                        },
                      }}
                    />
                  </div>
                </section>

                <section>
                  <LearnerSurfaceSectionHeader
                    eyebrow="Previous Reports"
                    title="Keep the latest evidence visible"
                    description="Reports should feel like an integral part of the mock flow, not an afterthought."
                    className="mb-4"
                  />
                  {reports.length === 0 ? (
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
          </>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
