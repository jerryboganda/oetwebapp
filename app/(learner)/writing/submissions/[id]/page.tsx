'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { ArrowRight, Clock, FileText, RefreshCw } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { cardClassName } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { getWritingSubmission } from '@/lib/writing/api';
import type { WritingSubmissionDto, WritingSubmissionStatus } from '@/lib/writing/types';

const STATUS_BADGE_VARIANT: Record<WritingSubmissionStatus, 'info' | 'warning' | 'success' | 'danger' | 'muted'> = {
  queued: 'muted',
  preflight: 'info',
  grading: 'warning',
  graded: 'success',
  failed: 'danger',
  cancelled: 'muted',
};

export default function WritingSubmissionDetailPage() {
  const t = useTranslations();
  const params = useParams<{ id: string }>();
  const submissionId = String(params?.id ?? '');
  const [submission, setSubmission] = useState<WritingSubmissionDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!submissionId) return;
    void getWritingSubmission(submissionId)
      .then(setSubmission)
      .catch((err) => setError(err instanceof Error ? err.message : t('writing.submissions.detail.error.load')));
  }, [submissionId, t]);

  const statusVariant = submission ? STATUS_BADGE_VARIANT[submission.status] : null;
  const statusLabel = submission ? t(`writing.submissions.detail.status.${submission.status}`) : null;

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.submissions.detail.eyebrow')}
        icon={FileText}
        accent="amber"
        title={submission ? t('writing.submissions.detail.heroTitle', { mode: submission.mode }) : t('writing.submissions.detail.heroTitleFallback')}
        description={t('writing.submissions.detail.heroDescription')}
        highlights={submission ? [
          { icon: Clock, label: t('writing.submissions.detail.highlights.submitted'), value: new Date(submission.submittedAt).toLocaleString() },
          { icon: FileText, label: t('writing.submissions.detail.highlights.words'), value: `${submission.wordCount}` },
          { icon: RefreshCw, label: t('writing.submissions.detail.highlights.revision'), value: submission.isRevision ? t('writing.submissions.detail.highlights.revisionYes') : t('writing.submissions.detail.highlights.revisionNo') },
        ] : []}
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {!submission && !error ? <LearnerSkeleton variant="list" /> : null}

      {submission ? (
        <MotionSection delayIndex={0}>
          <section aria-labelledby="status-heading" className={cardClassName({ padding: 'lg' })}>
            <header className="flex flex-wrap items-center justify-between gap-2">
              <h2 id="status-heading" className="text-base font-bold text-navy">{t('writing.submissions.detail.statusHeading')}</h2>
              {statusVariant && statusLabel ? <Badge variant={statusVariant} size="sm">{statusLabel}</Badge> : null}
            </header>
            <p className="mt-2 text-sm text-muted">
              {t('writing.submissions.detail.tierLabel')} <span className="font-bold capitalize text-navy">{submission.gradingTier}</span>{' '}
              · {t('writing.submissions.detail.sourceLabel')} <span className="font-bold capitalize text-navy">{submission.inputSource}</span>
            </p>
            <div className="mt-3 flex flex-wrap gap-2">
              {submission.status === 'graded' ? (
                <Button asChild>
                  <Link href={`/writing/submissions/${encodeURIComponent(submission.id)}/results`}>
                    {t('writing.submissions.detail.openResults')} <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
                  </Link>
                </Button>
              ) : null}
              {submission.status === 'queued' || submission.status === 'grading' || submission.status === 'preflight' ? (
                <Button asChild variant="outline">
                  <Link href={`/writing/submissions/${encodeURIComponent(submission.id)}/grading`}>
                    {t('writing.submissions.detail.waitForGrade')}
                  </Link>
                </Button>
              ) : null}
              {submission.status === 'graded' ? (
                <Button asChild variant="outline">
                  <Link href={`/writing/practice/session/${encodeURIComponent(submission.scenarioId)}`}>
                    {t('writing.submissions.results.actions.practiceAgain')}
                  </Link>
                </Button>
              ) : null}
            </div>
          </section>
        </MotionSection>
      ) : null}

      {submission ? (
        <MotionSection delayIndex={1}>
          <section aria-labelledby="letter-heading" className={cardClassName({ padding: 'lg' })}>
            <h2 id="letter-heading" className="text-base font-bold text-navy">{t('writing.submissions.detail.letterHeading')}</h2>
            {/* The submitted letter text is learner-authored English content. */}
            <pre className="mt-3 max-w-3xl whitespace-pre-wrap break-words rounded-control border border-border bg-background p-3 font-sans text-sm leading-relaxed sm:p-4" dir="ltr">
              {submission.letterContent}
            </pre>
          </section>
        </MotionSection>
      ) : null}
    </>
  );
}
