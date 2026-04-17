'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { MessageSquareText, Plus, Filter, ChevronLeft, ChevronRight, MessageCircle, Eye, ThumbsUp, Pin, Lock, Clock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
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

const PAGE_SIZE = 20;

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
      const res = await fetchForumThreads(catId || undefined, p, PAGE_SIZE) as ThreadsResponse;
      setThreads(res.threads ?? []);
      setTotal(res.total ?? 0);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load threads');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadCategories();
    analytics.track('community_threads_viewed');
  }, [loadCategories]);

  useEffect(() => {
    setPage(1);
    loadThreads(1, selectedCategory);
  }, [selectedCategory, loadThreads]);

  const handlePageChange = (newPage: number) => {
    setPage(newPage);
    loadThreads(newPage, selectedCategory);
  };

  const totalPages = Math.ceil(total / PAGE_SIZE);
  const categoryMap = new Map(categories.map(c => [c.id, c.name]));

  const displayedThreads = showMyThreads && user
    ? threads.filter(t => t.authorDisplayName === user.displayName)
    : threads;

  const heroHighlights = [
    { icon: MessageSquareText, label: 'Total threads', value: String(total) },
    { icon: Filter, label: 'Categories', value: String(categories.length) },
    { icon: MessageCircle, label: 'Page', value: `${page} / ${totalPages || 1}` },
  ];

  return (
    <LearnerDashboardShell pageTitle="Community">
      <LearnerPageHero
        title="Community Threads"
        description="Ask questions, share insights, and learn together with fellow OET candidates."
        icon={MessageSquareText}
        highlights={heroHighlights}
      />

      <MotionSection className="space-y-4 mt-6">
        {/* Controls bar */}
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          <div className="flex flex-wrap items-center gap-2">
            <Select
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
            <Plus className="mr-1.5 h-4 w-4" /> New Thread
          </Button>
        </div>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {/* Thread list */}
        {loading ? (
          <div className="space-y-3">
            {Array.from({ length: 5 }).map((_, i) => (
              <Skeleton key={i} className="h-24 w-full rounded-2xl" />
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
          <div className="space-y-2">
            {displayedThreads.map((thread, idx) => (
              <MotionItem key={thread.id} delayIndex={idx}>
                <Card
                  className="cursor-pointer p-4 shadow-sm transition-shadow hover:shadow-md"
                  onClick={() => router.push(`/community/threads/${thread.id}`)}
                >
                  <div className="flex items-start gap-3">
                    <div className="flex-1 min-w-0">
                      <div className="flex flex-wrap items-center gap-2 mb-1">
                        {thread.isPinned && (
                          <Badge variant="outline" className="text-amber-600 border-amber-300 bg-amber-50">
                            <Pin className="mr-1 h-3 w-3" /> Pinned
                          </Badge>
                        )}
                        {thread.isLocked && (
                          <Badge variant="outline" className="text-gray-500 border-gray-300">
                            <Lock className="mr-1 h-3 w-3" /> Locked
                          </Badge>
                        )}
                        {categoryMap.get(thread.categoryId) && (
                          <Badge variant="outline">{categoryMap.get(thread.categoryId)}</Badge>
                        )}
                      </div>
                      <h3 className="font-semibold text-navy truncate">{thread.title}</h3>
                      <div className="mt-1.5 flex flex-wrap items-center gap-3 text-xs text-muted">
                        <span className="font-medium">{thread.authorDisplayName}</span>
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
                  </div>
                </Card>
              </MotionItem>
            ))}
          </div>
        )}

        {/* Pagination */}
        {totalPages > 1 && !loading && (
          <div className="flex items-center justify-center gap-2 pt-2">
            <Button
              variant="outline"
              size="sm"
              disabled={page <= 1}
              onClick={() => handlePageChange(page - 1)}
            >
              <ChevronLeft className="h-4 w-4" />
            </Button>
            <span className="text-sm text-muted">
              Page {page} of {totalPages}
            </span>
            <Button
              variant="outline"
              size="sm"
              disabled={page >= totalPages}
              onClick={() => handlePageChange(page + 1)}
            >
              <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
        )}
      </MotionSection>
    </LearnerDashboardShell>
  );
}
