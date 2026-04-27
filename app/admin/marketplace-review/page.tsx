'use client';

import { useEffect, useState, useCallback } from 'react';
import { AnimatePresence } from 'motion/react';
import { Store, CheckCircle2, XCircle, Clock, ChevronDown, BookOpen, Mic, Pen, Headphones } from 'lucide-react';
import { AdminRouteHero, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { ExamTypeBadge } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionCollapse, MotionItem } from '@/components/ui/motion-primitives';
import { apiClient } from '@/lib/api';

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
        body: JSON.stringify({ decision, notes: reviewNotes || null, createContentItem: decision === 'approved' && createContentItem }),
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

  const dateFormatter = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', year: 'numeric', hour: '2-digit', minute: '2-digit' });

  return (
    <AdminRouteWorkspace role="main" aria-label="Marketplace review">
      <AdminRouteHero
        title="Marketplace Review Queue"
        description="Review and moderate community-submitted content before publication."
        icon={Store}
        accent="amber"
      />

      {error && <InlineAlert variant="warning">{error}</InlineAlert>}

      {loading ? (
        <AdminRoutePanel>
          <div className="space-y-4">
            {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-32 rounded-xl" />)}
          </div>
        </AdminRoutePanel>
      ) : items.length === 0 ? (
        <AdminRoutePanel>
          <div className="text-center py-16">
            <CheckCircle2 className="w-12 h-12 text-success mx-auto mb-3" />
            <p className="text-sm font-bold text-navy">All caught up!</p>
            <p className="text-xs text-muted mt-1">No pending submissions to review.</p>
          </div>
        </AdminRoutePanel>
      ) : (
        <AdminRoutePanel
          title="Pending Submissions"
          description={`${items.length} submission${items.length !== 1 ? 's' : ''} awaiting review.`}
        >
          <div className="text-sm text-muted">
            <Clock className="w-4 h-4 inline mr-1" /> {items.length} submission{items.length !== 1 ? 's' : ''} pending review
          </div>

          <AnimatePresence>
            {items.map((item, i) => {
              const SubIcon = SUBTEST_ICONS[item.subtestCode] ?? BookOpen;
              const isExpanded = expandedId === item.id;

              return (
                <MotionItem
                  key={item.id}
                  delayIndex={i}
                  className="bg-surface dark:bg-surface rounded-xl border border-border dark:border-border overflow-hidden"
                >
                  <button
                    onClick={() => { setExpandedId(isExpanded ? null : item.id); setReviewNotes(''); }}
                    className="w-full text-left p-4 flex items-center justify-between gap-3 hover:bg-surface dark:hover:bg-surface transition-colors"
                  >
                    <div className="flex items-center gap-3 min-w-0">
                      <div className="p-2 rounded-lg bg-warning/10 text-warning">
                        <SubIcon className="w-5 h-5" />
                      </div>
                      <div className="min-w-0">
                        <div className="flex items-center gap-2 mb-0.5">
                          <span className="text-sm font-semibold text-navy dark:text-navy truncate">{item.title}</span>
                          <ExamTypeBadge examType={item.examFamilyCode} size="sm" />
                          <span className="text-xs bg-lavender/30 dark:bg-surface text-muted px-1.5 py-0.5 rounded capitalize">{item.difficulty}</span>
                        </div>
                        <div className="text-xs text-muted">
                          {item.subtestCode} • {item.contentType.replace(/_/g, ' ')} • Submitted {dateFormatter.format(new Date(item.submittedAt))}
                        </div>
                      </div>
                    </div>
                    <ChevronDown className={`w-4 h-4 text-muted transition-transform ${isExpanded ? 'rotate-180' : ''}`} />
                  </button>

                  <MotionCollapse open={isExpanded} className="border-t border-border dark:border-border">
                      <div className="p-4 space-y-4">
                        {item.description && (
                          <div>
                            <div className="text-xs font-semibold text-muted mb-1 uppercase">Description</div>
                            <p className="text-sm text-navy dark:text-navy">{item.description}</p>
                          </div>
                        )}

                        {item.tags && (
                          <div>
                            <div className="text-xs font-semibold text-muted mb-1 uppercase">Tags</div>
                            <div className="flex flex-wrap gap-1">
                              {item.tags.split(',').map(tag => (
                                <span key={tag} className="text-xs bg-lavender/30 dark:bg-surface text-muted dark:text-muted px-2 py-0.5 rounded-full">
                                  {tag.trim()}
                                </span>
                              ))}
                            </div>
                          </div>
                        )}

                        <div>
                          <div className="text-xs font-semibold text-muted mb-1 uppercase">Review Notes (optional)</div>
                          <textarea rows={2} value={reviewNotes} onChange={e => setReviewNotes(e.target.value)}
                            placeholder="Add feedback for the contributor..."
                            className="w-full px-3 py-2 bg-background-light dark:bg-surface border border-border dark:border-border rounded-lg text-sm outline-none focus:ring-2 focus:ring-primary resize-none" />
                        </div>

                        <div className="flex items-center gap-2">
                          <input type="checkbox" id={`ci-${item.id}`} checked={createContentItem} onChange={e => setCreateContentItem(e.target.checked)}
                            className="w-4 h-4 text-warning rounded" />
                          <label htmlFor={`ci-${item.id}`} className="text-xs text-muted dark:text-muted">
                            Create ContentItem (Draft) on approval
                          </label>
                        </div>

                        <div className="flex gap-3">
                          <Button onClick={() => handleReview(item.id, 'approved')} loading={processing === item.id} fullWidth className="bg-success hover:bg-success/90">
                            <CheckCircle2 className="w-4 h-4" /> Approve
                          </Button>
                          <Button onClick={() => handleReview(item.id, 'rejected')} loading={processing === item.id} variant="destructive" fullWidth>
                            <XCircle className="w-4 h-4" /> Reject
                          </Button>
                        </div>
                      </div>
                  </MotionCollapse>
                </MotionItem>
              );
            })}
          </AnimatePresence>
        </AdminRoutePanel>
      )}
    </AdminRouteWorkspace>
  );
}
