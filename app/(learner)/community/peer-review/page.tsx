'use client';

import { useState, useEffect, useCallback } from 'react';
import { FileText, Send, ClipboardList, Star, Clock, ArrowRight } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { InlineAlert } from '@/components/ui/alert';
import { Textarea } from '@/components/ui/form-controls';
import { TabPanel, Tabs } from '@/components/ui/tabs';
import { apiClient } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { cn } from '@/lib/utils';

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

const TABS: { id: Tab; label: string }[] = [
  { id: 'submit', label: 'Submit for Review' },
  { id: 'available', label: 'Available Reviews' },
  { id: 'my-submissions', label: 'My Submissions' },
  { id: 'my-reviews', label: 'My Reviews' },
];

// Sub-test identity, not status (DESIGN.md §2).
const SKILL_CHIP: Record<string, string> = {
  writing: 'border-skill-writing/20 bg-skill-writing/10 text-skill-writing',
  speaking: 'border-skill-speaking/20 bg-skill-speaking/10 text-skill-speaking',
};

function SubtestBadge({ code }: { code: string }) {
  return <Badge variant="outline" className={cn('capitalize', SKILL_CHIP[code.toLowerCase()])}>{code}</Badge>;
}

function ListSkeleton() {
  return (
    <div className="space-y-3" aria-hidden="true">
      {[...Array(3)].map((_, i) => <Skeleton key={i} className="h-20 rounded-2xl" />)}
    </div>
  );
}

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
    <>
      <LearnerPageHero
        eyebrow="Community"
        title="Peer Review Exchange"
        description="Submit your writing or speaking practice for peer feedback, and help others improve by reviewing their work."
        icon={FileText}
      />

      <Tabs tabs={TABS} activeTab={activeTab} onChange={(id) => setActiveTab(id as Tab)} />

      {error && (
        <InlineAlert
          variant="error"
          action={activeTab !== 'submit'
            ? <Button size="sm" variant="outline" onClick={() => void loadTab(activeTab)}>Retry</Button>
            : undefined}
        >
          {error}
        </InlineAlert>
      )}

      <TabPanel id="submit" activeTab={activeTab}>
        <Card padding="lg" className="space-y-5">
          <LearnerSurfaceSectionHeader title="Submit Your Work for Peer Review" />

          <div className="space-y-2" role="group" aria-labelledby="peer-review-subtest-label">
            <p id="peer-review-subtest-label" className="text-sm font-semibold tracking-tight text-navy">
              Subtest
            </p>
            <div className="flex flex-wrap gap-2">
              {(['writing', 'speaking'] as const).map((code) => (
                <Button
                  key={code}
                  size="sm"
                  variant={subtestCode === code ? 'primary' : 'outline'}
                  aria-pressed={subtestCode === code}
                  onClick={() => setSubtestCode(code)}
                  className="text-sm"
                >
                  {code === 'writing' ? 'Writing' : 'Speaking'}
                </Button>
              ))}
            </div>
          </div>

          <Textarea
            id="peer-review-submission"
            label="Your Submission"
            value={submissionText}
            onChange={(e) => setSubmissionText(e.target.value)}
            placeholder={subtestCode === 'writing'
              ? 'Paste your referral letter or case notes here...'
              : 'Describe your speaking scenario and transcript here...'}
            className="min-h-[200px] resize-y"
          />

          {submitSuccess && (
            <InlineAlert variant="success" live="polite">
              Submitted successfully! You will be notified when feedback is available.
            </InlineAlert>
          )}

          <Button
            variant="primary"
            onClick={handleSubmit}
            loading={submitting}
            disabled={!submissionText.trim()}
          >
            {submitting ? 'Submitting...' : 'Submit for Peer Review'}
          </Button>
        </Card>
      </TabPanel>

      <TabPanel id="available" activeTab={activeTab}>
        {loading ? (
          <ListSkeleton />
        ) : available.length === 0 ? (
          <EmptyState
            icon={<ClipboardList className="h-8 w-8" />}
            title="No reviews available"
            description="Check back later. Peers are submitting new work all the time."
          />
        ) : (
          <div className="space-y-3">
            {available.map((item, index) => (
              <MotionItem key={item.id} delayIndex={Math.min(index, 5)}>
                <Card padding="md" className="flex flex-wrap items-center justify-between gap-3">
                  <div className="flex min-w-0 flex-wrap items-center gap-2">
                    <SubtestBadge code={item.subtestCode} />
                    <span className="flex items-center gap-1 text-xs text-muted tabular-nums">
                      <Clock className="h-3 w-3" aria-hidden="true" />
                      {new Date(item.createdAt).toLocaleDateString()}
                    </span>
                  </div>
                  <Button size="sm" variant="primary" onClick={() => handleClaim(item.id)}>
                    Claim <ArrowRight className="h-3 w-3 rtl:rotate-180" aria-hidden="true" />
                  </Button>
                </Card>
              </MotionItem>
            ))}
          </div>
        )}
      </TabPanel>

      <TabPanel id="my-submissions" activeTab={activeTab}>
        {loading ? (
          <ListSkeleton />
        ) : mySubmissions.length === 0 ? (
          <EmptyState
            icon={<Send className="h-8 w-8" />}
            title="No submissions yet"
            description="Submit your writing or speaking practice to get peer feedback."
            action={{ label: 'Submit for Review', onClick: () => setActiveTab('submit') }}
          />
        ) : (
          <div className="space-y-3">
            {mySubmissions.map((item, index) => (
              <MotionItem key={item.id} delayIndex={Math.min(index, 5)}>
                <CardLink href={`/community/peer-review/${item.id}`} prefetch={false}>
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge variant={STATUS_VARIANTS[item.status] ?? 'default'} className="capitalize">
                        {item.status}
                      </Badge>
                      <SubtestBadge code={item.subtestCode} />
                    </div>
                    <span className="text-xs text-muted tabular-nums">
                      {new Date(item.createdAt).toLocaleDateString()}
                    </span>
                  </div>
                  {item.feedback && (
                    <div className="mt-3 flex min-w-0 items-center gap-1 border-t border-border pt-3 text-sm">
                      <Star className="h-4 w-4 shrink-0 fill-warning text-warning-strong" aria-hidden="true" />
                      <span className="font-medium tabular-nums text-navy">{item.feedback.rating}/5</span>
                      <span className="ms-2 truncate text-muted">
                        {item.feedback.comments}
                      </span>
                    </div>
                  )}
                </CardLink>
              </MotionItem>
            ))}
          </div>
        )}
      </TabPanel>

      <TabPanel id="my-reviews" activeTab={activeTab}>
        {loading ? (
          <ListSkeleton />
        ) : myReviews.length === 0 ? (
          <EmptyState
            icon={<Star className="h-8 w-8" />}
            title="No reviews yet"
            description="Claim available reviews to help peers and build your review reputation."
            action={{ label: 'Browse Available Reviews', onClick: () => setActiveTab('available') }}
          />
        ) : (
          <div className="space-y-3">
            {myReviews.map((item, index) => (
              <MotionItem key={item.id} delayIndex={Math.min(index, 5)}>
                <CardLink href={`/community/peer-review/${item.id}`} prefetch={false}>
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="flex flex-wrap items-center gap-2">
                      <Badge variant={STATUS_VARIANTS[item.status] ?? 'default'} className="capitalize">
                        {item.status}
                      </Badge>
                      <SubtestBadge code={item.subtestCode} />
                    </div>
                    <span className="text-xs text-muted tabular-nums">
                      {item.claimedAt ? new Date(item.claimedAt).toLocaleDateString() : ''}
                    </span>
                  </div>
                </CardLink>
              </MotionItem>
            ))}
          </div>
        )}
      </TabPanel>
    </>
  );
}
