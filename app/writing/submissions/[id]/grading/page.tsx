'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { CircleDot, FileSearch, Sparkles, Award } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { getWritingSubmission, getWritingSubmissionGrade } from '@/lib/writing/api';
import { connectWritingSubmissionStream } from '@/lib/writing/realtime';
import type { WritingSubmissionDto } from '@/lib/writing/types';

const STEPS = [
  { code: 'preflight', labelKey: 'writing.submissions.grading.steps.reading', icon: FileSearch },
  { code: 'grading', labelKey: 'writing.submissions.grading.steps.scoring', icon: Sparkles },
  { code: 'exemplar', labelKey: 'writing.submissions.grading.steps.exemplar', icon: CircleDot },
  { code: 'ready', labelKey: 'writing.submissions.grading.steps.finalising', icon: Award },
] as const;

type StepCode = (typeof STEPS)[number]['code'];

function stepIndexForStatus(status: WritingSubmissionDto['status']): number {
  switch (status) {
    case 'queued':
      return 0;
    case 'preflight':
      return 1;
    case 'grading':
      return 2;
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
        setError(err instanceof Error ? err.message : t('writing.submissions.grading.error.load'));
      });
    return () => {
      cancelled = true;
    };
  }, [submissionId, router, t]);

  useEffect(() => {
    if (!submissionId) return;
    const d = connectWritingSubmissionStream(submissionId, {
      onGradeReady: () => {
        router.replace(`/writing/submissions/${encodeURIComponent(submissionId)}/results`);
      },
      onStatusChange: (s) => {
        if (s === 'connected') setStatusMessage(t('writing.submissions.grading.listening'));
        if (s === 'disconnected') setStatusMessage(t('writing.submissions.grading.reconnecting'));
      },
      onError: () => {
        setStatusMessage(t('writing.submissions.grading.polling'));
      },
    });
    // Polling fallback in case SignalR is unreachable.
    const timer = window.setInterval(() => {
      void getWritingSubmissionGrade(submissionId)
        .then(() => {
          router.replace(`/writing/submissions/${encodeURIComponent(submissionId)}/results`);
        })
        .catch(() => {
          /* not graded yet */
        });
    }, 5000);
    return () => {
      d.close();
      window.clearInterval(timer);
    };
  }, [submissionId, router, t]);

  const currentStepIdx = submission ? stepIndexForStatus(submission.status) : 0;

  return (
    <LearnerDashboardShell pageTitle={t('writing.submissions.grading.pageTitle')}>
      <div className="space-y-6" aria-busy>
        <LearnerPageHero
          eyebrow={t('writing.submissions.grading.eyebrow')}
          icon={Sparkles}
          accent="amber"
          title={t('writing.submissions.grading.title')}
          description={t('writing.submissions.grading.description')}
          highlights={[
            { icon: Award, label: t('writing.submissions.grading.highlights.status'), value: submission?.status ?? 'queued' },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <Card padding="lg" aria-live="polite" role="status">
          <CardContent>
            <p className="text-sm text-muted">{statusMessage}</p>
            <ol className="mt-4 space-y-3" aria-label={t('writing.submissions.grading.pipelineLabel')}>
              {STEPS.map((step, idx) => {
                const Icon = step.icon;
                const active = idx === currentStepIdx;
                const done = idx < currentStepIdx;
                const tone = done
                  ? 'bg-emerald-100 text-emerald-700 border-emerald-300'
                  : active
                    ? 'bg-amber-100 text-amber-700 border-amber-300 animate-pulse'
                    : 'bg-slate-100 text-slate-500 border-slate-200';
                return (
                  <li key={step.code as StepCode} className={`flex items-center gap-3 rounded-lg border p-3 ${tone}`}>
                    <Icon className="h-5 w-5 shrink-0" aria-hidden="true" />
                    <p className="text-sm font-bold">{t(step.labelKey)}</p>
                    {done ? <Badge variant="success" size="sm" className="ml-auto">{t('writing.submissions.grading.status.done')}</Badge> : null}
                    {active ? <Badge variant="warning" size="sm" className="ml-auto">{t('writing.submissions.grading.status.inProgress')}</Badge> : null}
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
          </CardContent>
        </Card>
      </div>
    </LearnerDashboardShell>
  );
}
