'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { fetchSubmissions } from '@/lib/api';
import type { ReviewStatus, Submission, SubTest } from '@/lib/mock-data';
import {
    AlertCircle, CheckCircle2, Clock, FileText, GitCompare, Headphones, History, MessageSquare, Mic, PenTool, Send
} from 'lucide-react';
import { useRouter } from 'next/navigation';
import React, { useEffect, useState } from 'react';
const SUBTEST_STYLE: Record<SubTest, { icon: React.ElementType; badge: string }> = {
  Reading:   { icon: FileText,   badge: 'bg-blue-100 text-blue-700' },
  Listening: { icon: Headphones, badge: 'bg-indigo-100 text-indigo-700' },
  Writing:   { icon: PenTool,    badge: 'bg-rose-100 text-rose-700' },
  Speaking:  { icon: Mic,        badge: 'bg-purple-100 text-purple-700' },
};

function formatSubmissionAttemptDate(value: string) {
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  const includesTime = /T\d{2}:\d{2}/.test(value);

  return new Intl.DateTimeFormat(undefined, includesTime
    ? {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
        hour: 'numeric',
        minute: '2-digit',
      }
    : {
        year: 'numeric',
        month: 'short',
        day: 'numeric',
      }).format(parsed);
}

function ReviewBadge({ status }: { status: ReviewStatus }) {
  if (status === 'reviewed') return (
    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-bold bg-green-100 text-green-700">
      <CheckCircle2 className="w-3.5 h-3.5" /> Reviewed
    </span>
  );
  if (status === 'pending') return (
    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-bold bg-amber-100 text-amber-700">
      <Clock className="w-3.5 h-3.5" /> Pending
    </span>
  );
  return (
    <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-bold bg-gray-100 text-gray-600">
      <AlertCircle className="w-3.5 h-3.5" /> Not Requested
    </span>
  );
}

export default function SubmissionHistory() {
  const router = useRouter();
  const [submissions, setSubmissions] = useState<Submission[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('evaluation_viewed', { type: 'submissions' });
    fetchSubmissions()
      .then((data) => { setSubmissions(data); setLoading(false); })
      .catch(() => { setError('Failed to load submissions. Please try again.'); setLoading(false); });
  }, []);

  const pendingReviewCount = submissions.filter((submission) => submission.reviewStatus === 'pending').length;
  const comparisonReadyCount = submissions.filter((submission) => Boolean(submission.actions.compareRoute)).length;

  return (
    <LearnerDashboardShell
      pageTitle="Submission History"
      subtitle="Review your past work and follow up on feedback"
      backHref="/"
    >
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Evidence History"
          icon={History}
          accent="slate"
          title="Reopen the attempts that need review or comparison"
          description="Use submission history to find the attempts that still need feedback, comparison, or a fresh follow-up decision."
          highlights={[
            { icon: History, label: 'Attempts', value: `${submissions.length} recorded` },
            { icon: Clock, label: 'Pending reviews', value: `${pendingReviewCount} waiting` },
            { icon: GitCompare, label: 'Compare ready', value: `${comparisonReadyCount} attempts` },
          ]}
        />

        {loading ? (
          <div className="space-y-4">
            {[1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-36 rounded-2xl" />
            ))}
          </div>
        ) : null}

        {!loading && error ? (
          <InlineAlert variant="error">{error}</InlineAlert>
        ) : null}

        {!loading && !error && submissions.length === 0 ? (
          <EmptyState title="No submissions yet" description="Complete a writing or speaking task to see your history here." action={{ label: 'Start a writing task', onClick: () => router.push('/writing') }} className="py-24" />
        ) : null}

        {!loading && !error && submissions.length > 0 ? (
          <section>
            <LearnerSurfaceSectionHeader
              eyebrow="Past Evidence"
              title="Keep review state and score direction visible"
              description="Each card should answer what was submitted, when, how it performed, and whether follow-up is still available."
              className="mb-4"
            />

            <div className="space-y-4">
              {submissions.map((sub, idx) => {
                const meta = SUBTEST_STYLE[sub.subTest] ?? SUBTEST_STYLE.Writing;
                const Icon = meta.icon;
                const canRequest = sub.canRequestReview;
                return (
                  <MotionItem
                    key={sub.id}
                    delayIndex={idx}
                    className="bg-surface rounded-[24px] border border-border p-5 sm:p-6 shadow-sm flex flex-col md:flex-row gap-6 justify-between hover:border-gray-300 transition-colors"
                  >
                    <div className="flex-1 space-y-4">
                      <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-4">
                        <div>
                          <div className="flex items-center gap-3 mb-2">
                            <span className={`inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-xs font-black uppercase tracking-widest ${meta.badge}`}>
                              <Icon className="w-4 h-4" />
                              {sub.subTest}
                            </span>
                            <span className="text-sm text-muted font-medium">{formatSubmissionAttemptDate(sub.attemptDate)}</span>
                          </div>
                          <h2 className="text-lg font-bold text-navy leading-tight">{sub.taskName}</h2>
                        </div>
                        <div className="sm:text-right bg-background-light sm:bg-transparent p-3 sm:p-0 rounded-xl border border-gray-100 sm:border-none">
                          <div className="text-xs font-bold text-muted uppercase tracking-widest mb-1">Score Estimate</div>
                          <div className={`text-xl font-black ${sub.scoreEstimate === 'Pending' ? 'text-muted' : 'text-navy'}`}>
                            {sub.scoreEstimate}
                          </div>
                        </div>
                      </div>
                      <div className="flex items-center gap-3 pt-2 border-t border-gray-50">
                        <span className="text-sm font-medium text-muted">Review Status:</span>
                        <ReviewBadge status={sub.reviewStatus} />
                      </div>
                    </div>

                    <div className="flex flex-col gap-2 md:w-48 shrink-0 border-t md:border-t-0 md:border-l border-gray-100 pt-5 md:pt-0 md:pl-6 justify-center">
                      <Button
                        variant="outline"
                        fullWidth
                        onClick={() => sub.actions.reopenFeedbackRoute && router.push(sub.actions.reopenFeedbackRoute)}
                        disabled={!sub.actions.reopenFeedbackRoute}
                      >
                        <MessageSquare className="w-4 h-4" />
                        Reopen Feedback
                      </Button>
                      <Button
                        variant="outline"
                        fullWidth
                        onClick={() => sub.actions.compareRoute && router.push(sub.actions.compareRoute)}
                        disabled={!sub.actions.compareRoute}
                      >
                        <GitCompare className="w-4 h-4" />
                        Compare Attempts
                      </Button>
                      <Button
                        variant="primary"
                        fullWidth
                        onClick={() => sub.actions.requestReviewRoute && router.push(sub.actions.requestReviewRoute)}
                        disabled={!canRequest || !sub.actions.requestReviewRoute}
                      >
                        <Send className="w-4 h-4" />
                        Request Review
                      </Button>
                    </div>
                  </MotionItem>
                );
              })}
            </div>
          </section>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
