'use client';

import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useState } from 'react';
import { ArrowLeft, MessageSquare } from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { FeedbackForm } from '@/components/class/FeedbackForm';
import { InlineAlert } from '@/components/ui/alert';
import { submitClassFeedback, type ClassFeedbackSubmitPayload } from '@/lib/api';

export default function ClassFeedbackPage() {
  const params = useParams();
  const sessionId = typeof params?.sessionId === 'string' ? params.sessionId : null;

  const [submitting, setSubmitting] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);
  const [apiSuccess, setApiSuccess] = useState<string | null>(null);

  async function handleSubmit(payload: ClassFeedbackSubmitPayload) {
    if (!sessionId) return;
    setSubmitting(true);
    setApiError(null);
    setApiSuccess(null);
    try {
      await submitClassFeedback(sessionId, payload);
      setApiSuccess('Thanks for your feedback!');
    } catch (err: unknown) {
      setApiError(err instanceof Error ? err.message : 'Could not submit feedback.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <Link
          href="/me/classes/past"
          className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-navy"
        >
          <ArrowLeft className="h-4 w-4" /> Back to past classes
        </Link>

        <LearnerPageHero
          title="Rate your class"
          description="Your feedback helps tutors improve and other learners pick the right class."
          icon={MessageSquare}
        />

        {!sessionId ? (
          <InlineAlert variant="warning">Invalid session id.</InlineAlert>
        ) : (
          <FeedbackForm
            onSubmit={handleSubmit}
            submitting={submitting}
            apiError={apiError}
            apiSuccess={apiSuccess}
          />
        )}
      </div>
    </LearnerDashboardShell>
  );
}
