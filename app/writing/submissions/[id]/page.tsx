'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowRight, Clock, FileText, RefreshCw } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { getWritingSubmission } from '@/lib/writing/api';
import type { WritingSubmissionDto, WritingSubmissionStatus } from '@/lib/writing/types';

const STATUS_BADGE: Record<WritingSubmissionStatus, { label: string; variant: 'info' | 'warning' | 'success' | 'danger' | 'muted' }> = {
  queued: { label: 'Queued', variant: 'muted' },
  preflight: { label: 'Pre-flight checks', variant: 'info' },
  grading: { label: 'Grading', variant: 'warning' },
  graded: { label: 'Graded', variant: 'success' },
  failed: { label: 'Failed', variant: 'danger' },
  cancelled: { label: 'Cancelled', variant: 'muted' },
};

export default function WritingSubmissionDetailPage() {
  const params = useParams<{ id: string }>();
  const submissionId = String(params?.id ?? '');
  const [submission, setSubmission] = useState<WritingSubmissionDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!submissionId) return;
    void getWritingSubmission(submissionId)
      .then(setSubmission)
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load submission.'));
  }, [submissionId]);

  const status = submission ? STATUS_BADGE[submission.status] : null;

  return (
    <LearnerDashboardShell pageTitle="Writing Submission">
      <div className="space-y-6" aria-busy={!submission}>
        <LearnerPageHero
          eyebrow="Submission"
          icon={FileText}
          accent="amber"
          title={submission ? `Letter in ${submission.mode} mode` : 'Submission'}
          description="Read the letter you sent and continue to the result or revise it."
          highlights={submission ? [
            { icon: Clock, label: 'Submitted', value: new Date(submission.submittedAt).toLocaleString() },
            { icon: FileText, label: 'Words', value: `${submission.wordCount}` },
            { icon: RefreshCw, label: 'Revision', value: submission.isRevision ? 'Yes' : 'No' },
          ] : []}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {submission ? (
          <section aria-labelledby="status-heading" className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
            <header className="flex flex-wrap items-center justify-between gap-2">
              <h2 id="status-heading" className="text-base font-bold text-navy">Status</h2>
              {status ? <Badge variant={status.variant} size="sm">{status.label}</Badge> : null}
            </header>
            <p className="mt-2 text-sm text-muted">
              Tier: <span className="font-bold text-navy capitalize">{submission.gradingTier}</span>{' '}
              · Source: <span className="font-bold text-navy capitalize">{submission.inputSource}</span>
            </p>
            <div className="mt-3 flex flex-wrap gap-2">
              {submission.status === 'graded' ? (
                <Button asChild>
                  <Link href={`/writing/submissions/${encodeURIComponent(submission.id)}/results`}>
                    Open results <ArrowRight className="h-4 w-4" aria-hidden="true" />
                  </Link>
                </Button>
              ) : null}
              {submission.status === 'queued' || submission.status === 'grading' || submission.status === 'preflight' ? (
                <Button asChild variant="outline">
                  <Link href={`/writing/submissions/${encodeURIComponent(submission.id)}/grading`}>
                    Wait for grade
                  </Link>
                </Button>
              ) : null}
              {submission.status === 'graded' && !submission.isRevision ? (
                <Button asChild variant="outline">
                  <Link href={`/writing/submissions/${encodeURIComponent(submission.id)}/revise`}>
                    Revise this letter
                  </Link>
                </Button>
              ) : null}
            </div>
          </section>
        ) : null}

        {submission ? (
          <section aria-labelledby="letter-heading">
            <Card padding="lg">
              <CardContent>
                <h2 id="letter-heading" className="text-base font-bold text-navy">Your letter</h2>
                <pre className="mt-3 whitespace-pre-wrap rounded-lg border border-border bg-background p-3 text-sm leading-relaxed font-sans">
                  {submission.letterContent}
                </pre>
              </CardContent>
            </Card>
          </section>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
