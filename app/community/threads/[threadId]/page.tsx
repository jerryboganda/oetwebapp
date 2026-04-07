'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { MessageSquare, ArrowLeft, Send, Lock } from 'lucide-react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchForumThread, fetchThreadReplies, createReply } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type ForumThread = {
  id: string; categoryId: string; authorDisplayName: string; authorRole: string;
  title: string; body: string; isPinned: boolean; isLocked: boolean;
  replyCount: number; viewCount: number; createdAt: string;
};
type ForumReply = {
  id: string; authorDisplayName: string; authorRole: string; body: string;
  isExpertVerified: boolean; likeCount: number; createdAt: string;
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
    Promise.all([
      fetchForumThread(params.threadId),
      fetchThreadReplies(params.threadId, 1, 50),
    ]).then(([t, r]) => {
      setThread(t as ForumThread);
      const rd = r as { replies: ForumReply[]; total: number };
      setReplies(rd.replies ?? []);
      setTotal(rd.total ?? 0);
      setLoading(false);
      analytics.track('forum_thread_viewed', { threadId: params.threadId });
    }).catch(() => {
      setError('Could not load thread.');
      setLoading(false);
    });
  }, [params.threadId]);

  async function handleReply() {
    if (!replyBody.trim() || !thread || submitting) return;
    setSubmitting(true);
    setReplyError(null);
    try {
      await createReply(thread.id, replyBody.trim());
      // Reload replies
      const r = await fetchThreadReplies(thread.id, 1, 50) as { replies: ForumReply[]; total: number };
      setReplies(r.replies ?? []);
      setTotal(r.total ?? 0);
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
        <Skeleton className="h-8 w-48 mb-4" />
        <Skeleton className="h-40 rounded-2xl mb-4" />
        <Skeleton className="h-24 rounded-xl" />
      </LearnerDashboardShell>
    );
  }

  if (!thread) {
    return <LearnerDashboardShell><InlineAlert variant="warning">Thread not found.</InlineAlert></LearnerDashboardShell>;
  }

  return (
    <LearnerDashboardShell>
      <div className="flex items-center gap-3 mb-6">
        <Link href="/community" className="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <div className="flex items-center gap-2">
          <MessageSquare className="w-5 h-5 text-green-500" />
          <span className="text-xs text-gray-400 uppercase font-medium">Community Forum</span>
          {thread.isLocked && <span className="flex items-center gap-1 text-xs text-gray-400"><Lock className="w-3 h-3" /> Locked</span>}
        </div>
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <div className="max-w-2xl mx-auto space-y-4">
        {/* Original post */}
        <div className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 p-6">
          <h1 className="text-xl font-bold text-gray-900 dark:text-white mb-3">{thread.title}</h1>
          <p className="text-sm text-gray-700 dark:text-gray-300 whitespace-pre-wrap mb-4">{thread.body}</p>
          <div className="flex items-center gap-3 text-xs text-gray-400 border-t border-gray-100 dark:border-gray-700 pt-3">
            <span className="font-medium text-gray-600 dark:text-gray-300">{thread.authorDisplayName}</span>
            <span>·</span>
            <span>{formatDate(thread.createdAt)}</span>
            <span>·</span>
            <span>{thread.viewCount} views</span>
          </div>
        </div>

        {/* Replies */}
        {replies.length > 0 && (
          <div>
            <h2 className="text-sm font-semibold text-gray-500 dark:text-gray-400 px-1 mb-2">{total} {total === 1 ? 'Reply' : 'Replies'}</h2>
            <div className="space-y-3">
              {replies.map((reply, i) => (
                <motion.div
                  key={reply.id}
                  initial={{ opacity: 0, y: 6 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: i * 0.04 }}
                  className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-4"
                >
                  <div className="flex items-center gap-2 mb-2">
                    <span className="text-sm font-medium text-gray-900 dark:text-white">{reply.authorDisplayName}</span>
                    {reply.isExpertVerified && (
                      <span className="text-xs px-1.5 py-0.5 bg-blue-100 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300 rounded-full">Expert</span>
                    )}
                    <span className="text-xs text-gray-400 ml-auto">{formatDate(reply.createdAt)}</span>
                  </div>
                  <p className="text-sm text-gray-700 dark:text-gray-300 whitespace-pre-wrap">{reply.body}</p>
                </motion.div>
              ))}
            </div>
          </div>
        )}

        {/* Reply form */}
        {!thread.isLocked && (
          <div className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-4">
            <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">Leave a Reply</h3>
            {replyError && <InlineAlert variant="warning" className="mb-3">{replyError}</InlineAlert>}
            <textarea
              value={replyBody}
              onChange={e => setReplyBody(e.target.value)}
              placeholder="Share your thoughts or answer..."
              rows={4}
              className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-800 text-gray-800 dark:text-gray-200 resize-none focus:outline-none focus:ring-2 focus:ring-indigo-400 mb-3"
            />
            <div className="flex justify-end">
              <button
                onClick={handleReply}
                disabled={!replyBody.trim() || submitting}
                className="flex items-center gap-2 px-5 py-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg text-sm font-medium disabled:opacity-50 transition-colors"
              >
                <Send className="w-4 h-4" />
                {submitting ? 'Posting...' : 'Post Reply'}
              </button>
            </div>
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
