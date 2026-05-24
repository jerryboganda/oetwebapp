'use client';

import { Suspense, use, useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { BookOpen, CheckCircle2, FileText, RefreshCw, Target, XCircle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { SafeRichText } from '@/components/domain/grammar/grammar-content-renderer';
import { getReadingAttemptReview, type ReadingAttemptReviewDto } from '@/lib/reading-authoring-api';
import { completeMockSection } from '@/lib/api';
import { isListeningReadingPassByScaled } from '@/lib/scoring';
import { readErrorMessage } from '@/lib/read-error-message';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';

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
  const [review, setReview] = useState<ReadingAttemptReviewDto | null>(null);
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
        if (!cancelled) setReview(data);
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

  return (
    <LearnerDashboardShell pageTitle="Reading Results" backHref="/reading">
      <main className="space-y-8">
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
              <p className="mt-2 text-xs text-red-600">{mockRetryError}</p>
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

            <section className="grid grid-cols-1 gap-6 lg:grid-cols-3">
              <LearnerSurfaceCard card={scoreCard(review, passed, isPracticeOnly)} />
              <LearnerSurfaceCard card={nextActionCard(passed, isPracticeOnly)} />
              <LearnerSurfaceCard card={policyCard(review)} />
            </section>

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
                    <p className="mt-2 text-2xl font-semibold text-navy">{part.rawScore}/{part.maxRawScore}</p>
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
                description="Correct answers and explanations appear once review is unlocked for this attempt."
                className="mb-5"
              />
              <div className="space-y-3">
                {review.items.map((item) => (
                  <details key={item.questionId} className="rounded-[16px] border border-border bg-surface p-4 shadow-sm">
                    <summary className="cursor-pointer list-none">
                      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                        <div>
                          <p className="text-sm font-bold text-navy">Part {item.partCode} | Question {item.displayOrder}</p>
                          <p className="mt-1 text-sm text-muted">{item.stem}</p>
                        </div>
                        <div className="flex flex-wrap items-center gap-2">
                          <Badge variant={item.isCorrect ? 'success' : 'danger'}>{item.isCorrect ? 'Correct' : 'Review'}</Badge>
                          <Badge variant="muted">{item.pointsEarned}/{item.maxPoints}</Badge>
                        </div>
                      </div>
                    </summary>
                    <div className="mt-4 grid grid-cols-1 gap-3 text-sm md:grid-cols-2">
                      <ReviewValue label="Your answer" value={item.userAnswer} />
                      <ReviewValue label="Correct answer" value={item.correctAnswer ?? 'Hidden by review policy'} />
                    </div>
                    {item.explanationMarkdown ? (
                      <div className="mt-4 rounded-xl bg-background-light p-3">
                        <SafeRichText markdown={item.explanationMarkdown} className="text-sm leading-6 text-muted" />
                      </div>
                    ) : null}
                  </details>
                ))}
              </div>
            </section>
          </>
        ) : null}
      </main>
    </LearnerDashboardShell>
  );
}

function scoreCard(review: ReadingAttemptReviewDto, passed: boolean, isPracticeOnly: boolean): LearnerSurfaceCardModel {
  return {
    kind: 'evidence',
    sourceType: 'backend_summary',
    accent: isPracticeOnly ? 'blue' : passed ? 'emerald' : 'amber',
    eyebrow: 'Score',
    eyebrowIcon: isPracticeOnly ? BookOpen : passed ? CheckCircle2 : Target,
    title: `${review.attempt.rawScore ?? 0}/${review.attempt.maxRawScore} raw`,
    description: isPracticeOnly
      ? 'Practice-only subset | No OET grade'
      : `${review.attempt.scaledScore ?? 0}/500 | Grade ${review.attempt.gradeLetter}`,
    metaItems: [
      { icon: FileText, label: isPracticeOnly ? 'Subset practice' : passed ? 'Pass anchor met' : 'Needs 30/42' },
    ],
  };
}

function nextActionCard(passed: boolean, isPracticeOnly: boolean): LearnerSurfaceCardModel {
  return {
    kind: 'navigation',
    sourceType: 'frontend_navigation',
    accent: 'blue',
    eyebrow: 'Next Action',
    eyebrowIcon: BookOpen,
    title: isPracticeOnly ? 'Keep sharpening this cluster' : passed ? 'Validate in a mock' : 'Repeat focused Reading practice',
    description: isPracticeOnly
      ? 'Use the reviewed items to repeat the same skill, then move back to a full Reading paper.'
      : passed
      ? 'Use a full mock to confirm the Reading gain transfers under cross-subtest pressure.'
      : 'Review missed clusters, then start another structured Reading paper.',
    primaryAction: {
      label: !isPracticeOnly && passed ? 'Enter Mock Setup' : 'Back to Reading',
      href: !isPracticeOnly && passed ? '/mocks' : '/reading',
    },
  };
}

function policyCard(review: ReadingAttemptReviewDto): LearnerSurfaceCardModel {
  return {
    kind: 'status',
    sourceType: 'backend_summary',
    accent: 'slate',
    eyebrow: 'Review Policy',
    eyebrowIcon: FileText,
    title: review.policy.showCorrectAnswerOnReview ? 'Answers visible' : 'Answers hidden',
    description: review.policy.showExplanationsAfterSubmit
      ? 'Explanations are available according to the policy snapshot for this attempt.'
      : 'Explanations are hidden for this attempt by policy.',
    metaItems: [
      { icon: Target, label: review.policy.showExplanationsOnlyIfWrong ? 'Wrong-only explanations' : 'Policy-redacted review' },
    ],
  };
}

function ReviewValue({ label, value }: { label: string; value: unknown }) {
  return (
    <div className="rounded-xl bg-background-light p-3">
      <p className="text-xs font-black uppercase tracking-[0.16em] text-muted">{label}</p>
      <p className="mt-1 break-words font-semibold text-navy">{formatReviewValue(value)}</p>
    </div>
  );
}

function formatReviewValue(value: unknown): string {
  if (value === null || value === undefined || value === '') return 'No answer';
  if (Array.isArray(value)) return value.join(', ');
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

