'use client';

import { Suspense, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { AnimatePresence, motion } from 'motion/react';
import { AlertCircle, AlertTriangle, ArrowRight, CheckCircle2, ChevronDown, ChevronUp, FileText, Headphones, Loader2, MinusCircle, Quote, Target, XCircle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { MotionCollapse, MotionItem, MotionList, MotionSection } from '@/components/ui/motion-primitives';
import { ResultsScorePanel } from '@/components/domain/results/results-score-panel';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { getListeningResult, type ListeningReviewDto } from '@/lib/listening-api';

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function ListeningResultsContent() {
  const params = useParams<{ id?: string | string[] }>();
  const id = firstParam(params?.id);
  const [result, setResult] = useState<ListeningReviewDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expandedItems, setExpandedItems] = useState<Record<string, boolean>>({});
  const [revealedTranscripts, setRevealedTranscripts] = useState<Record<string, boolean>>({});

  useEffect(() => {
    if (!id) return;
    let cancelled = false;

    const fetchWithRetry = async (): Promise<ListeningReviewDto | null> => {
      // The submit→navigate handoff can race the review endpoint when the
      // dev server is under load. A few short retries paper over the
      // fetch-cancellation that happens when the player unmounts mid-call
      // without forcing the learner to reload. Budget extends to ~30s to
      // cover cold-path AI grading and Next dev-server first-compile of
      // the result route under Playwright load.
      const delaysMs = [0, 250, 500, 1000, 2000, 3000, 5000, 8000, 10000];
      let lastErr: unknown = null;
      for (const delay of delaysMs) {
        if (cancelled) return null;
        if (delay > 0) await new Promise((r) => setTimeout(r, delay));
        try {
          return await getListeningResult(id);
        } catch (err) {
          lastErr = err;
        }
      }
      throw lastErr instanceof Error ? lastErr : new Error('Could not load Listening result.');
    };

    // The setState calls below kick off an async fetch lifecycle (loading →
    // result/error). This is the canonical React data-fetching pattern and
    // the React 19 set-state-in-effect rule's auto-fix would replace it with
    // useReducer, which is overkill here.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setLoading(true);
    setError(null);

    fetchWithRetry()
      .then((review) => {
        if (cancelled || !review) return;
        const expanded: Record<string, boolean> = {};
        review.itemReview.forEach((item) => {
          expanded[item.questionId] = !item.isCorrect;
        });
        setResult(review);
        setExpandedItems(expanded);
        analytics.track('evaluation_viewed', { subtest: 'listening', attemptId: id });
      })
      .catch((err) => {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load Listening result.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [id]);

  const toggleItem = (itemId: string) => {
    setExpandedItems((current) => ({ ...current, [itemId]: !current[itemId] }));
  };

  const toggleTranscript = (itemId: string, event: React.MouseEvent) => {
    event.stopPropagation();
    setRevealedTranscripts((current) => ({ ...current, [itemId]: !current[itemId] }));
  };

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Listening Results" backHref="/listening">
        <div className="space-y-6">
          <Skeleton className="h-64 rounded-2xl" />
          <Skeleton className="h-32 rounded-2xl" />
          <Skeleton className="h-48 rounded-2xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!result || error) {
    return (
      <LearnerDashboardShell pageTitle="Listening Results" backHref="/listening">
        <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-center">
          <AlertCircle className="h-12 w-12 text-danger" />
          <h2 className="text-xl font-black text-navy">Result not found</h2>
          <p className="max-w-md text-sm text-muted">{error ?? 'Complete a Listening task before opening results.'}</p>
          <Button variant="ghost" asChild>
<Link href="/listening">Back to Listening</Link>
</Button>
        </div>
      </LearnerDashboardShell>
    );
  }

  // A full OET Listening paper has 42 items (24 A + 6 B + 12 C). Anything
  // smaller is a drill / mini-test / starter — applying the official OET
  // grade + 350-pass threshold to those produces a misleading "Grade E ·
  // Below Threshold" because the 42-item scaling is hardwired. For non-full
  // papers show a practice-score frame (percent correct, no OET grade letter,
  // no pass/fail badge).
  const isFullOetPaper = result.maxRawScore >= 42;
  const percentCorrect = result.maxRawScore > 0
    ? Math.round((result.rawScore / result.maxRawScore) * 100)
    : 0;

  return (
    <LearnerDashboardShell pageTitle="Listening Results" subtitle={result.paper.title} backHref="/listening">
      <div className="space-y-5 sm:space-y-8 pb-24">
        <MotionSection>
          <ResultsScorePanel
            eyebrow="Listening result"
            icon={Headphones}
            title={result.paper.title}
            subtitle={isFullOetPaper
              ? result.scoreDisplay
              : `${result.rawScore}/${result.maxRawScore} correct · practice paper`}
            gaugeValue={percentCorrect}
            gaugeLabel="Accuracy"
            gaugeColor={isFullOetPaper ? (result.passed ? 'var(--color-success)' : 'var(--color-danger)') : 'var(--color-primary)'}
            grade={isFullOetPaper ? { label: `Grade ${result.grade}`, tone: result.passed ? 'success' : 'danger' } : null}
            stats={[
              { label: 'Correct', value: result.correctCount, tone: 'success', icon: <CheckCircle2 /> },
              { label: 'Incorrect', value: result.incorrectCount, tone: 'danger', icon: <XCircle /> },
              { label: 'Unanswered', value: result.unansweredCount, tone: 'warning', icon: <MinusCircle /> },
              {
                label: isFullOetPaper ? 'Scaled' : 'Raw',
                value: isFullOetPaper ? `${result.scaledScore}/500` : `${result.rawScore}/${result.maxRawScore}`,
                tone: 'info',
                icon: <Target />,
              },
            ]}
            aside={(
              <div className={`rounded-2xl border p-4 text-center ${
                isFullOetPaper
                  ? result.passed
                    ? 'border-success/20 bg-success/10 text-success'
                    : 'border-danger/20 bg-danger/10 text-danger'
                  : 'border-border bg-background-light text-navy'
              }`}>
                <p className="text-xs font-black uppercase tracking-widest">
                  {isFullOetPaper ? (result.passed ? 'Threshold met' : 'Below threshold') : 'Practice only'}
                </p>
                <p className="mt-1 text-xs leading-5 opacity-90">A pass on Listening is 30/42 (350/500, Grade B).</p>
              </div>
            )}
          />
        </MotionSection>

        {result.recommendedNextDrill ? (
          <MotionSection delayIndex={1}>
            <h2 className="mb-4 text-sm font-black uppercase tracking-widest text-muted">Recommended Next Step</h2>
            <Link href={result.recommendedNextDrill.launchRoute} className="group block rounded-2xl border border-primary/30 bg-primary/10 p-6 transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200 hover:border-primary/40 hover:shadow-md">
              <div className="flex items-start gap-4">
                <Target className="mt-1 h-6 w-6 shrink-0 text-primary" aria-hidden />
                <div className="flex-1">
                  <h3 className="mb-1 text-lg font-black text-primary">
                    {result.recommendedNextDrill.title}
                  </h3>
                  <p className="mb-4 text-sm leading-relaxed text-primary/80">
                    {result.recommendedNextDrill.description}
                  </p>
                  <span className="inline-flex items-center gap-2 rounded-xl bg-surface px-4 py-2 text-sm font-bold text-primary shadow-sm transition-colors group-hover:bg-primary group-hover:text-white">
                    Start Drill <ArrowRight className="h-4 w-4" />
                  </span>
                </div>
              </div>
            </Link>
          </MotionSection>
        ) : null}

        <MotionSection delayIndex={2}>
          <h2 className="mb-4 text-sm font-black uppercase tracking-widest text-muted">Detailed Review</h2>
          <MotionList className="space-y-4">
            {result.itemReview.map((item, index) => {
              const isExpanded = expandedItems[item.questionId];
              return (
                <MotionItem key={item.questionId} delayIndex={index} className="overflow-hidden rounded-2xl border border-border bg-surface shadow-sm">
                  <button
                    onClick={() => toggleItem(item.questionId)}
                    aria-expanded={isExpanded}
                    className="flex w-full items-start gap-4 p-5 text-left transition-colors hover:bg-background-light sm:p-6"
                  >
                    <div className="mt-0.5 shrink-0">
                      {item.isCorrect ? (
                        <CheckCircle2 className="h-6 w-6 text-success" />
                      ) : (
                        <XCircle className="h-6 w-6 text-danger" />
                      )}
                    </div>
                    <div className="flex-1 pr-4">
                      <span className="mb-1 block text-xs font-black uppercase tracking-widest text-muted">
                        Part {item.partCode} / Question {item.number}
                      </span>
                      <h3 className="text-base font-medium leading-relaxed text-navy">{item.prompt}</h3>
                    </div>
                    <div className="shrink-0 text-muted">
                      {isExpanded ? <ChevronUp className="h-5 w-5" /> : <ChevronDown className="h-5 w-5" />}
                    </div>
                  </button>

                  <MotionCollapse open={isExpanded} className="border-t border-border">
                        <div className="space-y-6 bg-background-light/50 p-5 sm:p-6">
                          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                            <div className={`rounded-xl border p-4 ${item.isCorrect ? 'border-success/30 bg-success/10' : 'border-danger/30 bg-danger/10'}`}>
                              <span className={`mb-2 block text-[10px] font-black uppercase tracking-widest ${item.isCorrect ? 'text-success' : 'text-danger'}`}>
                                Your Answer
                              </span>
                              <p className={`text-sm font-medium ${item.isCorrect ? 'text-success' : 'text-danger'}`}>
                                {item.learnerAnswer || 'No answer recorded'}
                              </p>
                            </div>
                            {!item.isCorrect ? (
                              <div className="rounded-xl border border-success/30 bg-success/10 p-4">
                                <span className="mb-2 block text-[10px] font-black uppercase tracking-widest text-success">
                                  Correct Answer
                                </span>
                                <p className="text-sm font-medium text-success">{item.correctAnswer}</p>
                              </div>
                            ) : null}
                          </div>

                          {!item.isCorrect && item.distractorExplanation ? (
                            <div className="flex items-start gap-3 rounded-xl border border-warning/30 bg-warning/10 p-4">
                              <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning" />
                              <div>
                                <span className="mb-1 block text-xs font-black uppercase tracking-widest text-warning">Distractor Trap</span>
                                <p className="text-sm leading-relaxed text-warning">{item.distractorExplanation}</p>
                              </div>
                            </div>
                          ) : null}

                          <div>
                            <span className="mb-2 block text-xs font-black uppercase tracking-widest text-muted">Explanation</span>
                            <p className="text-sm leading-relaxed text-muted">{item.explanation}</p>
                          </div>

                          {item.transcript?.allowed && item.transcript.excerpt ? (
                            <div className="pt-2">
                              {revealedTranscripts[item.questionId] ? (
                                <div className="relative rounded-xl border border-border bg-surface p-4">
                                  <Quote className="absolute left-2 top-2 h-8 w-8 text-primary/30" />
                                  <p className="relative z-10 pl-6 text-sm italic text-muted">
                                    {item.transcript.excerpt}
                                  </p>
                                  <button
                                    onClick={(event) => toggleTranscript(item.questionId, event)}
                                    className="mt-3 text-xs font-bold uppercase tracking-widest text-muted hover:text-navy"
                                  >
                                    Hide Transcript
                                  </button>
                                </div>
                              ) : (
                                <button
                                  onClick={(event) => toggleTranscript(item.questionId, event)}
                                  className="inline-flex items-center gap-2 rounded-lg bg-primary/5 px-4 py-2 text-sm font-bold text-primary transition-colors hover:bg-primary/10"
                                >
                                  <Quote className="h-4 w-4" /> Reveal Transcript Excerpt
                                </button>
                              )}
                            </div>
                          ) : null}
                        </div>
                  </MotionCollapse>
                </MotionItem>
              );
            })}
          </MotionList>
        </MotionSection>

        <section className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
          <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
            <div>
              <p className="text-xs font-black uppercase tracking-widest text-muted">Transcript Access</p>
              <p className="mt-2 text-sm text-muted">{result.transcriptAccess.reason}</p>
            </div>
            <Button variant="outline" className="gap-2" asChild>
<Link href={`/listening/review/${result.attemptId}`}>
                <FileText className="h-4 w-4" />
                Open Transcript Review
              </Link>
</Button>
          </div>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}

export default function ListeningResults() {
  return (
    <Suspense fallback={
      <LearnerDashboardShell pageTitle="Listening Results" backHref="/listening">
        <div className="flex flex-1 items-center justify-center">
          <Loader2 className="h-8 w-8 animate-spin text-primary" />
        </div>
      </LearnerDashboardShell>
    }>
      <ListeningResultsContent />
    </Suspense>
  );
}
