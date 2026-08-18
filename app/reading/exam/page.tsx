'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { ArrowLeft, BookOpen, Clock, ListChecks, Lock } from 'lucide-react';
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
  getReadingHome,
  startReadingAttempt,
  type ReadingHomeDto,
  type ReadingHomePaperDto,
} from '@/lib/reading-authoring-api';
import { ReadingExamFolderBrowser } from '@/components/domain/reading/reading-exam-folder-browser';
import { readErrorMessage } from '@/lib/read-error-message';
import {
  InsufficientCreditsModal,
  isInsufficientCreditsError,
  readInsufficientCreditsMessage,
} from '@/components/domain/InsufficientCreditsModal';

// Full Reading Exam uses the same book-folder list as Reading materials.
// New published papers appear automatically. Mock bundles stay on /mocks.

function isPaperAllowed(paper: ReadingHomePaperDto): boolean {
  return paper.entitlement?.allowed !== false;
}

export default function ReadingFullExamPage() {
  const router = useRouter();
  const [home, setHome] = useState<ReadingHomeDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [startingPaperId, setStartingPaperId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [lockedMessage, setLockedMessage] = useState<string | null>(null);
  const [insufficientCreditsMessage, setInsufficientCreditsMessage] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('content_view', { page: 'reading-full-exam' });
    let cancelled = false;
    getReadingHome()
      .then((value) => {
        if (!cancelled) {
          setHome(value);
          setLoading(false);
        }
      })
      .catch((caught) => {
        if (!cancelled) {
          setError(readErrorMessage(caught, 'Could not load Reading exams.'));
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
      if (attempt.canResume) map.set(attempt.paperId, attempt.route);
    }
    return map;
  }, [home?.activeAttempts]);

  async function handleStart(paper: ReadingHomePaperDto) {
    const resumeRoute = activeByPaper.get(paper.id);
    if (resumeRoute) {
      router.push(resumeRoute);
      return;
    }
    if (!isPaperAllowed(paper)) {
      setLockedMessage('This paper requires an active Reading subscription.');
      return;
    }

    setStartingPaperId(paper.id);
    setError(null);
    setLockedMessage(null);
    setInsufficientCreditsMessage(null);
    try {
      const started = await startReadingAttempt(paper.id);
      router.push(`/reading/paper/${paper.id}?attemptId=${started.attemptId}`);
    } catch (caught) {
      if (isInsufficientCreditsError(caught)) {
        setInsufficientCreditsMessage(readInsufficientCreditsMessage(caught));
      } else if (isContentLockedError(caught)) {
        setLockedMessage(readContentLockedMessage(caught));
      } else {
        setError(readErrorMessage(caught, 'Could not start the full Reading exam.'));
      }
    } finally {
      setStartingPaperId(null);
    }
  }

  return (
    <LearnerDashboardShell pageTitle="Full Reading Exam">
      <InsufficientCreditsModal
        open={insufficientCreditsMessage !== null}
        message={insufficientCreditsMessage ?? ''}
        onClose={() => setInsufficientCreditsMessage(null)}
      />
      <main className="space-y-5 sm:space-y-8" data-testid="reading-full-exam">
        <Link
          href="/reading"
          className="inline-flex items-center gap-2 text-sm font-semibold text-primary hover:underline"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden />
          Back to Reading
        </Link>

        <LearnerPageHero
          eyebrow="Full exam"
          icon={BookOpen}
          accent="blue"
          title="Full Reading Exam"
          description="Open a book folder, then start a published full exam. Each exam is 60 minutes, 42 questions, with Part A hard-locked and Parts B+C sharing a 45-minute window."
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {lockedMessage ? <ContentLockedNotice message={lockedMessage} /> : null}

        {loading ? (
          <LearnerSkeleton variant="card-grid" />
        ) : (
          <ReadingExamFolderBrowser
            papers={home?.papers ?? []}
            emptyMessage="No papers in this series yet. They will appear here after they are published."
            renderPaper={(paper) => {
              const allowed = isPaperAllowed(paper);
              const resumeRoute = activeByPaper.get(paper.id);
              const starting = startingPaperId === paper.id;
              return (
                <article className="flex h-full flex-col rounded-2xl border border-blue-100 bg-surface p-5 shadow-sm dark:border-blue-900/40">
                  <h3 className="text-base font-bold text-navy">{paper.title}</h3>
                  <p className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted">
                    <span className="inline-flex items-center gap-1">
                      <ListChecks className="h-3 w-3" aria-hidden />
                      {paper.partACount}+{paper.partBCount}+{paper.partCCount} items
                    </span>
                    <span className="inline-flex items-center gap-1">
                      <Clock className="h-3 w-3" aria-hidden />
                      {paper.estimatedDurationMinutes ||
                        paper.partATimerMinutes + paper.partBCTimerMinutes}{' '}
                      min
                    </span>
                    {!allowed ? (
                      <span className="inline-flex items-center gap-1 text-amber-800">
                        <Lock className="h-3 w-3" aria-hidden />
                        Subscription required
                      </span>
                    ) : null}
                  </p>
                  {paper.lastAttempt?.submittedAt ? (
                    <p className="mt-2 text-xs text-muted">
                      Last score {paper.lastAttempt.rawScore ?? '—'}/{paper.totalPoints || 42}
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
