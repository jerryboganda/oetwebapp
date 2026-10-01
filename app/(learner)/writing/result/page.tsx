'use client';

import { Suspense, useState, useEffect } from 'react';
import { FileText, BarChart3, ShieldAlert, ThumbsUp, AlertTriangle, Edit3, Star } from 'lucide-react';
import { useSearchParams } from 'next/navigation';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { EmptyState } from '@/components/ui/empty-error';
import { PageSkeleton } from '@/components/ui/skeleton';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { fetchWritingResult, isApiError } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { WritingResult } from '@/lib/mock-data';
import ProfessionRemediationCallout from '@/components/domain/profession-remediation-callout';
import { WritingDualAssessmentSection } from '@/components/domain/writing/WritingDualAssessmentSection';
import { TutorVoiceNotePlayer } from '@/components/domain/writing/TutorVoiceNotePlayer';

function WritingResultContent() {
  const searchParams = useSearchParams();
  const resultId = searchParams?.get('id') ?? '';
  const [result, setResult] = useState<WritingResult | null>(null);
  const [loading, setLoading] = useState(!!resultId);

  useEffect(() => {
    if (!resultId) {
      return;
    }

    let cancelled = false;

    let failedAttempts = 0;

    const poll = async () => {
      try {
        const response = await fetchWritingResult(resultId);
        if (cancelled) return;
        failedAttempts = 0;
        if (response.evalStatus === 'completed') {
          analytics.track('evaluation_viewed', { resultId, subtest: 'writing' });
          setResult(response);
          setLoading(false);
          return;
        }

        setTimeout(() => { void poll(); }, 2000);
      } catch (error) {
        if (!cancelled) {
          failedAttempts += 1;
          const shouldRetry =
            failedAttempts < 120 &&
            (!isApiError(error) || error.retryable || error.status === 404 || error.status === 409 || error.status === 425);

          if (shouldRetry) {
            setTimeout(() => { void poll(); }, 2000);
            return;
          }

          setLoading(false);
        }
      }
    };

    void poll();

    return () => { cancelled = true; };
  }, [resultId]);

  if (!resultId) {
    return <EmptyState icon={<FileText className="h-8 w-8" />} title="Open results from a completed writing submission." />;
  }

  if (loading) {
    return (
      <>
        <LearnerSkeleton variant="hero" />
        <LearnerSkeleton variant="card-grid" />
      </>
    );
  }

  if (!result) {
    return <EmptyState icon={<FileText className="h-8 w-8" />} title="Result not found." />;
  }

  const confidenceColor = result.confidenceBand === 'High'
    ? 'success'
    : result.confidenceBand === 'Medium'
      ? 'warning'
      : 'danger';

  return (
    <>
      {/* Score, confidence and method each show once below (score card, disclaimer),
          so the hero carries no duplicate highlight chips. */}
      <LearnerPageHero
        eyebrow="Assessment Output"
        icon={FileText}
        title="Evaluation Summary"
        description={result.taskTitle}
      />

      <InlineAlert variant="info" live="polite">
        <strong>{result.methodLabel}:</strong> {result.learnerDisclaimer}
      </InlineAlert>

      <MotionSection delayIndex={0}>
        <Card padding="lg" className="text-center">
          <div className="grid grid-cols-1 gap-6 divide-y divide-border md:grid-cols-3 md:gap-8 md:divide-y-0 md:divide-x">
            <div className="flex min-w-0 flex-col items-center justify-center">
              <p className="eyebrow mb-2 flex items-center gap-1.5 text-muted">
                <BarChart3 className="h-4 w-4" aria-hidden="true" />
                Estimated Score
              </p>
              <p className="text-4xl font-bold tabular-nums tracking-tight text-navy sm:text-5xl">{result.estimatedScoreRange}</p>
            </div>
            <div className="flex min-w-0 flex-col items-center justify-center pt-6 md:pt-0">
              <p className="eyebrow mb-2 text-muted">Estimated Grade</p>
              <p className="text-4xl font-bold tracking-tight text-primary sm:text-5xl">{result.estimatedGradeRange}</p>
            </div>
            <div className="flex min-w-0 flex-col items-center justify-center pt-6 md:pt-0">
              <p className="eyebrow mb-2 flex items-center gap-1.5 text-muted">
                <ShieldAlert className="h-4 w-4" aria-hidden="true" />
                AI Confidence
              </p>
              <Badge variant={confidenceColor} size="sm">{result.confidenceBand} Band</Badge>
              <p className="mt-2 text-xs text-muted">{result.confidenceLabel}</p>
            </div>
          </div>
        </Card>
      </MotionSection>

      <MotionSection delayIndex={1} className="grid grid-cols-1 gap-4 md:grid-cols-2">
        <Card className="min-w-0">
          <p className="tile-label text-muted">Exam Family</p>
          <p className="mt-2 text-base font-bold text-navy">{result.examFamilyLabel}</p>
        </Card>
        <Card className="min-w-0">
          <p className="tile-label text-muted">Provenance</p>
          <p className="mt-2 text-base font-bold text-navy">{result.provenanceLabel}</p>
        </Card>
      </MotionSection>

      {result.humanReviewRecommended ? (
        <InlineAlert variant="warning" live="polite">
          Human review is recommended here because the AI score is still a practice estimate. Use tutor review for higher-stakes decisions and borderline readiness calls.
        </InlineAlert>
      ) : null}

      <MotionSection delayIndex={2}>
        <WritingDualAssessmentSection evaluationId={resultId} />
      </MotionSection>

      {/* Tutor's spoken feedback (normal writing). Renders only when the id is a
          writing submission with a submitted note (e.g. the expert-request flow). */}
      <TutorVoiceNotePlayer submissionId={resultId} />

      <ProfessionRemediationCallout profession={result.profession} />

      <section className="space-y-4">
        <LearnerSurfaceSectionHeader
          eyebrow="What to do next"
          title="Turn the summary into action"
          description="Use the links below to inspect feedback or send it to a tutor reviewer."
        />

        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 md:gap-6">
          <MotionItem delayIndex={0} className="min-w-0">
            <Card padding="lg" className="h-full">
              <div className="mb-5 flex items-center gap-2">
                <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-success/10">
                  <ThumbsUp className="h-4 w-4 text-success-strong" aria-hidden="true" />
                </div>
                <h3 className="text-lg font-bold text-navy">Top Strengths</h3>
              </div>
              <ul className="space-y-4">
                {result.topStrengths.map((strength, index) => (
                  <li key={index} className="flex items-start gap-3 text-navy">
                    <span aria-hidden="true" className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full bg-success" />
                    <span className="leading-relaxed">{strength}</span>
                  </li>
                ))}
              </ul>
            </Card>
          </MotionItem>
          <MotionItem delayIndex={1} className="min-w-0">
            <Card padding="lg" className="h-full">
              <div className="mb-5 flex items-center gap-2">
                <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-warning/10">
                  <AlertTriangle className="h-4 w-4 text-warning-strong" aria-hidden="true" />
                </div>
                <h3 className="text-lg font-bold text-navy">Top Issues to Fix</h3>
              </div>
              <ul className="space-y-4">
                {result.topIssues.map((issue, index) => (
                  <li key={index} className="flex items-start gap-3 text-navy">
                    <span aria-hidden="true" className="mt-2 h-1.5 w-1.5 shrink-0 rounded-full bg-warning" />
                    <span className="leading-relaxed">{issue}</span>
                  </li>
                ))}
              </ul>
            </Card>
          </MotionItem>
        </div>

        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <CardLink href={`/writing/feedback?id=${resultId}`} className="flex flex-col items-center border-primary/30 bg-primary/5 text-center">
            <BarChart3 className="mb-2 h-6 w-6 text-primary" aria-hidden="true" />
            <span className="font-bold text-primary">View Detailed Feedback</span>
            <span className="mt-1 text-xs text-muted">See criterion breakdown</span>
          </CardLink>
          <CardLink href={`/writing/expert-request?id=${resultId}`} className="flex flex-col items-center text-center">
            <Star className="mb-2 h-6 w-6 text-muted" aria-hidden="true" />
            <span className="font-bold text-navy">Request Tutor Review</span>
            <span className="mt-1 text-xs text-muted">Get human feedback</span>
          </CardLink>
        </div>
      </section>

      <MotionSection>
        <Card padding="lg">
          <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
            <div className="min-w-0">
              <p className="eyebrow text-primary">Correction workflow</p>
              <h2 className="mt-2 text-xl font-bold text-navy">Use feedback as a rewrite cycle, not as a final score</h2>
              <p className="mt-2 max-w-3xl text-sm leading-6 text-muted">
                The writing module follows the intended teacher-correction path: review the six criteria, inspect anchored comments, rewrite the letter in learning mode, then request tutor review for final readiness decisions.
              </p>
            </div>
            <Badge variant={result.isOfficialScore ? 'success' : 'warning'} size="sm" className="self-start">
              {result.isOfficialScore ? 'Official score' : 'Practice estimate'}
            </Badge>
          </div>

          <ol className="mt-5 grid grid-cols-1 gap-3 md:grid-cols-3">
            <li className="min-w-0 rounded-xl bg-background-light p-4">
              <BarChart3 className="h-5 w-5 text-primary" aria-hidden="true" />
              <h3 className="mt-3 text-sm font-bold text-navy">1. Inspect criteria</h3>
              <p className="mt-1 text-xs leading-5 text-muted">Read the criterion breakdown before changing the letter, especially Purpose, Content, and Conciseness.</p>
            </li>
            <li className="min-w-0 rounded-xl bg-background-light p-4">
              <Edit3 className="h-5 w-5 text-primary" aria-hidden="true" />
              <h3 className="mt-3 text-sm font-bold text-navy">2. Rewrite in learning mode</h3>
              <p className="mt-1 text-xs leading-5 text-muted">Use guided drafting, reader-aware structure, and rulebook support to produce a better second version.</p>
            </li>
            <li className="min-w-0 rounded-xl bg-background-light p-4">
              <Star className="h-5 w-5 text-primary" aria-hidden="true" />
              <h3 className="mt-3 text-sm font-bold text-navy">3. Request tutor review</h3>
              <p className="mt-1 text-xs leading-5 text-muted">Use human review for borderline readiness, paid corrections, and final academy decisions.</p>
            </li>
          </ol>
        </Card>
      </MotionSection>
    </>
  );
}

export default function WritingResultSummary() {
  return (
    <Suspense fallback={<PageSkeleton />}>
      <WritingResultContent />
    </Suspense>
  );
}
