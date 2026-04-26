'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { CheckCircle2, Clock, MessageSquare, Send, Star, Users } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';

interface PeerItem { id: string; subtestCode: string; attemptId?: string; status?: string; createdAt?: string; claimedAt?: string; completedAt?: string; feedback?: { rating: number; comments: string; strengths?: string; improvements?: string }[] }
interface PoolData { availableToReview: PeerItem[]; mySubmissions: PeerItem[]; myReviews: PeerItem[]; stats: { reviewsGiven: number; reviewsReceived: number; averageHelpfulness: number } }

async function api<T>(path: string, init?: RequestInit): Promise<T> {
  return apiClient.request<T>(path, init);
}

export default function PeerReviewPage() {
  const [data, setData] = useState<PoolData | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<'available' | 'mine' | 'given'>('available');

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try { setData(await api<PoolData>('/v1/learner/peer-reviews')); } catch (e) { setError(e instanceof Error ? e.message : 'Load failed'); } finally { setLoading(false); }
  }, []);

  useEffect(() => { load(); analytics.track('peer_review_viewed'); }, [load]);

  const claim = async (id: string) => {
    try { await api(`/v1/learner/peer-reviews/${id}/claim`, { method: 'POST' }); analytics.track('peer_review_claimed', { id }); load(); } catch { setError('Failed to claim'); }
  };

  return (
    <LearnerDashboardShell>
      <LearnerPageHero title="Peer Review Exchange" description="Give and receive feedback from fellow OET learners." />

      <MotionSection className="max-w-4xl mx-auto space-y-6">
        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {data && (
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
            <Card className="p-4 text-center"><div className="text-2xl font-bold">{data.stats.reviewsGiven}</div><div className="text-xs text-muted-foreground">Reviews Given</div></Card>
            <Card className="p-4 text-center"><div className="text-2xl font-bold">{data.stats.reviewsReceived}</div><div className="text-xs text-muted-foreground">Reviews Received</div></Card>
            <Card className="p-4 text-center"><div className="text-2xl font-bold">{data.stats.averageHelpfulness > 0 ? data.stats.averageHelpfulness.toFixed(1) : '—'}</div><div className="text-xs text-muted-foreground">Avg Helpfulness</div></Card>
          </div>
        )}

        <div className="flex flex-wrap gap-2">
          {(['available', 'mine', 'given'] as const).map(t => (
            <Button key={t} variant={tab === t ? 'secondary' : 'outline'} size="sm" onClick={() => setTab(t)}>
              {t === 'available' ? 'Available' : t === 'mine' ? 'My Submissions' : 'My Reviews'}
            </Button>
          ))}
        </div>

        {loading && <div className="space-y-3">{[1,2,3].map(i => <Skeleton key={i} className="h-20 rounded-lg" />)}</div>}

        {data && tab === 'available' && (
          <div className="space-y-3">
            <LearnerSurfaceSectionHeader title={`${data.availableToReview.length} Submissions Awaiting Review`} />
            {data.availableToReview.map(item => (
              <MotionItem key={item.id}>
                <Card className="p-4 flex items-center justify-between">
                  <div>
                    <Badge variant="outline" className="mr-2">{item.subtestCode}</Badge>
                    <span className="text-sm text-muted-foreground">{item.createdAt ? new Date(item.createdAt).toLocaleDateString() : ''}</span>
                  </div>
                  <Button size="sm" onClick={() => claim(item.id)}><MessageSquare className="w-4 h-4 mr-1" /> Claim & Review</Button>
                </Card>
              </MotionItem>
            ))}
            {data.availableToReview.length === 0 && <Card className="p-6 text-center text-muted-foreground"><Users className="w-10 h-10 mx-auto mb-2 opacity-30" />No submissions available right now.</Card>}
          </div>
        )}

        {data && tab === 'mine' && (
          <div className="space-y-3">
            <LearnerSurfaceSectionHeader title="My Peer Review Submissions" />
            {data.mySubmissions.map(item => (
              <MotionItem key={item.id}>
                <Card className="p-4">
                  <div className="flex items-center gap-2 mb-2">
                    <Badge variant="outline">{item.subtestCode}</Badge>
                    <Badge variant={item.status === 'completed' ? 'default' : 'muted'}>
                      {item.status === 'completed' ? <><CheckCircle2 className="w-3 h-3 mr-1" /> Reviewed</> : <><Clock className="w-3 h-3 mr-1" /> {item.status}</>}
                    </Badge>
                  </div>
                  {item.feedback && item.feedback.length > 0 && item.feedback.map((fb, i) => (
                    <div key={i} className="mt-2 bg-success/10 rounded-lg p-3 text-sm">
                      <div className="flex items-center gap-1 mb-1">{Array.from({length: fb.rating}).map((_,j) => <Star key={j} className="w-3 h-3 fill-warning text-warning" />)}</div>
                      <p>{fb.comments}</p>
                      {fb.strengths && <p className="mt-1 text-success"><strong>Strengths:</strong> {fb.strengths}</p>}
                      {fb.improvements && <p className="mt-1 text-warning"><strong>To improve:</strong> {fb.improvements}</p>}
                    </div>
                  ))}
                </Card>
              </MotionItem>
            ))}
            {data.mySubmissions.length === 0 && <Card className="p-6 text-center text-muted-foreground"><Send className="w-10 h-10 mx-auto mb-2 opacity-30" />Submit your writing or speaking attempts for peer feedback.</Card>}
          </div>
        )}

        {data && tab === 'given' && (
          <div className="space-y-3">
            <LearnerSurfaceSectionHeader title="Reviews I&apos;ve Given" />
            {data.myReviews.map(item => (
              <MotionItem key={item.id}>
                <Card className="p-4 flex items-center justify-between">
                  <div>
                    <Badge variant="outline" className="mr-2">{item.subtestCode}</Badge>
                    <Badge variant={item.status === 'completed' ? 'default' : 'muted'}>{item.status}</Badge>
                  </div>
                  <span className="text-xs text-muted-foreground">{item.completedAt ? new Date(item.completedAt).toLocaleDateString() : item.claimedAt ? 'In Progress' : ''}</span>
                </Card>
              </MotionItem>
            ))}
            {data.myReviews.length === 0 && <Card className="p-6 text-center text-muted-foreground">You haven&apos;t reviewed any peers yet.</Card>}
          </div>
        )}
      </MotionSection>
    </LearnerDashboardShell>
  );
}
