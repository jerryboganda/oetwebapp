'use client';

import { useEffect, useRef, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, MessageCircle, ThumbsUp } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { getQuestionComments, postComment, type CommentDto } from '@/lib/reading-pathway-api';

function formatRelativeTime(iso: string): string {
  const diff = Date.now() - new Date(iso).getTime();
  const minutes = Math.floor(diff / 60000);
  if (minutes < 1) return 'just now';
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

export default function QuestionDiscussionPage() {
  const params = useParams<{ questionId: string }>();
  const questionId = params?.questionId ?? '';
  const [comments, setComments] = useState<CommentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [body, setBody] = useState('');
  const [posting, setPosting] = useState(false);
  const [postError, setPostError] = useState<string | null>(null);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  useEffect(() => {
    if (!questionId) {
      setLoading(false);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        const data = await getQuestionComments(questionId);
        if (!cancelled) setComments(data);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Failed to load comments.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [questionId]);

  async function handlePost(e: React.FormEvent) {
    e.preventDefault();
    const trimmed = body.trim();
    if (!trimmed || !questionId) return;
    setPosting(true);
    setPostError(null);
    try {
      const newComment = await postComment(questionId, trimmed);
      setComments((prev) => [newComment, ...prev]);
      setBody('');
      textareaRef.current?.focus();
    } catch (err) {
      setPostError(err instanceof Error ? err.message : 'Failed to post comment.');
    } finally {
      setPosting(false);
    }
  }

  return (
    <LearnerDashboardShell pageTitle="Discussion">
      <main className="space-y-8 max-w-2xl">
        <Link
          href="/reading"
          className="inline-flex items-center gap-1.5 text-sm text-muted hover:text-navy dark:hover:text-white"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden />
          Back to Reading
        </Link>

        <div className="flex items-center gap-3">
          <MessageCircle className="h-6 w-6 text-blue-500" aria-hidden />
          <h1 className="text-2xl font-bold text-navy dark:text-white">Discussion</h1>
        </div>

        {error ? (
          <div className="rounded-lg border border-red-200 bg-red-50 dark:border-red-800 dark:bg-red-900/20 px-4 py-3 text-sm text-red-700 dark:text-red-300">
            {error}
          </div>
        ) : null}

        {/* Comments list */}
        {loading ? (
          <div className="space-y-3">
            {[...Array(3)].map((_, i) => (
              <div key={i} className="h-20 animate-pulse rounded-xl bg-gray-100 dark:bg-gray-800" />
            ))}
          </div>
        ) : comments.length === 0 ? (
          <div className="rounded-xl border border-border bg-surface px-6 py-10 text-center">
            <MessageCircle className="mx-auto h-8 w-8 text-muted mb-3" aria-hidden />
            <p className="text-sm font-medium text-navy dark:text-white">Be the first to comment!</p>
            <p className="mt-1 text-xs text-muted">Share your thoughts or ask a question below.</p>
          </div>
        ) : (
          <ul className="space-y-4">
            {comments.map((comment) => (
              <li key={comment.id} className="rounded-xl border border-border bg-surface p-4">
                <div className="flex items-start justify-between gap-3">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-semibold text-navy dark:text-white">
                      {comment.userDisplayName}
                    </span>
                    {comment.isExpert ? (
                      <span className="inline-block rounded-full bg-violet-100 dark:bg-violet-900/30 px-2 py-0.5 text-xs font-semibold text-violet-700 dark:text-violet-300">
                        Expert
                      </span>
                    ) : null}
                  </div>
                  <span className="flex-shrink-0 text-xs text-muted">
                    {formatRelativeTime(comment.createdAt)}
                  </span>
                </div>
                <p className="mt-2 text-sm text-navy/80 dark:text-white/80 whitespace-pre-wrap">
                  {comment.body}
                </p>
                <div className="mt-3 flex items-center gap-1.5 text-xs text-muted">
                  <ThumbsUp className="h-3.5 w-3.5" aria-hidden />
                  <span>{comment.upvotes}</span>
                </div>
              </li>
            ))}
          </ul>
        )}

        {/* Post a comment */}
        <div className="rounded-xl border border-border bg-surface p-5">
          <h2 className="mb-3 text-sm font-semibold text-navy dark:text-white">Post a comment</h2>
          {postError ? (
            <p className="mb-2 text-xs text-red-600 dark:text-red-400">{postError}</p>
          ) : null}
          <form onSubmit={handlePost} className="space-y-3">
            <textarea
              ref={textareaRef}
              value={body}
              onChange={(e) => setBody(e.target.value)}
              rows={4}
              placeholder="Share your thoughts, ask a question, or explain your approach…"
              className="w-full resize-none rounded-lg border border-border bg-background px-3 py-2.5 text-sm text-navy dark:text-white placeholder:text-muted focus:outline-none focus:ring-2 focus:ring-blue-500 dark:bg-background-dark"
            />
            <div className="flex justify-end">
              <button
                type="submit"
                disabled={posting || !body.trim()}
                className="rounded-lg bg-blue-600 px-5 py-2 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {posting ? 'Posting…' : 'Post'}
              </button>
            </div>
          </form>
        </div>
      </main>
    </LearnerDashboardShell>
  );
}
