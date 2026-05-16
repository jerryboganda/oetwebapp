'use client';

import React, { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
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
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchReadiness, fetchReadinessRisk } from '@/lib/api';
import type { ReadinessData } from '@/lib/mock-data';
import { useReducedMotion } from 'motion/react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { getMotionDelay, getProgressFillTransition, prefersReducedMotion } from '@/lib/motion';

const SUBTEST_ICONS: Record<string, React.ElementType> = {
  reading:   FileText,
  listening: Headphones,
  writing:   PenTool,
  speaking:  Mic,
};

export default function ReadinessCenter() {
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const progressTransition = getProgressFillTransition(reducedMotion);
  const [data, setData] = useState<ReadinessData | null>(null);
  const [riskData, setRiskData] = useState<{ riskProbability: number; riskLevel: string; factors: { label: string; severity: string; impact: number; description: string }[]; recommendation: string } | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    analytics.track('readiness_viewed');
    fetchReadiness()
      .then(setData)
      .catch(() => setError('Could not load readiness data.'));
    fetchReadinessRisk()
      .then((r: { riskProbability: number; riskLevel: string; factors: { label: string; severity: string; impact: number; description: string }[]; recommendation: string }) => setRiskData(r))
      .catch(() => { /* non-critical — page still works without risk data */ });
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

  // Risk accent is expressed via icon tile + eyebrow chip on a Surface White card,
  // not via a saturated full-bleed background.
  const riskAccent =
    data.overallRisk === 'High'     ? { tile: 'bg-danger/10 text-danger', chip: 'bg-danger/10 text-danger border-danger/20' } :
    data.overallRisk === 'Moderate' ? { tile: 'bg-warning/10 text-warning', chip: 'bg-warning/10 text-warning border-warning/20' } :
                                      { tile: 'bg-success/10 text-success', chip: 'bg-success/10 text-success border-success/20' };
  const riskIcon =
    data.overallRisk === 'High' ? ShieldAlert :
    data.overallRisk === 'Moderate' ? Shield :
    ShieldCheck;
  const RiskIconCmp = riskIcon;

  // ── Risk Gauge Data ──
  const apiRiskPercent = riskData?.riskProbability;
  const riskPercent = typeof apiRiskPercent === 'number' ? apiRiskPercent : null;
  const needleAngle = riskPercent == null ? null : Math.PI * (1 - riskPercent / 100);
  const needleX = needleAngle == null ? null : 100 + 80 * Math.cos(needleAngle);
  const needleY = needleAngle == null ? null : 100 - 80 * Math.sin(needleAngle);

  interface RiskFactor { label: string; severity: 'high' | 'medium' | 'low'; impact: number; description: string }
  const riskFactors: RiskFactor[] = riskData?.factors?.length
    ? riskData.factors.map((f) => ({
        label: f.label,
        severity: (f.severity === 'high' || f.severity === 'medium' || f.severity === 'low' ? f.severity : 'medium') as 'high' | 'medium' | 'low',
        impact: f.impact,
        description: f.description,
      }))
    : [];

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

        {/* 1. Risk Probability Gauge + Recommended Study + Risk Factors */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
          <MotionSection
            className="bg-surface border border-border rounded-[24px] p-8 shadow-sm relative overflow-hidden"
          >
            <div className="relative z-10">
              <div className="flex items-center gap-2 mb-4">
                <span className={`inline-flex h-8 w-8 items-center justify-center rounded-xl ${riskAccent.tile}`}>
                  <RiskIconCmp className="w-4 h-4" />
                </span>
                <span className={`inline-flex items-center gap-1.5 rounded-md border px-2.5 py-1 text-[11px] font-bold uppercase tracking-wider ${riskAccent.chip}`}>
                  Target-Date Risk
                </span>
              </div>

              {/* ── Risk Probability Gauge ── */}
              <div className="flex flex-col items-center my-4">
                <svg viewBox="0 0 200 120" className="w-48 h-auto">
                  <defs>
                    <linearGradient id="gaugeGrad" x1="0%" y1="0%" x2="100%" y2="0%">
                      <stop offset="0%" stopColor="var(--color-success)" />
                      <stop offset="50%" stopColor="var(--color-warning)" />
                      <stop offset="100%" stopColor="var(--color-danger)" />
                    </linearGradient>
                  </defs>
                  <path d="M 20 100 A 80 80 0 0 1 180 100" fill="none" stroke="var(--color-border)" strokeWidth="14" strokeLinecap="round" />
                  {riskPercent == null ? null : (
                    <>
                      <path d="M 20 100 A 80 80 0 0 1 180 100" fill="none" stroke="url(#gaugeGrad)" strokeWidth="14" strokeLinecap="round" strokeDasharray={`${251 * (riskPercent / 100)} 251`} />
                      <circle cx={needleX ?? 0} cy={needleY ?? 0} r="6" fill="var(--color-navy)" stroke="var(--color-surface)" strokeWidth="2" />
                    </>
                  )}
                  <text x="100" y="95" textAnchor="middle" className="fill-navy text-xl font-black">{riskPercent == null ? 'Unavailable' : `${riskPercent}%`}</text>
                  <text x="100" y="112" textAnchor="middle" className="fill-muted text-[10px] font-bold uppercase tracking-widest">probability</text>
                </svg>
              </div>

              <h2 className="text-3xl font-black mb-2 text-center text-navy">{data.overallRisk} Risk</h2>
              <p className="text-muted text-sm leading-relaxed text-center max-w-sm mx-auto">
                {riskPercent == null
                  ? 'Target-date probability is unavailable until the readiness risk service returns an estimate.'
                  : `With ${data.weeksRemaining} weeks remaining until your exam, the backend estimate is ${riskPercent}%.`}
              </p>
            </div>
          </MotionSection>

          <MotionSection
            delayIndex={1}
            className="rounded-[24px] border border-slate-800 bg-slate-950 p-8 text-white relative overflow-hidden shadow-sm flex flex-col justify-center dark:border-slate-700"
          >
            <div className="absolute bottom-0 left-0 w-64 h-64 bg-primary/10 rounded-full blur-3xl translate-y-1/2 -translate-x-1/3" aria-hidden="true" />
            <div className="relative z-10">
              <div className="flex items-center gap-2 mb-4">
                <Clock className="w-6 h-6 text-primary" />
                <span className="text-sm font-black uppercase tracking-widest text-violet-200">Recommended Study</span>
              </div>
              <div className="flex items-baseline gap-2 mb-2">
                <h2 className="text-5xl font-black">{data.recommendedStudyHours}</h2>
                <span className="text-xl font-bold text-slate-200">hours</span>
              </div>
              <p className="text-slate-200 text-sm leading-relaxed">
                Estimated remaining study time to reach target readiness levels before {data.targetDate}.
              </p>
            </div>
          </MotionSection>

          {/* ── Risk Factors Breakdown ── */}
          <MotionSection
            delayIndex={2}
            className="bg-surface rounded-[24px] border border-border p-6 shadow-sm"
          >
            <div className="flex items-center gap-2 mb-5">
              <span className="inline-flex h-8 w-8 items-center justify-center rounded-xl bg-warning/10 text-warning">
                <AlertTriangle className="w-4 h-4" />
              </span>
              <span className="text-xs font-black uppercase tracking-widest text-muted">Risk Factors</span>
            </div>
              <div className="space-y-4">
                {riskFactors.length === 0 ? (
                  <p className="text-sm leading-6 text-muted">
                    Risk factors are unavailable until the backend returns an evidence-backed breakdown.
                  </p>
                ) : riskFactors.map((factor) => {
                const sevToken =
                  factor.severity === 'high'   ? { chip: 'bg-danger/10 text-danger',   bar: 'bg-danger' } :
                  factor.severity === 'medium' ? { chip: 'bg-warning/10 text-warning', bar: 'bg-warning' } :
                                                 { chip: 'bg-success/10 text-success', bar: 'bg-success' };
                return (
                <div key={factor.label}>
                  <div className="flex items-center justify-between mb-1.5">
                    <span className="text-sm font-semibold text-navy">{factor.label}</span>
                    <span className={`text-xs font-black uppercase tracking-widest px-2 py-0.5 rounded-md ${sevToken.chip}`}>{factor.severity}</span>
                  </div>
                  <div className="h-2 w-full bg-background-light rounded-full overflow-hidden">
                    <motion.div
                      initial={{ width: 0 }}
                      animate={{ width: `${factor.impact}%` }}
                      transition={{
                        ...progressTransition,
                        delay: getMotionDelay(1, reducedMotion, 0.4),
                      }}
                      className={`h-full rounded-full ${sevToken.bar}`}
                    />
                  </div>
                  <p className="text-[11px] text-muted mt-1">{factor.description}</p>
                </div>
                );
              })}
            </div>
          </MotionSection>
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
              <div className="bg-surface rounded-[24px] border border-border p-6 sm:p-8 shadow-sm space-y-8">
                {data.subTests.map((test, idx) => {
                  const Icon = SUBTEST_ICONS[test.id] ?? FileText;
                  return (
                    <MotionItem
                      key={test.id}
                      delayIndex={idx}
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
                                <span className="bg-danger/10 text-danger text-[10px] uppercase tracking-widest px-2 py-0.5 rounded-md font-black flex items-center gap-1">
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
                      <div className="h-3 w-full bg-background-light rounded-full overflow-hidden relative">
                        <div
                          className="absolute top-0 bottom-0 w-0.5 bg-border z-10"
                          style={{ left: `${test.target}%` }}
                        />
                        <motion.div
                          initial={{ width: 0 }}
                          animate={{ width: `${test.readiness}%` }}
                          transition={{
                            ...progressTransition,
                            delay: getMotionDelay(idx, reducedMotion, 0.5),
                          }}
                          className={`h-full rounded-full ${test.barColor}`}
                        />
                      </div>
                    </MotionItem>
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
                  <MotionItem
                    key={blocker.id}
                    delayIndex={idx}
                    className="bg-surface rounded-[24px] border border-danger/20 p-5 shadow-sm"
                  >
                    <div className="flex items-center gap-2 mb-2">
                      <span className="inline-flex h-7 w-7 items-center justify-center rounded-lg bg-danger/10 text-danger">
                        <AlertTriangle className="w-3.5 h-3.5" />
                      </span>
                      <h3 className="text-sm font-bold text-navy">{blocker.title}</h3>
                    </div>
                    <p className="text-xs text-muted leading-relaxed">{blocker.description}</p>
                  </MotionItem>
                ))}
              </div>
            </section>
          </div>

          {/* Right: Evidence Panel */}
          <div>
            <LearnerSurfaceSectionHeader
              eyebrow="Evidence"
              title="Show what the estimate is based on"
              description="Readiness should feel earned by visible practice, mock, and tutor-review evidence."
              className="mb-4"
            />
            <MotionSection
              delayIndex={1}
              className="bg-surface rounded-[24px] border border-border p-6 sm:p-8 shadow-sm"
            >
              <p className="text-sm text-muted mb-6 leading-relaxed">
                Readiness estimates are calculated using your recent performance data, weighted by full mocks and tutor reviews.
              </p>
              <div className="space-y-4">
                <div className="flex items-center justify-between py-3 border-b border-border">
                  <span className="text-sm font-medium text-muted">Full Mocks</span>
                  <span className="text-base font-black text-navy">{data.evidence.mocksCompleted}</span>
                </div>
                <div className="flex items-center justify-between py-3 border-b border-border">
                  <span className="text-sm font-medium text-muted">Practice Questions</span>
                  <span className="text-base font-black text-navy">{data.evidence.practiceQuestions}</span>
                </div>
                <div className="flex items-center justify-between py-3 border-b border-border">
                  <span className="text-sm font-medium text-muted">Tutor Reviews</span>
                  <span className="text-base font-black text-navy">{data.evidence.expertReviews}</span>
                </div>
              </div>
              <div className="mt-6 bg-background-light rounded-xl p-4 border border-border">
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
            </MotionSection>
          </div>
        </div>

      </div>
    </LearnerDashboardShell>
  );
}
