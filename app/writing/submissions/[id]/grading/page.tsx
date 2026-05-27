'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
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
  { code: 'preflight', label: 'Reading your letter', icon: FileSearch },
  { code: 'grading', label: 'Scoring against the rubric', icon: Sparkles },
  { code: 'exemplar', label: 'Comparing with exemplars', icon: CircleDot },
  { code: 'ready', label: 'Finalising results', icon: Award },
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
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const submissionId = String(params?.id ?? '');
  const [submission, setSubmission] = useState<WritingSubmissionDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [statusMessage, setStatusMessage] = useState('Connecting to grading service…');

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
        setError(err instanceof Error ? err.message : 'Could not check submission status.');
      });
    return () => {
      cancelled = true;
    };
  }, [submissionId, router]);

  useEffect(() => {
    if (!submissionId) return;
    const d = connectWritingSubmissionStream(submissionId, {
      onGradeReady: () => {
        router.replace(`/writing/submissions/${encodeURIComponent(submissionId)}/results`);
      },
      onStatusChange: (s) => {
        if (s === 'connected') setStatusMessage('Listening for grade-ready event…');
        if (s === 'disconnected') setStatusMessage('Reconnecting…');
      },
      onError: () => {
        setStatusMessage('Switched to polling — checking every 5 seconds.');
      },
    });
    // Polling fallback in case SignalR is unreachable.
    const t = window.setInterval(() => {
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
      window.clearInterval(t);
    };
  }, [submissionId, router]);

  const currentStepIdx = submission ? stepIndexForStatus(submission.status) : 0;

  return (
    <LearnerDashboardShell pageTitle="Grading your letter">
      <div className="space-y-6" aria-busy>
        <LearnerPageHero
          eyebrow="Grading"
          icon={Sparkles}
          accent="amber"
          title="Sit tight while we grade your letter"
          description="The pipeline runs in under a minute on average. You'll be redirected automatically when it finishes."
          highlights={[
            { icon: Award, label: 'Status', value: submission?.status ?? 'queued' },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <Card padding="lg" aria-live="polite" role="status">
          <CardContent>
            <p className="text-sm text-muted">{statusMessage}</p>
            <ol className="mt-4 space-y-3" aria-label="Grading pipeline">
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
                    <p className="text-sm font-bold">{step.label}</p>
                    {done ? <Badge variant="success" size="sm" className="ml-auto">Done</Badge> : null}
                    {active ? <Badge variant="warning" size="sm" className="ml-auto">In progress</Badge> : null}
                  </li>
                );
              })}
            </ol>
            <div className="mt-4 flex justify-end">
              <Button asChild variant="outline" size="sm">
                <Link href={`/writing/submissions/${encodeURIComponent(submissionId)}`}>
                  View submission details
                </Link>
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    </LearnerDashboardShell>
  );
}
