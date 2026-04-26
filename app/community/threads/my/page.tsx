'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { User, MessageCircle, Eye, ThumbsUp, Clock, Pin, Lock, ArrowLeft, Plus, Trash2, PenLine } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Pagination } from '@/components/ui/pagination';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton, EmptyState } from '@/components/ui';
import { useAuth } from '@/contexts/auth-context';
import { fetchForumThreads, fetchForumCategories } from '@/lib/api';
import { ensureFreshAccessToken } from '@/lib/auth-client';
import { env } from '@/lib/env';
import { analytics } from '@/lib/analytics';

interface ForumCategory {
  id: string;
  name: string;
}

interface ForumThreadSummary {
  id: string;
  categoryId: string;
  title: string;
  authorDisplayName: string;
  authorRole: string;
  isPinned: boolean;
  isLocked: boolean;
  replyCount: number;
  viewCount: number;
  likeCount: number;
  createdAt: string;
  lastActivityAt: string;
}

interface ThreadsResponse {
  total: number;
  threads: ForumThreadSummary[];
}


function formatRelativeDate(dateStr: string) {
  const d = new Date(dateStr);
  const now = new Date();
  const diffMs = now.getTime() - d.getTime();
  const diffMins = Math.floor(diffMs / 60000);
  if (diffMins < 1) return 'just now';
  if (diffMins < 60) return `${diffMins}m ago`;
  const diffHours = Math.floor(diffMins / 60);
  if (diffHours < 24) return `${diffHours}h ago`;
  const diffDays = Math.floor(diffHours / 24);
  if (diffDays < 7) return `${diffDays}d ago`;
  return d.toLocaleDateString();
}

export default function MyThreadsPage() {
  const router = useRouter();
  const { user } = useAuth();

  const [categories, setCategories] = useState<ForumCategory[]>([]);
  const [threads, setThreads] = useState<ForumThreadSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<string | null>(null);

  const loadCategories = useCallback(async () => {
    try {
      const cats = await fetchForumCategories() as ForumCategory[];
      setCategories(Array.isArray(cats) ? cats : []);
    } catch {
      // Non-blocking
    }
  }, []);

  const loadThreads = useCallback(async (p: number) => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetchForumThreads(undefined, p, pageSize) as ThreadsResponse;
      const allThreads = res.threads ?? [];
      // Filter client-side by author display name match
      const myThreads = user?.displayName
        ? allThreads.filter(t => t.authorDisplayName === user.displayName)
        : [];
      setThreads(myThreads);
      setTotal(myThreads.length);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load threads');
    } finally {
      setLoading(false);
    }
  }, [user, pageSize]);

  useEffect(() => {
    loadCategories();
    loadThreads(1);
    analytics.track('community_my_threads_viewed');
  }, [loadCategories, loadThreads]);


  async function handleDelete(threadId: string) {
    if (!confirm('Are you sure you want to delete this thread?')) return;
    setDeleting(threadId);
    try {
      const token = await ensureFreshAccessToken();
      const res = await fetch(`${env.apiBaseUrl}/v1/community/threads/${encodeURIComponent(threadId)}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) throw new Error(`Delete failed (${res.status})`);
      analytics.track('community_thread_deleted', { threadId });
      setThreads(prev => prev.filter(t => t.id !== threadId));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete thread');
    } finally {
      setDeleting(null);
    }
  }

  const categoryMap = new Map(categories.map(c => [c.id, c.name]));

  return (
    <LearnerDashboardShell pageTitle="My Threads">
      <LearnerPageHero
        title="My Threads"
        description="View and manage threads you've created."
        icon={User}
        highlights={[
          { icon: MessageCircle, label: 'Your threads', value: String(total) },
        ]}
      />

      <MotionSection className="space-y-4">
        <div className="flex items-center justify-between">
          <Button variant="outline" size="sm" onClick={() => router.push('/community')}>
            <ArrowLeft className="mr-1.5 h-4 w-4" /> All Threads
          </Button>
          <Button onClick={() => router.push('/community/threads/new')}>
            <Plus className="mr-1.5 h-4 w-4" /> New Thread
          </Button>
        </div>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {loading ? (
          <div className="space-y-3">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-24 w-full rounded-2xl" />
            ))}
          </div>
        ) : threads.length === 0 ? (
          <EmptyState
            icon={<MessageCircle className="h-8 w-8" />}
            title="No threads yet"
            description="You haven't created any threads. Start a new discussion!"
            action={{ label: 'Create Thread', onClick: () => router.push('/community/threads/new') }}
          />
        ) : (
          <div className="space-y-2">
            {threads.map((thread, idx) => (
              <MotionItem key={thread.id} delayIndex={idx}>
                <Card className="p-4 shadow-sm">
                  <div className="flex items-start justify-between gap-3">
                    <div
                      className="flex-1 min-w-0 cursor-pointer"
                      onClick={() => router.push(`/community/threads/${thread.id}`)}
                    >
                      <div className="flex flex-wrap items-center gap-2 mb-1">
                        {thread.isPinned && (
                          <Badge variant="outline" className="text-warning border-warning/30 bg-warning/10">
                            <Pin className="mr-1 h-3 w-3" /> Pinned
                          </Badge>
                        )}
                        {thread.isLocked && (
                          <Badge variant="outline" className="text-muted border-border-hover">
                            <Lock className="mr-1 h-3 w-3" /> Locked
                          </Badge>
                        )}
                        {categoryMap.get(thread.categoryId) && (
                          <Badge variant="outline">{categoryMap.get(thread.categoryId)}</Badge>
                        )}
                      </div>
                      <h3 className="font-semibold text-navy truncate">{thread.title}</h3>
                      <div className="mt-1.5 flex flex-wrap items-center gap-3 text-xs text-muted">
                        <span className="flex items-center gap-1">
                          <MessageCircle className="h-3 w-3" /> {thread.replyCount}
                        </span>
                        <span className="flex items-center gap-1">
                          <Eye className="h-3 w-3" /> {thread.viewCount}
                        </span>
                        <span className="flex items-center gap-1">
                          <ThumbsUp className="h-3 w-3" /> {thread.likeCount}
                        </span>
                        <span className="flex items-center gap-1">
                          <Clock className="h-3 w-3" /> {formatRelativeDate(thread.lastActivityAt)}
                        </span>
                      </div>
                    </div>
                    <div className="flex shrink-0 items-center gap-1">
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => router.push(`/community/threads/${thread.id}/edit`)}
                      >
                        <PenLine className="h-3.5 w-3.5" />
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={() => handleDelete(thread.id)}
                        disabled={deleting === thread.id}
                        className="text-danger hover:bg-danger/10"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </Button>
                    </div>
                  </div>
                </Card>
              </MotionItem>
            ))}
          </div>
        )}

        {!loading && (
          <div className="pt-2">
            <Pagination
              page={page}
              pageSize={pageSize}
              total={total}
              onPageChange={(next) => { setPage(next); loadThreads(next); }}
              onPageSizeChange={(next) => { setPageSize(next); }}
              itemLabel="thread"
              itemLabelPlural="threads"
            />
          </div>
        )}
      </MotionSection>
    </LearnerDashboardShell>
  );
}
