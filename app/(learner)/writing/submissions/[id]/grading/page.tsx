'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { CircleDot, FileSearch, Sparkles, Award } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { getWritingSubmission, retryWritingGrade } from '@/lib/writing/api';
import { toCandidateSafeWritingErrorMessage } from '@/lib/writing/submit-keys';
import { connectWritingSubmissionStream } from '@/lib/writing/realtime';
import type { WritingSubmissionDto } from '@/lib/writing/types';

const STEPS = [
  { code: 'preflight', labelKey: 'writing.submissions.grading.steps.reading', icon: FileSearch },
  { code: 'grading', labelKey: 'writing.submissions.grading.steps.scoring', icon: Sparkles },
  { code: 'modelAnswer', labelKey: 'writing.submissions.grading.steps.modelAnswer', icon: CircleDot },
  { code: 'ready', labelKey: 'writing.submissions.grading.steps.finalising', icon: Award },
] as const;

type StepCode = (typeof STEPS)[number]['code'];

function stepIndexForStatus(status: WritingSubmissionDto['status']): number {
  switch (status) {
    case 'queued':
      return 0;
    case 'preflight':
      return 0;
    case 'grading':
      return 1;
    case 'graded':
      return STEPS.length - 1;
    default:
      return 0;
  }
}

export default function WritingSubmissionGradingPage() {
  const t = useTranslations();
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const submissionId = String(params?.id ?? '');
  const [submission, setSubmission] = useState<WritingSubmissionDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [retrying, setRetrying] = useState(false);
  const [statusMessage, setStatusMessage] = useState(() => t('writing.submissions.grading.connecting'));

  useEffect(() => {
    if (!submissionId) return;
    let cancelled = false;
    void getWritingSubmission(submissionId)
      .then((s) => {
        if (cancelled) return;
        setSubmission(s);
        if (s.status === 'graded') {
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
        if (s.status === 'graded') {
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
    const d = connectWritingSubmissionStream(submissionId, {
      onGradeReady: () => {
        if (cancelled) return;
        router.replace(`/writing/submissions/${encodeURIComponent(submissionId)}/results`);
      },
      onStatusChange: (s) => {
        if (cancelled) return;
        if (s === 'connected') setStatusMessage(t('writing.submissions.grading.listening'));
        if (s === 'disconnected') setStatusMessage(t('writing.submissions.grading.reconnecting'));
      },
      onError: () => {
        if (cancelled) return;
        setStatusMessage(t('writing.submissions.grading.polling'));
      },
    });
    // Polling fallback in case SignalR is unreachable.
    const timer = window.setInterval(() => {
      if (polling) return;
      polling = true;
      void getWritingSubmission(submissionId)
        .then((s) => {
          if (cancelled) return;
          setSubmission(s);
          if (s.status === 'graded') {
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
    }, 5000);
    return () => {
      cancelled = true;
      d.close();
      window.clearInterval(timer);
    };
  }, [submissionId, router, t]);

  const currentStepIdx = submission ? stepIndexForStatus(submission.status) : 0;

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.submissions.grading.eyebrow')}
        icon={Sparkles}
        accent="writing"
        title={t('writing.submissions.grading.title')}
        description={t('writing.submissions.grading.description')}
        highlights={[
          {
            icon: Award,
            label: t('writing.submissions.grading.highlights.status'),
            value: t(`writing.submissions.detail.status.${submission?.status ?? 'queued'}`),
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

      {/* No looping pulse on the active step (WCAG 2.2.2): its tint and
          "In progress" badge carry the state. */}
      {!showFailure ? (
        <MotionSection delayIndex={0}>
          <Card padding="lg" aria-live="polite" role="status" aria-busy={submission?.status !== 'graded'}>
            <p className="text-sm text-muted">{statusMessage}</p>
            <ol
              className="mt-4 space-y-3"
              aria-label={t('writing.submissions.grading.pipelineLabel')}
              data-testid="writing-grading-steps"
            >
              {STEPS.map((step, idx) => {
                const Icon = step.icon;
                const active = idx === currentStepIdx;
                const done = idx < currentStepIdx;
                const tone = done
                  ? 'bg-success/10 text-success-strong border-success/30'
                  : active
                    ? 'bg-warning/10 text-warning-strong border-warning/30'
                    : 'bg-background-light text-muted border-border';
                return (
                  <li key={step.code as StepCode} className={`flex items-center gap-3 rounded-xl border p-3 ${tone}`}>
                    <Icon className="h-5 w-5 shrink-0" aria-hidden="true" />
                    <p className="min-w-0 text-sm font-bold">{t(step.labelKey)}</p>
                    {done ? <Badge variant="success" size="sm" className="ms-auto shrink-0">{t('writing.submissions.grading.status.done')}</Badge> : null}
                    {active ? <Badge variant="warning" size="sm" className="ms-auto shrink-0">{t('writing.submissions.grading.status.inProgress')}</Badge> : null}
                  </li>
                );
              })}
            </ol>
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
