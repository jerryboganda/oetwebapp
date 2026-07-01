'use client';

/**
 * Speaking card-type management.
 *
 * Full CRUD over the hidden communication-function card taxonomy
 * (`/v1/admin/speaking/card-types`). Card types are NEVER shown to students —
 * they guide human + AI marking only. Owner decision (2026-06-29): the 6
 * seeded types must be fully editable here (rename, re-describe, reorder,
 * activate/deactivate, delete). Inline creation also remains in the card
 * wizard's Classification step.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { ArrowDown, ArrowUp, Loader2, Pencil, Plus, Trash2, X } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Textarea } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { hasPermission, AdminPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  adminCreateSpeakingCardType,
  adminDeleteSpeakingCardType,
  adminListSpeakingCardTypes,
  adminUpdateSpeakingCardType,
  type SpeakingCardTypeDetail,
} from '@/lib/api/speaking-role-play-cards';

type ToastState = { variant: 'success' | 'error'; message: string } | null;
type EditState = { id: string | null; name: string; description: string } | null;

export default function AdminSpeakingCardTypesPage() {
  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [types, setTypes] = useState<SpeakingCardTypeDetail[]>([]);
  const [loading, setLoading] = useState(true);
  const [editing, setEditing] = useState<EditState>(null);
  const [saving, setSaving] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      setTypes(await adminListSpeakingCardTypes(true));
    } catch (e) {
      setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Failed to load card types.' });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const sorted = useMemo(
    () => [...types].sort((a, b) => a.sortOrder - b.sortOrder || a.name.localeCompare(b.name)),
    [types],
  );

  const startCreate = () => {
    setFormError(null);
    setEditing({ id: null, name: '', description: '' });
  };
  const startEdit = (t: SpeakingCardTypeDetail) => {
    setFormError(null);
    setEditing({ id: t.id, name: t.name, description: t.description ?? '' });
  };

  const saveForm = useCallback(async () => {
    if (!editing) return;
    if (!editing.name.trim()) {
      setFormError('Name is required.');
      return;
    }
    setSaving(true);
    setFormError(null);
    try {
      if (editing.id) {
        await adminUpdateSpeakingCardType(editing.id, {
          name: editing.name.trim(),
          description: editing.description.trim() || null,
        });
        setToast({ variant: 'success', message: 'Card type updated.' });
      } else {
        const nextOrder = (sorted.at(-1)?.sortOrder ?? 0) + 1;
        await adminCreateSpeakingCardType({
          name: editing.name.trim(),
          description: editing.description.trim() || null,
          sortOrder: nextOrder,
        });
        setToast({ variant: 'success', message: 'Card type created.' });
      }
      setEditing(null);
      await reload();
    } catch (e) {
      setFormError(e instanceof Error ? e.message : 'Could not save card type.');
    } finally {
      setSaving(false);
    }
  }, [editing, reload, sorted]);

  const toggleActive = useCallback(async (t: SpeakingCardTypeDetail) => {
    setBusyId(t.id);
    try {
      await adminUpdateSpeakingCardType(t.id, { name: t.name, isActive: !t.isActive });
      setToast({ variant: 'success', message: t.isActive ? 'Card type deactivated.' : 'Card type activated.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Update failed.' });
    } finally {
      setBusyId(null);
    }
  }, [reload]);

  const remove = useCallback(async (t: SpeakingCardTypeDetail) => {
    const msg = t.cardCount > 0
      ? `"${t.name}" is used by ${t.cardCount} card(s). It will be deactivated (kept for history) rather than deleted. Continue?`
      : `Delete "${t.name}"? This cannot be undone.`;
    if (!confirm(msg)) return;
    setBusyId(t.id);
    try {
      const res = await adminDeleteSpeakingCardType(t.id);
      setToast({ variant: 'success', message: res.softDeleted ? 'Card type deactivated (in use).' : 'Card type deleted.' });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Delete failed.' });
    } finally {
      setBusyId(null);
    }
  }, [reload]);

  // Reorder by swapping sortOrder with the adjacent row, persisting both.
  const move = useCallback(async (index: number, dir: -1 | 1) => {
    const a = sorted[index];
    const b = sorted[index + dir];
    if (!a || !b) return;
    setBusyId(a.id);
    try {
      await adminUpdateSpeakingCardType(a.id, { name: a.name, sortOrder: b.sortOrder });
      await adminUpdateSpeakingCardType(b.id, { name: b.name, sortOrder: a.sortOrder });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Reorder failed.' });
    } finally {
      setBusyId(null);
    }
  }, [sorted, reload]);

  const columns = useMemo<Column<SpeakingCardTypeDetail>[]>(() => [
    {
      key: 'order',
      header: 'Order',
      render: (row) => {
        const index = sorted.findIndex((t) => t.id === row.id);
        return (
          <div className="flex items-center gap-1">
            <span className="tabular-nums text-xs text-muted w-5">{row.sortOrder}</span>
            {canWrite ? (
              <>
                <button
                  type="button"
                  aria-label="Move up"
                  disabled={index <= 0 || busyId === row.id}
                  onClick={() => void move(index, -1)}
                  className="rounded p-1 text-muted hover:text-navy disabled:opacity-30"
                >
                  <ArrowUp className="h-3.5 w-3.5" />
                </button>
                <button
                  type="button"
                  aria-label="Move down"
                  disabled={index >= sorted.length - 1 || busyId === row.id}
                  onClick={() => void move(index, 1)}
                  className="rounded p-1 text-muted hover:text-navy disabled:opacity-30"
                >
                  <ArrowDown className="h-3.5 w-3.5" />
                </button>
              </>
            ) : null}
          </div>
        );
      },
    },
    {
      key: 'name',
      header: 'Type',
      render: (row) => (
        <div className="flex flex-col">
          <span className="font-bold">{row.name}</span>
          {row.description ? <span className="text-xs text-muted line-clamp-2 max-w-xl">{row.description}</span> : null}
        </div>
      ),
    },
    { key: 'cards', header: 'Cards', render: (row) => <span className="tabular-nums text-sm">{row.cardCount}</span> },
    {
      key: 'status',
      header: 'Status',
      render: (row) => <Badge variant={row.isActive ? 'success' : 'muted'}>{row.isActive ? 'Active' : 'Inactive'}</Badge>,
    },
    {
      key: 'actions',
      header: 'Actions',
      render: (row) =>
        canWrite ? (
          <div className="flex flex-wrap items-center gap-2">
            <Button size="sm" variant="outline" onClick={() => startEdit(row)} disabled={busyId === row.id}>
              <Pencil className="h-3.5 w-3.5" /> Edit
            </Button>
            <Button size="sm" variant="outline" onClick={() => void toggleActive(row)} disabled={busyId === row.id}>
              {row.isActive ? 'Deactivate' : 'Activate'}
            </Button>
            <Button size="sm" variant="outline" onClick={() => void remove(row)} disabled={busyId === row.id}>
              <Trash2 className="h-3.5 w-3.5" /> Delete
            </Button>
          </div>
        ) : (
          <span className="text-xs text-muted">Read-only</span>
        ),
    },
  ], [busyId, canWrite, move, sorted, toggleActive, remove]);

  const actions = canWrite ? (
    <Button variant="primary" onClick={startCreate}>
      <Plus className="h-4 w-4" /> New card type
    </Button>
  ) : null;

  return (
    <AdminCatalogLayout
      title="Speaking card types"
      description="Hidden communication-function taxonomy used by human and AI marking. Students never see card types. Rename, reorder, activate or delete them here — changes are never overwritten by seeding."
      eyebrow="Admin"
      hideViewModeToggle
      itemsClassName="flex flex-col gap-6"
      actions={actions}
    >
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      {editing ? (
        <div className="rounded-2xl border border-border bg-surface p-4">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="text-sm font-bold uppercase tracking-[0.18em] text-muted">
              {editing.id ? 'Edit card type' : 'New card type'}
            </h2>
            <Button size="sm" variant="ghost" onClick={() => setEditing(null)} disabled={saving}>
              <X className="h-3.5 w-3.5" /> Cancel
            </Button>
          </div>
          {formError ? <InlineAlert variant="error">{formError}</InlineAlert> : null}
          <div className="mt-2 grid gap-3">
            <Input
              label="Name"
              value={editing.name}
              onChange={(e) => setEditing({ ...editing, name: e.target.value })}
              placeholder='e.g. "Reassurance (anxious patient)"'
              maxLength={120}
              required
            />
            <Textarea
              label="Description (marking guidance for human + AI)"
              value={editing.description}
              onChange={(e) => setEditing({ ...editing, description: e.target.value })}
              rows={3}
              maxLength={2000}
              placeholder="What this card type tests and how it should be marked."
            />
            <div className="flex items-center gap-2">
              <Button variant="primary" onClick={() => void saveForm()} disabled={saving}>
                {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Plus className="h-4 w-4" />}
                {editing.id ? 'Save changes' : 'Create type'}
              </Button>
            </div>
          </div>
        </div>
      ) : null}

      <div className="rounded-2xl border border-border bg-surface p-4">
        {loading ? (
          <p className="inline-flex items-center gap-2 text-sm text-muted">
            <Loader2 className="h-4 w-4 animate-spin" /> Loading card types…
          </p>
        ) : sorted.length === 0 ? (
          <p className="text-sm text-muted">No card types yet. {canWrite ? 'Create one to get started.' : ''}</p>
        ) : (
          <DataTable<SpeakingCardTypeDetail> columns={columns} data={sorted} keyExtractor={(row) => row.id} />
        )}
      </div>
    </AdminCatalogLayout>
  );
}
