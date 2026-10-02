'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowRight, BarChart3, Headphones, Hourglass, ListChecks } from 'lucide-react';
import { apiClient } from '@/lib/api';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { ScoreBandGraph } from '@/components/domain/results/score-band-graph';
import { ScoreConversionEvidence } from '@/components/domain/results/score-conversion-evidence';
import { TimeUsedSummary } from '@/components/domain/results/time-used-summary';
import { AnswerComparisonCard } from '@/components/domain/results/answer-comparison-card';
import { LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { cardClassName } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { PageSkeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';

interface MockResult {
  sessionId: string;
  rawScore: number;
  totalQuestions?: number | null;
  scaledScore: number | null;
  gradeLabel: string;
  scoreConversionTableVersionKey?: string | null;
  scoreConversionPassed?: boolean | null;
  durationSeconds?: number | null;
  partBreakdown?: Array<{
    partCode: string;
    rawScore: number;
    maxRawScore: number;
    correctCount: number;
    incorrectCount: number;
    unansweredCount: number;
    accuracyPercentage: number;
  }> | null;
  timeUsed?: {
    totalMilliseconds: number | null;
    sections: Array<{
      sectionCode: string;
      elapsedMilliseconds: number | null;
    }>;
  } | null;
  itemReview?: Array<{
    questionId: string;
    partCode: string;
    number: number;
    questionType: string;
    stem: string | null;
    learnerAnswer: string | null;
    correctAnswer: string | null;
    isCorrect: boolean;
    isUnanswered: boolean;
    pointsEarned: number;
    maxPoints: number;
    errorCategory: string | null;
    explanation: string | null;
    evidence: string | null;
    evidenceStartMilliseconds?: number | null;
    evidenceEndMilliseconds?: number | null;
  }> | null;
  errorSummary?: Array<{
    errorCategory: string;
    count: number;
    questionIds: string[];
  }> | null;
  nextStep?: {
    title: string;
    description: string;
    route: string;
  } | null;
  studyPlanRoute?: string | null;
}

export default function ListeningMockResultsPage() {
  const params = useParams<{ sessionId: string }>();
  const sessionId = params?.sessionId ?? '';
  const [result, setResult] = useState<MockResult | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    apiClient.get<MockResult | null>(`/v1/listening-pathway/mocks/sessions/${encodeURIComponent(sessionId)}/results`)
      .then((r) => {
        if (!cancelled) {
          setResult(r);
          setLoading(false);
        }
      })
      .catch(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [sessionId]);

  if (loading) {
    return (
      <div aria-busy="true">
        <p className="sr-only">Loading your mock results…</p>
        <PageSkeleton />
      </div>
    );
  }

  if (!result) {
    return (
      <EmptyState
        icon={<Hourglass className="h-8 w-8" aria-hidden />}
        title="Mock results not yet available"
        description="This mock session is being graded. Refresh in a moment, or come back from the dashboard."
        action={{ label: 'Back to dashboard', href: '/listening' }}
      />
    );
  }

  return (
    <>
      {(() => {
        const scaledScore = result.scaledScore;
        const maxRawScore = result.totalQuestions ?? 0;
        const hasConversion = result.totalQuestions === 42
          && scaledScore !== null
          && result.scoreConversionTableVersionKey != null
          && result.scoreConversionPassed != null;
        const gradeTone: 'success' | 'warning' | 'danger' | 'info' = !hasConversion
          ? 'info'
          : result.gradeLabel === 'A' || result.gradeLabel === 'B'
            ? 'success'
            : result.gradeLabel === 'C'
              ? 'warning'
              : 'danger';
        return (
          <>
            <MotionSection>
              <ResultsScorePanel
                eyebrow="Listening mock"
                icon={Headphones}
                title="Mock result"
                subtitle={hasConversion ? `Scaled OET Listening · Grade ${result.gradeLabel}` : 'Raw Listening practice result'}
                gaugeValue={hasConversion ? (scaledScore / 500) * 100 : 0}
                gaugeCenter={<span className="text-2xl font-black text-navy">{hasConversion ? result.gradeLabel : '—'}</span>}
                gaugeLabel={hasConversion ? `${scaledScore}/500` : 'Owner table unavailable'}
                gaugeColor={
                  !hasConversion ? 'var(--color-info)' : result.gradeLabel === 'A' || result.gradeLabel === 'B'
                    ? 'var(--color-success)'
                    : result.gradeLabel === 'C'
                      ? 'var(--color-warning)'
                      : 'var(--color-danger)'
                }
                grade={{
                  label: hasConversion ? `Grade ${result.gradeLabel}` : 'Scaled score unavailable',
                  tone: gradeTone,
                }}
                stats={[
                  { label: 'Scaled', value: hasConversion ? `${scaledScore}/500` : 'Unavailable', tone: 'info' },
                  { label: 'Raw score', value: `${result.rawScore}/${maxRawScore || 'unknown'}`, tone: 'default' },
                  {
                    label: 'Pass status',
                    value: hasConversion ? (result.scoreConversionPassed ? 'Passed' : 'Not passed') : 'Unavailable',
                    tone: !hasConversion ? 'info' : result.scoreConversionPassed ? 'success' : 'danger',
                  },
                  {
                    label: 'Grade',
                    value: hasConversion ? result.gradeLabel : '—',
                    tone: gradeTone,
                  },
                ]}
                chartSlot={(
                  <ScoreBandGraph
                    rawScore={result.rawScore}
                    maxRawScore={maxRawScore}
                    scaledScore={hasConversion ? scaledScore : null}
                    passed={hasConversion ? (result.scoreConversionPassed ?? null) : null}
                    grade={hasConversion ? result.gradeLabel : null}
                    tableVersion={hasConversion ? result.scoreConversionTableVersionKey : null}
                  />
                )}
              />
            </MotionSection>
            {/* The page's two disclosures, as one callout under the result they qualify. */}
            <InlineAlert
              variant="warning"
              live="polite"
              title="AI Practice Score — not an official OET result."
              data-testid="listening-marking-strictness-disclosure"
            >
              This platform grades minor spelling variations strictly to build exam-safe habits — some real OET examiners may allow minor variants at their discretion.
            </InlineAlert>
            <MotionSection>
              <ScoreConversionEvidence
                assessment="Listening"
                rawScore={result.rawScore}
                maxRawScore={maxRawScore}
                scaledScore={hasConversion ? scaledScore : null}
                passed={hasConversion ? (result.scoreConversionPassed ?? null) : null}
                grade={hasConversion ? result.gradeLabel : null}
                tableVersion={hasConversion ? result.scoreConversionTableVersionKey : null}
              />
            </MotionSection>
            <MotionSection>
              <TimeUsedSummary
                totalMilliseconds={result.timeUsed?.totalMilliseconds ?? (result.durationSeconds == null ? null : result.durationSeconds * 1000)}
                sections={(result.timeUsed?.sections ?? ['A1', 'A2', 'B', 'C1', 'C2'].map((sectionCode) => ({ sectionCode, elapsedMilliseconds: null }))).map((section) => ({
                  label: section.sectionCode,
                  milliseconds: section.elapsedMilliseconds,
                }))}
                description="Time is reported from persisted mock telemetry. Missing section telemetry is shown as not recorded."
              />
            </MotionSection>
            <MotionSection>
              <section className={cardClassName({})} aria-labelledby="listening-mock-part-breakdown-title">
                <h2 id="listening-mock-part-breakdown-title" className="text-base font-bold text-navy">Part breakdown</h2>
                {(result.partBreakdown ?? []).length > 0 ? (
                  <ul className="mt-3 divide-y divide-border">
                    {(result.partBreakdown ?? []).map((part) => (
                      <li key={part.partCode} className="py-3 last:pb-0">
                        <div className="flex items-center justify-between gap-3">
                          <span className="text-sm font-medium text-navy">{part.partCode}</span>
                          <span className="text-sm font-bold tabular-nums text-navy">{part.rawScore}/{part.maxRawScore}</span>
                        </div>
                        <p className="mt-1 text-xs tabular-nums text-muted">
                          {part.correctCount} correct | {part.incorrectCount} wrong | {part.unansweredCount} unanswered
                        </p>
                        <p className="mt-1 text-xs font-semibold tabular-nums text-primary">Accuracy {part.accuracyPercentage.toFixed(1)}%</p>
                      </li>
                    ))}
                  </ul>
                ) : (
                  <EmptyState
                    className="mt-3 py-6"
                    icon={<BarChart3 className="h-7 w-7" aria-hidden />}
                    title="No part telemetry recorded."
                  />
                )}
              </section>
            </MotionSection>
            <MotionSection>
              <section className={cardClassName({})} aria-labelledby="listening-mock-error-summary-title">
                <h2 id="listening-mock-error-summary-title" className="text-base font-bold text-navy">Error pattern summary</h2>
                {(result.errorSummary ?? []).length > 0 ? (
                  <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
                    {result.errorSummary!.map((summary) => (
                      <div key={summary.errorCategory} className="rounded-xl border border-border bg-background-light px-4 py-3">
                        <p className="text-sm font-semibold capitalize text-navy">{summary.errorCategory.replace(/_/g, ' ')}</p>
                        <p className="mt-1 text-xs tabular-nums text-muted">{summary.count} item{summary.count === 1 ? '' : 's'} · Questions {summary.questionIds.map((id) => result.itemReview?.find((item) => item.questionId === id)?.number ?? id).join(', ')}</p>
                      </div>
                    ))}
                  </div>
                ) : (
                  <EmptyState
                    className="mt-3 py-6"
                    icon={<ListChecks className="h-7 w-7" aria-hidden />}
                    title="No error patterns were recorded."
                  />
                )}
              </section>
            </MotionSection>
            {result.nextStep ? (
              <MotionSection>
                <section className={cn(cardClassName({}), 'border-primary/30 bg-primary/5')} aria-labelledby="listening-mock-next-step-title">
                  <h2 id="listening-mock-next-step-title" className="text-base font-bold text-navy">{result.nextStep.title}</h2>
                  <p className="mt-1 text-sm text-muted">{result.nextStep.description}</p>
                  <div className="mt-4 flex flex-wrap gap-3">
                    <Button asChild size="sm">
                      <Link href={result.nextStep.route}>
                        Open targeted practice <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden />
                      </Link>
                    </Button>
                    {result.studyPlanRoute ? (
                      <Button asChild size="sm" variant="outline">
                        <Link href={result.studyPlanRoute}>
                          Edit this plan <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden />
                        </Link>
                      </Button>
                    ) : null}
                  </div>
                </section>
              </MotionSection>
            ) : null}
            <section className="space-y-4" aria-label="Question-by-question review">
              <LearnerSurfaceSectionHeader
                title="Question-by-question review"
                description="Post-submit answers, marks, explanations, and transcript evidence from the persisted mock attempt."
              />
              {(result.itemReview ?? []).length > 0 ? result.itemReview!.map((item, index) => (
                <MotionItem key={item.questionId} delayIndex={Math.min(index, 5)}>
                  <AnswerComparisonCard
                    testId={`listening-mock-review-item-${item.questionId}`}
                    label={`Part ${item.partCode} · Question ${item.number}`}
                    stem={item.stem}
                    isCorrect={item.isCorrect}
                    unanswered={item.isUnanswered}
                    yourAnswer={item.learnerAnswer ?? 'No answer recorded'}
                    correctAnswer={item.correctAnswer}
                    pointsEarned={item.pointsEarned}
                    maxPoints={item.maxPoints}
                    missReason={item.errorCategory ? { title: `Result: ${item.errorCategory.replace(/_/g, ' ')}` } : null}
                    explanation={item.explanation ? <p>{item.explanation}</p> : null}
                  >
                    {item.evidence ? <div className="rounded-xl border border-info/30 bg-info/10 p-3 text-sm text-info">Evidence: {item.evidence}</div> : null}
                  </AnswerComparisonCard>
                </MotionItem>
              )) : (
                <EmptyState
                  icon={<ListChecks className="h-8 w-8" aria-hidden />}
                  title="No item review data is recorded for this mock."
                />
              )}
            </section>
          </>
        );
      })()}
      <div className="flex flex-wrap gap-3">
        <Button asChild size="sm">
          <Link href="/listening">Back to dashboard</Link>
        </Button>
        <Button asChild size="sm" variant="outline">
          <Link href="/listening/stats">See full analytics</Link>
        </Button>
      </div>
    </>
  );
}
