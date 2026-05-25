'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, BookOpen, Plus, Save, Trash2, X, RefreshCw } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Input } from '@/components/admin/ui/input';
import { Textarea } from '@/components/admin/ui/textarea';
import { Checkbox } from '@/components/admin/ui/checkbox';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/admin/ui/dialog';
import { Toast } from '@/components/ui/alert';
import {
  adminListRecallSetTags,
  adminCreateRecallSetTag,
  adminUpdateRecallSetTag,
  adminArchiveRecallSetTag,
  adminUnarchiveRecallSetTag,
  adminDeleteRecallSetTag,
  type RecallSetTagDto,
} from '@/lib/api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function AdminRecallSetTagsPage() {
  const [items, setItems] = useState<RecallSetTagDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [includeArchived, setIncludeArchived] = useState(false);
  const [busyCode, setBusyCode] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const [editOpen, setEditOpen] = useState(false);
  const [editing, setEditing] = useState<RecallSetTagDto | null>(null);
  const [form, setForm] = useState({
    code: '',
    displayName: '',
    shortLabel: '',
    description: '',
    sortOrder: 100,
    isActive: true,
    examTypeCode: 'oet' as string,
  });
  const [saving, setSaving] = useState(false);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListRecallSetTags({ includeArchived });
      setItems(data ?? []);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setLoading(false);
    }
  }, [includeArchived]);

  useEffect(() => { void reload(); }, [reload]);

  function openCreate() {
    setEditing(null);
    setForm({ code: '', displayName: '', shortLabel: '', description: '', sortOrder: 100, isActive: true, examTypeCode: 'oet' });
    setEditOpen(true);
  }

  function openEdit(row: RecallSetTagDto) {
    setEditing(row);
    setForm({
      code: row.code,
      displayName: row.displayName,
      shortLabel: row.shortLabel ?? '',
      description: row.description ?? '',
      sortOrder: row.sortOrder,
      isActive: row.isActive,
      examTypeCode: row.examTypeCode ?? 'oet',
    });
    setEditOpen(true);
  }

  async function handleSave(e?: React.FormEvent) {
    e?.preventDefault();
    if (!form.code.trim() || !form.displayName.trim()) {
      setToast({ variant: 'error', message: 'Code and Display name are required.' });
      return;
    }
    setSaving(true);
    try {
      if (editing) {
        await adminUpdateRecallSetTag(editing.code, {
          displayName: form.displayName.trim(),
          shortLabel: form.shortLabel.trim() || null,
          description: form.description.trim() || null,
          sortOrder: form.sortOrder,
          isActive: form.isActive,
          examTypeCode: form.examTypeCode || null,
        });
        setToast({ variant: 'success', message: `Updated "${editing.code}".` });
      } else {
        const created = await adminCreateRecallSetTag({
          code: form.code.trim().toLowerCase(),
          displayName: form.displayName.trim(),
          shortLabel: form.shortLabel.trim() || null,
          description: form.description.trim() || null,
          sortOrder: form.sortOrder,
          isActive: form.isActive,
          examTypeCode: form.examTypeCode || null,
        });
        setToast({ variant: 'success', message: `Created "${created.code}".` });
      }
      setEditOpen(false);
      await reload();
    } catch (err) {
      setToast({ variant: 'error', message: (err as Error).message });
    } finally {
      setSaving(false);
    }
  }

  async function handleArchiveToggle(row: RecallSetTagDto) {
    setBusyCode(row.code);
    try {
      if (row.isActive) await adminArchiveRecallSetTag(row.code);
      else await adminUnarchiveRecallSetTag(row.code);
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyCode(null);
    }
  }

  async function handleDelete(row: RecallSetTagDto) {
    if (!confirm(`Delete recall set tag "${row.code}"?\n\nCanonical codes (old / 2023-2025 / 2026) and codes in use by existing terms will be archived instead of hard-deleted.`)) return;
    setBusyCode(row.code);
    try {
      const res = await adminDeleteRecallSetTag(row.code);
      setToast({
        variant: 'success',
        message: res.hardDelete ? `Deleted "${row.code}".` : `Archived "${row.code}" (${res.reason ?? 'canonical or in use'}).`,
      });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyCode(null);
    }
  }

  const filters = (
    <div className="flex items-center justify-between w-full gap-3">
      <label className="inline-flex items-center gap-2 text-sm text-admin-fg-muted">
        <Checkbox
          checked={includeArchived}
          onCheckedChange={(v) => setIncludeArchived(v === true)}
        />
        Include archived
      </label>
      <Button size="sm" variant="ghost" onClick={() => void reload()}>
        <RefreshCw className="h-4 w-4 mr-1" /> Refresh
      </Button>
    </div>
  );

  return (
    <>
      <AdminCatalogLayout
        eyebrow="CMS"
        title="Recall practice collection labels"
        description="Manage the categorisation labels admins pick when bulk-uploading vocabulary recalls. Canonical codes (old / 2023-2025 / 2026) are protected from hard delete."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Vocabulary', href: '/admin/content/vocabulary' },
          { label: 'Recall set tags' },
        ]}
        actions={(
          <>
            <Button variant="secondary" size="sm" asChild>
              <Link href="/admin/content/vocabulary"><ArrowLeft className="mr-1.5 h-4 w-4" />Back to vocab</Link>
            </Button>
            <Button variant="primary" size="sm" onClick={openCreate}><Plus className="mr-1.5 h-4 w-4" />New tag</Button>
          </>
        )}
        filters={filters}
        hideViewModeToggle
        itemsClassName="flex flex-col gap-3"
      >
        {loading ? (
          <div className="space-y-3">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-20 rounded-admin-lg" />)}
          </div>
        ) : items.length === 0 ? (
          <EmptyState
            illustration={<BookOpen />}
            title="No recall set tags yet"
            description='Click "New tag" to add the first one.'
            primaryAction={{ label: 'New tag', onClick: openCreate }}
          />
        ) : (
          items.map((row) => (
            <Card key={row.code}>
              <CardContent className="p-4 pt-4">
                <div className="flex items-start justify-between flex-wrap gap-3">
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2 flex-wrap mb-1">
                      <h3 className="font-semibold text-admin-fg-strong truncate">{row.displayName}</h3>
                      {row.canonical ? <Badge variant="info" size="sm">canonical</Badge> : null}
                      {!row.isActive ? <Badge variant="secondary" size="sm">archived</Badge> : <Badge variant="success" size="sm">active</Badge>}
                      {row.shortLabel ? <Badge variant="default" size="sm">{row.shortLabel}</Badge> : null}
                      <code className="text-xs text-admin-fg-muted">{row.code}</code>
                      {row.examTypeCode ? <Badge variant="default" size="sm">{row.examTypeCode}</Badge> : null}
                    </div>
                    {row.description ? (
                      <p className="text-sm text-admin-fg-muted line-clamp-2">{row.description}</p>
                    ) : null}
                    <p className="text-xs text-admin-fg-muted mt-1">
                      sort {row.sortOrder} · updated {new Date(row.updatedAt).toLocaleDateString()}
                    </p>
                  </div>
                  <div className="flex items-center gap-1 flex-wrap">
                    <Button size="sm" variant="secondary" onClick={() => openEdit(row)} disabled={busyCode === row.code}>Edit</Button>
                    <Button size="sm" variant="secondary" onClick={() => void handleArchiveToggle(row)} disabled={busyCode === row.code}>
                      {row.isActive ? 'Archive' : 'Unarchive'}
                    </Button>
                    <Button size="sm" variant="ghost" onClick={() => void handleDelete(row)} disabled={busyCode === row.code}>
                      <Trash2 className="h-4 w-4" />
                    </Button>
                  </div>
                </div>
              </CardContent>
            </Card>
          ))
        )}
      </AdminCatalogLayout>

      <Dialog open={editOpen} onOpenChange={setEditOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>{editing ? `Edit "${editing.code}"` : 'New recall set tag'}</DialogTitle>
          </DialogHeader>
          <form className="space-y-3" onSubmit={(e) => void handleSave(e)}>
            <Input
              label="Code (lowercase, a-z 0-9 -)"
              value={form.code}
              onChange={(e) => setForm({ ...form, code: e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, '') })}
              placeholder='e.g. "2027-q1"'
              required
              disabled={!!editing}
              maxLength={64}
            />
            <Input
              label="Display name"
              value={form.displayName}
              onChange={(e) => setForm({ ...form, displayName: e.target.value })}
              placeholder='e.g. "January 2027 Q1 recalls"'
              required
              maxLength={200}
            />
            <Input
              label="Short label (optional, for chip display)"
              value={form.shortLabel}
              onChange={(e) => setForm({ ...form, shortLabel: e.target.value })}
              placeholder='e.g. "2027 Q1"'
              maxLength={64}
            />
            <Textarea
              label="Description (optional)"
              value={form.description}
              onChange={(e) => setForm({ ...form, description: e.target.value })}
              rows={3}
            />
            <div className="grid grid-cols-2 gap-3">
              <Input
                type="number"
                label="Sort order (lower shows first)"
                value={String(form.sortOrder)}
                onChange={(e) => setForm({ ...form, sortOrder: Number.parseInt(e.target.value || '100', 10) })}
              />
              <Input
                label="Exam (leave blank for all)"
                value={form.examTypeCode}
                onChange={(e) => setForm({ ...form, examTypeCode: e.target.value.trim().toLowerCase() })}
                placeholder="oet"
                maxLength={16}
              />
            </div>
            <label className="inline-flex items-center gap-2 text-sm text-admin-fg-default">
              <Checkbox
                checked={form.isActive}
                onCheckedChange={(v) => setForm({ ...form, isActive: v === true })}
              />
              Active (appears in the recall set picker)
            </label>
            <DialogFooter>
              <Button type="button" variant="secondary" onClick={() => setEditOpen(false)} disabled={saving}>
                <X className="h-4 w-4 mr-1" /> Cancel
              </Button>
              <Button type="submit" variant="primary" disabled={saving}>
                <Save className="h-4 w-4 mr-1" /> {saving ? 'Saving…' : (editing ? 'Update' : 'Create')}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </>
  );
}
