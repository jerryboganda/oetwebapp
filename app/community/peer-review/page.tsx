'use client';

import { useState, useEffect, useCallback } from 'react';
import { FileText, Send, ClipboardList, Star, CheckCircle, Clock, ArrowRight } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { apiClient } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import Link from 'next/link';

type Tab = 'submit' | 'available' | 'my-submissions' | 'my-reviews';

interface PeerReviewRequestItem {
  id: string;
  subtestCode: string;
  status: string;
  createdAt: string;
  completedAt?: string;
  claimedAt?: string;
  feedback?: {
    id: string;
    comments: string;
    rating: number;
    strengthNotes?: string;
    improvementNotes?: string;
    createdAt: string;
  };
}

const STATUS_VARIANTS: Record<string, 'default' | 'success' | 'warning' | 'danger'> = {
  open: 'warning',
  claimed: 'default',
  completed: 'success',
  expired: 'danger',
};

const TABS: { key: Tab; label: string }[] = [
  { key: 'submit', label: 'Submit for Review' },
  { key: 'available', label: 'Available Reviews' },
  { key: 'my-submissions', label: 'My Submissions' },
  { key: 'my-reviews', label: 'My Reviews' },
];

export default function PeerReviewPage() {
  const [activeTab, setActiveTab] = useState<Tab>('submit');
  const [submissionText, setSubmissionText] = useState('');
  const [subtestCode, setSubtestCode] = useState<'writing' | 'speaking'>('writing');
  const [submitting, setSubmitting] = useState(false);
  const [submitSuccess, setSubmitSuccess] = useState(false);

  const [available, setAvailable] = useState<PeerReviewRequestItem[]>([]);
  const [mySubmissions, setMySubmissions] = useState<PeerReviewRequestItem[]>([]);
  const [myReviews, setMyReviews] = useState<PeerReviewRequestItem[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('page_viewed', { page: 'community-peer-review' });
  }, []);

  const loadTab = useCallback(async (tab: Tab) => {
    if (tab === 'submit') return;
    setLoading(true);
    setError(null);
    try {
      if (tab === 'available') {
        const data = await apiClient.get<PeerReviewRequestItem[]>('/v1/community/peer-review/available');
        setAvailable(Array.isArray(data) ? data : []);
      } else if (tab === 'my-submissions') {
        const data = await apiClient.get<PeerReviewRequestItem[]>('/v1/community/peer-review/my-submissions');
        setMySubmissions(Array.isArray(data) ? data : []);
      } else if (tab === 'my-reviews') {
        const data = await apiClient.get<PeerReviewRequestItem[]>('/v1/community/peer-review/my-reviews');
        setMyReviews(Array.isArray(data) ? data : []);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load data');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadTab(activeTab);
  }, [activeTab, loadTab]);

  const handleSubmit = async () => {
    if (!submissionText.trim()) return;
    setSubmitting(true);
    setSubmitSuccess(false);
    try {
      await apiClient.post('/v1/community/peer-review/submit', {
        subtestCode,
        submissionText: submissionText.trim(),
      });
      setSubmitSuccess(true);
      setSubmissionText('');
      analytics.track('peer_review_submitted', { subtestCode });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to submit');
    } finally {
      setSubmitting(false);
    }
  };

  const handleClaim = async (requestId: string) => {
    try {
      await apiClient.post(`/v1/community/peer-review/${encodeURIComponent(requestId)}/claim`);
      analytics.track('peer_review_claimed', { requestId });
      loadTab('available');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to claim review');
    }
  };

  return (
    <LearnerDashboardShell pageTitle="Peer Review Exchange">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Community"
          title="Peer Review Exchange"
          description="Submit your writing or speaking practice for peer feedback, and help others improve by reviewing their work."
          icon={FileText}
          highlights={[
            { icon: Send, label: 'Submit', value: 'Get peer feedback' },
            { icon: ClipboardList, label: 'Review', value: 'Help others improve' },
            { icon: Star, label: 'Rating', value: '1–5 star system' },
          ]}
        />

        {/* Tab Navigation */}
        <div className="flex gap-1 border-b border-border overflow-x-auto">
          {TABS.map((tab) => (
            <button
              key={tab.key}
              onClick={() => setActiveTab(tab.key)}
              className={`px-4 py-2 text-sm font-medium whitespace-nowrap border-b-2 transition-colors ${
                activeTab === tab.key
                  ? 'border-primary text-primary'
                  : 'border-transparent text-muted-foreground hover:text-foreground'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>

        {error && (
          <div className="rounded-md bg-red-50 dark:bg-red-950/20 border border-red-200 dark:border-red-800 p-3 text-sm text-red-700 dark:text-red-300">
            {error}
          </div>
        )}

        {/* Submit Tab */}
        {activeTab === 'submit' && (
          <Card className="p-6 space-y-4">
            <LearnerSurfaceSectionHeader title="Submit Your Work for Peer Review" />

            <div className="space-y-3">
              <label className="block text-sm font-medium text-foreground">
                Subtest
              </label>
              <div className="flex gap-3">
                <button
                  onClick={() => setSubtestCode('writing')}
                  className={`px-4 py-2 rounded-md text-sm font-medium transition-colors ${
                    subtestCode === 'writing'
                      ? 'bg-primary text-primary-foreground'
                      : 'bg-muted text-muted-foreground hover:bg-muted/80'
                  }`}
                >
                  Writing
                </button>
                <button
                  onClick={() => setSubtestCode('speaking')}
                  className={`px-4 py-2 rounded-md text-sm font-medium transition-colors ${
                    subtestCode === 'speaking'
                      ? 'bg-primary text-primary-foreground'
                      : 'bg-muted text-muted-foreground hover:bg-muted/80'
                  }`}
                >
                  Speaking
                </button>
              </div>
            </div>

            <div className="space-y-2">
              <label className="block text-sm font-medium text-foreground">
                Your Submission
              </label>
              <textarea
                value={submissionText}
                onChange={(e) => setSubmissionText(e.target.value)}
                placeholder={subtestCode === 'writing'
                  ? 'Paste your referral letter or case notes here...'
                  : 'Describe your speaking scenario and transcript here...'}
                className="w-full min-h-[200px] rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring resize-y"
              />
            </div>

            {submitSuccess && (
              <div className="flex items-center gap-2 text-sm text-green-700 dark:text-green-400">
                <CheckCircle className="w-4 h-4" />
                Submitted successfully! You will be notified when feedback is available.
              </div>
            )}

            <Button
              variant="primary"
              onClick={handleSubmit}
              disabled={submitting || !submissionText.trim()}
            >
              {submitting ? 'Submitting...' : 'Submit for Peer Review'}
            </Button>
          </Card>
        )}

        {/* Available Reviews Tab */}
        {activeTab === 'available' && (
          <div className="space-y-4">
            <LearnerSurfaceSectionHeader title="Available Reviews" />
            {loading ? (
              <div className="space-y-3">
                {[...Array(3)].map((_, i) => <Skeleton key={i} className="h-20 rounded-lg" />)}
              </div>
            ) : available.length === 0 ? (
              <EmptyState
                title="No reviews available"
                description="Check back later. Peers are submitting new work all the time."
              />
            ) : (
              <MotionSection className="space-y-3">
                {available.map((item) => (
                  <MotionItem key={item.id}>
                    <Card className="p-4 flex items-center justify-between gap-4">
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-1">
                          <Badge variant={STATUS_VARIANTS[item.status] ?? 'default'}>
                            {item.subtestCode}
                          </Badge>
                          <span className="text-xs text-muted-foreground flex items-center gap-1">
                            <Clock className="w-3 h-3" />
                            {new Date(item.createdAt).toLocaleDateString()}
                          </span>
                        </div>
                      </div>
                      <Button size="sm" variant="primary" onClick={() => handleClaim(item.id)}>
                        Claim <ArrowRight className="w-3 h-3 ml-1" />
                      </Button>
                    </Card>
                  </MotionItem>
                ))}
              </MotionSection>
            )}
          </div>
        )}

        {/* My Submissions Tab */}
        {activeTab === 'my-submissions' && (
          <div className="space-y-4">
            <LearnerSurfaceSectionHeader title="My Submissions" />
            {loading ? (
              <div className="space-y-3">
                {[...Array(3)].map((_, i) => <Skeleton key={i} className="h-20 rounded-lg" />)}
              </div>
            ) : mySubmissions.length === 0 ? (
              <EmptyState
                title="No submissions yet"
                description="Submit your writing or speaking practice to get peer feedback."
              />
            ) : (
              <MotionSection className="space-y-3">
                {mySubmissions.map((item) => (
                  <MotionItem key={item.id}>
                    <Link href={`/community/peer-review/${item.id}`}>
                      <Card className="p-4 hover:shadow-clinical transition-shadow duration-200">
                        <div className="flex items-center justify-between">
                          <div className="flex items-center gap-2">
                            <Badge variant={STATUS_VARIANTS[item.status] ?? 'default'}>
                              {item.status}
                            </Badge>
                            <Badge variant="default">{item.subtestCode}</Badge>
                          </div>
                          <span className="text-xs text-muted-foreground">
                            {new Date(item.createdAt).toLocaleDateString()}
                          </span>
                        </div>
                        {item.feedback && (
                          <div className="mt-3 pt-3 border-t border-border">
                            <div className="flex items-center gap-1 text-sm">
                              <Star className="w-4 h-4 text-yellow-500" />
                              <span className="font-medium">{item.feedback.rating}/5</span>
                              <span className="text-muted-foreground ml-2 truncate">
                                {item.feedback.comments}
                              </span>
                            </div>
                          </div>
                        )}
                      </Card>
                    </Link>
                  </MotionItem>
                ))}
              </MotionSection>
            )}
          </div>
        )}

        {/* My Reviews Tab */}
        {activeTab === 'my-reviews' && (
          <div className="space-y-4">
            <LearnerSurfaceSectionHeader title="My Reviews" />
            {loading ? (
              <div className="space-y-3">
                {[...Array(3)].map((_, i) => <Skeleton key={i} className="h-20 rounded-lg" />)}
              </div>
            ) : myReviews.length === 0 ? (
              <EmptyState
                title="No reviews yet"
                description="Claim available reviews to help peers and build your review reputation."
              />
            ) : (
              <MotionSection className="space-y-3">
                {myReviews.map((item) => (
                  <MotionItem key={item.id}>
                    <Link href={`/community/peer-review/${item.id}`}>
                      <Card className="p-4 hover:shadow-clinical transition-shadow duration-200">
                        <div className="flex items-center justify-between">
                          <div className="flex items-center gap-2">
                            <Badge variant={STATUS_VARIANTS[item.status] ?? 'default'}>
                              {item.status}
                            </Badge>
                            <Badge variant="default">{item.subtestCode}</Badge>
                          </div>
                          <span className="text-xs text-muted-foreground">
                            {item.claimedAt ? new Date(item.claimedAt).toLocaleDateString() : ''}
                          </span>
                        </div>
                      </Card>
                    </Link>
                  </MotionItem>
                ))}
              </MotionSection>
            )}
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
