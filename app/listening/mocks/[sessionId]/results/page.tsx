'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { Headphones } from 'lucide-react';
import { apiClient } from '@/lib/api';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { ScoreBandGraph } from '@/components/domain/results/score-band-graph';
import { ScoreConversionEvidence } from '@/components/domain/results/score-conversion-evidence';
import { TimeUsedSummary } from '@/components/domain/results/time-used-summary';
import { AnswerComparisonCard } from '@/components/domain/results/answer-comparison-card';

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
      <main className="mx-auto max-w-3xl px-4 py-12">
        <p className="text-muted">Loading your mock results…</p>
      </main>
    );
  }

  if (!result) {
    return (
      <main className="mx-auto max-w-3xl px-4 py-12 space-y-4">
        <h1 className="text-2xl font-bold text-navy">Mock results not yet available</h1>
        <p className="text-muted">
          This mock session is being graded. Refresh in a moment, or come back from the dashboard.
        </p>
        <Link href="/listening" className="rounded-md bg-primary px-4 py-2 text-white text-sm inline-block transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600">
          Back to dashboard
        </Link>
      </main>
    );
  }

  return (
    <main className="mx-auto max-w-3xl px-4 py-12 space-y-6">
      <p className="rounded-lg border border-border bg-background-light px-4 py-3 text-sm text-muted dark:bg-slate-900/50">
        AI Practice Score — not an official OET result.
      </p>
      <p
        data-testid="listening-marking-strictness-disclosure"
        className="rounded-lg border border-border bg-background-light px-4 py-3 text-sm text-muted dark:bg-slate-900/50"
      >
        This platform grades minor spelling variations strictly to build exam-safe habits — some real OET examiners may allow minor variants at their discretion.
      </p>
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
      <ResultsScorePanel
        eyebrow="Listening mock"
        icon={Headphones}
        title="Mock result"
        subtitle={hasConversion ? `Scaled OET Listening · Grade ${result.gradeLabel}` : 'Raw Listening practice result'}
        gaugeValue={hasConversion ? (scaledScore / 500) * 100 : 0}
        gaugeCenter={<span className="text-2xl font-black text-navy dark:text-white">{hasConversion ? result.gradeLabel : '—'}</span>}
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
      <ScoreConversionEvidence
        assessment="Listening"
        rawScore={result.rawScore}
        maxRawScore={maxRawScore}
        scaledScore={hasConversion ? scaledScore : null}
        passed={hasConversion ? (result.scoreConversionPassed ?? null) : null}
        grade={hasConversion ? result.gradeLabel : null}
        tableVersion={hasConversion ? result.scoreConversionTableVersionKey : null}
      />
      <TimeUsedSummary
        totalMilliseconds={result.timeUsed?.totalMilliseconds ?? (result.durationSeconds == null ? null : result.durationSeconds * 1000)}
        sections={(result.timeUsed?.sections ?? ['A1', 'A2', 'B', 'C1', 'C2'].map((sectionCode) => ({ sectionCode, elapsedMilliseconds: null }))).map((section) => ({
          label: section.sectionCode,
          milliseconds: section.elapsedMilliseconds,
        }))}
        description="Time is reported from persisted mock telemetry. Missing section telemetry is shown as not recorded."
      />
      <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm" aria-labelledby="listening-mock-part-breakdown-title">
        <h2 id="listening-mock-part-breakdown-title" className="text-base font-black text-navy">Part breakdown</h2>
        <div className="mt-4 space-y-2">
          {(result.partBreakdown ?? []).map((part) => (
            <div key={part.partCode} className="rounded-lg border border-border px-4 py-3">
              <div className="flex items-center justify-between">
                <span className="text-sm font-medium text-navy">{part.partCode}</span>
                <span className="text-sm font-bold tabular-nums text-navy">{part.rawScore}/{part.maxRawScore}</span>
              </div>
              <p className="mt-1 text-xs text-muted">
                {part.correctCount} correct | {part.incorrectCount} wrong | {part.unansweredCount} unanswered
              </p>
              <p className="mt-1 text-xs font-semibold text-primary">Accuracy {part.accuracyPercentage.toFixed(1)}%</p>
            </div>
          ))}
          {(result.partBreakdown ?? []).length === 0 ? <p className="text-sm text-muted">No part telemetry recorded.</p> : null}
        </div>
      </section>
      <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm" aria-labelledby="listening-mock-error-summary-title">
        <h2 id="listening-mock-error-summary-title" className="text-base font-black text-navy">Error pattern summary</h2>
        {(result.errorSummary ?? []).length > 0 ? (
          <div className="mt-4 grid gap-3 sm:grid-cols-2">
            {result.errorSummary!.map((summary) => (
              <div key={summary.errorCategory} className="rounded-lg border border-border px-4 py-3">
                <p className="text-sm font-semibold capitalize text-navy">{summary.errorCategory.replace(/_/g, ' ')}</p>
                <p className="mt-1 text-xs text-muted">{summary.count} item{summary.count === 1 ? '' : 's'} · Questions {summary.questionIds.map((id) => result.itemReview?.find((item) => item.questionId === id)?.number ?? id).join(', ')}</p>
              </div>
            ))}
          </div>
        ) : <p className="mt-2 text-sm text-muted">No error patterns were recorded.</p>}
      </section>
      {result.nextStep ? (
        <section className="rounded-2xl border border-primary/30 bg-primary/10 p-5" aria-labelledby="listening-mock-next-step-title">
          <h2 id="listening-mock-next-step-title" className="text-base font-black text-primary">{result.nextStep.title}</h2>
          <p className="mt-1 text-sm text-primary/80">{result.nextStep.description}</p>
          <Link href={result.nextStep.route} className="mt-4 inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark">
            Open targeted practice <span aria-hidden>→</span>
          </Link>
          {result.studyPlanRoute ? (
            <Link href={result.studyPlanRoute} className="mt-4 ml-3 inline-flex items-center gap-2 rounded-lg border border-primary px-4 py-2 text-sm font-semibold text-primary hover:bg-primary/10">
              Edit this plan <span aria-hidden>→</span>
            </Link>
          ) : null}
        </section>
      ) : null}
      <section className="space-y-4" aria-labelledby="listening-mock-item-review-title">
        <div>
          <h2 id="listening-mock-item-review-title" className="text-base font-black text-navy">Question-by-question review</h2>
          <p className="mt-1 text-sm text-muted">Post-submit answers, marks, explanations, and transcript evidence from the persisted mock attempt.</p>
        </div>
        {(result.itemReview ?? []).length > 0 ? result.itemReview!.map((item) => (
          <AnswerComparisonCard
            key={item.questionId}
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
        )) : <p className="rounded-xl border border-border bg-surface px-4 py-3 text-sm text-muted">No item review data is recorded for this mock.</p>}
      </section>
          </>
        );
      })()}
      <nav className="flex flex-wrap gap-3 text-sm">
        <Link href="/listening" className="rounded-md bg-primary px-4 py-2 text-white transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600">
          Back to dashboard
        </Link>
        <Link
          href="/listening/stats"
          className="rounded-md border border-border px-4 py-2 text-navy transition-colors hover:bg-background-light"
        >
          See full analytics
        </Link>
      </nav>
    </main>
  );
}
