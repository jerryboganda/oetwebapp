'use client';

import { Suspense, use, useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { BookOpen, CheckCircle2, FileText, MessageSquare, MinusCircle, RefreshCw, Target, XCircle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MarkdownContent } from '@/components/ui/markdown-content';
import { AnswerComparisonCard } from '@/components/domain/results/answer-comparison-card';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { formatAnswerValue } from '@/lib/results/format-answer';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import {
  getReadingAttemptReview,
  getReadingPaperAnnotations,
  getReadingStructureLearner,
  type ReadingAttemptReviewDto,
  type ReadingLearnerStructureDto,
  type ReadingPaperAnnotationDto,
} from '@/lib/reading-authoring-api';
import { ReadingPdfViewer } from '@/components/domain/reading-pdf-viewer';
import { completeMockSection } from '@/lib/api';
import { isListeningReadingPassByScaled } from '@/lib/scoring';
import { readErrorMessage } from '@/lib/read-error-message';

/**
 * The backend post-submit review payload returns a few fields that are not
 * yet in the shared {@link ReadingAttemptReviewDto} (policy-gated answer keys,
 * timing, distractor + miss diagnostics, and tutor feedback). We extend the
 * shape locally and only ever render a field when the API actually supplies
 * it — never fabricated. Answer keys / explanations are gated server-side.
 */
type ReviewItem = ReadingAttemptReviewDto['items'][number] & {
  correctAnswer?: string | null;
  explanationMarkdown?: string | null;
  selectedDistractorCategory?: string | null;
  missReason?: string | null;
  elapsedMs?: number | null;
  totalElapsedMs?: number | null;
  boxExplanations?: Record<string, string> | null;
};

type PartBreakdownEntry = ReadingAttemptReviewDto['partBreakdown'][number] & {
  accuracyPercent?: number | null;
};

interface TutorFeedbackEntry {
  id: string;
  scope: string;
  targetRef: string | null;
  feedbackText: string;
  createdAt: string;
  updatedAt: string;
}

type ReadingReviewData = Omit<ReadingAttemptReviewDto, 'items' | 'partBreakdown'> & {
  items: ReviewItem[];
  partBreakdown: PartBreakdownEntry[];
  feedback?: TutorFeedbackEntry[];
};

interface PendingMockCompletion {
  mockAttemptId: string;
  mockSectionId: string;
  rawScore: number;
  rawScoreMax: number;
  scaledScore: number | null;
  grade: string;
}

export default function ReadingPaperResultsPage({ params }: { params: Promise<{ paperId: string }> }) {
  return (
    <Suspense fallback={<LearnerDashboardShell pageTitle="Reading Results"><Skeleton className="h-64" /></LearnerDashboardShell>}>
      <ReadingPaperResultsContent params={params} />
    </Suspense>
  );
}

function ReadingPaperResultsContent({ params }: { params: Promise<{ paperId: string }> }) {
  const { paperId } = use(params);
  const search = useSearchParams();
  const attemptId = search?.get('attemptId') ?? '';
  const [review, setReview] = useState<ReadingReviewData | null>(null);
  const [structure, setStructure] = useState<ReadingLearnerStructureDto | null>(null);
  const [pdfAnnotations, setPdfAnnotations] = useState<ReadingPaperAnnotationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [errorBankToast, setErrorBankToast] = useState<string | null>(null);

  useEffect(() => {
    if (!attemptId) {
      setError('Missing Reading attempt.');
      setLoading(false);
      return;
    }

    let cancelled = false;
    (async () => {
      try {
        setLoading(true);
        setError(null);
        const data = await getReadingAttemptReview(attemptId);
        const [loadedStructure, loadedAnnotations] = await Promise.all([
          getReadingStructureLearner(paperId).catch(() => null),
          getReadingPaperAnnotations(paperId).catch(() => [] as ReadingPaperAnnotationDto[]),
        ]);
        if (!cancelled) {
          setReview(data as ReadingReviewData);
          setStructure(loadedStructure);
          setPdfAnnotations(loadedAnnotations);
        }
      } catch (err) {
        if (!cancelled) setError(readErrorMessage(err, 'Failed to load Reading review.'));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [attemptId]);

  useEffect(() => {
    if (!review || typeof window === 'undefined') return;
    const scrollToHash = () => {
      const targetId = window.location.hash.replace(/^#/, '');
      if (!targetId) return;

      window.requestAnimationFrame(() => {
        document.getElementById(targetId)?.scrollIntoView({ block: 'start' });
      });
    };

    scrollToHash();
    window.addEventListener('hashchange', scrollToHash);
    return () => window.removeEventListener('hashchange', scrollToHash);
  }, [review]);

  useEffect(() => {
    if (!review) return;
    // TODO: wire up when API returns errorBankCleared count on review items
    // const cleared = review.items.filter(i => i.errorBankCleared).length;
    // if (cleared > 0) setErrorBankToast(`✓ ${cleared} error bank ${cleared === 1 ? 'entry' : 'entries'} resolved`);
  }, [review]);

  /**
   * Phase 6 closure — detect a pending mock-section-complete marker the
   * player wrote to sessionStorage on failure, and surface a retry CTA
   * here so the learner can re-fire the write without losing context.
   */
  const [pendingMockCompletion, setPendingMockCompletion] = useState<PendingMockCompletion | null>(null);
  const [mockRetryState, setMockRetryState] = useState<'idle' | 'retrying' | 'done' | 'error'>('idle');
  const [mockRetryError, setMockRetryError] = useState<string | null>(null);

  useEffect(() => {
    if (!attemptId || typeof window === 'undefined') return;
    try {
      const raw = window.sessionStorage.getItem(`oet-mock-section-complete-pending:${attemptId}`);
      if (!raw) return;
      const parsed = JSON.parse(raw) as PendingMockCompletion;
      if (parsed && typeof parsed.mockAttemptId === 'string' && typeof parsed.mockSectionId === 'string') {
        setPendingMockCompletion(parsed);
      }
    } catch { /* ignore corrupt marker */ }
  }, [attemptId]);

  const handleRetryMockCompletion = useCallback(async () => {
    if (!pendingMockCompletion || !attemptId) return;
    setMockRetryState('retrying');
    setMockRetryError(null);
    try {
      await completeMockSection(pendingMockCompletion.mockAttemptId, pendingMockCompletion.mockSectionId, {
        contentAttemptId: attemptId,
        rawScore: pendingMockCompletion.rawScore,
        rawScoreMax: pendingMockCompletion.rawScoreMax,
        scaledScore: pendingMockCompletion.scaledScore,
        grade: pendingMockCompletion.grade,
        evidence: { source: 'reading_results_retry' },
      });
      if (typeof window !== 'undefined') {
        window.sessionStorage.removeItem(`oet-mock-section-complete-pending:${attemptId}`);
      }
      setMockRetryState('done');
      setPendingMockCompletion(null);
    } catch (err) {
      setMockRetryState('error');
      setMockRetryError(err instanceof Error ? err.message : 'Retry failed.');
    }
  }, [attemptId, pendingMockCompletion]);

  const raw = review?.attempt.rawScore ?? 0;
  const scaled = review?.attempt.scaledScore ?? null;
  const isPracticeOnly = scaled === null;
  const passed = !isPracticeOnly && typeof scaled === 'number' && isListeningReadingPassByScaled(scaled);

  const partTotals = (review?.partBreakdown ?? []).reduce(
    (acc, part) => ({
      correct: acc.correct + part.correctCount,
      incorrect: acc.incorrect + part.incorrectCount,
      unanswered: acc.unanswered + part.unansweredCount,
    }),
    { correct: 0, incorrect: 0, unanswered: 0 },
  );
  const gradedItems = partTotals.correct + partTotals.incorrect + partTotals.unanswered;
  const accuracyPct = gradedItems > 0 ? (partTotals.correct / gradedItems) * 100 : 0;
  const nextAction = !isPracticeOnly && passed
    ? { label: 'Enter Mock Setup', href: '/mocks', title: 'Validate in a mock', desc: 'Confirm the Reading gain transfers under full-exam pressure.' }
    : {
        label: 'Back to Reading',
        href: '/reading',
        title: isPracticeOnly ? 'Keep sharpening this cluster' : 'Repeat focused Reading practice',
        desc: isPracticeOnly
          ? 'Repeat the same skill, then return to a full Reading paper.'
          : 'Review missed clusters, then start another structured Reading paper.',
      };

  return (
    <LearnerDashboardShell pageTitle="Reading Results" backHref="/reading">
      <main className="space-y-5 sm:space-y-8">
        {loading ? <Skeleton className="h-96" /> : null}
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {pendingMockCompletion ? (
          <InlineAlert variant="warning">
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <p className="text-sm">
                Your Reading attempt is graded, but the mock dashboard did not
                receive the score on the first try. Click <strong>Retry mock
                completion</strong> to send it again.
              </p>
              <div className="flex items-center gap-2">
                <Button
                  type="button"
                  variant="primary"
                  size="sm"
                  onClick={() => void handleRetryMockCompletion()}
                  disabled={mockRetryState === 'retrying'}
                >
                  <RefreshCw className="mr-1.5 h-4 w-4" aria-hidden />
                  {mockRetryState === 'retrying' ? 'Retrying…' : 'Retry mock completion'}
                </Button>
                <Link
                  href={`/mocks/player/${pendingMockCompletion.mockAttemptId}`}
                  className="text-xs font-semibold text-primary hover:underline"
                >
                  Open mock dashboard
                </Link>
              </div>
            </div>
            {mockRetryError ? (
              <p className="mt-2 text-xs text-danger">{mockRetryError}</p>
            ) : null}
          </InlineAlert>
        ) : null}
        {mockRetryState === 'done' ? (
          <InlineAlert variant="success">
            Mock section marked complete. You can return to the mock dashboard.
          </InlineAlert>
        ) : null}

        {errorBankToast ? (
          <Toast message={errorBankToast} variant="success" onClose={() => setErrorBankToast(null)} />
        ) : null}

        {review ? (
          <>
            <LearnerPageHero
              eyebrow="Reading Review"
              icon={BookOpen}
              accent={isPracticeOnly ? 'blue' : passed ? 'emerald' : 'amber'}
              title={review.paper.title}
              description={isPracticeOnly
                ? `${raw}/${review.attempt.maxRawScore} practice marks`
                : `${raw}/${review.attempt.maxRawScore} raw | ${scaled}/500 scaled`}
              highlights={[
                { icon: Target, label: 'Raw score', value: `${raw}/${review.attempt.maxRawScore}` },
                { icon: FileText, label: 'Scaled score', value: isPracticeOnly ? 'Practice only' : `${scaled}/500` },
                { icon: isPracticeOnly ? BookOpen : passed ? CheckCircle2 : XCircle, label: 'Grade', value: isPracticeOnly ? 'No OET grade' : `Grade ${review.attempt.gradeLetter}` },
              ]}
              aside={(
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <Badge variant={isPracticeOnly ? 'info' : passed ? 'success' : 'warning'}>
                    {isPracticeOnly ? 'Practice-only review' : passed ? 'Reading pass evidence' : 'Below Reading pass anchor'}
                  </Badge>
                  <p className="mt-3 text-sm leading-6 text-muted">
                    {isPracticeOnly
                      ? 'This subset attempt reports practice marks only. Use the item review to choose the next focused route.'
                      : 'Reading pass evidence is anchored to 30/42 equalling 350/500. Use the item review to choose the next practice route.'}
                  </p>
                  {!isPracticeOnly ? (
                    <p
                      className="mt-3 border-t border-border pt-3 text-xs font-semibold leading-5 text-muted"
                      data-testid="reading-results-estimate-disclaimer"
                    >
                      This is an estimate, not an official OET conversion.
                    </p>
                  ) : null}
                </div>
              )}
            />

            <ResultsScorePanel
              eyebrow="Reading review"
              icon={BookOpen}
              title={review.paper.title}
              subtitle={isPracticeOnly
                ? `${raw}/${review.attempt.maxRawScore} practice marks`
                : `${scaled}/500 scaled · Grade ${review.attempt.gradeLetter}`}
              gaugeValue={accuracyPct}
              gaugeLabel="Accuracy"
              gaugeColor={isPracticeOnly ? 'var(--color-primary)' : passed ? 'var(--color-success)' : 'var(--color-warning)'}
              grade={isPracticeOnly ? null : { label: `Grade ${review.attempt.gradeLetter}`, tone: passed ? 'success' : 'warning' }}
              stats={[
                { label: 'Correct', value: partTotals.correct, tone: 'success', icon: <CheckCircle2 /> },
                { label: 'Incorrect', value: partTotals.incorrect, tone: 'danger', icon: <XCircle /> },
                { label: 'Unanswered', value: partTotals.unanswered, tone: 'warning', icon: <MinusCircle /> },
                {
                  label: isPracticeOnly ? 'Raw score' : 'Scaled',
                  value: isPracticeOnly ? `${raw}/${review.attempt.maxRawScore}` : `${scaled}/500`,
                  tone: 'info',
                  icon: <Target />,
                },
              ]}
              aside={(
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-sm font-semibold text-navy dark:text-white">{nextAction.title}</p>
                  <p className="mt-1 text-xs leading-5 text-muted">{nextAction.desc}</p>
                  <Button asChild variant="primary" size="sm" className="mt-3">
                    <Link href={nextAction.href}>{nextAction.label}</Link>
                  </Button>
                </div>
              )}
            />

            {structure?.paper.questionPaperAssets?.length ? (
              <section id="pdf-review">
                <LearnerSurfaceSectionHeader
                  eyebrow="Original PDFs"
                  title="Your saved highlights"
                  description="The uploaded PDFs are shown read-only here. Highlights are your paper-level annotations and carry across attempts."
                  className="mb-5"
                />
                <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
                  {(['A', 'B', 'C'] as const).map((partCode) => (
                    <ReadingPdfViewer
                      key={partCode}
                      paperId={paperId}
                      partCode={partCode}
                      assets={structure.paper.questionPaperAssets ?? []}
                      annotations={pdfAnnotations}
                      readOnly
                    />
                  ))}
                </div>
              </section>
            ) : null}

            <section id="part-breakdown">
              <LearnerSurfaceSectionHeader
                eyebrow="Part Breakdown"
                title="Score by Reading section"
                description="Use the A/B/C split to decide whether speed, workplace extracts, or long-text inference needs the next practice block."
                className="mb-5"
              />
              <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
                {review.partBreakdown.map((part) => (
                  <div key={part.partCode} className="rounded-[20px] border border-border bg-surface p-5 shadow-sm">
                    <p className="text-sm font-black uppercase tracking-[0.16em] text-muted">Part {part.partCode}</p>
                    <div className="mt-2 flex items-baseline gap-2">
                      <p className="text-2xl font-semibold text-navy">{part.rawScore}/{part.maxRawScore}</p>
                      {typeof part.accuracyPercent === 'number' ? (
                        <span
                          className="text-sm font-bold text-primary"
                          data-testid={`reading-part-accuracy-${part.partCode}`}
                        >
                          {formatPercent(part.accuracyPercent)} correct
                        </span>
                      ) : null}
                    </div>
                    <p className="mt-1 text-sm text-muted">
                      {part.correctCount} correct | {part.incorrectCount} wrong | {part.unansweredCount} unanswered
                    </p>
                  </div>
                ))}
              </div>
            </section>

            <section id="skill-breakdown">
              <LearnerSurfaceSectionHeader
                eyebrow="Skill Breakdown"
                title="Skill-level pattern"
                description="Grouped by authored skill tag where available, otherwise by question type."
                className="mb-5"
              />
              <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                {review.skillBreakdown.map((skill) => (
                  <div key={skill.label} className="rounded-[20px] border border-border bg-surface p-5 shadow-sm">
                    <div className="flex items-center justify-between gap-3">
                      <p className="text-sm font-black uppercase tracking-[0.16em] text-muted">{skill.label}</p>
                      <Badge variant="muted">{skill.totalCount} item(s)</Badge>
                    </div>
                    <p className="mt-2 text-sm text-muted">
                      {skill.correctCount} correct | {skill.incorrectCount} wrong | {skill.unansweredCount} unanswered
                    </p>
                  </div>
                ))}
              </div>
            </section>

            <section id="item-review">
              <LearnerSurfaceSectionHeader
                eyebrow="Error Clusters"
                title="Where marks were lost"
                description="Clusters are grouped by authored skill tag first, then by question type."
                className="mb-5"
              />
              {review.clusters.length ? (
                <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
                  {review.clusters.map((cluster) => (
                    <div key={cluster.label} className="rounded-[20px] border border-border bg-surface p-5 shadow-sm">
                      <p className="text-sm font-black uppercase tracking-[0.16em] text-muted">{cluster.label}</p>
                      <p className="mt-2 text-2xl font-semibold text-navy">{cluster.incorrectCount} missed</p>
                      <p className="mt-1 text-sm text-muted">Questions {cluster.questions.map((question) => question.label).join(', ')}</p>
                    </div>
                  ))}
                </div>
              ) : (
                <div className="rounded-[20px] border border-border bg-surface p-5 text-sm font-semibold text-muted shadow-sm">
                  No incorrect clusters on this attempt.
                </div>
              )}
            </section>

            <section>
              <LearnerSurfaceSectionHeader
                eyebrow="Item Review"
                title="Question-by-question review"
                description="Each question shows your answer beside the correct answer, colour-coded green for correct and red for incorrect, with explanations and miss diagnostics."
                className="mb-5"
              />
              <div className="space-y-3">
                {review.items.map((item) => (
                  <ReviewItemDetails key={item.questionId} item={item} />
                ))}
              </div>
            </section>

            {review.feedback && review.feedback.length ? (
              <section id="tutor-feedback">
                <LearnerSurfaceSectionHeader
                  eyebrow="Tutor Feedback"
                  title="Notes from your reviewer"
                  description="Feedback left by an expert or admin on this attempt, scoped to the whole test, a section, a skill, or a single question."
                  className="mb-5"
                />
                <div className="space-y-3">
                  {review.feedback.map((entry) => (
                    <div
                      key={entry.id}
                      className="rounded-[16px] border border-border bg-surface p-4 shadow-sm"
                      data-testid="reading-tutor-feedback"
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <Badge variant="info" className="inline-flex items-center gap-1">
                          <MessageSquare className="h-3.5 w-3.5" aria-hidden />
                          {feedbackScopeLabel(entry.scope, entry.targetRef)}
                        </Badge>
                      </div>
                      <p className="mt-2 whitespace-pre-wrap text-sm leading-6 text-navy">{entry.feedbackText}</p>
                    </div>
                  ))}
                </div>
              </section>
            ) : null}
          </>
        ) : null}
      </main>
    </LearnerDashboardShell>
  );
}

function ReviewItemDetails({ item }: { item: ReviewItem }) {
  const missReason = !item.isCorrect && item.missReason ? missReasonLabel(item.missReason) : null;
  const distractor = !item.isCorrect && item.selectedDistractorCategory
    ? distractorCategoryLabel(item.selectedDistractorCategory)
    : null;
  const unanswered =
    item.userAnswer === null ||
    item.userAnswer === undefined ||
    item.userAnswer === '' ||
    (Array.isArray(item.userAnswer) && item.userAnswer.length === 0);
  const timeMs = typeof item.totalElapsedMs === 'number' && item.totalElapsedMs > 0
    ? item.totalElapsedMs
    : typeof item.elapsedMs === 'number' && item.elapsedMs > 0
      ? item.elapsedMs
      : null;

  return (
    <AnswerComparisonCard
      testId={`reading-review-item-${item.questionId}`}
      label={`Part ${item.partCode} · Question ${item.displayOrder}`}
      stem={item.stem}
      isCorrect={item.isCorrect}
      unanswered={!item.isCorrect && unanswered}
      yourAnswer={formatAnswerValue(item.userAnswer)}
      correctAnswer={item.correctAnswer ?? null}
      pointsEarned={item.pointsEarned}
      maxPoints={item.maxPoints}
      timeMs={timeMs}
      missReason={missReason}
      distractor={distractor}
      explanation={item.explanationMarkdown ? (
        <MarkdownContent markdown={item.explanationMarkdown} className="text-sm leading-6 text-navy dark:text-white/90" />
      ) : null}
    >
      {item.boxExplanations && Object.keys(item.boxExplanations).length > 0 ? (
        <div className="space-y-2">
          {Object.entries(item.boxExplanations).map(([key, text]) => (
            <div
              key={key}
              className="rounded-xl border border-border bg-background-light p-3"
              data-testid={`reading-box-explanation-${key}`}
            >
              <p className="text-[11px] font-black uppercase tracking-[0.14em] text-muted">Explanation ({key})</p>
              <p className="mt-1 text-sm leading-6 text-navy dark:text-white">{text}</p>
            </div>
          ))}
        </div>
      ) : null}
    </AnswerComparisonCard>
  );
}

function formatPercent(value: number): string {
  return Number.isInteger(value) ? `${value}%` : `${value.toFixed(1)}%`;
}

function missReasonLabel(reason: string): { title: string; detail?: string } {
  switch (reason) {
    case 'spelling':
      return {
        title: 'Spelling caused this miss',
        detail: 'Your answer matched the meaning but the spelling did not match the accepted key. Reading answers are graded on exact spelling.',
      };
    case 'incomplete':
      return { title: 'Incomplete answer', detail: 'Part of the required answer was missing.' };
    case 'distractor':
      return { title: 'Chose a distractor', detail: 'The option you selected was a deliberate trap rather than the correct answer.' };
    case 'wrong_text':
      return { title: 'Answer from the wrong text', detail: 'Your answer came from a different part or text than the question asked about.' };
    case 'blank':
      return { title: 'Left blank', detail: 'No answer was recorded for this question.' };
    case 'wrong':
      return { title: 'Incorrect answer' };
    default:
      return { title: 'Needs review' };
  }
}

function distractorCategoryLabel(category: string): string {
  switch (category) {
    case 'Opposite':
      return 'Opposite meaning';
    case 'TooBroad':
      return 'Too broad / over-generalised';
    case 'TooSpecific':
      return 'Too specific';
    case 'WrongSpeaker':
      return 'Wrong speaker or source';
    case 'NotInText':
      return 'Not in the text';
    case 'DistortedDetail':
      return 'Distorted detail';
    case 'OutOfScope':
      return 'Out of scope / off topic';
    default:
      return category;
  }
}

function feedbackScopeLabel(scope: string, targetRef: string | null): string {
  switch (scope) {
    case 'test':
      return 'Whole test';
    case 'section':
      return targetRef ? `Section ${targetRef}` : 'Section';
    case 'skill':
      return targetRef ? `Skill: ${targetRef}` : 'Skill';
    case 'question':
      return targetRef ? `Question ${targetRef}` : 'Question';
    default:
      return scope;
  }
}
