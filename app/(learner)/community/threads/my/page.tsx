'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { User, MessageCircle, Eye, ThumbsUp, Clock, Pin, Lock, ArrowLeft, Plus } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { CardLink } from '@/components/ui/card-link';
import { Pagination } from '@/components/ui/pagination';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton, EmptyState } from '@/components/ui';
import { useAuth } from '@/contexts/auth-context';
import { fetchForumThreads, fetchForumCategories } from '@/lib/api';
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


  const categoryMap = new Map(categories.map(c => [c.id, c.name]));

  return (
    <>
      <LearnerPageHero
        title="My Threads"
        description="View and manage threads you've created."
        icon={User}
        highlights={[
          { icon: MessageCircle, label: 'Your threads', value: String(total) },
        ]}
      />

      <section className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <Button variant="outline" size="sm" onClick={() => router.push('/community')}>
            <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> All Threads
          </Button>
          <Button onClick={() => router.push('/community/threads/new')}>
            <Plus className="h-4 w-4" aria-hidden="true" /> New Thread
          </Button>
        </div>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {loading ? (
          <div className="space-y-3" role="status" aria-busy="true" aria-label="Loading">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton aria-hidden key={i} className="h-24 w-full rounded-2xl" />
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
          <div className="space-y-3">
            {threads.map((thread, idx) => (
              <MotionItem key={thread.id} delayIndex={Math.min(idx, 5)}>
                <CardLink href={`/community/threads/${thread.id}`} prefetch={false}>
                  <div className="mb-1.5 flex flex-wrap items-center gap-2 empty:hidden">
                    {thread.isPinned && (
                      <Badge variant="warning" className="gap-1">
                        <Pin className="h-3 w-3" aria-hidden="true" /> Pinned
                      </Badge>
                    )}
                    {thread.isLocked && (
                      <Badge variant="muted" className="gap-1">
                        <Lock className="h-3 w-3" aria-hidden="true" /> Locked
                      </Badge>
                    )}
                    {categoryMap.get(thread.categoryId) && (
                      <Badge variant="outline">{categoryMap.get(thread.categoryId)}</Badge>
                    )}
                  </div>
                  <h2 className="truncate text-base font-bold text-navy">{thread.title}</h2>
                  <div className="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted tabular-nums">
                    <span className="flex items-center gap-1">
                      <MessageCircle className="h-3 w-3" aria-hidden="true" /> {thread.replyCount}<span className="sr-only"> replies</span>
                    </span>
                    <span className="flex items-center gap-1">
                      <Eye className="h-3 w-3" aria-hidden="true" /> {thread.viewCount}<span className="sr-only"> views</span>
                    </span>
                    <span className="flex items-center gap-1">
                      <ThumbsUp className="h-3 w-3" aria-hidden="true" /> {thread.likeCount}<span className="sr-only"> likes</span>
                    </span>
                    <span className="flex items-center gap-1">
                      <Clock className="h-3 w-3" aria-hidden="true" /> {formatRelativeDate(thread.lastActivityAt)}
                    </span>
                  </div>
                </CardLink>
              </MotionItem>
            ))}
          </div>
        )}

        {!loading && (
          <Pagination
            page={page}
            pageSize={pageSize}
            total={total}
            onPageChange={(next) => { setPage(next); loadThreads(next); }}
            onPageSizeChange={(next) => { setPageSize(next); }}
            itemLabel="thread"
            itemLabelPlural="threads"
          />
        )}
      </section>
    </>
  );
}
