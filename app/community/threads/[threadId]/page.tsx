'use client';

import { useEffect, useState } from 'react';
import { ArrowLeft, Lock, MessageSquare, Send } from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { MotionItem, MotionList, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchForumThread, fetchThreadReplies, createReply } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type ForumThread = {
  id: string;
  categoryId: string;
  authorDisplayName: string;
  authorRole: string;
  title: string;
  body: string;
  isPinned: boolean;
  isLocked: boolean;
  replyCount: number;
  viewCount: number;
  createdAt: string;
};

type ForumReply = {
  id: string;
  authorDisplayName: string;
  authorRole: string;
  body: string;
  isExpertVerified: boolean;
  likeCount: number;
  createdAt: string;
};

function formatDate(iso: string) {
  return new Date(iso).toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' });
}

export default function ThreadPage() {
  const params = useParams<{ threadId: string }>();
  const [thread, setThread] = useState<ForumThread | null>(null);
  const [replies, setReplies] = useState<ForumReply[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [replyBody, setReplyBody] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [replyError, setReplyError] = useState<string | null>(null);

  useEffect(() => {
    if (!params.threadId) return;

    Promise.all([fetchForumThread(params.threadId), fetchThreadReplies(params.threadId, 1, 50)])
      .then(([threadResponse, repliesResponse]) => {
        setThread(threadResponse as ForumThread);
        const replyData = repliesResponse as { replies: ForumReply[]; total: number };
        setReplies(replyData.replies ?? []);
        setTotal(replyData.total ?? 0);
        analytics.track('forum_thread_viewed', { threadId: params.threadId });
      })
      .catch(() => {
        setError('Could not load thread.');
      })
      .finally(() => {
        setLoading(false);
      });
  }, [params.threadId]);

  async function handleReply() {
    if (!replyBody.trim() || !thread || submitting) return;

    setSubmitting(true);
    setReplyError(null);

    try {
      await createReply(thread.id, replyBody.trim());
      const replyData = (await fetchThreadReplies(thread.id, 1, 50)) as { replies: ForumReply[]; total: number };
      setReplies(replyData.replies ?? []);
      setTotal(replyData.total ?? 0);
      setReplyBody('');
    } catch {
      setReplyError('Could not post reply.');
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <LearnerDashboardShell>
        <MotionSection className="mb-6">
          <Skeleton className="mb-4 h-8 w-48" />
          <Skeleton className="mb-4 h-40 rounded-2xl" />
          <Skeleton className="h-24 rounded-xl" />
        </MotionSection>
      </LearnerDashboardShell>
    );
  }

  if (!thread) {
    return (
      <LearnerDashboardShell>
        <MotionSection>
          <InlineAlert variant="warning">Thread not found.</InlineAlert>
        </MotionSection>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <MotionSection className="mb-6 flex items-center gap-3">
        <Link href="/community" className="text-gray-400 transition-colors hover:text-gray-600 dark:hover:text-gray-300">
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <div className="flex items-center gap-2">
          <MessageSquare className="h-5 w-5 text-green-500" />
          <span className="text-xs font-medium uppercase text-gray-400">Community Forum</span>
          {thread.isLocked && (
            <span className="flex items-center gap-1 text-xs text-gray-400">
              <Lock className="h-3 w-3" /> Locked
            </span>
          )}
        </div>
      </MotionSection>

      {error && (
        <MotionSection className="mb-4">
          <InlineAlert variant="warning">{error}</InlineAlert>
        </MotionSection>
      )}

      <div className="mx-auto max-w-2xl space-y-4">
        <MotionSection className="rounded-2xl border border-gray-200 bg-white p-6 dark:border-gray-700 dark:bg-gray-800">
          <h1 className="mb-3 text-xl font-bold text-gray-900 dark:text-white">{thread.title}</h1>
          <p className="mb-4 whitespace-pre-wrap text-sm text-gray-700 dark:text-gray-300">{thread.body}</p>
          <div className="flex items-center gap-3 border-t border-gray-100 pt-3 text-xs text-gray-400 dark:border-gray-700">
            <span className="font-medium text-gray-600 dark:text-gray-300">{thread.authorDisplayName}</span>
            <span>&middot;</span>
            <span>{formatDate(thread.createdAt)}</span>
            <span>&middot;</span>
            <span>{thread.viewCount} views</span>
          </div>
        </MotionSection>

        {replies.length > 0 && (
          <MotionSection>
            <h2 className="mb-2 px-1 text-sm font-semibold text-gray-500 dark:text-gray-400">
              {total} {total === 1 ? 'Reply' : 'Replies'}
            </h2>
            <MotionList className="space-y-3">
              {replies.map((reply, index) => (
                <MotionItem
                  key={reply.id}
                  delayIndex={index}
                  className="rounded-xl border border-gray-200 bg-white p-4 dark:border-gray-700 dark:bg-gray-800"
                >
                  <div className="mb-2 flex items-center gap-2">
                    <span className="text-sm font-medium text-gray-900 dark:text-white">{reply.authorDisplayName}</span>
                    {reply.isExpertVerified && (
                      <span className="rounded-full bg-blue-100 px-1.5 py-0.5 text-xs text-blue-700 dark:bg-blue-900/30 dark:text-blue-300">
                        Expert
                      </span>
                    )}
                    <span className="ml-auto text-xs text-gray-400">{formatDate(reply.createdAt)}</span>
                  </div>
                  <p className="whitespace-pre-wrap text-sm text-gray-700 dark:text-gray-300">{reply.body}</p>
                </MotionItem>
              ))}
            </MotionList>
          </MotionSection>
        )}

        {!thread.isLocked && (
          <MotionSection className="rounded-xl border border-gray-200 bg-white p-4 dark:border-gray-700 dark:bg-gray-800">
            <h3 className="mb-3 text-sm font-semibold text-gray-700 dark:text-gray-300">Leave a Reply</h3>
            {replyError && <InlineAlert variant="warning" className="mb-3">{replyError}</InlineAlert>}
            <textarea
              value={replyBody}
              onChange={(event) => setReplyBody(event.target.value)}
              placeholder="Share your thoughts or answer..."
              rows={4}
              className="mb-3 w-full resize-none rounded-lg border border-gray-200 bg-white px-3 py-2.5 text-sm text-gray-800 focus:outline-none focus:ring-2 focus:ring-indigo-400 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-200"
            />
            <div className="flex justify-end">
              <Button onClick={handleReply} disabled={!replyBody.trim() || submitting} loading={submitting}>
                <Send className="h-4 w-4" />
                {submitting ? 'Posting...' : 'Post Reply'}
              </Button>
            </div>
          </MotionSection>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
