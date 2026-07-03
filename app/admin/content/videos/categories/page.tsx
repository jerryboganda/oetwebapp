'use client';

/**
 * Video Library — category management. Flat taxonomy with manual ordering:
 * create form, up/down reordering (PUT /categories/order), edit modal and a
 * delete-or-deactivate confirm (categories still assigned to videos are
 * deactivated server-side instead of hard-deleted).
 */

import { type FormEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { ArrowDown, ArrowUp, FolderTree, Pencil, Plus, Trash2 } from 'lucide-react';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Button, buttonVariants } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input } from '@/components/admin/ui/input';
import { EmptyState } from '@/components/admin/ui/empty-state';
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
import { Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { DataTable, type Column } from '@/components/ui/data-table';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  adminCreateVideoCategory,
  adminDeleteVideoCategory,
  adminListVideoCategories,
  adminOrderVideoCategories,
  adminPatchVideoCategory,
  type AdminVideoCategory,
} from '@/lib/api/video-library';

export default function AdminVideoCategoriesPage() {
  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [loading, setLoading] = useState(true);
  const [categories, setCategories] = useState<AdminVideoCategory[]>([]);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const [newTitle, setNewTitle] = useState('');
  const [newDescription, setNewDescription] = useState('');
  const [creating, setCreating] = useState(false);

  const [editing, setEditing] = useState<AdminVideoCategory | null>(null);
  const [editTitle, setEditTitle] = useState('');
  const [editDescription, setEditDescription] = useState('');
  const [editActive, setEditActive] = useState(true);
  const [savingEdit, setSavingEdit] = useState(false);

  const [deleting, setDeleting] = useState<AdminVideoCategory | null>(null);
  const [deleteBusy, setDeleteBusy] = useState(false);
  const [orderBusy, setOrderBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const list = await adminListVideoCategories(true);
      setCategories([...list].sort((a, b) => a.displayOrder - b.displayOrder));
    } catch {
      setToast({ variant: 'error', message: 'Failed to load categories.' });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  async function handleCreate(event: FormEvent) {
    event.preventDefault();
    if (!newTitle.trim() || creating) return;
    setCreating(true);
    try {
      await adminCreateVideoCategory({
        title: newTitle.trim(),
        description: newDescription.trim() || undefined,
      });
      setToast({ variant: 'success', message: 'Category created.' });
      setNewTitle('');
      setNewDescription('');
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Create failed.' });
    } finally {
      setCreating(false);
    }
  }

  function openEdit(category: AdminVideoCategory) {
    setEditing(category);
    setEditTitle(category.title);
    setEditDescription(category.description ?? '');
    setEditActive(category.status === 'active');
  }

  async function handleSaveEdit(event: FormEvent) {
    event.preventDefault();
    if (!editing || savingEdit) return;
    if (!editTitle.trim()) {
      setToast({ variant: 'error', message: 'Category name is required.' });
      return;
    }
    setSavingEdit(true);
    try {
      await adminPatchVideoCategory(editing.id, {
        title: editTitle.trim(),
        description: editDescription.trim(),
        status: editActive ? 'active' : 'inactive',
      });
      setToast({ variant: 'success', message: 'Category updated.' });
      setEditing(null);
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Update failed.' });
    } finally {
      setSavingEdit(false);
    }
  }

  async function handleDeleteConfirm() {
    if (!deleting) return;
    setDeleteBusy(true);
    try {
      await adminDeleteVideoCategory(deleting.id);
      setToast({
        variant: 'success',
        message:
          deleting.videoCount > 0
            ? `"${deleting.title}" deactivated (still assigned to ${deleting.videoCount} video${deleting.videoCount === 1 ? '' : 's'}).`
            : `"${deleting.title}" deleted.`,
      });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Delete failed.' });
    } finally {
      setDeleteBusy(false);
      setDeleting(null);
    }
  }

  async function handleMove(index: number, direction: -1 | 1) {
    const target = index + direction;
    if (target < 0 || target >= categories.length || orderBusy) return;
    const orderedIds = categories.map((c) => c.id);
    [orderedIds[index], orderedIds[target]] = [orderedIds[target], orderedIds[index]];
    setOrderBusy(true);
    try {
      await adminOrderVideoCategories(orderedIds);
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Reorder failed.' });
    } finally {
      setOrderBusy(false);
    }
  }

  const columns = useMemo<Column<AdminVideoCategory>[]>(() => [
    {
      key: 'title',
      header: 'Name',
      render: (row) => (
        <div className="min-w-0">
          <p className="font-semibold text-admin-fg-strong line-clamp-1">{row.title}</p>
          <p className="mt-0.5 font-mono text-xs text-admin-fg-muted line-clamp-1">{row.slug}</p>
        </div>
      ),
    },
    {
      key: 'description',
      header: 'Description',
      render: (row) =>
        row.description ? (
          <span className="text-sm text-admin-fg-default line-clamp-2">{row.description}</span>
        ) : (
          <span className="text-admin-fg-muted">—</span>
        ),
      hideOnMobile: true,
    },
    {
      key: 'videos',
      header: 'Videos',
      render: (row) => <span className="text-sm tabular-nums text-admin-fg-default">{row.videoCount}</span>,
      hideOnMobile: true,
    },
    {
      key: 'order',
      header: 'Order',
      render: (row) => {
        const index = categories.findIndex((c) => c.id === row.id);
        return canWrite ? (
          <div className="flex items-center gap-1">
            <Button
              size="sm"
              variant="ghost"
              onClick={() => void handleMove(index, -1)}
              disabled={index <= 0 || orderBusy}
              aria-label={`Move ${row.title} up`}
            >
              <ArrowUp className="h-4 w-4" />
            </Button>
            <Button
              size="sm"
              variant="ghost"
              onClick={() => void handleMove(index, 1)}
              disabled={index === categories.length - 1 || orderBusy}
              aria-label={`Move ${row.title} down`}
            >
              <ArrowDown className="h-4 w-4" />
            </Button>
          </div>
        ) : (
          <span className="text-sm tabular-nums text-admin-fg-muted">{row.displayOrder}</span>
        );
      },
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge variant={row.status === 'active' ? 'success' : 'muted'}>
          {row.status === 'active' ? 'Active' : 'Inactive'}
        </Badge>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (row) =>
        canWrite ? (
          <div className="flex items-center justify-end gap-2">
            <Button size="sm" variant="outline" onClick={() => openEdit(row)} aria-label={`Edit ${row.title}`}>
              <Pencil className="mr-1 h-4 w-4" /> Edit
            </Button>
            <Button size="sm" variant="ghost" onClick={() => setDeleting(row)} aria-label={`Delete ${row.title}`}>
              <Trash2 className="h-4 w-4" />
            </Button>
          </div>
        ) : null,
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
  ], [categories, canWrite, orderBusy]);

  return (
    <>
      <AdminOperationsLayout
        eyebrow="Content"
        title="Video Categories"
        description="Organise the video library into learner-facing categories with manual ordering."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Video Library', href: '/admin/content/videos' },
          { label: 'Categories' },
        ]}
      >
        <Card>
          <CardContent className="p-5 space-y-6">
            {canWrite ? (
              <form
                onSubmit={handleCreate}
                className="grid gap-3 rounded-admin border border-admin-border bg-admin-bg-subtle p-4 md:grid-cols-[1fr_1.5fr_auto]"
              >
                <Input
                  label="Category name"
                  value={newTitle}
                  onChange={(e) => setNewTitle(e.target.value)}
                  placeholder="e.g. Exam strategy"
                />
                <Input
                  label="Description (optional)"
                  value={newDescription}
                  onChange={(e) => setNewDescription(e.target.value)}
                  placeholder="Shown on the learner library"
                />
                <Button type="submit" variant="primary" className="self-end" disabled={!newTitle.trim() || creating}>
                  <Plus className="mr-1 h-4 w-4" /> Create
                </Button>
              </form>
            ) : null}

            {!loading && categories.length === 0 ? (
              <EmptyState
                icon={<FolderTree className="h-6 w-6" />}
                title="No categories yet"
                description="Create the first category to start organising the video library."
              />
            ) : (
              <DataTable columns={columns} data={categories} keyExtractor={(row) => row.id} />
            )}
          </CardContent>
        </Card>
      </AdminOperationsLayout>

      <Modal open={Boolean(editing)} onClose={() => setEditing(null)} title="Edit category" size="md">
        {editing ? (
          <form className="space-y-4" onSubmit={handleSaveEdit}>
            <Input label="Name" value={editTitle} onChange={(e) => setEditTitle(e.target.value)} />
            <Input
              label="Description"
              value={editDescription}
              onChange={(e) => setEditDescription(e.target.value)}
            />
            <label className="flex items-center gap-3 rounded-admin border border-admin-border bg-admin-bg-subtle px-3 py-2 text-sm font-semibold text-admin-fg-strong">
              <input
                type="checkbox"
                checked={editActive}
                onChange={(e) => setEditActive(e.target.checked)}
                className="h-4 w-4 rounded border-border"
              />
              Active (visible to learners)
            </label>
            <div className="flex flex-wrap justify-end gap-2 border-t border-border pt-4">
              <Button type="button" variant="ghost" onClick={() => setEditing(null)}>
                Cancel
              </Button>
              <Button type="submit" variant="primary" disabled={savingEdit}>
                Save category
              </Button>
            </div>
          </form>
        ) : null}
      </Modal>

      <AlertDialog open={Boolean(deleting)} onOpenChange={(open) => !open && setDeleting(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {deleting && deleting.videoCount > 0 ? 'Deactivate this category?' : 'Delete this category?'}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {deleting && deleting.videoCount > 0
                ? `"${deleting.title}" is still assigned to ${deleting.videoCount} video${deleting.videoCount === 1 ? '' : 's'}, so it will be deactivated (hidden from learners) instead of deleted. Unassign the videos first to remove it permanently.`
                : `"${deleting?.title}" has no videos and will be removed permanently.`}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleteBusy}>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className={buttonVariants({ variant: 'destructive' })}
              disabled={deleteBusy}
              onClick={(e) => {
                e.preventDefault();
                void handleDeleteConfirm();
              }}
            >
              {deleting && deleting.videoCount > 0 ? 'Deactivate' : 'Delete'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {toast ? (
        <Toast
          variant={toast.variant}
          message={toast.message}
          duration={toast.variant === 'error' ? 12000 : 5000}
          onClose={() => setToast(null)}
        />
      ) : null}
    </>
  );
}
