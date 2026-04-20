'use client';

import React, { Suspense, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
  TrendingUp,
  TrendingDown,
  Minus,
  AlertTriangle,
  RefreshCw,
  FileText,
  Headphones,
  PenTool,
  Mic
} from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { OetStatementOfResultsCard } from '@/components/domain';
import { MockVocabularyReview } from '@/components/domain/vocabulary';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchMockReport } from '@/lib/api';
import type { MockReport } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';
import { oetGradeFromScaled } from '@/lib/scoring';
import { mockReportToStatementOfResults } from '@/lib/adapters/oet-sor-adapter';

const SUBTEST_META: Record<string, { icon: React.ElementType; color: string; bg: string }> = {
  listening: { icon: Headphones, color: 'text-indigo-600', bg: 'bg-indigo-100' },
  reading:   { icon: FileText,   color: 'text-blue-600',   bg: 'bg-blue-100' },
  writing:   { icon: PenTool,    color: 'text-rose-600',   bg: 'bg-rose-100' },
  speaking:  { icon: Mic,        color: 'text-purple-600', bg: 'bg-purple-100' },
};

/**
 * Colour a sub-test score cell based on its grade band. Always derives the
 * grade through the canonical scoring module so we can never drift from the
 * 350/300 thresholds or the 30/42 raw mapping. Accepts either a scaled
 * numeric string ("370", "350") or a letter-prefixed label ("A", "B").
 */
function scoreColor(score: string) {
  const trimmed = score.trim();
  if (trimmed.length === 0) return 'text-muted';
  const numeric = Number(trimmed);
  const grade = Number.isFinite(numeric)
    ? oetGradeFromScaled(numeric)
    : (trimmed.toUpperCase().replace(/^GRADE\s*/, '').split(/\s|,/)[0] ?? '');
  if (grade === 'A' || grade === 'B') return 'text-green-600';
  if (grade === 'C+' || grade === 'C') return 'text-amber-500';
  return 'text-rose-600';
}

function MockReportContent() {
  const params = useParams();
  const id = Array.isArray(params?.id) ? params?.id[0] : params?.id ?? '';
  const [report, setReport] = useState<MockReport | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    analytics.track('evaluation_viewed', { type: 'mock_report', id });
    fetchMockReport(id)
      .then(setReport)
      .catch(() => setError('Could not load report.'));
  }, [id]);

  if (error) {
    return (
      <LearnerDashboardShell pageTitle="Mock Report" backHref="/mocks">
        <div>
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!report) {
    return (
      <LearnerDashboardShell pageTitle="Mock Report" backHref="/mocks">
        <div className="space-y-6">
          {[1, 2, 3].map(i => (
            <Skeleton key={i} className="h-32 rounded-2xl" />
          ))}
        </div>
      </LearnerDashboardShell>
    );
  }

  const comp = report.priorComparison;

  return (
    <LearnerDashboardShell
      pageTitle="Mock Report"
      subtitle={`${report.title} · ${report.date}`}
      backHref="/mocks"
    >
      <div className="space-y-8">

        {/* 0. OET Statement of Results — pixel-faithful CBLA format.
            Mission-critical: this is the single place the "official" OET
            result card is rendered. See docs/OET-RESULT-CARD-SPEC.md. */}
        <MotionSection delayIndex={0}>
          <OetStatementOfResultsCard data={mockReportToStatementOfResults({ report })} />
        </MotionSection>

        {/* 1. Overall Score */}
        <MotionSection
          delayIndex={1}
          className="bg-surface rounded-[32px] border border-gray-200 p-8 sm:p-10 text-center shadow-sm flex flex-col items-center"
        >
          <div className="w-24 h-24 rounded-full bg-primary/10 flex items-center justify-center mb-6">
            <span className="text-5xl font-black text-primary">{report.overallScore}</span>
          </div>
          <h2 className="text-xl font-black text-navy mb-3">Overall Performance</h2>
          <p className="text-sm text-muted max-w-lg leading-relaxed">{report.summary}</p>
        </MotionSection>

        {/* 2. Prior Comparison */}
        {comp.exists && (
          <MotionSection
            delayIndex={1}
            className="bg-background-light rounded-[24px] border border-gray-200 p-6"
          >
            <div className="flex items-start gap-4">
              <div className={`w-10 h-10 rounded-full flex items-center justify-center shrink-0 ${
                comp.overallTrend === 'up'   ? 'bg-green-100 text-green-600' :
                comp.overallTrend === 'down' ? 'bg-rose-100 text-rose-600' :
                                               'bg-gray-200 text-gray-600'
              }`}>
                {comp.overallTrend === 'up'   && <TrendingUp className="w-5 h-5" />}
                {comp.overallTrend === 'down' && <TrendingDown className="w-5 h-5" />}
                {comp.overallTrend === 'flat' && <Minus className="w-5 h-5" />}
              </div>
              <div>
                <h3 className="text-sm font-black text-navy uppercase tracking-widest mb-1">
                  Compared to {comp.priorMockName}
                </h3>
                <p className="text-sm text-muted leading-relaxed">{comp.details}</p>
              </div>
            </div>
          </MotionSection>
        )}

        {/* 3. Sub-test Breakdown */}
        <MotionSection
          delayIndex={2}
        >
          <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Sub-test Breakdown</h2>
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            {report.subTests.map((test) => {
              const meta = SUBTEST_META[test.id] ?? SUBTEST_META.listening;
              const Icon = meta.icon;
              return (
                <div key={test.id} className="bg-surface rounded-[24px] border border-gray-200 p-5 flex items-center justify-between shadow-sm">
                  <div className="flex items-center gap-4">
                    <div className={`w-12 h-12 rounded-2xl ${meta.bg} flex items-center justify-center shrink-0`}>
                      <Icon className={`w-6 h-6 ${meta.color}`} />
                    </div>
                    <div>
                      <h3 className="text-base font-bold text-navy">{test.name}</h3>
                      <p className="text-xs text-muted">Raw: {test.rawScore}</p>
                    </div>
                  </div>
                  <span className={`text-2xl font-black ${scoreColor(test.score)}`}>{test.score}</span>
                </div>
              );
            })}
          </div>
        </MotionSection>

        {/* 4. Weakest Criterion */}
        <MotionSection
          delayIndex={3}
        >
          <h2 className="text-sm font-black text-muted uppercase tracking-widest mb-4">Area for Improvement</h2>
          <div className="bg-rose-50 rounded-[24px] border border-rose-100 p-6 sm:p-8">
            <div className="flex items-start gap-4">
              <div className="w-12 h-12 rounded-2xl bg-rose-100 flex items-center justify-center shrink-0">
                <AlertTriangle className="w-6 h-6 text-rose-600" />
              </div>
              <div>
                <div className="flex items-center gap-2 mb-1">
                  <span className="text-xs font-black text-rose-600 uppercase tracking-widest">{report.weakestCriterion.subtest}</span>
                  <span className="text-rose-300">•</span>
                  <span className="text-xs font-black text-rose-600 uppercase tracking-widest">Weakest Criterion</span>
                </div>
                <h3 className="text-lg font-black text-rose-900 mb-2">{report.weakestCriterion.criterion}</h3>
                <p className="text-sm text-rose-800/80 leading-relaxed">{report.weakestCriterion.description}</p>
              </div>
            </div>
          </div>
        </MotionSection>

        {/* 5. Words to Review — surfaces OET vocabulary tied to the weakest criterion */}
        <MockVocabularyReview
          mockId={report.id}
          weakSubtest={report.weakestCriterion.subtest}
          weakCriterion={report.weakestCriterion.criterion}
          weakDescription={report.weakestCriterion.description}
        />

        {/* 6. Study Plan CTA */}
        <MotionSection
          delayIndex={5}
          className="pt-4"
        >
          <div className="bg-navy rounded-[32px] p-8 text-center text-white relative overflow-hidden shadow-lg">
            <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_right,_rgba(255,255,255,0.1),_transparent)]" />
            <div className="relative z-10">
              <div className="w-16 h-16 rounded-full bg-white/10 flex items-center justify-center mx-auto mb-4">
                <RefreshCw className="w-8 h-8 text-white" />
              </div>
              <h2 className="text-xl font-black mb-2">Update Your Study Plan</h2>
              <p className="text-sm text-white/70 max-w-md mx-auto mb-6">
                Based on this report, we recommend focusing on <strong>{report.weakestCriterion.criterion}</strong> in {report.weakestCriterion.subtest}.
              </p>
              <Link
                href="/study-plan"
                className="bg-white text-navy px-8 py-4 rounded-xl font-black hover:bg-gray-100 transition-colors inline-flex items-center justify-center gap-2"
              >
                Update Study Plan
              </Link>
            </div>
          </div>
        </MotionSection>

      </div>
    </LearnerDashboardShell>
  );
}

export default function MockReport() {
  return (
    <Suspense fallback={
      <LearnerDashboardShell pageTitle="Mock Report" backHref="/mocks">
        <div className="space-y-6">
          {[1, 2, 3].map(i => (
            <Skeleton key={i} className="h-32 rounded-2xl" />
          ))}
        </div>
      </LearnerDashboardShell>
    }>
      <MockReportContent />
    </Suspense>
  );
}
