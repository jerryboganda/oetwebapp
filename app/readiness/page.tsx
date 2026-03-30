'use client';

import React, { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import {
  Calendar,
  Clock,
  AlertTriangle,
  ShieldAlert,
  ShieldCheck,
  Shield,
  FileText,
  Headphones,
  PenTool,
  Mic,
  TrendingUp,
  Info,
  Search
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchReadiness } from '@/lib/api';
import type { ReadinessData } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';

const SUBTEST_ICONS: Record<string, React.ElementType> = {
  reading:   FileText,
  listening: Headphones,
  writing:   PenTool,
  speaking:  Mic,
};

export default function ReadinessCenter() {
  const [data, setData] = useState<ReadinessData | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    analytics.track('readiness_viewed');
    fetchReadiness()
      .then(setData)
      .catch(() => setError('Could not load readiness data.'));
  }, []);

  if (error) {
    return (
      <LearnerDashboardShell pageTitle="Readiness Center" backHref="/">
        <div>
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!data) {
    return (
      <LearnerDashboardShell pageTitle="Readiness Center" backHref="/">
        <div className="space-y-6">
          {[1, 2, 3].map(i => (
            <Skeleton key={i} className="h-40 rounded-2xl" />
          ))}
        </div>
      </LearnerDashboardShell>
    );
  }

  const riskBg =
    data.overallRisk === 'High'     ? 'bg-rose-600' :
    data.overallRisk === 'Moderate' ? 'bg-amber-500' :
                                      'bg-green-600';
  const riskIcon =
    data.overallRisk === 'High' ? ShieldAlert :
    data.overallRisk === 'Moderate' ? Shield :
    ShieldCheck;

  return (
    <LearnerDashboardShell
      pageTitle="Readiness Center"
      subtitle={`Target Exam: ${data.targetDate}`}
      backHref="/"
    >
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Readiness Focus"
          icon={TrendingUp}
          accent="primary"
          title="See what needs to close before your target date"
          description="Use readiness evidence to identify current risk, weakest links, and the study volume you should protect next."
          highlights={[
            { icon: Calendar, label: 'Target date', value: data.targetDate },
            { icon: riskIcon, label: 'Current risk', value: data.overallRisk },
            { icon: Clock, label: 'Study volume', value: `${data.recommendedStudyHours} hours left` },
          ]}
        />

        {/* 1. Risk + Recommended Study Cards */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          <motion.section
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            className={`rounded-[32px] p-8 text-white relative overflow-hidden shadow-lg ${riskBg}`}
          >
            <div className="absolute top-0 right-0 w-64 h-64 bg-white opacity-10 rounded-full blur-3xl -translate-y-1/2 translate-x-1/3" />
            <div className="relative z-10">
              <div className="flex items-center gap-2 mb-4">
                {data.overallRisk === 'High'     ? <ShieldAlert className="w-6 h-6" /> :
                 data.overallRisk === 'Moderate' ? <Shield className="w-6 h-6" /> :
                                                   <ShieldCheck className="w-6 h-6" />}
                <span className="text-sm font-black uppercase tracking-widest opacity-90">Target-Date Risk</span>
              </div>
              <h2 className="text-4xl font-black mb-2">{data.overallRisk} Risk</h2>
              <p className="text-white/90 text-sm leading-relaxed max-w-sm">
                With {data.weeksRemaining} weeks remaining until your exam, you are at a {data.overallRisk.toLowerCase()} risk of not meeting your target scores.
              </p>
            </div>
          </motion.section>

          <motion.section
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: 0.1 }}
            className="bg-navy rounded-[32px] p-8 text-white relative overflow-hidden shadow-lg flex flex-col justify-center"
          >
            <div className="absolute bottom-0 left-0 w-64 h-64 bg-primary opacity-20 rounded-full blur-3xl translate-y-1/2 -translate-x-1/3" />
            <div className="relative z-10">
              <div className="flex items-center gap-2 mb-4">
                <Clock className="w-6 h-6 text-primary" />
                <span className="text-sm font-black uppercase tracking-widest text-primary">Recommended Study</span>
              </div>
              <div className="flex items-baseline gap-2 mb-2">
                <h2 className="text-5xl font-black">{data.recommendedStudyHours}</h2>
                <span className="text-xl font-bold text-white/70">hours</span>
              </div>
              <p className="text-white/70 text-sm leading-relaxed">
                Estimated remaining study time to reach target readiness levels before {data.targetDate}.
              </p>
            </div>
          </motion.section>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">

          {/* Left: Sub-test Readiness + Blockers */}
          <div className="lg:col-span-2 space-y-8">

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Readiness by Sub-test"
                title="See where the gap actually is"
                description="Each sub-test should show current readiness, target, and whether it is the weakest link."
                className="mb-4"
              />
              <div className="bg-surface rounded-[32px] border border-gray-200 p-6 sm:p-8 shadow-sm space-y-8">
                {data.subTests.map((test, idx) => {
                  const Icon = SUBTEST_ICONS[test.id] ?? FileText;
                  return (
                    <motion.div
                      key={test.id}
                      initial={{ opacity: 0, x: -20 }}
                      animate={{ opacity: 1, x: 0 }}
                      transition={{ delay: 0.2 + idx * 0.1 }}
                    >
                      <div className="flex items-center justify-between mb-3">
                        <div className="flex items-center gap-3">
                          <div className={`w-10 h-10 rounded-xl ${test.bg} flex items-center justify-center shrink-0`}>
                            <Icon className={`w-5 h-5 ${test.color}`} />
                          </div>
                          <div>
                            <h3 className="text-base font-bold text-navy flex items-center gap-2">
                              {test.name}
                              {test.isWeakest && (
                                <span className="bg-rose-100 text-rose-700 text-[10px] uppercase tracking-widest px-2 py-0.5 rounded-md font-black flex items-center gap-1">
                                  <AlertTriangle className="w-3 h-3" /> Weakest Link
                                </span>
                              )}
                            </h3>
                            <p className="text-xs text-muted">{test.status}</p>
                          </div>
                        </div>
                        <div className="text-right">
                          <span className="text-lg font-black text-navy">{test.readiness}%</span>
                          <span className="text-xs text-muted ml-1">/ {test.target}% target</span>
                        </div>
                      </div>
                      <div className="h-3 w-full bg-gray-100 rounded-full overflow-hidden relative">
                        <div
                          className="absolute top-0 bottom-0 w-0.5 bg-gray-400 z-10"
                          style={{ left: `${test.target}%` }}
                        />
                        <motion.div
                          initial={{ width: 0 }}
                          animate={{ width: `${test.readiness}%` }}
                          transition={{ duration: 1, delay: 0.5 + idx * 0.1, ease: 'easeOut' }}
                          className={`h-full rounded-full ${test.barColor}`}
                        />
                      </div>
                    </motion.div>
                  );
                })}
              </div>
            </section>

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Key Blockers"
                title="Make the biggest constraints explicit"
                description="A learner should immediately understand what is slowing progress before looking at more charts."
                className="mb-4"
              />
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                {data.blockers.map((blocker, idx) => (
                  <motion.div
                    key={blocker.id}
                    initial={{ opacity: 0, y: 20 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: 0.4 + idx * 0.1 }}
                    className="bg-rose-50 rounded-2xl border border-rose-100 p-5"
                  >
                    <h3 className="text-sm font-bold text-rose-900 mb-2">{blocker.title}</h3>
                    <p className="text-xs text-rose-800/80 leading-relaxed">{blocker.description}</p>
                  </motion.div>
                ))}
              </div>
            </section>
          </div>

          {/* Right: Evidence Panel */}
          <div>
            <LearnerSurfaceSectionHeader
              eyebrow="Evidence"
              title="Show what the estimate is based on"
              description="Readiness should feel earned by visible practice, mock, and expert-review evidence."
              className="mb-4"
            />
            <motion.div
              initial={{ opacity: 0, x: 20 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ delay: 0.3 }}
              className="bg-surface rounded-[32px] border border-gray-200 p-6 sm:p-8 shadow-sm"
            >
              <p className="text-sm text-muted mb-6 leading-relaxed">
                Readiness estimates are calculated using your recent performance data, weighted by full mocks and expert reviews.
              </p>
              <div className="space-y-4">
                <div className="flex items-center justify-between py-3 border-b border-gray-100">
                  <span className="text-sm font-medium text-muted">Full Mocks</span>
                  <span className="text-base font-black text-navy">{data.evidence.mocksCompleted}</span>
                </div>
                <div className="flex items-center justify-between py-3 border-b border-gray-100">
                  <span className="text-sm font-medium text-muted">Practice Questions</span>
                  <span className="text-base font-black text-navy">{data.evidence.practiceQuestions}</span>
                </div>
                <div className="flex items-center justify-between py-3 border-b border-gray-100">
                  <span className="text-sm font-medium text-muted">Expert Reviews</span>
                  <span className="text-base font-black text-navy">{data.evidence.expertReviews}</span>
                </div>
              </div>
              <div className="mt-6 bg-background-light rounded-xl p-4 border border-gray-100">
                <div className="flex items-start gap-3">
                  <TrendingUp className="w-5 h-5 text-primary shrink-0 mt-0.5" />
                  <div>
                    <h4 className="text-xs font-black text-navy uppercase tracking-widest mb-1">Recent Trend</h4>
                    <p className="text-xs text-muted leading-relaxed">{data.evidence.recentTrend}</p>
                  </div>
                </div>
              </div>
              <div className="mt-6 flex items-center gap-2 text-[10px] font-bold text-muted uppercase tracking-widest">
                <Info className="w-3 h-3" /> Updated: {data.evidence.lastUpdated}
              </div>
            </motion.div>
          </div>
        </div>

      </div>
    </LearnerDashboardShell>
  );
}
