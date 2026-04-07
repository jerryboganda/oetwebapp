'use client';

import { useEffect, useState, useCallback } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { Shield, CheckCircle2, XCircle, Eye, Clock, Loader2, ChevronDown, BookOpen, Mic, Pen, Headphones } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, ExamTypeBadge } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';

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
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Marketplace Review Queue"
        description="Review and moderate community-submitted content before publication."
        icon={Shield}
        accent="amber"
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {loading ? (
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-32 rounded-xl" />)}
        </div>
      ) : items.length === 0 ? (
        <div className="text-center py-16 bg-gray-50 dark:bg-gray-800/50 rounded-2xl border border-dashed border-gray-200 dark:border-gray-700">
          <CheckCircle2 className="w-12 h-12 text-green-400 mx-auto mb-3" />
          <p className="text-sm font-bold text-gray-600 dark:text-gray-400">All caught up!</p>
          <p className="text-xs text-gray-400 mt-1">No pending submissions to review.</p>
        </div>
      ) : (
        <div className="space-y-4">
          <div className="text-sm text-gray-500">
            <Clock className="w-4 h-4 inline mr-1" /> {items.length} submission{items.length !== 1 ? 's' : ''} pending review
          </div>

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
                  className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 overflow-hidden"
                >
                  <button
                    onClick={() => { setExpandedId(isExpanded ? null : item.id); setReviewNotes(''); }}
                    className="w-full text-left p-4 flex items-center justify-between gap-3 hover:bg-gray-50 dark:hover:bg-gray-750 transition-colors"
                  >
                    <div className="flex items-center gap-3 min-w-0">
                      <div className="p-2 rounded-lg bg-amber-50 dark:bg-amber-900/20 text-amber-600 dark:text-amber-400">
                        <SubIcon className="w-5 h-5" />
                      </div>
                      <div className="min-w-0">
                        <div className="flex items-center gap-2 mb-0.5">
                          <span className="text-sm font-semibold text-gray-900 dark:text-white truncate">{item.title}</span>
                          <ExamTypeBadge examType={item.examFamilyCode} size="sm" />
                          <span className="text-xs bg-gray-100 dark:bg-gray-700 text-gray-500 px-1.5 py-0.5 rounded capitalize">{item.difficulty}</span>
                        </div>
                        <div className="text-xs text-gray-400">
                          {item.subtestCode} • {item.contentType.replace(/_/g, ' ')} • Submitted {dateFormatter.format(new Date(item.submittedAt))}
                        </div>
                      </div>
                    </div>
                    <ChevronDown className={`w-4 h-4 text-gray-400 transition-transform ${isExpanded ? 'rotate-180' : ''}`} />
                  </button>

                  {isExpanded && (
                    <motion.div initial={{ height: 0, opacity: 0 }} animate={{ height: 'auto', opacity: 1 }} exit={{ height: 0, opacity: 0 }}
                      className="border-t border-gray-100 dark:border-gray-700">
                      <div className="p-4 space-y-4">
                        {item.description && (
                          <div>
                            <div className="text-xs font-semibold text-gray-500 mb-1 uppercase">Description</div>
                            <p className="text-sm text-gray-700 dark:text-gray-300">{item.description}</p>
                          </div>
                        )}

                        {item.tags && (
                          <div>
                            <div className="text-xs font-semibold text-gray-500 mb-1 uppercase">Tags</div>
                            <div className="flex flex-wrap gap-1">
                              {item.tags.split(',').map(tag => (
                                <span key={tag} className="text-xs bg-gray-100 dark:bg-gray-700 text-gray-600 dark:text-gray-400 px-2 py-0.5 rounded-full">
                                  {tag.trim()}
                                </span>
                              ))}
                            </div>
                          </div>
                        )}

                        <div>
                          <div className="text-xs font-semibold text-gray-500 mb-1 uppercase">Review Notes (optional)</div>
                          <textarea rows={2} value={reviewNotes} onChange={e => setReviewNotes(e.target.value)}
                            placeholder="Add feedback for the contributor..."
                            className="w-full px-3 py-2 bg-gray-50 dark:bg-gray-900 border border-gray-200 dark:border-gray-700 rounded-lg text-sm outline-none focus:ring-2 focus:ring-amber-500 resize-none" />
                        </div>

                        <div className="flex items-center gap-2">
                          <input type="checkbox" id={`ci-${item.id}`} checked={createContentItem} onChange={e => setCreateContentItem(e.target.checked)}
                            className="w-4 h-4 text-amber-600 rounded" />
                          <label htmlFor={`ci-${item.id}`} className="text-xs text-gray-600 dark:text-gray-400">
                            Create ContentItem (Draft) on approval
                          </label>
                        </div>

                        <div className="flex gap-3">
                          <button onClick={() => handleReview(item.id, 'approved')} disabled={processing === item.id}
                            className="flex-1 py-2.5 bg-green-600 hover:bg-green-700 disabled:opacity-50 text-white rounded-lg font-semibold text-sm flex items-center justify-center gap-2 transition-colors">
                            {processing === item.id ? <Loader2 className="w-4 h-4 animate-spin" /> : <CheckCircle2 className="w-4 h-4" />} Approve
                          </button>
                          <button onClick={() => handleReview(item.id, 'rejected')} disabled={processing === item.id}
                            className="flex-1 py-2.5 bg-red-600 hover:bg-red-700 disabled:opacity-50 text-white rounded-lg font-semibold text-sm flex items-center justify-center gap-2 transition-colors">
                            {processing === item.id ? <Loader2 className="w-4 h-4 animate-spin" /> : <XCircle className="w-4 h-4" />} Reject
                          </button>
                        </div>
                      </div>
                    </motion.div>
                  )}
                </motion.div>
              );
            })}
          </AnimatePresence>
        </div>
      )}
    </LearnerDashboardShell>
  );
}
