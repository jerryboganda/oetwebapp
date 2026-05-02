'use client';

import { useState, useEffect } from 'react';
import { FileText, BarChart3, ShieldAlert, ThumbsUp, AlertTriangle, Edit3, Star, Info } from 'lucide-react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { MotionSection } from '@/components/ui/motion-primitives';
import { fetchWritingResult } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingResult } from '@/lib/mock-data';

export default function WritingResultSummary() {
  const searchParams = useSearchParams();
  const resultId = searchParams?.get('id') ?? '';
  const [result, setResult] = useState<WritingResult | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!resultId) {
      return;
    }

    let cancelled = false;

    const poll = async () => {
      try {
        const response = await fetchWritingResult(resultId);
        if (cancelled) return;
        if (response.evalStatus === 'completed') {
          analytics.track('evaluation_viewed', { resultId, subtest: 'writing' });
          setResult(response);
          setLoading(false);
          return;
        }

        setTimeout(() => { void poll(); }, 2000);
      } catch {
        if (!cancelled) {
          setLoading(false);
        }
      }
    };

    void poll();

    return () => { cancelled = true; };
  }, [resultId]);

  if (!resultId) {
    return (
      <LearnerDashboardShell pageTitle="Not Found">
        <div className="p-10 text-center text-muted">Open results from a completed writing submission.</div>
      </LearnerDashboardShell>
    );
  }

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Evaluation Summary">
        <div className="space-y-6">
          <Skeleton className="h-40 rounded-2xl" />
          <Skeleton className="h-32 rounded-2xl" />
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <Skeleton className="h-48 rounded-2xl" />
            <Skeleton className="h-48 rounded-2xl" />
          </div>
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!result) {
    return (
      <LearnerDashboardShell pageTitle="Not Found">
        <div className="p-10 text-center text-muted">Result not found.</div>
      </LearnerDashboardShell>
    );
  }

  const confidenceColor = result.confidenceBand === 'High'
    ? 'success'
    : result.confidenceBand === 'Medium'
      ? 'warning'
      : 'danger';

  return (
    <LearnerDashboardShell pageTitle="Evaluation Summary">
      <main className="space-y-8">
        <LearnerPageHero
          eyebrow="Assessment Output"
          icon={FileText}
          title="Evaluation Summary"
          description={result.taskTitle}
          highlights={[
            { icon: BarChart3, label: 'Estimated score', value: result.estimatedScoreRange },
            { icon: ShieldAlert, label: 'Confidence', value: result.confidenceLabel },
            { icon: Info, label: 'Method', value: result.methodLabel },
          ]}
        />

        <MotionSection className="rounded-2xl border border-info/30 bg-info/10 p-4 flex items-start gap-3 shadow-sm">
          <Info className="w-5 h-5 shrink-0 mt-0.5 text-info" />
          <p className="text-sm leading-relaxed text-info">
            <strong>{result.methodLabel}:</strong> {result.learnerDisclaimer}
          </p>
        </MotionSection>

        <MotionSection delayIndex={1} className="grid grid-cols-1 gap-4 md:grid-cols-3 mb-8">
          <Card className="border-border bg-background-light p-4">
            <p className="text-xs font-bold uppercase tracking-wider text-muted">Exam Family</p>
            <p className="mt-2 text-base font-bold text-navy">{result.examFamilyLabel}</p>
          </Card>
          <Card className="border-border bg-background-light p-4">
            <p className="text-xs font-bold uppercase tracking-wider text-muted">Confidence</p>
            <p className="mt-2 text-base font-bold text-navy">{result.confidenceLabel}</p>
          </Card>
          <Card className="border-border bg-background-light p-4">
            <p className="text-xs font-bold uppercase tracking-wider text-muted">Provenance</p>
            <p className="mt-2 text-base font-bold text-navy">{result.provenanceLabel}</p>
          </Card>
        </MotionSection>

        {result.humanReviewRecommended ? (
          <MotionSection delayIndex={2} className="rounded-2xl border border-warning/30 bg-warning/10 p-4 flex items-start gap-3 shadow-sm">
            <ShieldAlert className="w-5 h-5 text-warning shrink-0 mt-0.5" />
            <p className="text-sm text-warning leading-relaxed">
              Human review is recommended here because the AI score is still a practice estimate. Use tutor review for higher-stakes decisions and borderline readiness calls.
            </p>
          </MotionSection>
        ) : null}

        <MotionSection delayIndex={3}>
          <Card className="border-border bg-surface p-6">
            <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
              <div>
                <p className="text-xs font-black uppercase tracking-widest text-primary">Correction workflow</p>
                <h2 className="mt-2 text-xl font-black text-navy">Use feedback as a rewrite cycle, not as a final score</h2>
                <p className="mt-2 max-w-3xl text-sm leading-6 text-muted">
                  The writing module follows the intended teacher-correction path: review the six criteria, inspect anchored comments, rewrite the letter in learning mode, then request tutor review for final readiness decisions.
                </p>
              </div>
              <Badge variant={result.isOfficialScore ? 'success' : 'warning'} size="sm">
                {result.isOfficialScore ? 'Official score' : 'Practice estimate'}
              </Badge>
            </div>

            <div className="mt-5 grid gap-3 md:grid-cols-3">
              <div className="rounded-2xl border border-border bg-background-light p-4">
                <BarChart3 className="h-5 w-5 text-primary" />
                <h3 className="mt-3 text-sm font-bold text-navy">1. Inspect criteria</h3>
                <p className="mt-1 text-xs leading-5 text-muted">Read the criterion breakdown before changing the letter, especially Purpose, Content, and Conciseness.</p>
              </div>
              <div className="rounded-2xl border border-border bg-background-light p-4">
                <Edit3 className="h-5 w-5 text-primary" />
                <h3 className="mt-3 text-sm font-bold text-navy">2. Rewrite in learning mode</h3>
                <p className="mt-1 text-xs leading-5 text-muted">Use guided drafting, reader-aware structure, and rulebook support to produce a better second version.</p>
              </div>
              <div className="rounded-2xl border border-border bg-background-light p-4">
                <Star className="h-5 w-5 text-primary" />
                <h3 className="mt-3 text-sm font-bold text-navy">3. Request tutor review</h3>
                <p className="mt-1 text-xs leading-5 text-muted">Use human review for borderline readiness, paid corrections, and final academy decisions.</p>
              </div>
            </div>
          </Card>
        </MotionSection>

        <MotionSection delayIndex={4}>
          <Card className="border-border bg-surface p-8 text-center">
            <div className="grid grid-cols-1 md:grid-cols-3 gap-8 divide-y md:divide-y-0 md:divide-x divide-border">
              <div className="flex flex-col items-center justify-center pt-4 md:pt-0">
                <div className="text-sm font-bold text-muted uppercase tracking-wider mb-2 flex items-center gap-1.5">
                  <BarChart3 className="w-4 h-4" />
                  Estimated Score
                </div>
                <div className="text-4xl sm:text-5xl font-black text-navy tracking-tight">{result.estimatedScoreRange}</div>
              </div>
              <div className="flex flex-col items-center justify-center pt-8 md:pt-0">
                <div className="text-sm font-bold text-muted uppercase tracking-wider mb-2">Estimated Grade</div>
                <div className="text-4xl sm:text-5xl font-black text-primary tracking-tight">{result.estimatedGradeRange}</div>
              </div>
              <div className="flex flex-col items-center justify-center pt-8 md:pt-0">
                <div className="text-sm font-bold text-muted uppercase tracking-wider mb-2 flex items-center gap-1.5">
                  <ShieldAlert className="w-4 h-4" />
                  AI Confidence
                </div>
                <Badge variant={confidenceColor} size="sm">{result.confidenceBand} Band</Badge>
                <p className="mt-2 text-xs text-muted">{result.confidenceLabel}</p>
              </div>
            </div>
          </Card>
        </MotionSection>

        <LearnerSurfaceSectionHeader
          eyebrow="What to do next"
          title="Turn the summary into action"
          description="Use the links below to inspect feedback, revise the submission, or send it to a tutor reviewer."
        />

        <div className="grid grid-cols-1 md:grid-cols-2 gap-6 mb-8">
          <MotionSection delayIndex={5}>
            <Card className="border-border bg-surface p-6">
              <div className="flex items-center gap-2 mb-6">
                <div className="w-8 h-8 rounded-full bg-success/10 flex items-center justify-center shrink-0">
                  <ThumbsUp className="w-4 h-4 text-success" />
                </div>
                <h2 className="text-lg font-bold text-navy">Top Strengths</h2>
              </div>
              <ul className="space-y-4">
                {result.topStrengths.map((strength, index) => (
                  <li key={index} className="flex items-start gap-3 text-navy">
                    <span className="w-1.5 h-1.5 rounded-full bg-success mt-2 shrink-0" />
                    <span className="leading-relaxed">{strength}</span>
                  </li>
                ))}
              </ul>
            </Card>
          </MotionSection>
          <MotionSection delayIndex={6}>
            <Card className="border-border bg-surface p-6">
              <div className="flex items-center gap-2 mb-6">
                <div className="w-8 h-8 rounded-full bg-warning/10 flex items-center justify-center shrink-0">
                  <AlertTriangle className="w-4 h-4 text-warning" />
                </div>
                <h2 className="text-lg font-bold text-navy">Top Issues to Fix</h2>
              </div>
              <ul className="space-y-4">
                {result.topIssues.map((issue, index) => (
                  <li key={index} className="flex items-start gap-3 text-navy">
                    <span className="w-1.5 h-1.5 rounded-full bg-warning mt-2 shrink-0" />
                    <span className="leading-relaxed">{issue}</span>
                  </li>
                ))}
              </ul>
            </Card>
          </MotionSection>
        </div>

        <MotionSection delayIndex={7} className="grid grid-cols-1 sm:grid-cols-3 gap-4 pb-8">
          <Link href={`/writing/feedback?id=${resultId}`} className="group rounded-2xl border border-primary/20 bg-primary px-4 py-5 text-center text-white transition-all shadow-sm hover:bg-primary/90 hover:shadow-md">
            <BarChart3 className="w-6 h-6 mb-2 opacity-80 group-hover:opacity-100 transition-opacity" />
            <span className="font-bold">View Detailed Feedback</span>
            <span className="text-xs text-info mt-1">See criterion breakdown</span>
          </Link>
          <Link href={`/writing/player?taskId=${result.taskId}&mode=learning`} className="group rounded-2xl border border-border bg-surface px-4 py-5 text-center text-navy transition-all shadow-sm hover:border-primary/30 hover:shadow-md">
            <Edit3 className="w-6 h-6 mb-2 text-muted/60 group-hover:text-primary transition-colors" />
            <span className="font-bold group-hover:text-primary transition-colors">Revise Submission</span>
            <span className="text-xs text-muted mt-1">Rewrite with learning support</span>
          </Link>
          <Link href={`/writing/expert-request?id=${resultId}`} className="group rounded-2xl border border-border bg-surface px-4 py-5 text-center text-navy transition-all shadow-sm hover:border-warning/30 hover:shadow-md">
            <Star className="w-6 h-6 mb-2 text-muted/60 group-hover:text-warning transition-colors" />
            <span className="font-bold group-hover:text-warning transition-colors">Request Tutor Review</span>
            <span className="text-xs text-muted mt-1">Get human feedback</span>
          </Link>
        </MotionSection>
      </main>
    </LearnerDashboardShell>
  );
}
