'use client';

import { useCallback, useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import {
  Shield,
  CheckCircle2,
  XCircle,
  Clock,
  ChevronDown,
  BookOpen,
  Mic,
  Pen,
  Headphones,
} from 'lucide-react';
import { ExamTypeBadge } from '@/components/domain';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRoutePanelFooter,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Checkbox, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';

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

type Status = 'loading' | 'error' | 'empty' | 'success';

const SUBTEST_ICONS: Record<string, typeof BookOpen> = {
  writing: Pen,
  speaking: Mic,
  reading: BookOpen,
  listening: Headphones,
};

async function apiFetch(path: string, options?: RequestInit) {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const token = await ensureFreshAccessToken();
  const { env } = await import('@/lib/env');
  const res = await fetch(`${env.apiBaseUrl}${path}`, {
    ...options,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}`, ...options?.headers },
  });
  if (!res.ok) throw new Error(`API ${res.status}`);
  return res.json();
}

export default function AdminMarketplaceReviewPage() {
  const [items, setItems] = useState<Submission[]>([]);
  const [status, setStatus] = useState<Status>('loading');
  const [error, setError] = useState<string | null>(null);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [reviewNotes, setReviewNotes] = useState('');
  const [processing, setProcessing] = useState<string | null>(null);
  const [createContentItem, setCreateContentItem] = useState(true);

  const loadPending = useCallback(async () => {
    setStatus('loading');
    try {
      const data = await apiFetch('/v1/admin/marketplace/pending?page=1&pageSize=50');
      const next = Array.isArray(data.items) ? data.items : [];
      setItems(next);
      setStatus(next.length === 0 ? 'empty' : 'success');
    } catch {
      setError('Failed to load pending submissions.');
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    void loadPending();
  }, [loadPending]);

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
      setItems((prev) => {
        const next = prev.filter((item) => item.id !== submissionId);
        if (next.length === 0) setStatus('empty');
        return next;
      });
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
    <AdminRouteWorkspace role="main" aria-label="Marketplace review queue">
      <AdminRouteHero
        eyebrow="Content · Moderation"
        icon={Shield}
        accent="amber"
        title="Marketplace Review Queue"
        description="Review and moderate community-submitted content before publication."
        highlights={[{ icon: Clock, label: 'Pending', value: String(items.length) }]}
      />

      {error ? <InlineAlert variant="warning">{error}</InlineAlert> : null}

      <AdminRoutePanel
        eyebrow="Queue"
        title="Pending submissions"
        description="Expand a row to preview details, leave notes, and approve or reject."
      >
        <AsyncStateWrapper
          status={status}
          onRetry={() => void loadPending()}
          emptyContent={
            <EmptyState
              icon={<CheckCircle2 className="h-6 w-6 text-success" aria-hidden />}
              title="All caught up"
              description="No pending marketplace submissions. Check back after new contributor uploads."
            />
          }
        >
          <div className="space-y-3">
            <AnimatePresence>
              {items.map((item, i) => {
                const SubIcon = SUBTEST_ICONS[item.subtestCode] ?? BookOpen;
                const isExpanded = expandedId === item.id;

                return (
                  <motion.div
                    key={item.id}
                    layout
                    initial={{ opacity: 0, y: 10 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0, x: -50 }}
                    transition={{ delay: i * 0.04 }}
                    className="overflow-hidden rounded-2xl border border-border bg-surface shadow-sm"
                  >
                    <Button
                      type="button"
                      variant="ghost"
                      fullWidth
                      onClick={() => {
                        setExpandedId(isExpanded ? null : item.id);
                        setReviewNotes('');
                      }}
                      className="h-auto justify-between rounded-none p-4 text-left hover:bg-background-light"
                    >
                      <div className="flex min-w-0 items-center gap-3">
                        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-amber-50 text-amber-700">
                          <SubIcon className="h-5 w-5" aria-hidden />
                        </span>
                        <div className="min-w-0">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="truncate text-sm font-semibold text-navy">{item.title}</span>
                            <ExamTypeBadge examType={item.examFamilyCode} size="sm" />
                            <Badge variant="muted" className="capitalize">{item.difficulty}</Badge>
                          </div>
                          <div className="mt-0.5 text-xs text-muted">
                            {item.subtestCode} · {item.contentType.replace(/_/g, ' ')} · Submitted{' '}
                            {dateFormatter.format(new Date(item.submittedAt))}
                          </div>
                        </div>
                      </div>
                      <ChevronDown
                        className={`h-4 w-4 text-muted transition-transform ${isExpanded ? 'rotate-180' : ''}`}
                        aria-hidden
                      />
                    </Button>

                    {isExpanded && (
                      <motion.div
                        initial={{ height: 0, opacity: 0 }}
                        animate={{ height: 'auto', opacity: 1 }}
                        exit={{ height: 0, opacity: 0 }}
                        className="border-t border-border"
                      >
                        <div className="space-y-4 p-4">
                          {item.description ? (
                            <div>
                              <p className="mb-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-muted">
                                Description
                              </p>
                              <p className="text-sm text-navy">{item.description}</p>
                            </div>
                          ) : null}

                          {item.tags ? (
                            <div>
                              <p className="mb-1 text-[11px] font-semibold uppercase tracking-[0.14em] text-muted">
                                Tags
                              </p>
                              <div className="flex flex-wrap gap-1.5">
                                {item.tags.split(',').map((tag) => (
                                  <Badge key={tag} variant="muted">
                                    {tag.trim()}
                                  </Badge>
                                ))}
                              </div>
                            </div>
                          ) : null}

                          <Textarea
                            label="Review notes (optional)"
                            rows={2}
                            value={reviewNotes}
                            onChange={(e) => setReviewNotes(e.target.value)}
                            placeholder="Add feedback for the contributor…"
                          />

                          <Checkbox
                            label="Create ContentItem (Draft) on approval"
                            checked={createContentItem}
                            onChange={(e) => setCreateContentItem(e.target.checked)}
                          />

                          <div className="flex flex-col gap-2 sm:flex-row">
                            <Button
                              className="flex-1"
                              onClick={() => void handleReview(item.id, 'approved')}
                              loading={processing === item.id}
                              disabled={processing === item.id}
                            >
                              <CheckCircle2 className="h-4 w-4" /> Approve
                            </Button>
                            <Button
                              variant="destructive"
                              className="flex-1"
                              onClick={() => void handleReview(item.id, 'rejected')}
                              loading={processing === item.id}
                              disabled={processing === item.id}
                            >
                              <XCircle className="h-4 w-4" /> Reject
                            </Button>
                          </div>
                        </div>
                      </motion.div>
                    )}
                  </motion.div>
                );
              })}
            </AnimatePresence>
          </div>
        </AsyncStateWrapper>
        <AdminRoutePanelFooter source="Marketplace submissions" />
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
