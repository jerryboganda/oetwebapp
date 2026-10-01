'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { BookOpen, Clock, ListChecks, Lock } from 'lucide-react';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cardClassName } from '@/components/ui/card';
import { ErrorState } from '@/components/ui/empty-error';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { LearnerPageHero } from '@/components/domain';
import { LearnerSurfaceMetaRow } from '@/components/domain/learner-surface';
import { cn } from '@/lib/utils';
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
import { showCreditFeedback } from '@/lib/credit-feedback';

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
      showCreditFeedback(started.feedbackMessage);
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
    <>
      <InsufficientCreditsModal
        open={insufficientCreditsMessage !== null}
        message={insufficientCreditsMessage ?? ''}
        onClose={() => setInsufficientCreditsMessage(null)}
      />
      <LearnerPageHero
        eyebrow="Full exam"
        icon={BookOpen}
        accent="blue"
        title="Full Reading Exam"
        description="Open a book folder, then start a published full exam. Each exam is 60 minutes, 42 questions, with Part A hard-locked and Parts B+C sharing a 45-minute window."
      />

      {/* A failed load is the error state below; this is for a failed start. */}
      {error && home ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {lockedMessage ? <ContentLockedNotice message={lockedMessage} /> : null}

      {loading ? (
        <LearnerSkeleton variant="card-grid" />
      ) : !home ? (
        <ErrorState message={error ?? undefined} />
      ) : (
        <ReadingExamFolderBrowser
          papers={home.papers ?? []}
          emptyMessage="No papers in this series yet. They will appear here after they are published."
          renderPaper={(paper) => {
            const allowed = isPaperAllowed(paper);
            const resumeRoute = activeByPaper.get(paper.id);
            const starting = startingPaperId === paper.id;
            return (
              <article className={cn(cardClassName({}), 'flex h-full flex-col')}>
                <h3 className="text-base font-bold text-navy">{paper.title}</h3>
                <LearnerSurfaceMetaRow
                  size="compact"
                  className="mt-1.5 tabular-nums"
                  items={[
                    { label: `${paper.partACount}+${paper.partBCount}+${paper.partCCount} items`, icon: ListChecks },
                    { label: `${paper.estimatedDurationMinutes || paper.partATimerMinutes + paper.partBCTimerMinutes} min`, icon: Clock },
                  ]}
                />
                {!allowed ? (
                  <div className="mt-2">
                    <Badge variant="warning" className="gap-1">
                      <Lock className="h-3 w-3" aria-hidden />
                      Subscription required
                    </Badge>
                  </div>
                ) : null}
                {paper.lastAttempt?.submittedAt ? (
                  <p className="mt-2 text-xs tabular-nums text-muted">
                    Last score {paper.lastAttempt.rawScore ?? '—'}/{paper.totalPoints || 42}
                  </p>
                ) : null}
                <div className="mt-auto pt-4">
                  <Button
                    size="sm"
                    onClick={() => handleStart(paper)}
                    disabled={starting}
                  >
                    {starting
                      ? 'Starting...'
                      : resumeRoute
                        ? 'Resume exam'
                        : allowed
                          ? 'Start full exam'
                          : 'View access'}
                  </Button>
                </div>
              </article>
            );
          }}
        />
      )}
    </>
  );
}
