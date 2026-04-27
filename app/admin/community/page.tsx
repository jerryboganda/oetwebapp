'use client';

import { useEffect, useState, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { MessageSquareText, Pin, Lock, Trash2, Eye, MessageCircle, Clock } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { Pagination } from '@/components/ui/pagination';
import { fetchForumThreads, pinCommunityThread, lockCommunityThread, adminDeleteCommunityThread } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

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

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;


function formatDate(dateStr: string) {
  return new Date(dateStr).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}

export default function AdminCommunityPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const router = useRouter();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [threads, setThreads] = useState<ForumThreadSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [pageSize, setPageSize] = useState(20);
  const [page, setPage] = useState(1);
  const [deleteTarget, setDeleteTarget] = useState<ForumThreadSummary | null>(null);
  const [isMutating, setIsMutating] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const loadThreads = useCallback(async (p: number, size: number) => {
    setPageStatus('loading');
    try {
      const res = await fetchForumThreads(undefined, p, size) as ThreadsResponse;
      setThreads(res.threads ?? []);
      setTotal(res.total ?? 0);
      setPageStatus((res.threads ?? []).length > 0 ? 'success' : 'empty');
    } catch {
      setPageStatus('error');
    }
  }, []);

  useEffect(() => {
    loadThreads(page, pageSize);
  }, [loadThreads, page, pageSize]);

  async function handlePin(thread: ForumThreadSummary) {
    const newPinned = !thread.isPinned;
    setIsMutating(true);
    try {
      await pinCommunityThread(thread.id, newPinned);
      setThreads((prev) => prev.map((t) => t.id === thread.id ? { ...t, isPinned: newPinned } : t));
      setToast({ variant: 'success', message: newPinned ? 'Thread pinned.' : 'Thread unpinned.' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to update pin status.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleLock(thread: ForumThreadSummary) {
    const newLocked = !thread.isLocked;
    setIsMutating(true);
    try {
      await lockCommunityThread(thread.id, newLocked);
      setThreads((prev) => prev.map((t) => t.id === thread.id ? { ...t, isLocked: newLocked } : t));
      setToast({ variant: 'success', message: newLocked ? 'Thread locked.' : 'Thread unlocked.' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to update lock status.' });
    } finally {
      setIsMutating(false);
    }
  }

  async function handleDeleteConfirm() {
    if (!deleteTarget) return;
    setIsMutating(true);
    try {
      await adminDeleteCommunityThread(deleteTarget.id);
      setThreads((prev) => prev.filter((t) => t.id !== deleteTarget.id));
      setTotal((prev) => prev - 1);
      setToast({ variant: 'success', message: 'Thread deleted.' });
      setDeleteTarget(null);
    } catch {
      setToast({ variant: 'error', message: 'Failed to delete thread.' });
    } finally {
      setIsMutating(false);
    }
  }

  const columns: Column<ForumThreadSummary>[] = [
    {
      key: 'title',
      header: 'Thread',
      render: (row) => (
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-1.5 mb-0.5">
            {row.isPinned && (
              <Badge variant="warning" className="text-[10px] px-1.5 py-0">
                <Pin className="mr-0.5 h-2.5 w-2.5" /> Pinned
              </Badge>
            )}
            {row.isLocked && (
              <Badge variant="muted" className="text-[10px] px-1.5 py-0">
                <Lock className="mr-0.5 h-2.5 w-2.5" /> Locked
              </Badge>
            )}
          </div>
          <span className="font-medium text-navy truncate block">{row.title}</span>
          <span className="text-xs text-muted">by {row.authorDisplayName}</span>
        </div>
      ),
    },
    {
      key: 'stats',
      header: 'Stats',
      hideOnMobile: true,
      render: (row) => (
        <div className="flex items-center gap-3 text-xs text-muted">
          <span className="flex items-center gap-1"><MessageCircle className="h-3 w-3" /> {row.replyCount}</span>
          <span className="flex items-center gap-1"><Eye className="h-3 w-3" /> {row.viewCount}</span>
        </div>
      ),
    },
    {
      key: 'date',
      header: 'Created',
      hideOnMobile: true,
      render: (row) => (
        <span className="flex items-center gap-1 text-xs text-muted">
          <Clock className="h-3 w-3" /> {formatDate(row.createdAt)}
        </span>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      render: (row) => (
        <div className="flex items-center gap-1.5" onClick={(e) => e.stopPropagation()}>
          <Button
            variant="outline"
            size="sm"
            onClick={() => handlePin(row)}
            disabled={isMutating}
            title={row.isPinned ? 'Unpin thread' : 'Pin thread'}
          >
            <Pin className={`h-3.5 w-3.5 ${row.isPinned ? 'text-warning' : ''}`} />
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => handleLock(row)}
            disabled={isMutating}
            title={row.isLocked ? 'Unlock thread' : 'Lock thread'}
          >
            <Lock className={`h-3.5 w-3.5 ${row.isLocked ? 'text-danger' : ''}`} />
          </Button>
          <Button
            variant="outline"
            size="sm"
            onClick={() => setDeleteTarget(row)}
            disabled={isMutating}
            className="text-danger"
            title="Delete thread"
          >
            <Trash2 className="h-3.5 w-3.5" />
          </Button>
        </div>
      ),
    },
  ];

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Community moderation">
      <AsyncStateWrapper status={pageStatus} onRetry={() => loadThreads(page, pageSize)}>
        <div className="space-y-6">
          <AdminRouteSectionHeader
            icon={MessageSquareText}
            title="Community moderation"
            description="Pin, lock, or delete forum threads to keep discussions on-topic."
          />

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <AdminRouteSummaryCard label="Total Threads" value={String(total)} />
            <AdminRouteSummaryCard label="Pinned" value={String(threads.filter((t) => t.isPinned).length)} />
            <AdminRouteSummaryCard label="Locked" value={String(threads.filter((t) => t.isLocked).length)} />
          </div>

          <AdminRoutePanel>
            {threads.length === 0 ? (
              <EmptyState
                icon={<MessageSquareText className="h-8 w-8" />}
                title="No threads"
                description="No community threads have been created yet."
              />
            ) : (
              <>
                <DataTable<ForumThreadSummary>
                  columns={columns}
                  data={threads}
                  keyExtractor={(row) => row.id}
                  onRowClick={(row) => router.push(`/community/threads/${row.id}`)}
                  mobileCardRender={(row, _index) => (
                    <div className="space-y-2">
                      <div className="flex flex-wrap items-center gap-1.5">
                        {row.isPinned && (
                          <Badge variant="warning" className="text-xs">
                            <Pin className="mr-0.5 h-3 w-3" /> Pinned
                          </Badge>
                        )}
                        {row.isLocked && (
                          <Badge variant="muted" className="text-xs">
                            <Lock className="mr-0.5 h-3 w-3" /> Locked
                          </Badge>
                        )}
                      </div>
                      <p className="font-medium text-navy">{row.title}</p>
                      <p className="text-xs text-muted">by {row.authorDisplayName} · {formatDate(row.createdAt)}</p>
                      <div className="flex items-center gap-3 text-xs text-muted">
                        <span className="flex items-center gap-1"><MessageCircle className="h-3 w-3" /> {row.replyCount}</span>
                        <span className="flex items-center gap-1"><Eye className="h-3 w-3" /> {row.viewCount}</span>
                      </div>
                      <div className="flex items-center gap-1.5 pt-1">
                        <Button variant="outline" size="sm" onClick={() => handlePin(row)} disabled={isMutating}>
                          <Pin className={`h-3.5 w-3.5 mr-1 ${row.isPinned ? 'text-warning' : ''}`} />
                          {row.isPinned ? 'Unpin' : 'Pin'}
                        </Button>
                        <Button variant="outline" size="sm" onClick={() => handleLock(row)} disabled={isMutating}>
                          <Lock className={`h-3.5 w-3.5 mr-1 ${row.isLocked ? 'text-danger' : ''}`} />
                          {row.isLocked ? 'Unlock' : 'Lock'}
                        </Button>
                        <Button variant="outline" size="sm" onClick={() => setDeleteTarget(row)} disabled={isMutating} className="text-danger">
                          <Trash2 className="h-3.5 w-3.5 mr-1" /> Delete
                        </Button>
                      </div>
                    </div>
                  )}
                  aria-label="Community threads"
                />

                <Pagination
                  page={page}
                  pageSize={pageSize}
                  total={total}
                  onPageChange={setPage}
                  onPageSizeChange={setPageSize}
                  itemLabel="thread"
                  itemLabelPlural="threads"
                />
              </>
            )}
          </AdminRoutePanel>
        </div>
      </AsyncStateWrapper>

      {/* Delete confirmation modal */}
      <Modal open={Boolean(deleteTarget)} onClose={() => setDeleteTarget(null)} title="Delete Thread" size="sm">
        <div className="space-y-4">
          <p className="text-sm text-muted">
            Are you sure you want to delete <span className="font-semibold text-navy">&ldquo;{deleteTarget?.title}&rdquo;</span>?
            This action cannot be undone.
          </p>
          <div className="flex justify-end gap-2">
            <Button variant="outline" size="sm" onClick={() => setDeleteTarget(null)} disabled={isMutating}>
              Cancel
            </Button>
            <Button
              size="sm"
              variant="primary"
              onClick={handleDeleteConfirm}
              disabled={isMutating}
            >
              {isMutating ? 'Deleting…' : 'Delete Thread'}
            </Button>
          </div>
        </div>
      </Modal>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
