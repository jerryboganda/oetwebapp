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
  PenLine,
  Trash2,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Textarea } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { Skeleton, EmptyState } from '@/components/ui';
import { useAuth } from '@/contexts/auth-context';
import { fetchForumThread, fetchThreadReplies, createReply, pinCommunityThread, lockCommunityThread, adminDeleteCommunityThread, adminDeleteCommunityReply } from '@/lib/api';
import { ensureFreshAccessToken } from '@/lib/auth-client';
import { env } from '@/lib/env';
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

function roleColor(role: string) {
  switch (role) {
    case 'expert': return 'text-purple-700 bg-purple-50 border-purple-200';
    case 'admin': return 'text-red-700 bg-red-50 border-red-200';
    default: return 'text-blue-700 bg-blue-50 border-blue-200';
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
  const [replyBody, setReplyBody] = useState('');
  const [loadingThread, setLoadingThread] = useState(true);
  const [loadingReplies, setLoadingReplies] = useState(true);
  const [submittingReply, setSubmittingReply] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [replyError, setReplyError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);
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
    try {
      const res = await fetchThreadReplies(threadId, p, REPLIES_PAGE_SIZE) as RepliesResponse;
      setReplies(res.replies ?? []);
      setRepliesTotal(res.total ?? 0);
    } catch {
      // Non-critical: thread still shown
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

  async function handleDelete() {
    if (!threadId || !confirm('Are you sure you want to delete this thread?')) return;
    setDeleting(true);
    try {
      const token = await ensureFreshAccessToken();
      const res = await fetch(`${env.apiBaseUrl}/v1/community/threads/${encodeURIComponent(threadId)}`, {
        method: 'DELETE',
        headers: { Authorization: `Bearer ${token}` },
      });
      if (!res.ok) throw new Error(`Delete failed (${res.status})`);
      analytics.track('community_thread_deleted', { threadId });
      router.push('/community');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to delete thread');
      setDeleting(false);
    }
  }

  return (
    <LearnerDashboardShell pageTitle={thread?.title ?? 'Thread'}>
      <MotionSection className="space-y-4">
        <Button variant="outline" size="sm" onClick={() => router.push('/community')}>
          <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to Threads
        </Button>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {/* Thread header */}
        {loadingThread ? (
          <div className="space-y-3">
            <Skeleton className="h-8 w-3/4 rounded-xl" />
            <Skeleton className="h-4 w-1/2 rounded-lg" />
            <Skeleton className="h-40 w-full rounded-2xl" />
          </div>
        ) : thread ? (
          <Card className="p-6 shadow-sm">
            <div className="space-y-4">
              {/* Title + badges */}
              <div>
                <div className="flex flex-wrap items-center gap-2 mb-2">
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
                  {isAuthor && (
                    <Badge variant="outline" className="text-emerald-600 border-emerald-300 bg-emerald-50">
                      Your thread
                    </Badge>
                  )}
                </div>
                <h1 className="text-2xl font-bold text-navy">{thread.title}</h1>
                {isAuthor && (
                  <div className="flex items-center gap-2 mt-2">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => router.push(`/community/threads/${thread.id}/edit`)}
                    >
                      <PenLine className="mr-1 h-3.5 w-3.5" /> Edit
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={handleDelete}
                      disabled={deleting}
                      className="text-red-600 hover:bg-red-50"
                    >
                      <Trash2 className="mr-1 h-3.5 w-3.5" /> {deleting ? 'Deleting…' : 'Delete'}
                    </Button>
                  </div>
                )}
                {isAdmin && (
                  <div className="flex flex-wrap items-center gap-2 mt-2 rounded-lg border border-red-100 bg-red-50/50 p-2">
                    <Badge variant="outline" className="text-red-600 border-red-300 bg-red-50 text-xs mr-1">Admin</Badge>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={handleAdminPin}
                      disabled={moderating}
                    >
                      <Pin className={`mr-1 h-3.5 w-3.5 ${thread.isPinned ? 'text-amber-600' : ''}`} />
                      {thread.isPinned ? 'Unpin' : 'Pin'}
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={handleAdminLock}
                      disabled={moderating}
                    >
                      <Lock className={`mr-1 h-3.5 w-3.5 ${thread.isLocked ? 'text-red-600' : ''}`} />
                      {thread.isLocked ? 'Unlock' : 'Lock'}
                    </Button>
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => setDeleteThreadConfirm(true)}
                      disabled={moderating}
                      className="text-red-600 hover:bg-red-50"
                    >
                      <Trash2 className="mr-1 h-3.5 w-3.5" /> Delete
                    </Button>
                  </div>
                )}
              </div>

              {/* Author + meta */}
              <div className="flex flex-wrap items-center gap-3 text-sm text-muted">
                <span className="flex items-center gap-1.5">
                  <User className="h-4 w-4" />
                  <span className="font-medium">{thread.authorDisplayName}</span>
                  <Badge variant="outline" className={roleColor(thread.authorRole)}>
                    {thread.authorRole}
                  </Badge>
                </span>
                <span className="flex items-center gap-1"><Clock className="h-3.5 w-3.5" /> {formatDate(thread.createdAt)}</span>
                <span className="flex items-center gap-1"><MessageCircle className="h-3.5 w-3.5" /> {thread.replyCount} replies</span>
                <span className="flex items-center gap-1"><Eye className="h-3.5 w-3.5" /> {thread.viewCount} views</span>
                <span className="flex items-center gap-1"><ThumbsUp className="h-3.5 w-3.5" /> {thread.likeCount}</span>
              </div>

              {/* Body */}
              <div className="prose prose-sm max-w-none rounded-xl bg-background-light p-4 text-navy whitespace-pre-wrap">
                {thread.body}
              </div>
            </div>
          </Card>
        ) : null}

        {/* Replies section */}
        <div className="space-y-3">
          <h2 className="text-lg font-bold text-navy">
            Replies {repliesTotal > 0 && <span className="text-muted font-normal">({repliesTotal})</span>}
          </h2>

          {loadingReplies ? (
            <div className="space-y-2">
              {Array.from({ length: 3 }).map((_, i) => (
                <Skeleton key={i} className="h-20 w-full rounded-2xl" />
              ))}
            </div>
          ) : replies.length === 0 ? (
            <EmptyState
              icon={<MessageCircle className="h-7 w-7" />}
              title="No replies yet"
              description="Be the first to reply to this thread."
            />
          ) : (
            <div className="space-y-2">
              {replies.map((reply, idx) => (
                <MotionItem key={reply.id} delayIndex={idx}>
                  <Card className="p-4 shadow-sm">
                    <div className="flex items-start gap-3">
                      <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-surface text-muted">
                        {reply.isExpertVerified ? (
                          <ShieldCheck className="h-4 w-4 text-purple-600" />
                        ) : (
                          <User className="h-4 w-4" />
                        )}
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex flex-wrap items-center gap-2 mb-1">
                          <span className="text-sm font-semibold text-navy">{reply.authorDisplayName}</span>
                          <Badge variant="outline" className={roleColor(reply.authorRole)}>
                            {reply.authorRole}
                          </Badge>
                          {reply.isExpertVerified && (
                            <Badge variant="outline" className="text-purple-600 border-purple-300 bg-purple-50">
                              <ShieldCheck className="mr-1 h-3 w-3" /> Verified
                            </Badge>
                          )}
                          <span className="text-xs text-muted">{formatDate(reply.createdAt)}</span>
                          {reply.editedAt && <span className="text-xs text-muted italic">(edited)</span>}
                        </div>
                        <p className="text-sm text-navy whitespace-pre-wrap">{reply.body}</p>
                        <div className="mt-1.5 flex flex-wrap items-center gap-2">
                          {reply.likeCount > 0 && (
                            <span className="flex items-center gap-1 text-xs text-muted">
                              <ThumbsUp className="h-3 w-3" /> {reply.likeCount}
                            </span>
                          )}
                          {isAdmin && (
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => setDeleteReplyTarget(reply)}
                              disabled={moderating}
                              className="text-red-600 hover:bg-red-50 ml-auto text-xs h-6 px-2"
                            >
                              <Trash2 className="mr-1 h-3 w-3" /> Delete
                            </Button>
                          )}
                        </div>
                      </div>
                    </div>
                  </Card>
                </MotionItem>
              ))}
            </div>
          )}

          {/* Replies pagination */}
          {repliesTotalPages > 1 && !loadingReplies && (
            <div className="flex items-center justify-center gap-2 pt-1">
              <Button variant="outline" size="sm" disabled={repliesPage <= 1} onClick={() => handleRepliesPageChange(repliesPage - 1)}>
                <ChevronLeft className="h-4 w-4" />
              </Button>
              <span className="text-sm text-muted">Page {repliesPage} of {repliesTotalPages}</span>
              <Button variant="outline" size="sm" disabled={repliesPage >= repliesTotalPages} onClick={() => handleRepliesPageChange(repliesPage + 1)}>
                <ChevronRight className="h-4 w-4" />
              </Button>
            </div>
          )}
        </div>

        {/* Reply form */}
        {thread && !thread.isLocked ? (
          <Card className="p-5 shadow-sm">
            <h3 className="mb-3 text-sm font-bold text-navy">Post a Reply</h3>
            {replyError && <InlineAlert variant="error" className="mb-3">{replyError}</InlineAlert>}
            <form onSubmit={handleSubmitReply} className="space-y-3">
              <Textarea
                placeholder="Write your reply..."
                value={replyBody}
                onChange={e => setReplyBody(e.target.value)}
                rows={4}
              />
              <div className="flex justify-end">
                <Button type="submit" disabled={submittingReply || !replyBody.trim()}>
                  <Send className="mr-1.5 h-4 w-4" />
                  {submittingReply ? 'Posting…' : 'Post Reply'}
                </Button>
              </div>
            </form>
          </Card>
        ) : thread?.isLocked ? (
          <InlineAlert variant="info">
            <Lock className="mr-1.5 h-4 w-4 inline" />
            This thread is locked and no longer accepts replies.
          </InlineAlert>
        ) : null}
      </MotionSection>

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
              className="bg-red-600 text-white hover:bg-red-700"
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
              className="bg-red-600 text-white hover:bg-red-700"
            >
              {moderating ? 'Deleting…' : 'Delete Reply'}
            </Button>
          </div>
        </div>
      </Modal>

      {modToast && <Toast variant={modToast.variant} message={modToast.message} onClose={() => setModToast(null)} />}
    </LearnerDashboardShell>
  );
}
