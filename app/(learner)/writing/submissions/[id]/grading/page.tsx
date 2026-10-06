'use client';

import { useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { Sparkles, Award } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { WritingReleaseCountdown } from '@/components/domain/writing/WritingReleaseCountdown';
import { getWritingSubmission, retryWritingGrade } from '@/lib/writing/api';
import { isReleased } from '@/lib/writing/release';
import { toCandidateSafeWritingErrorMessage } from '@/lib/writing/submit-keys';
import { connectWritingSubmissionStream } from '@/lib/writing/realtime';
import type { WritingSubmissionDto } from '@/lib/writing/types';

export default function WritingSubmissionGradingPage() {
  const t = useTranslations();
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const submissionId = String(params?.id ?? '');
  const [submission, setSubmission] = useState<WritingSubmissionDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [retrying, setRetrying] = useState(false);
  // Refetch hook for the countdown: set by the realtime/poll effect below.
  const checkNow = useRef<() => void>(() => {});

  useEffect(() => {
    if (!submissionId) return;
    let cancelled = false;
    void getWritingSubmission(submissionId)
      .then((s) => {
        if (cancelled) return;
        setSubmission(s);
        if (isReleased(s)) {
          router.replace(`/writing/submissions/${encodeURIComponent(submissionId)}/results`);
        }
      })
      .catch((err) => {
        if (cancelled) return;
        setError(toCandidateSafeWritingErrorMessage(err, t('writing.submissions.grading.error.load')));
      });
    return () => {
      cancelled = true;
    };
  }, [submissionId, router, t]);

  // The server decides whether Retry can help (`canRetry`: a failed run, or a
  // run stuck past its lease). An older API without the field: any `failed` row.
  // Retry re-grades the SAME submission — no retyping, no duplicate charge.
  const failed = submission?.status === 'failed';
  const canRetry = submission ? (submission.canRetry ?? failed) : false;
  const failureCode = submission?.failureCode ?? null;
  const showFailure = failed || canRetry;
  // The server re-queued a failed run by itself and keeps retrying.
  const delayed = !showFailure && submission?.autoRetrying === true;
  const handleRetry = () => {
    if (retrying || !submissionId) return;
    setRetrying(true);
    setError(null);
    void retryWritingGrade(submissionId)
      .then(() => getWritingSubmission(submissionId))
      .then((s) => {
        setSubmission(s);
        if (isReleased(s)) {
          router.replace(`/writing/submissions/${encodeURIComponent(submissionId)}/results`);
        }
      })
      .catch((err) => {
        setError(toCandidateSafeWritingErrorMessage(err, t('writing.submissions.grading.error.load')));
      })
      .finally(() => {
        setRetrying(false);
      });
  };

  useEffect(() => {
    if (!submissionId) return;
    let cancelled = false;
    let polling = false;
    // Open the result only when the server says it is released; a finished but
    // held result keeps the countdown on screen.
    const check = () => {
      if (polling) return;
      polling = true;
      void getWritingSubmission(submissionId)
        .then((s) => {
          if (cancelled) return;
          setSubmission(s);
          if (isReleased(s)) {
            router.replace(`/writing/submissions/${encodeURIComponent(submissionId)}/results`);
          }
        })
        .catch((err) => {
          if (cancelled) return;
          setError(toCandidateSafeWritingErrorMessage(err, t('writing.submissions.grading.error.load')));
        })
        .finally(() => {
          polling = false;
        });
    };
    checkNow.current = check;
    // A grade-ready push is only a nudge: the server decides whether it is released.
    const d = connectWritingSubmissionStream(submissionId, {
      onGradeReady: () => {
        if (!cancelled) check();
      },
    });
    // Polling fallback in case SignalR is unreachable.
    const timer = window.setInterval(check, 5000);
    return () => {
      cancelled = true;
      checkNow.current = () => {};
      d.close();
      window.clearInterval(timer);
    };
  }, [submissionId, router, t]);

  // The countdown applies when the server sent a release time; accounts without
  // a hold (and failed rows) get none, so the page must not promise 15 minutes.
  // The 15-minute notice lives only inside the countdown, which swaps it for the
  // finalising line at zero, so no stale promise outlives the window and an
  // account without a hold never sees it (not even before the first load).
  const counting = Boolean(submission?.releaseAt);
  // Real status only: 'finalising' is for a finished letter waiting out its window; a letter still being assessed says so.
  const heroDescription = showFailure || !submission || counting
    ? undefined
    : t(submission.releaseState === 'held' ? 'writing.release.finalising' : 'writing.release.stillProcessing');
  // A re-queued run is being assessed again, not waiting in line: never label it "Queued".
  // Before the first response there is no real status yet: never flash "Queued" for a letter that may be graded.
  const statusLabel = !submission
    ? t('writing.submissions.grading.connecting')
    : t(`writing.submissions.detail.status.${delayed && submission.status === 'queued' ? 'grading' : submission.status}`);

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.submissions.grading.eyebrow')}
        icon={Sparkles}
        accent="writing"
        title={t('writing.submissions.grading.title')}
        description={heroDescription}
        highlights={[
          {
            icon: Award,
            label: t('writing.submissions.grading.highlights.status'),
            value: statusLabel,
          },
        ]}
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {showFailure ? (
        <Card padding="lg" aria-live="polite" role="alert" data-testid="writing-grading-failed">
          {canRetry && failureCode === 'credits_insufficient' ? (
            <>
              <p className="text-sm font-bold text-navy">{t('writing.submissions.grading.creditsTitle')}</p>
              <p className="mt-1 text-sm text-muted">{t('writing.submissions.grading.creditsDescription')}</p>
            </>
          ) : canRetry ? (
            <>
              <p className="text-sm font-bold text-navy">{t('writing.submissions.grading.failedTitle')}</p>
              <p className="mt-1 text-sm text-muted">{t('writing.submissions.grading.failedDescription')}</p>
            </>
          ) : (
            <>
              <p className="text-sm font-bold text-navy">{t('writing.submissions.grading.notGradableTitle')}</p>
              <p className="mt-1 text-sm text-muted">
                {failureCode === 'manual_review'
                  ? t('writing.submissions.grading.notGradable.manualReview')
                  : failureCode === 'letter_invalid'
                    ? t('writing.submissions.grading.notGradable.letterInvalid')
                    : t('writing.submissions.grading.notGradable.taskNotReady')}
              </p>
            </>
          )}
          <div className="mt-4 flex flex-wrap justify-end gap-2">
            <Button asChild variant="outline" size="sm">
              <Link href="/writing/practice/library">
                {t('writing.submissions.grading.backToLibrary')}
              </Link>
            </Button>
            {canRetry && failureCode === 'credits_insufficient' ? (
              <Button asChild variant="outline" size="sm">
                <Link href="/ai-packages">{t('writing.submissions.grading.buyCredits')}</Link>
              </Button>
            ) : null}
            {canRetry ? (
              <Button size="sm" onClick={handleRetry} disabled={retrying} data-testid="writing-grading-retry">
                {retrying
                  ? t('writing.submissions.grading.retrying')
                  : t('writing.submissions.grading.retry')}
              </Button>
            ) : null}
          </div>
        </Card>
      ) : null}

      {delayed ? (
        <Card padding="lg" aria-live="polite" role="status">
          <p className="text-sm font-bold text-navy">{t('writing.submissions.grading.delayedTitle')}</p>
          <p className="mt-1 text-sm text-muted">{t('writing.submissions.grading.delayedDescription')}</p>
        </Card>
      ) : null}

      {/* No live region around the card: the ticking timer would be announced
          every second. The finalising line inside the countdown is its own status. */}
      {!showFailure ? (
        <MotionSection delayIndex={0}>
          <Card padding="lg" aria-busy={!submission || !isReleased(submission)}>
            <WritingReleaseCountdown
              releaseAt={submission?.releaseAt}
              serverNow={submission?.serverNow}
              releaseState={submission?.releaseState}
              onHeldElapsed={() => checkNow.current()}
            />
            {submission && !counting ? (
              <p className="text-sm text-muted">{t('writing.release.savedNote')}</p>
            ) : null}
            <div className="mt-4 flex justify-end">
              <Button asChild variant="outline" size="sm">
                <Link href={`/writing/submissions/${encodeURIComponent(submissionId)}`}>
                  {t('writing.submissions.grading.viewDetails')}
                </Link>
              </Button>
            </div>
          </Card>
        </MotionSection>
      ) : null}
    </>
  );
}
