'use client';

import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'next/navigation';
import { FileText, Star, Send, ArrowLeft, CheckCircle, Clock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
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

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Peer Review">
        <div className="space-y-4">
          <Skeleton className="h-8 w-48" />
          <Skeleton className="h-40 rounded-lg" />
          <Skeleton className="h-32 rounded-lg" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (error || !request) {
    return (
      <LearnerDashboardShell pageTitle="Peer Review">
        <div className="space-y-4">
          <Link href="/community/peer-review" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
            <ArrowLeft className="w-4 h-4" /> Back to Peer Reviews
          </Link>
          <Card className="p-6 text-center">
            <p className="text-muted-foreground">{error ?? 'Review not found'}</p>
          </Card>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Peer Review">
      <div className="space-y-6">
        <Link href="/community/peer-review" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground">
          <ArrowLeft className="w-4 h-4" /> Back to Peer Reviews
        </Link>

        <LearnerPageHero
          eyebrow="Peer Review"
          title={`${request.subtestCode.charAt(0).toUpperCase() + request.subtestCode.slice(1)} Review`}
          description={isReviewer ? 'Provide feedback for this submission.' : 'View your submission status and feedback.'}
          icon={FileText}
        />

        {/* Status Card */}
        <Card className="p-5">
          <div className="flex items-center justify-between mb-3">
            <h3 className="font-semibold text-sm">Review Status</h3>
            <Badge variant={STATUS_VARIANTS[request.status] ?? 'default'}>
              {request.status}
            </Badge>
          </div>
          <div className="grid grid-cols-2 md:grid-cols-3 gap-3 text-sm">
            <div>
              <span className="text-muted-foreground">Subtest</span>
              <p className="font-medium capitalize">{request.subtestCode}</p>
            </div>
            <div>
              <span className="text-muted-foreground">Submitted</span>
              <p className="font-medium">{new Date(request.createdAt).toLocaleDateString()}</p>
            </div>
            {request.claimedAt && (
              <div>
                <span className="text-muted-foreground">Claimed</span>
                <p className="font-medium">{new Date(request.claimedAt).toLocaleDateString()}</p>
              </div>
            )}
            {request.completedAt && (
              <div>
                <span className="text-muted-foreground">Completed</span>
                <p className="font-medium">{new Date(request.completedAt).toLocaleDateString()}</p>
              </div>
            )}
          </div>
        </Card>

        {/* Feedback Section — shown for submitter when feedback exists */}
        {isSubmitter && feedback && (
          <Card className="p-5 space-y-3">
            <h3 className="font-semibold text-sm">Peer Feedback</h3>
            <div className="flex items-center gap-1">
              {[1, 2, 3, 4, 5].map((star) => (
                <Star
                  key={star}
                  className={`w-5 h-5 ${star <= feedback.rating ? 'text-yellow-500 fill-yellow-500' : 'text-muted-foreground'}`}
                />
              ))}
              <span className="ml-2 text-sm font-medium">{feedback.rating}/5</span>
            </div>
            <p className="text-sm text-foreground whitespace-pre-wrap">{feedback.comments}</p>
            {feedback.strengthNotes && (
              <div>
                <span className="text-xs font-medium text-green-700 dark:text-green-400">Strengths</span>
                <p className="text-sm text-muted-foreground">{feedback.strengthNotes}</p>
              </div>
            )}
            {feedback.improvementNotes && (
              <div>
                <span className="text-xs font-medium text-amber-700 dark:text-amber-400">Areas for Improvement</span>
                <p className="text-sm text-muted-foreground">{feedback.improvementNotes}</p>
              </div>
            )}
          </Card>
        )}

        {/* Feedback Form — shown for reviewer on claimed requests */}
        {isReviewer && request.status === 'claimed' && !submitSuccess && (
          <Card className="p-5 space-y-4">
            <h3 className="font-semibold text-sm">Submit Your Feedback</h3>

            <div className="space-y-2">
              <label className="block text-sm font-medium text-foreground">Rating</label>
              <div className="flex items-center gap-1">
                {[1, 2, 3, 4, 5].map((star) => (
                  <button
                    key={star}
                    type="button"
                    onClick={() => setRating(star)}
                    className="p-0.5 transition-transform hoverable:scale-110 active:scale-95 motion-reduce:active:scale-100"
                  >
                    <Star
                      className={`w-6 h-6 ${star <= rating ? 'text-yellow-500 fill-yellow-500' : 'text-muted-foreground'}`}
                    />
                  </button>
                ))}
                {rating > 0 && <span className="ml-2 text-sm text-muted-foreground">{rating}/5</span>}
              </div>
            </div>

            <div className="space-y-2">
              <label className="block text-sm font-medium text-foreground">
                Your Feedback
              </label>
              <textarea
                value={feedbackText}
                onChange={(e) => setFeedbackText(e.target.value)}
                placeholder="Provide constructive feedback on this submission..."
                className="w-full min-h-[150px] rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring resize-y"
              />
            </div>

            <Button
              variant="primary"
              onClick={handleSubmitFeedback}
              disabled={submitting || !feedbackText.trim() || rating < 1}
            >
              <Send className="w-4 h-4 mr-1" />
              {submitting ? 'Submitting...' : 'Submit Feedback'}
            </Button>
          </Card>
        )}

        {/* Success message after feedback */}
        {submitSuccess && (
          <Card className="p-5">
            <div className="flex items-center gap-2 text-green-700 dark:text-green-400">
              <CheckCircle className="w-5 h-5" />
              <span className="font-medium">Feedback submitted successfully!</span>
            </div>
            <p className="text-sm text-muted-foreground mt-2">
              Thank you for helping a fellow learner improve.
            </p>
          </Card>
        )}

        {/* Waiting state for submitter */}
        {isSubmitter && !feedback && request.status !== 'completed' && (
          <Card className="p-5">
            <div className="flex items-center gap-2 text-muted-foreground">
              <Clock className="w-5 h-5" />
              <span className="text-sm">
                {request.status === 'open'
                  ? 'Waiting for a peer to claim your review...'
                  : 'A peer has claimed your review and is preparing feedback...'}
              </span>
            </div>
          </Card>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
