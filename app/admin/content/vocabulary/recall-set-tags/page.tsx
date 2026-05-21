'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, BookOpen, Plus, Save, Trash2, X, RefreshCw } from 'lucide-react';
import {
  AdminRouteWorkspace,
  AdminRoutePanel,
  AdminRouteSectionHeader,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Modal } from '@/components/ui/modal';
import { Input, Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
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

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        eyebrow="CMS"
        title="Recall practice collection labels"
        description="Manage the categorisation labels admins pick when bulk-uploading vocabulary recalls. Canonical codes (old / 2023-2025 / 2026) are protected from hard delete."
        icon={BookOpen}
        actions={(
          <>
            <Button variant="secondary" size="sm" asChild>
              <Link href="/admin/content/vocabulary"><ArrowLeft className="mr-1.5 h-4 w-4" />Back to vocab</Link>
            </Button>
            <Button onClick={openCreate}><Plus className="mr-1.5 h-4 w-4" />New tag</Button>
          </>
        )}
      />

      <AdminRoutePanel>
        <div className="flex items-center justify-between">
          <label className="inline-flex items-center gap-2 text-sm text-admin-text-muted">
            <input
              type="checkbox"
              checked={includeArchived}
              onChange={(e) => setIncludeArchived(e.target.checked)}
            />
            Include archived
          </label>
          <Button size="sm" variant="outline" onClick={() => void reload()}>
            <RefreshCw className="h-4 w-4 mr-1" /> Refresh
          </Button>
        </div>
      </AdminRoutePanel>

      {loading ? (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => <Skeleton key={i} className="h-20" />)}
        </div>
      ) : items.length === 0 ? (
        <Card className="p-8 text-center text-muted">
          No recall set tags yet. Click <strong>New tag</strong> to add the first one.
        </Card>
      ) : (
        <div className="space-y-3">
          {items.map((row) => (
            <Card key={row.code} className="p-4">
              <div className="flex items-start justify-between flex-wrap gap-3">
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2 flex-wrap mb-1">
                    <h3 className="font-semibold truncate">{row.displayName}</h3>
                    {row.canonical ? <Badge variant="info">canonical</Badge> : null}
                    {!row.isActive ? <Badge variant="outline">archived</Badge> : <Badge variant="success">active</Badge>}
                    {row.shortLabel ? <Badge variant="outline">{row.shortLabel}</Badge> : null}
                    <code className="text-xs text-muted">{row.code}</code>
                    {row.examTypeCode ? <Badge variant="outline">{row.examTypeCode}</Badge> : null}
                  </div>
                  {row.description ? (
                    <p className="text-sm text-muted line-clamp-2">{row.description}</p>
                  ) : null}
                  <p className="text-xs text-muted mt-1">
                    sort {row.sortOrder} · updated {new Date(row.updatedAt).toLocaleDateString()}
                  </p>
                </div>
                <div className="flex items-center gap-1 flex-wrap">
                  <Button size="sm" variant="outline" onClick={() => openEdit(row)} disabled={busyCode === row.code}>Edit</Button>
                  <Button size="sm" variant="outline" onClick={() => void handleArchiveToggle(row)} disabled={busyCode === row.code}>
                    {row.isActive ? 'Archive' : 'Unarchive'}
                  </Button>
                  <Button size="sm" variant="ghost" onClick={() => void handleDelete(row)} disabled={busyCode === row.code}>
                    <Trash2 className="h-4 w-4" />
                  </Button>
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      {editOpen ? (
        <Modal open={editOpen} onClose={() => setEditOpen(false)} title={editing ? `Edit "${editing.code}"` : 'New recall set tag'}>
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
            <label className="inline-flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={form.isActive}
                onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
              />
              Active (appears in the recall set picker)
            </label>
            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" variant="outline" onClick={() => setEditOpen(false)} disabled={saving}>
                <X className="h-4 w-4 mr-1" /> Cancel
              </Button>
              <Button type="submit" disabled={saving}>
                <Save className="h-4 w-4 mr-1" /> {saving ? 'Saving…' : (editing ? 'Update' : 'Create')}
              </Button>
            </div>
          </form>
        </Modal>
      ) : null}

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </AdminRouteWorkspace>
  );
}
