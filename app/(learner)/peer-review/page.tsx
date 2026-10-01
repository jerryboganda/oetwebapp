'use client';

import { useEffect, useState, useCallback } from 'react';
import { Users, Send, Star, CheckCircle2, Clock, MessageSquare, ClipboardList } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { CountUp } from '@/components/ui/count-up';
import { TabPanel, Tabs } from '@/components/ui/tabs';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { cn } from '@/lib/utils';

interface PeerItem { id: string; subtestCode: string; attemptId?: string; status?: string; createdAt?: string; claimedAt?: string; completedAt?: string; feedback?: { rating: number; comments: string; strengths?: string; improvements?: string }[] }
interface PoolData { availableToReview: PeerItem[]; mySubmissions: PeerItem[]; myReviews: PeerItem[]; stats: { reviewsGiven: number; reviewsReceived: number; averageHelpfulness: number } }

type PeerTab = 'available' | 'mine' | 'given';

const TABS: { id: PeerTab; label: string }[] = [
  { id: 'available', label: 'Available' },
  { id: 'mine', label: 'My Submissions' },
  { id: 'given', label: 'My Reviews' },
];

// Sub-test identity, not status (DESIGN.md §2).
const SKILL_CHIP: Record<string, string> = {
  writing: 'border-skill-writing/20 bg-skill-writing/10 text-skill-writing',
  speaking: 'border-skill-speaking/20 bg-skill-speaking/10 text-skill-speaking',
};

function SubtestBadge({ code }: { code: string }) {
  return <Badge variant="outline" className={cn('capitalize', SKILL_CHIP[code.toLowerCase()])}>{code}</Badge>;
}

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  return apiClient.request<T>(path, init);
}

export default function PeerReviewPage() {
  const [data, setData] = useState<PoolData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<PeerTab>('available');

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try { setData(await api<PoolData>('/v1/learner/peer-reviews')); } catch (e) { setError(e instanceof Error ? e.message : 'Load failed'); } finally { setLoading(false); }
  }, []);

  useEffect(() => { load(); analytics.track('peer_review_viewed'); }, [load]);

  const claim = async (id: string) => {
    try { await api(`/v1/learner/peer-reviews/${id}/claim`, { method: 'POST' }); analytics.track('peer_review_claimed', { id }); load(); } catch { setError('Failed to claim'); }
  };

  return (
    <>
      <LearnerPageHero title="Peer Review Exchange" description="Give and receive feedback from fellow OET learners." icon={Users} />

      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      {data && (
        <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,8.5rem),1fr))] gap-3">
          <Card padding="md" className="text-center">
            <div className="text-2xl font-bold text-navy"><CountUp value={data.stats.reviewsGiven} /></div>
            <div className="mt-1 text-xs text-muted">Reviews Given</div>
          </Card>
          <Card padding="md" className="text-center">
            <div className="text-2xl font-bold text-navy"><CountUp value={data.stats.reviewsReceived} /></div>
            <div className="mt-1 text-xs text-muted">Reviews Received</div>
          </Card>
          <Card padding="md" className="text-center">
            <div className="text-2xl font-bold tabular-nums text-navy">{data.stats.averageHelpfulness > 0 ? data.stats.averageHelpfulness.toFixed(1) : 'N/A'}</div>
            <div className="mt-1 text-xs text-muted">Avg Helpfulness</div>
          </Card>
        </div>
      )}

      <Tabs tabs={TABS} activeTab={tab} onChange={(id) => setTab(id as PeerTab)} />

      {loading && (
        <div className="space-y-3" role="status" aria-busy="true" aria-label="Loading">
          {[1, 2, 3].map(i => <Skeleton aria-hidden key={i} className="h-20 rounded-2xl" />)}
        </div>
      )}

      {data && (
        <>
          <TabPanel id="available" activeTab={tab} className="space-y-3">
            <LearnerSurfaceSectionHeader title={`${data.availableToReview.length} Submissions Awaiting Review`} />
            {data.availableToReview.length === 0 ? (
              <EmptyState icon={<ClipboardList className="h-8 w-8" />} title="No submissions available right now." />
            ) : data.availableToReview.map((item, index) => (
              <MotionItem key={item.id} delayIndex={Math.min(index, 5)}>
                <Card padding="md" className="flex flex-wrap items-center justify-between gap-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <SubtestBadge code={item.subtestCode} />
                    <span className="text-sm text-muted tabular-nums">{item.createdAt ? new Date(item.createdAt).toLocaleDateString() : ''}</span>
                  </div>
                  <Button size="sm" onClick={() => claim(item.id)}><MessageSquare className="h-4 w-4" aria-hidden="true" /> Claim & Review</Button>
                </Card>
              </MotionItem>
            ))}
          </TabPanel>

          <TabPanel id="mine" activeTab={tab} className="space-y-3">
            <LearnerSurfaceSectionHeader title="My Peer Review Submissions" />
            {data.mySubmissions.length === 0 ? (
              <EmptyState icon={<Send className="h-8 w-8" />} title="Submit your writing or speaking attempts for peer feedback." />
            ) : data.mySubmissions.map((item, index) => (
              <MotionItem key={item.id} delayIndex={Math.min(index, 5)}>
                <Card padding="md">
                  <div className="flex flex-wrap items-center gap-2">
                    <SubtestBadge code={item.subtestCode} />
                    <Badge variant={item.status === 'completed' ? 'default' : 'muted'} className="gap-1">
                      {item.status === 'completed'
                        ? <><CheckCircle2 className="h-3 w-3" aria-hidden="true" /> Reviewed</>
                        : <><Clock className="h-3 w-3" aria-hidden="true" /> <span className="capitalize">{item.status}</span></>}
                    </Badge>
                  </div>
                  {item.feedback && item.feedback.length > 0 && item.feedback.map((fb, i) => (
                    <div key={i} className="mt-3 rounded-xl border border-success/20 bg-success/5 p-3 text-sm text-navy">
                      <div className="mb-1 flex items-center gap-1">
                        {Array.from({ length: fb.rating }).map((_, j) => <Star key={j} className="h-3 w-3 fill-warning text-warning-strong" aria-hidden="true" />)}
                        <span className="ms-1 text-xs font-medium tabular-nums text-muted">{fb.rating}/5</span>
                      </div>
                      <p className="whitespace-pre-wrap break-words">{fb.comments}</p>
                      {fb.strengths && <p className="mt-1 text-success-strong"><strong>Strengths:</strong> {fb.strengths}</p>}
                      {fb.improvements && <p className="mt-1 text-warning-strong"><strong>To improve:</strong> {fb.improvements}</p>}
                    </div>
                  ))}
                </Card>
              </MotionItem>
            ))}
          </TabPanel>

          <TabPanel id="given" activeTab={tab} className="space-y-3">
            <LearnerSurfaceSectionHeader title={"Reviews I've Given"} />
            {data.myReviews.length === 0 ? (
              <EmptyState icon={<Star className="h-8 w-8" />} title={"You haven't reviewed any peers yet."} />
            ) : data.myReviews.map((item, index) => (
              <MotionItem key={item.id} delayIndex={Math.min(index, 5)}>
                <Card padding="md" className="flex flex-wrap items-center justify-between gap-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <SubtestBadge code={item.subtestCode} />
                    <Badge variant={item.status === 'completed' ? 'default' : 'muted'} className="capitalize">{item.status}</Badge>
                  </div>
                  <span className="text-xs text-muted tabular-nums">{item.completedAt ? new Date(item.completedAt).toLocaleDateString() : item.claimedAt ? 'In Progress' : ''}</span>
                </Card>
              </MotionItem>
            ))}
          </TabPanel>
        </>
      )}
    </>
  );
}
