'use client';

import { useEffect, useRef, useState } from 'react';
import { useParams } from 'next/navigation';
import { MessageCircle, ThumbsUp } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
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

  // The breadcrumb's "Reading" crumb is the way back, so no back link above the header.
  return (
    <>
      <LearnerPageHero icon={MessageCircle} accent="reading" title="Discussion" description="" />

      {/* Comments list. A failed load is its own state: it used to sit on top of
          "Be the first to comment!", which told the learner the thread was empty. */}
      {loading ? (
        <div className="space-y-3">
          {[...Array(3)].map((_, i) => (
            <Skeleton key={i} className="h-20 rounded-xl" />
          ))}
        </div>
      ) : error ? (
        <ErrorState message={error} />
      ) : comments.length === 0 ? (
        <EmptyState
          icon={<MessageCircle className="h-8 w-8" aria-hidden />}
          title="Be the first to comment!"
          description="Share your thoughts or ask a question below."
        />
      ) : (
        <ul className="space-y-3">
          {comments.map((comment, index) => (
            <li key={comment.id}>
              <MotionItem delayIndex={Math.min(index, 5)}>
                <Card padding="sm">
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex min-w-0 flex-wrap items-center gap-2">
                      <span className="text-sm font-semibold text-navy">
                        {comment.userDisplayName}
                      </span>
                      {comment.isExpert ? <Badge>Expert</Badge> : null}
                    </div>
                    <span className="shrink-0 text-xs tabular-nums text-muted">
                      {formatRelativeTime(comment.createdAt)}
                    </span>
                  </div>
                  <p className="mt-2 max-w-prose whitespace-pre-wrap text-sm text-navy/80">
                    {comment.body}
                  </p>
                  <div className="mt-3 flex items-center gap-1.5 text-xs text-muted">
                    <ThumbsUp className="h-3.5 w-3.5" aria-hidden />
                    <span className="tabular-nums">{comment.upvotes}</span>
                  </div>
                </Card>
              </MotionItem>
            </li>
          ))}
        </ul>
      )}

      {/* Post a comment */}
      <MotionSection>
        <Card>
          <h2 className="mb-3 text-base font-bold text-navy">Post a comment</h2>
          {postError ? (
            <p role="alert" className="mb-2 text-xs text-danger-strong">{postError}</p>
          ) : null}
          <form onSubmit={handlePost} className="space-y-3">
            <textarea
              ref={textareaRef}
              value={body}
              onChange={(e) => setBody(e.target.value)}
              rows={4}
              aria-label="Write a reply"
              placeholder="Share your thoughts, ask a question, or explain your approach…"
              className="w-full resize-none rounded-control border border-border bg-background-light px-3 py-2.5 text-sm text-navy placeholder:text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
            />
            <div className="flex justify-end">
              <Button type="submit" disabled={posting || !body.trim()}>
                {posting ? 'Posting…' : 'Post'}
              </Button>
            </div>
          </form>
        </Card>
      </MotionSection>
    </>
  );
}
