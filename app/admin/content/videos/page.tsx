'use client';

/**
 * Video Library — admin list page. Modelled on the mocks list page
 * (app/admin/content/mocks/page.tsx): AdminOperationsLayout + filters +
 * AdminManagedTable with bulk lifecycle actions, but with true server paging
 * via the X-Total-Count header.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  Archive,
  BarChart3,
  CheckCircle,
  Clapperboard,
  FolderTree,
  ImageIcon,
  Loader2,
  Pencil,
  Plus,
  ShieldAlert,
  Star,
  Trash2,
} from 'lucide-react';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/admin/ui/alert-dialog';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input } from '@/components/admin/ui/input';
import { NativeSelect } from '@/components/admin/ui/native-select';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Toast } from '@/components/ui/alert';
import { type Column } from '@/components/ui/data-table';
import {
  AdminManagedTable,
  type BulkResult,
  type ManagedBulkAction,
} from '@/components/admin/managed-table/admin-managed-table';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { ApiError } from '@/lib/api';
import {
  adminBulkVideoLifecycle,
  adminForceDeleteVideo,
  adminListVideoCategories,
  adminListVideos,
  adminPublishVideo,
  type AdminVideoCategory,
  type AdminVideoSummary,
} from '@/lib/api/video-library';
import { EncodeStatusBadge } from '@/components/domain/video-library/EncodeStatusBadge';
import { buildVideoStepHref } from '@/components/domain/video-library/video-wizard-config';

function formatDuration(totalSeconds: number | null): string {
  if (totalSeconds == null) return '—';
  const s = Math.max(0, Math.round(totalSeconds));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
}

export default function AdminVideoLibraryPage() {
  const { user } = useCurrentUser();
  const userPermissions = user?.adminPermissions;
  const canWrite = hasPermission(userPermissions, AdminPermission.ContentWrite);
  const canPublish = hasPermission(userPermissions, AdminPermission.ContentPublish);
  const isSystemAdmin = hasPermission(userPermissions, AdminPermission.SystemAdmin);

  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState<AdminVideoSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [categories, setCategories] = useState<AdminVideoCategory[]>([]);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<AdminVideoSummary | null>(null);
  const [deleting, setDeleting] = useState(false);

  const [status, setStatus] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [accessTier, setAccessTier] = useState('');
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  // Debounce the free-text search by 300ms so typing doesn't spam the API.
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebouncedSearch(search.trim());
      setPage(1);
    }, 300);
    return () => clearTimeout(timer);
  }, [search]);

  useEffect(() => {
    adminListVideoCategories(true)
      .then(setCategories)
      .catch(() => setCategories([]));
  }, []);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await adminListVideos({
        q: debouncedSearch || undefined,
        status: status || undefined,
        accessTier: accessTier || undefined,
        categoryId: categoryId || undefined,
        page,
        pageSize,
      });
      setRows(response.items);
      setTotal(response.total);
    } catch {
      setToast({ variant: 'error', message: 'Failed to load videos.' });
    } finally {
      setLoading(false);
    }
  }, [debouncedSearch, status, accessTier, categoryId, page, pageSize]);

  useEffect(() => {
    void load();
  }, [load]);

  async function handlePublish(videoId: string, title: string) {
    try {
      await adminPublishVideo(videoId);
      setToast({ variant: 'success', message: `Published "${title}".` });
      await load();
    } catch (err) {
      // Surface the gate's field errors — "not ready" alone is not actionable.
      const details =
        err instanceof ApiError && err.fieldErrors.length > 0
          ? ` ${err.fieldErrors.map((f) => f.message).join(' ')}`
          : '';
      const message = err instanceof Error ? err.message : 'Publish failed.';
      setToast({ variant: 'error', message: `${message}${details}` });
    }
  }

  async function handleDeleteConfirmed() {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await adminForceDeleteVideo(deleteTarget.videoId, 'Permanently deleted from the Video Library.');
      setToast({ variant: 'success', message: `Permanently deleted "${deleteTarget.title}".` });
      setDeleteTarget(null);
      await load();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Delete failed.';
      setToast({ variant: 'error', message });
    } finally {
      setDeleting(false);
    }
  }

  const columns = useMemo<Column<AdminVideoSummary>[]>(() => [
    {
      key: 'thumb',
      header: '',
      render: (row) =>
        row.thumbnailUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={row.thumbnailUrl}
            alt=""
            className="h-10 w-16 rounded-md border border-admin-border object-cover"
          />
        ) : (
          <span className="inline-flex h-10 w-16 items-center justify-center rounded-md border border-dashed border-admin-border text-admin-fg-muted">
            <ImageIcon className="h-4 w-4" />
          </span>
        ),
      hideOnMobile: true,
    },
    {
      key: 'title',
      header: 'Title',
      render: (row) => (
        <div className="min-w-0">
          <p className="flex items-center gap-1.5 font-semibold text-admin-fg-strong line-clamp-1">
            {row.isFeatured ? <Star className="h-3.5 w-3.5 shrink-0 text-amber-500" aria-label="Featured" /> : null}
            {row.title}
          </p>
          <p className="mt-0.5 font-mono text-xs text-admin-fg-muted line-clamp-1">{row.videoId}</p>
        </div>
      ),
    },
    {
      key: 'category',
      header: 'Category',
      render: (row) =>
        row.categoryNames.length > 0 ? (
          <span className="text-sm text-admin-fg-default">{row.categoryNames.join(', ')}</span>
        ) : (
          <span className="text-admin-fg-muted">—</span>
        ),
      hideOnMobile: true,
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge variant={row.status === 'Published' ? 'success' : row.status === 'Archived' ? 'muted' : 'info'}>
          {row.status}
        </Badge>
      ),
    },
    {
      key: 'encode',
      header: 'Encode',
      render: (row) => <EncodeStatusBadge status={row.encodeStatus} />,
    },
    {
      key: 'access',
      header: 'Access',
      render: (row) => (
        <Badge variant={row.accessTier === 'premium' ? 'primary' : 'default'}>
          {row.accessTier === 'premium' ? 'Premium' : 'Free'}
        </Badge>
      ),
      hideOnMobile: true,
    },
    {
      key: 'duration',
      header: 'Duration',
      render: (row) => <span className="text-sm tabular-nums text-admin-fg-default">{formatDuration(row.durationSeconds)}</span>,
      hideOnMobile: true,
    },
    {
      key: 'views',
      header: 'Views',
      render: (row) => <span className="text-sm tabular-nums text-admin-fg-default">{row.viewCount}</span>,
      hideOnMobile: true,
    },
    {
      key: 'updated',
      header: 'Updated',
      render: (row) => (
        <span className="text-sm text-admin-fg-muted">{new Date(row.updatedAt).toLocaleDateString()}</span>
      ),
      hideOnMobile: true,
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div className="flex items-center justify-end gap-2">
          <Link
            href={`/admin/content/videos/${encodeURIComponent(row.videoId)}/analytics`}
            aria-label={`Analytics for ${row.title}`}
            className="inline-flex items-center rounded-lg border border-border px-2.5 py-1.5 text-sm font-medium text-admin-fg-strong hover:bg-admin-bg-subtle"
          >
            <BarChart3 className="h-4 w-4" />
          </Link>
          {canPublish && row.status === 'Draft' && row.encodeStatus === 'ready' ? (
            <Button
              size="sm"
              variant="primary"
              onClick={() => void handlePublish(row.videoId, row.title)}
              aria-label={`Publish ${row.title}`}
            >
              <CheckCircle className="mr-1 h-4 w-4" /> Publish
            </Button>
          ) : null}
          {canWrite ? (
            <Button size="sm" variant="outline" asChild aria-label={`Edit ${row.title}`}>
              <Link href={buildVideoStepHref(row.videoId, 'details')}>
                <Pencil className="mr-1 h-4 w-4" /> Edit
              </Link>
            </Button>
          ) : null}
          {canWrite ? (
            <Button
              size="sm"
              variant="outline"
              onClick={() => setDeleteTarget(row)}
              aria-label={`Delete ${row.title}`}
              className="border-red-200 text-red-600 hover:border-red-300 hover:bg-red-50 hover:text-red-700"
            >
              <Trash2 className="h-4 w-4" />
            </Button>
          ) : null}
        </div>
      ),
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [canWrite, canPublish]);

  const bulkActions = useMemo<ManagedBulkAction<AdminVideoSummary>[]>(() => {
    if (!canWrite) return [];
    const actions: ManagedBulkAction<AdminVideoSummary>[] = [];
    if (canPublish) {
      actions.push({
        key: 'publish',
        label: 'Publish',
        icon: <CheckCircle className="h-4 w-4" />,
        variant: 'primary',
        isEligible: (row) => row.status === 'Draft' && row.encodeStatus === 'ready',
        run: (ids) => adminBulkVideoLifecycle('publish', ids),
      });
    }
    actions.push({
      key: 'archive',
      label: 'Archive',
      icon: <Archive className="h-4 w-4" />,
      variant: 'danger',
      isEligible: (row) => row.status !== 'Archived',
      confirm: {
        title: (n) => `Archive ${n} ${n === 1 ? 'video' : 'videos'}?`,
        description: (n) =>
          `${n} ${n === 1 ? 'video' : 'videos'} will be archived and hidden from learners. An admin can restore them later.`,
        confirmLabel: 'Archive',
        destructive: true,
      },
      run: (ids) => adminBulkVideoLifecycle('archive', ids),
    });
    if (isSystemAdmin) {
      actions.push({
        key: 'force-delete',
        label: 'Force delete',
        icon: <ShieldAlert className="h-4 w-4" />,
        variant: 'danger',
        // Archived-only. Permanently purges the video, its Bunny stream and
        // every learner's watch history for it.
        isEligible: (row) => row.status === 'Archived',
        confirm: {
          title: (n) => `Force-delete ${n} ${n === 1 ? 'video' : 'videos'} and all learner data?`,
          description: (n) =>
            `${n} ${n === 1 ? 'video' : 'videos'} will be permanently removed along with the Bunny stream and every learner's watch history. This cannot be undone.`,
          confirmLabel: 'Force delete',
          destructive: true,
          requireReason: true,
          reasonLabel: 'Reason (recorded in the audit log)',
          reasonPlaceholder: 'Why is this video being permanently deleted?',
        },
        // Force-delete is a per-video endpoint (not part of bulk-lifecycle) —
        // run it sequentially and aggregate a BulkResult.
        run: async (ids, reason) => {
          const errors: string[] = [];
          let succeeded = 0;
          for (const id of ids) {
            try {
              await adminForceDeleteVideo(id, reason ?? '');
              succeeded += 1;
            } catch (err) {
              errors.push(`${id}: ${err instanceof Error ? err.message : 'delete failed'}`);
            }
          }
          return {
            totalRequested: ids.length,
            succeeded,
            skipped: 0,
            failed: errors.length,
            errors,
          };
        },
      });
    }
    return actions;
  }, [canWrite, canPublish, isSystemAdmin]);

  function handleBulkResult(action: ManagedBulkAction<AdminVideoSummary>, result: BulkResult) {
    const verb = action.key === 'publish' ? 'Published' : action.key === 'force-delete' ? 'Deleted' : 'Archived';
    const noun = result.succeeded === 1 ? 'video' : 'videos';
    setToast({
      variant: 'success',
      message: `${verb} ${result.succeeded} ${noun} (${result.skipped ?? 0} skipped, ${result.failed ?? 0} failed)`,
    });
    void load();
  }

  function handleBulkError() {
    setToast({ variant: 'error', message: 'Bulk action failed.' });
  }

  return (
    <>
      <AdminOperationsLayout
        eyebrow="Content"
        title="Video Library"
        description="Manage the learner video catalog hosted on Bunny Stream: upload, organise, gate access, schedule and publish."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Video Library' },
        ]}
      >
        <Card>
          <CardContent className="p-5 space-y-6">
            <div className="flex flex-wrap gap-3">
              {canWrite ? (
                <Button variant="primary" asChild>
                  <Link href="/admin/content/videos/new">
                    <Plus className="mr-2 h-4 w-4" /> New video
                  </Link>
                </Button>
              ) : null}
              <Link
                href="/admin/content/videos/categories"
                className="inline-flex items-center rounded-admin border border-admin-border bg-admin-bg-surface px-4 py-2 text-sm font-bold text-admin-fg-strong hover:bg-admin-bg-subtle"
              >
                <FolderTree className="mr-2 h-4 w-4" /> Categories
              </Link>
              <Link
                href="/admin/content/videos/analytics"
                className="inline-flex items-center rounded-admin border border-admin-border bg-admin-bg-surface px-4 py-2 text-sm font-bold text-admin-fg-strong hover:bg-admin-bg-subtle"
              >
                <BarChart3 className="mr-2 h-4 w-4" /> Analytics
              </Link>
            </div>

            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <Input
                label="Search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="title, tag, video id"
              />
              <NativeSelect
                label="Status"
                value={status}
                onChange={(e) => { setStatus(e.target.value); setPage(1); }}
                options={[
                  { value: '', label: 'All' },
                  { value: 'Draft', label: 'Draft' },
                  { value: 'InReview', label: 'In review' },
                  { value: 'Published', label: 'Published' },
                  { value: 'Rejected', label: 'Rejected' },
                  { value: 'Archived', label: 'Archived' },
                ]}
              />
              <NativeSelect
                label="Category"
                value={categoryId}
                onChange={(e) => { setCategoryId(e.target.value); setPage(1); }}
                options={[
                  { value: '', label: 'All categories' },
                  ...categories.map((c) => ({ value: c.id, label: c.title })),
                ]}
              />
              <NativeSelect
                label="Access"
                value={accessTier}
                onChange={(e) => { setAccessTier(e.target.value); setPage(1); }}
                options={[
                  { value: '', label: 'All' },
                  { value: 'free', label: 'Free' },
                  { value: 'premium', label: 'Premium' },
                ]}
              />
            </div>

            <AdminManagedTable
              columns={columns}
              data={rows}
              keyExtractor={(r) => r.videoId}
              total={total}
              loading={loading}
              page={page}
              pageSize={pageSize}
              onPageChange={setPage}
              onPageSizeChange={(s) => { setPageSize(s); setPage(1); }}
              pageSizeOptions={[10, 25, 50, 100]}
              itemLabel="video"
              itemLabelPlural="videos"
              bulkActions={bulkActions}
              onResult={handleBulkResult}
              onError={handleBulkError}
              emptyState={
                loading ? undefined : (
                  <EmptyState
                    icon={<Clapperboard className="h-6 w-6" />}
                    title="No videos yet"
                    description="Create a video, upload the file straight to Bunny Stream, then publish it for learners."
                  />
                )
              }
            />
          </CardContent>
        </Card>
      </AdminOperationsLayout>

      {toast ? (
        <Toast
          variant={toast.variant}
          message={toast.message}
          duration={toast.variant === 'error' ? 12000 : 5000}
          onClose={() => setToast(null)}
        />
      ) : null}

      <AlertDialog open={deleteTarget !== null} onOpenChange={(open) => { if (!open) setDeleteTarget(null); }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Permanently delete this video?</AlertDialogTitle>
            <AlertDialogDescription>
              {deleteTarget ? (
                <>
                  &ldquo;{deleteTarget.title}&rdquo; and its Bunny Stream video will be deleted, along with all
                  learner watch progress, bookmarks, captions, attachments and analytics. This is irreversible
                  and cannot be undone.
                </>
              ) : null}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleting}>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={(event) => {
                event.preventDefault();
                void handleDeleteConfirmed();
              }}
              disabled={deleting}
              className="bg-red-600 text-white hover:bg-red-700"
            >
              {deleting ? (
                <>
                  <Loader2 className="mr-1 h-4 w-4 animate-spin" /> Deleting…
                </>
              ) : (
                <>
                  <Trash2 className="mr-1 h-4 w-4" /> Delete permanently
                </>
              )}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
