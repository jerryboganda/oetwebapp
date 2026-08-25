'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowLeft, Clock, Headphones, ListChecks, Lock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { LearnerPageHero } from '@/components/domain';
import {
  ContentLockedNotice,
  isContentLockedError,
  readContentLockedMessage,
} from '@/components/domain/ContentLockedNotice';
import { analytics } from '@/lib/analytics';
import {
  getListeningHome,
  startListeningAttempt,
  type ListeningHomeDto,
  type ListeningHomePaperDto,
} from '@/lib/listening-api';
import { ListeningExamFolderBrowser } from '@/components/domain/listening/listening-exam-folder-browser';
import { readErrorMessage } from '@/lib/read-error-message';
import {
  InsufficientCreditsModal,
  isInsufficientCreditsError,
  readInsufficientCreditsMessage,
} from '@/components/domain/InsufficientCreditsModal';
import { showCreditFeedback } from '@/lib/credit-feedback';

function isPaperAllowed(paper: ListeningHomePaperDto): boolean {
  return paper.requiresSubscription !== true;
}

function isPartialListeningExam(paper: Pick<ListeningHomePaperDto, 'questionCount' | 'title'>) {
  return (
    paper.questionCount !== 42
    || paper.title.includes('Q37–42 unavailable')
    || paper.title.includes('Q37-42 unavailable')
  );
}

export default function ListeningFullExamPage() {
  const router = useRouter();
  const [home, setHome] = useState<ListeningHomeDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [startingPaperId, setStartingPaperId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [lockedMessage, setLockedMessage] = useState<string | null>(null);
  const [insufficientCreditsMessage, setInsufficientCreditsMessage] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { page: 'listening-full-exam' });
    let cancelled = false;
    getListeningHome()
      .then((value) => {
        if (!cancelled) {
          setHome(value);
          setLoading(false);
        }
      })
      .catch((caught) => {
        if (!cancelled) {
          setError(readErrorMessage(caught, 'Could not load Listening exams.'));
          setLoading(false);
        }
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const activeByPaper = useMemo(() => {
    const map = new Map<string, string>();
    for (const attempt of home?.activeAttempts ?? []) {
      if (attempt.mode === 'exam' || attempt.mode === 'home') {
        map.set(attempt.paperId, `/listening/paper/${attempt.paperId}?attemptId=${attempt.attemptId}`);
      }
    }
    return map;
  }, [home?.activeAttempts]);

  async function handleStart(paper: ListeningHomePaperDto) {
    const resumeRoute = activeByPaper.get(paper.id)
      ?? (paper.lastAttempt && !paper.lastAttempt.submittedAt
        ? `/listening/paper/${paper.id}?attemptId=${paper.lastAttempt.attemptId}`
        : null);
    if (resumeRoute) {
      router.push(resumeRoute);
      return;
    }
    if (!isPaperAllowed(paper)) {
      setLockedMessage('This paper requires an active Listening subscription.');
      return;
    }

    setStartingPaperId(paper.id);
    setError(null);
    setLockedMessage(null);
    setInsufficientCreditsMessage(null);
    try {
      const started = await startListeningAttempt(paper.id, 'exam');
      showCreditFeedback(started.feedbackMessage);
      router.push(`/listening/paper/${paper.id}?attemptId=${started.attemptId}`);
    } catch (caught) {
      if (isInsufficientCreditsError(caught)) {
        setInsufficientCreditsMessage(readInsufficientCreditsMessage(caught));
      } else if (isContentLockedError(caught)) {
        setLockedMessage(readContentLockedMessage(caught));
      } else {
        setError(readErrorMessage(caught, 'Could not start the full Listening exam.'));
      }
    } finally {
      setStartingPaperId(null);
    }
  }

  return (
    <LearnerDashboardShell pageTitle="Full Listening Exam">
      <InsufficientCreditsModal
        open={insufficientCreditsMessage !== null}
        message={insufficientCreditsMessage ?? ''}
        onClose={() => setInsufficientCreditsMessage(null)}
      />
      <main className="space-y-5 sm:space-y-8" data-testid="listening-full-exam">
        <Link
          href="/listening"
          className="inline-flex items-center gap-2 text-sm font-semibold text-primary hover:underline"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden />
          Back to Listening
        </Link>

        <LearnerPageHero
          eyebrow="Full exam"
          icon={Headphones}
          accent="purple"
          title="Full Listening Exam"
          description="Open a Listening series folder, then start a published Atlas or Nova exam. Audio plays once. You can submit at any time; you cannot jump freely between Parts A, B, and C."
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {lockedMessage ? <ContentLockedNotice message={lockedMessage} /> : null}

        {loading ? (
          <LearnerSkeleton variant="card-grid" />
        ) : (
          <ListeningExamFolderBrowser
            papers={home?.papers ?? []}
            emptyMessage="no published Atlas/Nova Listening papers yet."
            renderPaper={(paper) => {
              const allowed = isPaperAllowed(paper);
              const resumeRoute = activeByPaper.get(paper.id)
                ?? (paper.lastAttempt && !paper.lastAttempt.submittedAt
                  ? `/listening/paper/${paper.id}?attemptId=${paper.lastAttempt.attemptId}`
                  : null);
              const starting = startingPaperId === paper.id;
              const partial = isPartialListeningExam(paper);
              return (
                <article className="flex h-full flex-col rounded-2xl border border-violet-100 bg-surface p-5 shadow-sm dark:border-violet-900/40">
                  <h3 className="text-base font-bold text-navy">{paper.title}</h3>
                  <p className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted">
                    <span className="inline-flex items-center gap-1">
                      <ListChecks className="h-3 w-3" aria-hidden />
                      {paper.questionCount} questions
                    </span>
                    <span className="inline-flex items-center gap-1">
                      <Clock className="h-3 w-3" aria-hidden />
                      {paper.estimatedDurationMinutes || 45} min
                    </span>
                    {!allowed ? (
                      <span className="inline-flex items-center gap-1 text-amber-800">
                        <Lock className="h-3 w-3" aria-hidden />
                        Subscription required
                      </span>
                    ) : null}
                    {partial ? (
                      <span className="rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide text-amber-900">
                        Partial · Q37–42 unavailable
                      </span>
                    ) : null}
                  </p>
                  {partial ? (
                    <p className="mt-2 text-xs text-muted">
                      Questions 37–42 are unavailable in the supplied source. This paper is {paper.questionCount} items.
                    </p>
                  ) : null}
                  {paper.lastAttempt?.submittedAt ? (
                    <p className="mt-2 text-xs text-muted">
                      Last attempt submitted
                    </p>
                  ) : null}
                  <div className="mt-auto pt-4">
                    <button
                      type="button"
                      onClick={() => handleStart(paper)}
                      disabled={starting}
                      className="rounded-md bg-info px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-info/90 disabled:opacity-70"
                    >
                      {starting
                        ? 'Starting...'
                        : resumeRoute
                          ? 'Resume exam'
                          : allowed
                            ? 'Start full exam'
                            : 'View access'}
                    </button>
                  </div>
                </article>
              );
            }}
          />
        )}
      </main>
    </LearnerDashboardShell>
  );
}
