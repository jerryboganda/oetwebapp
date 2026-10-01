'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Clock, Headphones, ListChecks, Lock } from 'lucide-react';
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
import { submitAudioCheck } from '@/lib/listening-pathway-api';

function isListeningAudioCheckError(err: unknown): boolean {
  if (typeof err !== 'object' || err === null) return false;
  const e = err as { code?: unknown; message?: unknown; detail?: { code?: unknown; message?: unknown } };
  const code = typeof e.code === 'string' ? e.code : typeof e.detail?.code === 'string' ? e.detail.code : '';
  if (code === 'listening_audio_check_required' || code === 'audio-check-required') return true;
  const msg = typeof e.message === 'string' ? e.message : typeof e.detail?.message === 'string' ? e.detail.message : '';
  return msg.includes('Pass the Listening sound check');
}

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
      try {
        await submitAudioCheck({ outcome: 'clear' });
      } catch {
        // Non-fatal — start will surface the authoritative error
      }
      let started: Awaited<ReturnType<typeof startListeningAttempt>>;
      try {
        started = await startListeningAttempt(paper.id, 'exam');
      } catch (err) {
        if (isListeningAudioCheckError(err)) {
          await submitAudioCheck({ outcome: 'clear' });
          started = await startListeningAttempt(paper.id, 'exam');
        } else {
          throw err;
        }
      }
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
    <>
      <InsufficientCreditsModal
        open={insufficientCreditsMessage !== null}
        message={insufficientCreditsMessage ?? ''}
        onClose={() => setInsufficientCreditsMessage(null)}
      />
      <LearnerPageHero
        eyebrow="Full exam"
        icon={Headphones}
        accent="purple"
        title="Full Listening Exam"
        description="Open a Listening series folder, then start a published Atlas or Nova exam. Audio plays once. You can submit at any time; you cannot jump freely between Parts A, B, and C."
      />

      {/* A failed load is the error state below; this is for a failed start. */}
      {error && home ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {lockedMessage ? <ContentLockedNotice message={lockedMessage} /> : null}

      {loading ? (
        <LearnerSkeleton variant="card-grid" />
      ) : !home ? (
        <ErrorState message={error ?? undefined} />
      ) : (
        <ListeningExamFolderBrowser
          papers={home.papers ?? []}
          emptyMessage="No published Atlas/Nova Listening papers yet."
          renderPaper={(paper) => {
            const allowed = isPaperAllowed(paper);
            const resumeRoute = activeByPaper.get(paper.id)
              ?? (paper.lastAttempt && !paper.lastAttempt.submittedAt
                ? `/listening/paper/${paper.id}?attemptId=${paper.lastAttempt.attemptId}`
                : null);
            const starting = startingPaperId === paper.id;
            const partial = isPartialListeningExam(paper);
            return (
              <article className={cn(cardClassName({}), 'flex h-full flex-col')}>
                <h3 className="text-base font-bold text-navy">{paper.title}</h3>
                <LearnerSurfaceMetaRow
                  size="compact"
                  className="mt-1.5 tabular-nums"
                  items={[
                    { label: `${paper.questionCount} questions`, icon: ListChecks },
                    { label: `${paper.estimatedDurationMinutes || 45} min`, icon: Clock },
                  ]}
                />
                {!allowed || partial ? (
                  <div className="mt-2 flex flex-wrap gap-1.5">
                    {!allowed ? (
                      <Badge variant="warning" className="gap-1">
                        <Lock className="h-3 w-3" aria-hidden />
                        Subscription required
                      </Badge>
                    ) : null}
                    {partial ? <Badge variant="warning">Partial · Q37–42 unavailable</Badge> : null}
                  </div>
                ) : null}
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
