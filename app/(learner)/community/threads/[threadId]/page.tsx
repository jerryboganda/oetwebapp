'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter, useParams } from 'next/navigation';
import {
  ArrowLeft,
  MessageCircle,
  Eye,
  ThumbsUp,
  Clock,
  Pin,
  Lock,
  Send,
  ChevronLeft,
  ChevronRight,
  ShieldCheck,
  User,
  Trash2,
} from 'lucide-react';
import { LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Textarea } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { Skeleton, EmptyState, ErrorState } from '@/components/ui';
import { useAuth } from '@/contexts/auth-context';
import { fetchForumThread, fetchThreadReplies, createReply, pinCommunityThread, lockCommunityThread, adminDeleteCommunityThread, adminDeleteCommunityReply } from '@/lib/api';
import { analytics } from '@/lib/analytics';

interface ForumThread {
  id: string;
  categoryId: string;
  authorUserId: string;
  authorDisplayName: string;
  authorRole: string;
  title: string;
  body: string;
  isPinned: boolean;
  isLocked: boolean;
  replyCount: number;
  viewCount: number;
  likeCount: number;
  createdAt: string;
  lastActivityAt: string;
}

interface ForumReply {
  id: string;
  authorDisplayName: string;
  authorRole: string;
  body: string;
  isExpertVerified: boolean;
  likeCount: number;
  createdAt: string;
  editedAt: string | null;
}

interface RepliesResponse {
  total: number;
  replies: ForumReply[];
}

const REPLIES_PAGE_SIZE = 20;

function formatDate(dateStr: string) {
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

function roleVariant(role: string): 'default' | 'danger' | 'info' {
  switch (role) {
    case 'expert': return 'default';
    case 'admin': return 'danger';
    default: return 'info';
  }
}

export default function ThreadPage() {
  const router = useRouter();
  const params = useParams();
  const threadId = params?.threadId as string;
  const { user, role } = useAuth();
  const isAdmin = role === 'admin';

  const [thread, setThread] = useState<ForumThread | null>(null);
  const [replies, setReplies] = useState<ForumReply[]>([]);
  const [repliesTotal, setRepliesTotal] = useState(0);
  const [repliesPage, setRepliesPage] = useState(1);
  const [repliesError, setRepliesError] = useState<string | null>(null);
  const [replyBody, setReplyBody] = useState('');
  const [loadingThread, setLoadingThread] = useState(true);
  const [loadingReplies, setLoadingReplies] = useState(true);
  const [submittingReply, setSubmittingReply] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [replyError, setReplyError] = useState<string | null>(null);
  const [moderating, setModerating] = useState(false);
  const [deleteReplyTarget, setDeleteReplyTarget] = useState<ForumReply | null>(null);
  const [deleteThreadConfirm, setDeleteThreadConfirm] = useState(false);
  const [modToast, setModToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const loadThread = useCallback(async () => {
    if (!threadId) return;
    setLoadingThread(true);
    setError(null);
    try {
      const t = await fetchForumThread(threadId) as ForumThread;
      setThread(t);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load thread');
    } finally {
      setLoadingThread(false);
    }
  }, [threadId]);

  const loadReplies = useCallback(async (p: number) => {
    if (!threadId) return;
    setLoadingReplies(true);
    setRepliesError(null);
    try {
      const res = await fetchThreadReplies(threadId, p, REPLIES_PAGE_SIZE) as RepliesResponse;
      setReplies(res.replies ?? []);
      setRepliesTotal(res.total ?? 0);
    } catch (err) {
      // The thread still shows; the replies say they failed instead of claiming there are none.
      setRepliesError(err instanceof Error ? err.message : 'Could not load the replies.');
    } finally {
      setLoadingReplies(false);
    }
  }, [threadId]);

  useEffect(() => {
    loadThread();
    loadReplies(1);
    analytics.track('community_thread_viewed', { threadId });
  }, [loadThread, loadReplies, threadId]);

  const handleRepliesPageChange = (p: number) => {
    setRepliesPage(p);
    loadReplies(p);
  };

  async function handleSubmitReply(e: React.FormEvent) {
    e.preventDefault();
    if (!replyBody.trim() || !threadId) return;

    setSubmittingReply(true);
    setReplyError(null);
    try {
      await createReply(threadId, replyBody.trim());
      analytics.track('community_reply_posted', { threadId });
      setReplyBody('');
      // Reload replies to show new one
      await loadReplies(repliesPage);
      // Update thread reply count locally
      if (thread) setThread({ ...thread, replyCount: thread.replyCount + 1 });
    } catch (err) {
      setReplyError(err instanceof Error ? err.message : 'Failed to post reply');
    } finally {
      setSubmittingReply(false);
    }
  }

  const repliesTotalPages = Math.ceil(repliesTotal / REPLIES_PAGE_SIZE);
  const isAuthor = thread && user && thread.authorUserId === user.userId;

  async function handleAdminPin() {
    if (!thread) return;
    setModerating(true);
    try {
      const newPinned = !thread.isPinned;
      await pinCommunityThread(thread.id, newPinned);
      setThread({ ...thread, isPinned: newPinned });
      setModToast({ variant: 'success', message: newPinned ? 'Thread pinned.' : 'Thread unpinned.' });
    } catch {
      setModToast({ variant: 'error', message: 'Failed to update pin status.' });
    } finally {
      setModerating(false);
    }
  }

  async function handleAdminLock() {
    if (!thread) return;
    setModerating(true);
    try {
      const newLocked = !thread.isLocked;
      await lockCommunityThread(thread.id, newLocked);
      setThread({ ...thread, isLocked: newLocked });
      setModToast({ variant: 'success', message: newLocked ? 'Thread locked.' : 'Thread unlocked.' });
    } catch {
      setModToast({ variant: 'error', message: 'Failed to update lock status.' });
    } finally {
      setModerating(false);
    }
  }

  async function handleAdminDeleteThread() {
    if (!threadId) return;
    setModerating(true);
    try {
      await adminDeleteCommunityThread(threadId);
      analytics.track('admin_community_thread_deleted', { threadId });
      router.push('/community');
    } catch {
      setModToast({ variant: 'error', message: 'Failed to delete thread.' });
      setModerating(false);
      setDeleteThreadConfirm(false);
    }
  }

  async function handleAdminDeleteReply() {
    if (!threadId || !deleteReplyTarget) return;
    setModerating(true);
    try {
      await adminDeleteCommunityReply(threadId, deleteReplyTarget.id);
      setReplies((prev) => prev.filter((r) => r.id !== deleteReplyTarget.id));
      setRepliesTotal((prev) => prev - 1);
      if (thread) setThread({ ...thread, replyCount: thread.replyCount - 1 });
      setModToast({ variant: 'success', message: 'Reply deleted.' });
      setDeleteReplyTarget(null);
    } catch {
      setModToast({ variant: 'error', message: 'Failed to delete reply.' });
    } finally {
      setModerating(false);
    }
  }

  return (
    <>
      <div>
        <Button variant="outline" size="sm" onClick={() => router.push('/community')}>
          <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Back to Threads
        </Button>
      </div>

      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      {/* Thread header: the page's one h1 block */}
      {loadingThread ? (
        <div className="space-y-3" role="status" aria-busy="true" aria-label="Loading">
          <Skeleton aria-hidden className="h-8 w-3/4 rounded-xl" />
          <Skeleton aria-hidden className="h-4 w-1/2 rounded-lg" />
          <Skeleton aria-hidden className="h-40 w-full rounded-2xl" />
        </div>
      ) : thread ? (
        <Card padding="lg" className="space-y-4">
          <div>
            <div className="mb-2 flex flex-wrap items-center gap-2 empty:hidden">
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
              {isAuthor && <Badge variant="success">Your thread</Badge>}
            </div>
            <h1 className="text-balance break-words text-xl font-bold leading-tight tracking-tight text-navy sm:text-2xl">{thread.title}</h1>
            {isAdmin && (
              <div className="mt-3 flex flex-wrap items-center gap-2 rounded-xl border border-danger/20 bg-danger/5 p-2">
                <Badge variant="danger">Admin</Badge>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleAdminPin}
                  disabled={moderating}
                >
                  <Pin className={`h-3.5 w-3.5 ${thread.isPinned ? 'text-warning-strong' : ''}`} aria-hidden="true" />
                  {thread.isPinned ? 'Unpin' : 'Pin'}
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={handleAdminLock}
                  disabled={moderating}
                >
                  <Lock className={`h-3.5 w-3.5 ${thread.isLocked ? 'text-danger-strong' : ''}`} aria-hidden="true" />
                  {thread.isLocked ? 'Unlock' : 'Lock'}
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setDeleteThreadConfirm(true)}
                  disabled={moderating}
                  className="text-danger-strong hover:bg-danger/10"
                >
                  <Trash2 className="h-3.5 w-3.5" aria-hidden="true" /> Delete
                </Button>
              </div>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5 text-sm text-muted tabular-nums">
            <span className="flex items-center gap-1.5">
              <User className="h-4 w-4" aria-hidden="true" />
              <span className="font-medium">{thread.authorDisplayName}</span>
              <Badge variant={roleVariant(thread.authorRole)} className="capitalize">
                {thread.authorRole}
              </Badge>
            </span>
            <span className="flex items-center gap-1"><Clock className="h-3.5 w-3.5" aria-hidden="true" /> {formatDate(thread.createdAt)}</span>
            <span className="flex items-center gap-1"><MessageCircle className="h-3.5 w-3.5" aria-hidden="true" /> {thread.replyCount} replies</span>
            <span className="flex items-center gap-1"><Eye className="h-3.5 w-3.5" aria-hidden="true" /> {thread.viewCount} views</span>
            <span className="flex items-center gap-1"><ThumbsUp className="h-3.5 w-3.5" aria-hidden="true" /> {thread.likeCount}<span className="sr-only"> likes</span></span>
          </div>

          <p className="max-w-3xl whitespace-pre-wrap break-words border-t border-border pt-4 text-sm leading-7 text-navy">
            {thread.body}
          </p>
        </Card>
      ) : null}

      {/* Replies: each reply reveals on its own, never the whole thread at once */}
      <section className="space-y-3">
        <LearnerSurfaceSectionHeader title={repliesTotal > 0 ? `Replies (${repliesTotal})` : 'Replies'} />

        {loadingReplies ? (
          <div className="space-y-3" role="status" aria-busy="true" aria-label="Loading">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton aria-hidden key={i} className="h-20 w-full rounded-2xl" />
            ))}
          </div>
        ) : repliesError ? (
          <ErrorState title="Could not load the replies" message={repliesError} onRetry={() => void loadReplies(repliesPage)} />
        ) : replies.length === 0 ? (
          <EmptyState
            icon={<MessageCircle className="h-7 w-7" />}
            title="No replies yet"
            description="Be the first to reply to this thread."
          />
        ) : (
          <div className="space-y-3">
            {replies.map((reply, idx) => (
              <MotionItem key={reply.id} delayIndex={Math.min(idx, 5)}>
                <Card padding="md">
                  <div className="flex items-start gap-3">
                    <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-background-light text-muted" aria-hidden="true">
                      {reply.isExpertVerified ? (
                        <ShieldCheck className="h-4 w-4 text-primary" />
                      ) : (
                        <User className="h-4 w-4" />
                      )}
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="mb-1 flex flex-wrap items-center gap-2">
                        <span className="text-sm font-semibold text-navy">{reply.authorDisplayName}</span>
                        <Badge variant={roleVariant(reply.authorRole)} className="capitalize">
                          {reply.authorRole}
                        </Badge>
                        {reply.isExpertVerified && (
                          <Badge variant="default" className="gap-1">
                            <ShieldCheck className="h-3 w-3" aria-hidden="true" /> Verified
                          </Badge>
                        )}
                        <span className="text-xs text-muted">{formatDate(reply.createdAt)}</span>
                        {reply.editedAt && <span className="text-xs italic text-muted">(edited)</span>}
                      </div>
                      <p className="whitespace-pre-wrap break-words text-sm leading-6 text-navy">{reply.body}</p>
                      {(reply.likeCount > 0 || isAdmin) && (
                        <div className="mt-1.5 flex flex-wrap items-center gap-2">
                          {reply.likeCount > 0 && (
                            <span className="flex items-center gap-1 text-xs text-muted tabular-nums">
                              <ThumbsUp className="h-3 w-3" aria-hidden="true" /> {reply.likeCount}<span className="sr-only"> likes</span>
                            </span>
                          )}
                          {isAdmin && (
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => setDeleteReplyTarget(reply)}
                              disabled={moderating}
                              className="ms-auto text-danger-strong hover:bg-danger/10"
                            >
                              <Trash2 className="h-3 w-3" aria-hidden="true" /> Delete
                            </Button>
                          )}
                        </div>
                      )}
                    </div>
                  </div>
                </Card>
              </MotionItem>
            ))}
          </div>
        )}

        {repliesTotalPages > 1 && !loadingReplies && (
          <div className="flex items-center justify-center gap-2 pt-1">
            <Button variant="outline" size="sm" aria-label="Previous page" disabled={repliesPage <= 1} onClick={() => handleRepliesPageChange(repliesPage - 1)}>
              <ChevronLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
            </Button>
            <span className="text-sm text-muted tabular-nums">Page {repliesPage} of {repliesTotalPages}</span>
            <Button variant="outline" size="sm" aria-label="Next page" disabled={repliesPage >= repliesTotalPages} onClick={() => handleRepliesPageChange(repliesPage + 1)}>
              <ChevronRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
            </Button>
          </div>
        )}
      </section>

      {thread && !thread.isLocked ? (
        <MotionSection>
          <Card padding="lg">
            <h2 className="mb-3 text-base font-bold text-navy">Post a Reply</h2>
            {replyError && <InlineAlert variant="error" className="mb-3">{replyError}</InlineAlert>}
            <form onSubmit={handleSubmitReply} className="space-y-3">
              <Textarea
                aria-label="Post a Reply"
                placeholder="Write your reply..."
                value={replyBody}
                onChange={e => setReplyBody(e.target.value)}
                rows={4}
              />
              <div className="flex justify-end">
                <Button type="submit" disabled={submittingReply || !replyBody.trim()}>
                  <Send className="h-4 w-4" aria-hidden="true" />
                  {submittingReply ? 'Posting…' : 'Post Reply'}
                </Button>
              </div>
            </form>
          </Card>
        </MotionSection>
      ) : thread?.isLocked ? (
        <InlineAlert variant="info" live="polite">
          This thread is locked and no longer accepts replies.
        </InlineAlert>
      ) : null}

      {/* Admin delete thread confirmation */}
      <Modal open={deleteThreadConfirm} onClose={() => setDeleteThreadConfirm(false)} title="Delete Thread" size="sm">
        <div className="space-y-4">
          <p className="text-sm text-muted">
            Are you sure you want to delete <span className="font-semibold text-navy">&ldquo;{thread?.title}&rdquo;</span>?
            This action cannot be undone.
          </p>
          <div className="flex justify-end gap-2">
            <Button variant="outline" size="sm" onClick={() => setDeleteThreadConfirm(false)} disabled={moderating}>
              Cancel
            </Button>
            <Button
              size="sm"
              onClick={handleAdminDeleteThread}
              disabled={moderating}
              variant="destructive"
            >
              {moderating ? 'Deleting…' : 'Delete Thread'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Admin delete reply confirmation */}
      <Modal open={Boolean(deleteReplyTarget)} onClose={() => setDeleteReplyTarget(null)} title="Delete Reply" size="sm">
        <div className="space-y-4">
          <p className="text-sm text-muted">
            Are you sure you want to delete this reply by <span className="font-semibold text-navy">{deleteReplyTarget?.authorDisplayName}</span>?
            This action cannot be undone.
          </p>
          <div className="flex justify-end gap-2">
            <Button variant="outline" size="sm" onClick={() => setDeleteReplyTarget(null)} disabled={moderating}>
              Cancel
            </Button>
            <Button
              size="sm"
              onClick={handleAdminDeleteReply}
              disabled={moderating}
              variant="destructive"
            >
              {moderating ? 'Deleting…' : 'Delete Reply'}
            </Button>
          </div>
        </div>
      </Modal>

      {modToast && <Toast variant={modToast.variant} message={modToast.message} onClose={() => setModToast(null)} />}
    </>
  );
}
