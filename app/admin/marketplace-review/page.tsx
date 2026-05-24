'use client';

import { useEffect, useState, useCallback } from 'react';
import { AnimatePresence } from 'motion/react';
import {
  Store,
  CheckCircle2,
  XCircle,
  Clock,
  ChevronDown,
  BookOpen,
  Mic,
  Pen,
  Headphones,
} from 'lucide-react';
import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { ExamTypeBadge } from '@/components/domain';
import { MotionCollapse, MotionItem } from '@/components/ui/motion-primitives';
import { apiClient } from '@/lib/api';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Input } from '@/components/admin/ui/input';
import { Textarea } from '@/components/admin/ui/textarea';
import { Checkbox } from '@/components/admin/ui/checkbox';

type Submission = {
  id: string;
  contributorId: string;
  examFamilyCode: string;
  subtestCode: string;
  title: string;
  description: string | null;
  contentType: string;
  contentPayloadJson?: string;
  difficulty: string;
  tags: string | null;
  status: string;
  reviewedBy: string | null;
  reviewNotes: string | null;
  submittedAt: string;
};

const SUBTEST_ICONS: Record<string, typeof BookOpen> = {
  writing: Pen,
  speaking: Mic,
  reading: BookOpen,
  listening: Headphones,
};

const apiFetch = apiClient.request;

export default function AdminMarketplaceReviewPage() {
  const [items, setItems] = useState<Submission[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [reviewNotes, setReviewNotes] = useState('');
  const [processing, setProcessing] = useState<string | null>(null);
  const [createContentItem, setCreateContentItem] = useState(true);

  const loadPending = useCallback(async () => {
    try {
      const data = await apiFetch('/v1/admin/marketplace/pending?page=1&pageSize=50');
      setItems(Array.isArray(data.items) ? data.items : []);
    } catch {
      setError('Failed to load pending submissions.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void loadPending(); }, [loadPending]);

  async function handleReview(submissionId: string, decision: 'approved' | 'rejected') {
    setProcessing(submissionId);
    setError(null);
    try {
      await apiFetch(`/v1/admin/marketplace/submissions/${submissionId}/review`, {
        method: 'POST',
        body: JSON.stringify({
          decision,
          notes: reviewNotes || null,
          createContentItem: decision === 'approved' && createContentItem,
        }),
      });
      setItems(prev => prev.filter(item => item.id !== submissionId));
      setExpandedId(null);
      setReviewNotes('');
    } catch {
      setError(`Failed to ${decision === 'approved' ? 'approve' : 'reject'} submission.`);
    } finally {
      setProcessing(null);
    }
  }

  const dateFormatter = new Intl.DateTimeFormat('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });

  return (
    <AdminRouteWorkspace role="main" aria-label="Marketplace review">
      <AdminTableLayout
        title="Marketplace Review Queue"
        description="Review and moderate community-submitted content before publication."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Marketplace Review' },
        ]}
        banner={
          error ? (
            <Card surface="tinted-warning">
              <CardContent className="py-3 text-sm text-admin-fg-strong">
                {error}
              </CardContent>
            </Card>
          ) : undefined
        }
      >
        {loading ? (
          <div className="space-y-4 p-4 sm:p-5">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-32 w-full rounded-admin-lg" />
            ))}
          </div>
        ) : items.length === 0 ? (
          <div className="p-4 sm:p-5">
            <EmptyState
              variant="onboarding"
              size="md"
              illustration={<CheckCircle2 />}
              title="All caught up!"
              description="No pending submissions to review."
            />
          </div>
        ) : (
          <div className="p-4 sm:p-5 space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <h2 className="text-lg font-semibold text-admin-fg-strong">
                    Pending Submissions
                  </h2>
                  <p className="text-sm text-admin-fg-muted">
                    {items.length} submission{items.length !== 1 ? 's' : ''} awaiting review.
                  </p>
                </div>
                <Badge variant="warning" intensity="tinted" startIcon={<Clock className="h-3 w-3" />}>
                  {items.length} pending
                </Badge>
              </div>

              <AnimatePresence>
                {items.map((item, i) => {
                  const SubIcon = SUBTEST_ICONS[item.subtestCode] ?? BookOpen;
                  const isExpanded = expandedId === item.id;

                  return (
                    <MotionItem
                      key={item.id}
                      delayIndex={i}
                      className="rounded-admin-lg border border-admin-border bg-admin-bg-surface overflow-hidden"
                    >
                      <button
                        onClick={() => {
                          setExpandedId(isExpanded ? null : item.id);
                          setReviewNotes('');
                        }}
                        className="w-full text-left p-4 flex items-center justify-between gap-3 hover:bg-admin-bg-subtle transition-colors"
                      >
                        <div className="flex items-center gap-3 min-w-0">
                          <div className="p-2 rounded-admin-md bg-[var(--admin-warning-tint)] text-[var(--admin-warning)]">
                            <SubIcon className="w-5 h-5" />
                          </div>
                          <div className="min-w-0">
                            <div className="flex items-center gap-2 mb-0.5 flex-wrap">
                              <span className="text-sm font-semibold text-admin-fg-strong truncate">
                                {item.title}
                              </span>
                              <ExamTypeBadge examType={item.examFamilyCode} size="sm" />
                              <Badge variant="default" intensity="tinted" size="sm" className="capitalize">
                                {item.difficulty}
                              </Badge>
                            </div>
                            <div className="text-xs text-admin-fg-muted">
                              {item.subtestCode} • {item.contentType.replace(/_/g, ' ')} • Submitted {dateFormatter.format(new Date(item.submittedAt))}
                            </div>
                          </div>
                        </div>
                        <ChevronDown
                          className={`w-4 h-4 text-admin-fg-muted transition-transform ${isExpanded ? 'rotate-180' : ''}`}
                        />
                      </button>

                      <MotionCollapse open={isExpanded} className="border-t border-admin-border">
                        <div className="p-4 space-y-4">
                          {item.description && (
                            <div>
                              <div className="text-xs font-semibold text-admin-fg-muted mb-1 uppercase tracking-wider">
                                Description
                              </div>
                              <p className="text-sm text-admin-fg-strong">{item.description}</p>
                            </div>
                          )}

                          {item.tags && (
                            <div>
                              <div className="text-xs font-semibold text-admin-fg-muted mb-1 uppercase tracking-wider">
                                Tags
                              </div>
                              <div className="flex flex-wrap gap-1">
                                {item.tags.split(',').map(tag => (
                                  <Badge key={tag} variant="default" intensity="tinted" size="sm">
                                    {tag.trim()}
                                  </Badge>
                                ))}
                              </div>
                            </div>
                          )}

                          <div>
                            <label
                              htmlFor={`notes-${item.id}`}
                              className="text-xs font-semibold text-admin-fg-muted mb-1 uppercase tracking-wider block"
                            >
                              Review Notes (optional)
                            </label>
                            <Textarea
                              id={`notes-${item.id}`}
                              rows={2}
                              value={reviewNotes}
                              onChange={e => setReviewNotes(e.target.value)}
                              placeholder="Add feedback for the contributor..."
                            />
                          </div>

                          <label
                            htmlFor={`ci-${item.id}`}
                            className="flex items-center gap-2 cursor-pointer"
                          >
                            <Checkbox
                              id={`ci-${item.id}`}
                              checked={createContentItem}
                              onCheckedChange={(checked) => setCreateContentItem(checked === true)}
                            />
                            <span className="text-xs text-admin-fg-muted">
                              Create ContentItem (Draft) on approval
                            </span>
                          </label>

                          <div className="flex gap-3">
                            <Button
                              variant="primary"
                              onClick={() => handleReview(item.id, 'approved')}
                              loading={processing === item.id}
                              className="flex-1"
                            >
                              <CheckCircle2 className="w-4 h-4" /> Approve
                            </Button>
                            <Button
                              variant="destructive"
                              onClick={() => handleReview(item.id, 'rejected')}
                              loading={processing === item.id}
                              className="flex-1"
                            >
                              <XCircle className="w-4 h-4" /> Reject
                            </Button>
                          </div>
                        </div>
                      </MotionCollapse>
                    </MotionItem>
                  );
                })}
              </AnimatePresence>
          </div>
        )}
      </AdminTableLayout>
    </AdminRouteWorkspace>
  );
}
