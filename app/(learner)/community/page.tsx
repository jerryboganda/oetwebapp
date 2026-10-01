'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { MessageSquareText, Plus, Filter, MessageCircle, Eye, ThumbsUp, Pin, Lock, Clock } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Pagination } from '@/components/ui/pagination';
import { Button } from '@/components/ui/button';
import { CardLink } from '@/components/ui/card-link';
import { Badge } from '@/components/ui/badge';
import { Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton, EmptyState } from '@/components/ui';
import { useAuth } from '@/contexts/auth-context';
import { fetchForumCategories, fetchForumThreads } from '@/lib/api';
import { analytics } from '@/lib/analytics';

interface ForumCategory {
  id: string;
  examTypeCode: string | null;
  name: string;
  description: string;
  sortOrder: number;
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

export default function CommunityPage() {
  const router = useRouter();
  const { user } = useAuth();

  const [categories, setCategories] = useState<ForumCategory[]>([]);
  const [threads, setThreads] = useState<ForumThreadSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [selectedCategory, setSelectedCategory] = useState('');
  const [showMyThreads, setShowMyThreads] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadCategories = useCallback(async () => {
    try {
      const cats = await fetchForumCategories() as ForumCategory[];
      setCategories(Array.isArray(cats) ? cats : []);
    } catch {
      // Categories are optional for display
    }
  }, []);

  const loadThreads = useCallback(async (p: number, catId?: string) => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetchForumThreads(catId || undefined, p, pageSize) as ThreadsResponse;
      setThreads(res.threads ?? []);
      setTotal(res.total ?? 0);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load threads');
    } finally {
      setLoading(false);
    }
  }, [pageSize]);

  useEffect(() => {
    loadCategories();
    analytics.track('community_threads_viewed');
  }, [loadCategories]);

  useEffect(() => {
    setPage(1);
    loadThreads(1, selectedCategory);
  }, [selectedCategory, loadThreads]);

  const categoryMap = new Map(categories.map(c => [c.id, c.name]));

  const displayedThreads = showMyThreads && user
    ? threads.filter(t => t.authorDisplayName === user.displayName)
    : threads;

  // Pagination below already shows the page position, so the hero keeps to real totals.
  const heroHighlights = [
    { icon: MessageSquareText, label: 'Total threads', value: String(total) },
    { icon: Filter, label: 'Categories', value: String(categories.length) },
  ];

  return (
    <>
      <LearnerPageHero
        title="Community Threads"
        description="Ask questions, share insights, and learn together with fellow OET candidates."
        icon={MessageSquareText}
        highlights={heroHighlights}
      />

      <section className="space-y-4">
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex flex-wrap items-center gap-2">
            <Select
              aria-label="Category"
              options={[
                { value: '', label: 'All categories' },
                ...categories.map(c => ({ value: c.id, label: c.name })),
              ]}
              value={selectedCategory}
              onChange={e => setSelectedCategory(e.target.value)}
              className="w-48"
            />
            <Button
              variant={showMyThreads ? 'secondary' : 'outline'}
              size="sm"
              onClick={() => setShowMyThreads(!showMyThreads)}
            >
              {showMyThreads ? 'All Threads' : 'My Threads'}
            </Button>
          </div>
          <Button onClick={() => router.push('/community/threads/new')}>
            <Plus className="h-4 w-4" aria-hidden="true" /> New Thread
          </Button>
        </div>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {loading ? (
          <div className="space-y-3" role="status" aria-busy="true" aria-label="Loading">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton aria-hidden key={i} className="h-24 w-full rounded-2xl" />
            ))}
          </div>
        ) : displayedThreads.length === 0 ? (
          <EmptyState
            icon={<MessageSquareText className="h-8 w-8" />}
            title={showMyThreads ? 'No threads yet' : 'No threads found'}
            description={showMyThreads ? "You haven't created any threads yet." : 'Be the first to start a discussion!'}
            action={{ label: 'Create Thread', onClick: () => router.push('/community/threads/new') }}
          />
        ) : (
          <div className="space-y-3">
            {displayedThreads.map((thread, idx) => (
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
                    <span className="font-medium">{thread.authorDisplayName}</span>
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
            onPageChange={(next) => { setPage(next); loadThreads(next, selectedCategory); }}
            onPageSizeChange={(next) => { setPageSize(next); }}
            itemLabel="thread"
            itemLabelPlural="threads"
          />
        )}
      </section>
    </>
  );
}
