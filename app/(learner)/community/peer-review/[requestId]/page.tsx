'use client';

import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'next/navigation';
import { FileText, Star, Send, ArrowLeft, CheckCircle, Clock, ClipboardList } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { ErrorState } from '@/components/ui/empty-error';
import { InlineAlert } from '@/components/ui/alert';
import { Textarea } from '@/components/ui/form-controls';
import { apiClient } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { useAuth } from '@/contexts/auth-context';
import Link from 'next/link';

interface PeerReviewDetail {
  id: string;
  submitterUserId: string;
  reviewerUserId?: string;
  subtestCode: string;
  attemptId: string;
  status: string;
  createdAt: string;
  claimedAt?: string;
  completedAt?: string;
}

interface PeerReviewFeedbackDetail {
  id: string;
  comments: string;
  rating: number;
  strengthNotes?: string;
  improvementNotes?: string;
  createdAt: string;
}

const STATUS_VARIANTS: Record<string, 'default' | 'success' | 'warning' | 'danger'> = {
  open: 'warning',
  claimed: 'default',
  completed: 'success',
  expired: 'danger',
};

export default function PeerReviewDetailPage() {
  const params = useParams();
  const requestId = params?.requestId as string;
  const { user } = useAuth();

  const [request, setRequest] = useState<PeerReviewDetail | null>(null);
  const [feedback, setFeedback] = useState<PeerReviewFeedbackDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Feedback form state
  const [feedbackText, setFeedbackText] = useState('');
  const [rating, setRating] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [submitSuccess, setSubmitSuccess] = useState(false);

  const loadRequest = useCallback(async () => {
    if (!requestId) return;
    setLoading(true);
    setError(null);
    try {
      // Load from my-submissions to get full detail with feedback
      const submissions = await apiClient.get<PeerReviewDetail[]>('/v1/community/peer-review/my-submissions');
      const found = submissions.find((s: PeerReviewDetail) => s.id === requestId);
      if (found) {
        setRequest(found);
        const withFeedback = found as PeerReviewDetail & { feedback?: PeerReviewFeedbackDetail };
        if (withFeedback.feedback) setFeedback(withFeedback.feedback);
        return;
      }

      // Check my-reviews
      const reviews = await apiClient.get<PeerReviewDetail[]>('/v1/community/peer-review/my-reviews');
      const reviewFound = reviews.find((r: PeerReviewDetail) => r.id === requestId);
      if (reviewFound) {
        setRequest(reviewFound);
        return;
      }

      setError('Review request not found');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load review');
    } finally {
      setLoading(false);
    }
  }, [requestId]);

  useEffect(() => {
    analytics.track('page_viewed', { page: 'peer-review-detail', requestId });
    loadRequest();
  }, [loadRequest, requestId]);

  const isReviewer = user?.userId === request?.reviewerUserId;
  const isSubmitter = user?.userId === request?.submitterUserId;

  const handleSubmitFeedback = async () => {
    if (!feedbackText.trim() || rating < 1 || rating > 5) return;
    setSubmitting(true);
    try {
      await apiClient.post(`/v1/community/peer-review/${encodeURIComponent(requestId)}/feedback`, {
        feedbackText: feedbackText.trim(),
        rating,
      });
      setSubmitSuccess(true);
      analytics.track('peer_review_feedback_submitted', { requestId, rating });
      loadRequest();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to submit feedback');
    } finally {
      setSubmitting(false);
    }
  };

  const backLink = (
    <div>
      <Button asChild variant="outline" size="sm">
        <Link href="/community/peer-review">
          <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Back to Peer Reviews
        </Link>
      </Button>
    </div>
  );

  if (loading) {
    return (
      <div className="space-y-4" role="status" aria-busy="true" aria-label="Loading">
        <Skeleton aria-hidden className="h-8 w-48" />
        <Skeleton aria-hidden className="h-40 rounded-2xl" />
        <Skeleton aria-hidden className="h-32 rounded-2xl" />
      </div>
    );
  }

  if (error || !request) {
    return (
      <>
        {backLink}
        <ErrorState message={error ?? 'Review not found'} onRetry={() => void loadRequest()} />
      </>
    );
  }

  const formatDay = (iso: string) => new Date(iso).toLocaleDateString();

  return (
    <>
      {backLink}

      {/* Subtest is in the title and status is the badge, so the old status card is gone. */}
      <LearnerPageHero
        eyebrow="Peer Review"
        title={`${request.subtestCode.charAt(0).toUpperCase() + request.subtestCode.slice(1)} Review`}
        description={isReviewer ? 'Provide feedback for this submission.' : 'View your submission status and feedback.'}
        icon={FileText}
        aside={(
          <Badge variant={STATUS_VARIANTS[request.status] ?? 'default'} size="md" className="capitalize">
            {request.status}
          </Badge>
        )}
        highlights={[
          { icon: Clock, label: 'Submitted', value: formatDay(request.createdAt) },
          ...(request.claimedAt ? [{ icon: ClipboardList, label: 'Claimed', value: formatDay(request.claimedAt) }] : []),
          ...(request.completedAt ? [{ icon: CheckCircle, label: 'Completed', value: formatDay(request.completedAt) }] : []),
        ]}
      />

      {isSubmitter && feedback && (
        <MotionSection>
          <Card padding="lg" className="space-y-3">
            <h2 className="text-base font-bold text-navy">Peer Feedback</h2>
            <div className="flex items-center gap-1">
              {[1, 2, 3, 4, 5].map((star) => (
                <Star
                  key={star}
                  aria-hidden="true"
                  className={`h-5 w-5 ${star <= feedback.rating ? 'fill-warning text-warning-strong' : 'text-muted'}`}
                />
              ))}
              <span className="ms-2 text-sm font-medium tabular-nums text-navy">{feedback.rating}/5</span>
            </div>
            <p className="max-w-3xl whitespace-pre-wrap break-words text-sm leading-6 text-navy">{feedback.comments}</p>
            {feedback.strengthNotes && (
              <div>
                <p className="text-xs font-semibold text-success-strong">Strengths</p>
                <p className="text-sm text-muted">{feedback.strengthNotes}</p>
              </div>
            )}
            {feedback.improvementNotes && (
              <div>
                <p className="text-xs font-semibold text-warning-strong">Areas for Improvement</p>
                <p className="text-sm text-muted">{feedback.improvementNotes}</p>
              </div>
            )}
          </Card>
        </MotionSection>
      )}

      {isReviewer && request.status === 'claimed' && !submitSuccess && (
        <MotionSection>
          <Card padding="lg" className="space-y-4">
            <h2 className="text-base font-bold text-navy">Submit Your Feedback</h2>

            <div className="space-y-2" role="group" aria-labelledby="peer-rating-label">
              <p id="peer-rating-label" className="text-sm font-semibold tracking-tight text-navy">Rating</p>
              <div className="flex flex-wrap items-center gap-1">
                {[1, 2, 3, 4, 5].map((star) => (
                  <button
                    key={star}
                    type="button"
                    onClick={() => setRating(star)}
                    aria-label={`${star} star${star === 1 ? '' : 's'}`}
                    aria-pressed={rating === star}
                    className="pressable flex h-11 w-11 items-center justify-center rounded-control hover:bg-background-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                  >
                    <Star
                      aria-hidden="true"
                      className={`h-6 w-6 ${star <= rating ? 'fill-warning text-warning-strong' : 'text-muted'}`}
                    />
                  </button>
                ))}
                {rating > 0 && <span className="ms-2 text-sm tabular-nums text-muted">{rating}/5</span>}
              </div>
            </div>

            <Textarea
              id="peer-feedback-text"
              label="Your Feedback"
              value={feedbackText}
              onChange={(e) => setFeedbackText(e.target.value)}
              placeholder="Provide constructive feedback on this submission..."
              className="min-h-[150px] resize-y"
            />

            <Button
              variant="primary"
              onClick={handleSubmitFeedback}
              disabled={submitting || !feedbackText.trim() || rating < 1}
            >
              <Send className="h-4 w-4" aria-hidden="true" />
              {submitting ? 'Submitting...' : 'Submit Feedback'}
            </Button>
          </Card>
        </MotionSection>
      )}

      {submitSuccess && (
        <InlineAlert variant="success" live="polite" title="Feedback submitted successfully!">
          Thank you for helping a fellow learner improve.
        </InlineAlert>
      )}

      {isSubmitter && !feedback && request.status !== 'completed' && (
        <Card padding="md" className="flex items-center gap-2 text-muted">
          <Clock className="h-5 w-5 shrink-0" aria-hidden="true" />
          <span className="text-sm">
            {request.status === 'open'
              ? 'Waiting for a peer to claim your review...'
              : 'A peer has claimed your review and is preparing feedback...'}
          </span>
        </Card>
      )}
    </>
  );
}
