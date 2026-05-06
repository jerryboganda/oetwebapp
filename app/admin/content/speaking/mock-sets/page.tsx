'use client';

// Wave 3 of docs/SPEAKING-MODULE-PLAN.md - admin authoring UI for
// `SpeakingMockSet`. List + create + publish/archive in a single
// surface; reuses the existing /v1/admin/content endpoints to pick the
// two role-play `ContentItem` ids.

import { useEffect, useMemo, useState } from 'react';
import { Plus, Sparkles, Archive, CheckCircle2, Pencil } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Toast } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import {
  fetchAdminSpeakingMockSets,
  fetchAdminSpeakingContentOptions,
  createAdminSpeakingMockSet,
  updateAdminSpeakingMockSet,
  publishAdminSpeakingMockSet,
  archiveAdminSpeakingMockSet,
  type AdminSpeakingMockSetRow,
} from '@/lib/api';

type SpeakingContentOption = { id: string; title: string; status: string };

export default function AdminSpeakingMockSetsPage() {
  const [rows, setRows] = useState<AdminSpeakingMockSetRow[]>([]);
  const [status, setStatus] = useState<'loading' | 'success' | 'error' | 'empty'>('loading');
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [reloadNonce, setReloadNonce] = useState(0);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const [createOpen, setCreateOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [editingRow, setEditingRow] = useState<AdminSpeakingMockSetRow | null>(null);
  const [savingEdit, setSavingEdit] = useState(false);
  const [contentOptions, setContentOptions] = useState<SpeakingContentOption[]>([]);
  const [form, setForm] = useState({
    title: '',
    description: '',
    professionId: 'nursing',
    difficulty: 'core',
    rolePlay1ContentId: '',
    rolePlay2ContentId: '',
    criteriaFocus: '',
    tags: '',
    sortOrder: 0,
  });

  // Load mock sets when filter changes.
  useEffect(() => {
    let cancelled = false;
    setStatus('loading');
    fetchAdminSpeakingMockSets({ status: statusFilter || undefined })
      .then((data) => {
        if (cancelled) return;
        setRows(data);
        setStatus(data.length === 0 ? 'empty' : 'success');
      })
      .catch((e: unknown) => {
        if (cancelled) return;
        setStatus('error');
        setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Failed to load mock sets.' });
      });
    return () => { cancelled = true; };
  }, [statusFilter, reloadNonce]);

  // Lazy-load speaking content options the first time an authoring modal opens.
  useEffect(() => {
    if ((!createOpen && !editingRow) || contentOptions.length > 0) return;
    void fetchAdminSpeakingContentOptions()
      .then((opts) => setContentOptions(opts))
      .catch(() => {
        setToast({ variant: 'error', message: 'Could not load speaking content options.' });
      });
  }, [createOpen, contentOptions.length, editingRow]);

  const dupSelected = form.rolePlay1ContentId && form.rolePlay1ContentId === form.rolePlay2ContentId;

  const submitCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (creating) return;
    if (!form.title.trim()) {
      setToast({ variant: 'error', message: 'Title is required.' });
      return;
    }
    if (!form.rolePlay1ContentId || !form.rolePlay2ContentId) {
      setToast({ variant: 'error', message: 'Pick two role-play content items.' });
      return;
    }
    if (dupSelected) {
      setToast({ variant: 'error', message: 'Role-play 1 and 2 must be different content items.' });
      return;
    }
    setCreating(true);
    try {
      await createAdminSpeakingMockSet({
        title: form.title.trim(),
        description: form.description.trim(),
        professionId: form.professionId,
        difficulty: form.difficulty,
        rolePlay1ContentId: form.rolePlay1ContentId,
        rolePlay2ContentId: form.rolePlay2ContentId,
        criteriaFocus: form.criteriaFocus,
        tags: form.tags,
        sortOrder: form.sortOrder,
      });
      setToast({ variant: 'success', message: 'Mock set created (still draft).' });
      setCreateOpen(false);
      setForm({
        title: '', description: '', professionId: 'nursing', difficulty: 'core',
        rolePlay1ContentId: '', rolePlay2ContentId: '', criteriaFocus: '', tags: '', sortOrder: 0,
      });
      setReloadNonce((n) => n + 1);
    } catch (err: unknown) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to create mock set.' });
    } finally {
      setCreating(false);
    }
  };

  const handlePublish = async (mockSetId: string) => {
    try {
      await publishAdminSpeakingMockSet(mockSetId);
      setToast({ variant: 'success', message: 'Mock set published. Learners will see it immediately.' });
      setReloadNonce((n) => n + 1);
    } catch (err: unknown) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to publish mock set.' });
    }
  };

  const handleArchive = async (mockSetId: string) => {
    try {
      await archiveAdminSpeakingMockSet(mockSetId);
      setToast({ variant: 'success', message: 'Mock set archived.' });
      setReloadNonce((n) => n + 1);
    } catch (err: unknown) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to archive mock set.' });
    }
  };

  const openEdit = (row: AdminSpeakingMockSetRow) => {
    setEditingRow(row);
    setForm({
      title: row.title,
      description: row.description,
      professionId: row.professionId,
      difficulty: row.difficulty,
      rolePlay1ContentId: row.rolePlay1.contentId,
      rolePlay2ContentId: row.rolePlay2.contentId,
      criteriaFocus: row.criteriaFocus.join(', '),
      tags: row.tags.join(', '),
      sortOrder: row.sortOrder,
    });
  };

  const closeEdit = () => {
    setEditingRow(null);
    setSavingEdit(false);
  };

  const submitEdit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingRow || savingEdit) return;
    if (!form.title.trim()) {
      setToast({ variant: 'error', message: 'Title is required.' });
      return;
    }
    if (!form.rolePlay1ContentId || !form.rolePlay2ContentId) {
      setToast({ variant: 'error', message: 'Pick two role-play content items.' });
      return;
    }
    if (dupSelected) {
      setToast({ variant: 'error', message: 'Role-play 1 and 2 must be different content items.' });
      return;
    }
    setSavingEdit(true);
    try {
      await updateAdminSpeakingMockSet(editingRow.mockSetId, {
        title: form.title.trim(),
        description: form.description.trim(),
        professionId: form.professionId,
        difficulty: form.difficulty,
        rolePlay1ContentId: form.rolePlay1ContentId,
        rolePlay2ContentId: form.rolePlay2ContentId,
        criteriaFocus: form.criteriaFocus,
        tags: form.tags,
        sortOrder: form.sortOrder,
      });
      setToast({ variant: 'success', message: 'Mock set updated.' });
      closeEdit();
      setReloadNonce((n) => n + 1);
    } catch (err: unknown) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to update mock set.' });
    } finally {
      setSavingEdit(false);
    }
  };

  const columns = useMemo<Column<AdminSpeakingMockSetRow>[]>(() => [
    {
      key: 'title',
      header: 'Mock set',
      render: (row) => (
        <div className="flex flex-col">
          <span className="font-bold">{row.title}</span>
          {row.description && (
            <span className="text-xs text-muted line-clamp-1">{row.description}</span>
          )}
          <span className="mt-1 text-[10px] uppercase tracking-widest text-muted">id {row.mockSetId}</span>
        </div>
      ),
    },
    {
      key: 'rolePlays',
      header: 'Role-plays',
      render: (row) => (
        <div className="flex flex-col gap-1 text-xs">
          <span className={row.rolePlay1.isSpeaking ? '' : 'text-danger'}>
            1. {row.rolePlay1.title}{!row.rolePlay1.isSpeaking && ' (not speaking!)'}
          </span>
          <span className={row.rolePlay2.isSpeaking ? '' : 'text-danger'}>
            2. {row.rolePlay2.title}{!row.rolePlay2.isSpeaking && ' (not speaking!)'}
          </span>
        </div>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => {
        const variant = row.status === 'published' ? 'success' : row.status === 'archived' ? 'muted' : 'info';
        return <Badge variant={variant}>{row.status}</Badge>;
      },
    },
    {
      key: 'difficulty',
      header: 'Difficulty',
      render: (row) => <Badge variant="muted">{row.difficulty}</Badge>,
    },
    {
      key: 'actions',
      header: 'Actions',
      render: (row) => (
        <div className="flex flex-wrap items-center gap-2">
          {row.status !== 'archived' && (
            <Button size="sm" variant="outline" onClick={() => openEdit(row)}>
              <Pencil className="h-3.5 w-3.5" /> Edit
            </Button>
          )}
          {row.status === 'draft' && (
            <Button size="sm" variant="primary" onClick={() => handlePublish(row.mockSetId)}>
              <CheckCircle2 className="h-3.5 w-3.5" /> Publish
            </Button>
          )}
          {row.status !== 'archived' && (
            <Button size="sm" variant="outline" onClick={() => handleArchive(row.mockSetId)}>
              <Archive className="h-3.5 w-3.5" /> Archive
            </Button>
          )}
        </div>
      ),
    },
  ], []);

  return (
    <AdminRouteWorkspace role="main" aria-label="Speaking mock sets">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title="Speaking mock sets"
        description="Each mock set bundles two speaking role-plays into one OET-shape sub-test for learners."
        actions={
          <Button onClick={() => setCreateOpen(true)} className="gap-2">
            <Plus className="h-4 w-4" /> New mock set
          </Button>
        }
      />

      <AdminRoutePanel title="Filter" description="Narrow the list by lifecycle status.">
        <div className="flex flex-wrap items-end gap-3">
          <div className="w-48">
            <Select
              label="Status"
              value={statusFilter}
              onChange={(e) => setStatusFilter(e.target.value)}
              options={[
                { value: '', label: 'All' },
                { value: 'draft', label: 'Draft' },
                { value: 'published', label: 'Published' },
                { value: 'archived', label: 'Archived' },
              ]}
            />
          </div>
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel title="Mock sets" description="Curatorial pairings of speaking role-plays.">
        <AsyncStateWrapper
          status={status}
          onRetry={() => setReloadNonce((n) => n + 1)}
          emptyContent={
            <EmptyState
              icon={<Sparkles className="h-10 w-10 text-muted" />}
              title="No mock sets yet"
              description="Create the first one to surface a paired-role-play exam to learners."
              action={{ label: 'New mock set', onClick: () => setCreateOpen(true) }}
            />
          }
        >
          <DataTable<AdminSpeakingMockSetRow>
            columns={columns}
            data={rows}
            keyExtractor={(row) => row.mockSetId}
          />
        </AsyncStateWrapper>
      </AdminRoutePanel>

      <Modal open={createOpen} onClose={() => setCreateOpen(false)} title="Create speaking mock set" size="lg">
        <form onSubmit={submitCreate} className="space-y-4">
          <Input
            label="Title"
            value={form.title}
            onChange={(e) => setForm({ ...form, title: e.target.value })}
            placeholder="e.g. Nursing Mock Set 3 - Discharge planning"
            required
          />
          <div className="grid gap-3 sm:grid-cols-2">
            <Select
              label="Profession"
              value={form.professionId}
              onChange={(e) => setForm({ ...form, professionId: e.target.value })}
              options={[
                { value: 'nursing', label: 'Nursing' },
                { value: 'medicine', label: 'Medicine' },
                { value: 'pharmacy', label: 'Pharmacy' },
                { value: 'physiotherapy', label: 'Physiotherapy' },
              ]}
            />
            <Select
              label="Difficulty"
              value={form.difficulty}
              onChange={(e) => setForm({ ...form, difficulty: e.target.value })}
              options={[
                { value: 'core', label: 'Core' },
                { value: 'extension', label: 'Extension' },
                { value: 'exam', label: 'Exam' },
              ]}
            />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <Select
              label="Role-play 1 (speaking content)"
              value={form.rolePlay1ContentId}
              onChange={(e) => setForm({ ...form, rolePlay1ContentId: e.target.value })}
              options={[
                { value: '', label: 'Select…' },
                ...contentOptions.map((o) => ({ value: o.id, label: `${o.title} [${o.status}]` })),
              ]}
              required
            />
            <Select
              label="Role-play 2 (speaking content)"
              value={form.rolePlay2ContentId}
              onChange={(e) => setForm({ ...form, rolePlay2ContentId: e.target.value })}
              options={[
                { value: '', label: 'Select…' },
                ...contentOptions.map((o) => ({ value: o.id, label: `${o.title} [${o.status}]` })),
              ]}
              required
            />
          </div>
          {dupSelected && (
            <p className="text-xs text-danger">Role-play 1 and 2 must be different content items.</p>
          )}
          <Input
            label="Description (optional)"
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
          />
          <div className="grid gap-3 sm:grid-cols-2">
            <Input
              label="Criteria focus (CSV, optional)"
              value={form.criteriaFocus}
              onChange={(e) => setForm({ ...form, criteriaFocus: e.target.value })}
              placeholder="informationGiving, relationshipBuilding"
            />
            <Input
              label="Tags (CSV, optional)"
              value={form.tags}
              onChange={(e) => setForm({ ...form, tags: e.target.value })}
              placeholder="nursing, week-1"
            />
          </div>
          <Input
            label="Sort order"
            type="number"
            value={String(form.sortOrder)}
            onChange={(e) => setForm({ ...form, sortOrder: Number(e.target.value) || 0 })}
          />
          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" variant="outline" onClick={() => setCreateOpen(false)} disabled={creating}>
              Cancel
            </Button>
            <Button type="submit" variant="primary" disabled={creating}>
              {creating ? 'Creating…' : 'Create draft'}
            </Button>
          </div>
        </form>
      </Modal>

      <Modal open={editingRow !== null} onClose={closeEdit} title="Edit speaking mock set" size="lg">
        <form onSubmit={submitEdit} className="space-y-4">
          <Input
            label="Title"
            value={form.title}
            onChange={(e) => setForm({ ...form, title: e.target.value })}
            required
          />
          <div className="grid gap-3 sm:grid-cols-2">
            <Select
              label="Profession"
              value={form.professionId}
              onChange={(e) => setForm({ ...form, professionId: e.target.value })}
              options={[
                { value: 'nursing', label: 'Nursing' },
                { value: 'medicine', label: 'Medicine' },
                { value: 'pharmacy', label: 'Pharmacy' },
                { value: 'physiotherapy', label: 'Physiotherapy' },
              ]}
            />
            <Select
              label="Difficulty"
              value={form.difficulty}
              onChange={(e) => setForm({ ...form, difficulty: e.target.value })}
              options={[
                { value: 'core', label: 'Core' },
                { value: 'extension', label: 'Extension' },
                { value: 'exam', label: 'Exam' },
              ]}
            />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <Select
              label="Role-play 1 (speaking content)"
              value={form.rolePlay1ContentId}
              onChange={(e) => setForm({ ...form, rolePlay1ContentId: e.target.value })}
              options={[
                { value: '', label: 'Select...' },
                ...contentOptions.map((o) => ({ value: o.id, label: `${o.title} [${o.status}]` })),
              ]}
              required
            />
            <Select
              label="Role-play 2 (speaking content)"
              value={form.rolePlay2ContentId}
              onChange={(e) => setForm({ ...form, rolePlay2ContentId: e.target.value })}
              options={[
                { value: '', label: 'Select...' },
                ...contentOptions.map((o) => ({ value: o.id, label: `${o.title} [${o.status}]` })),
              ]}
              required
            />
          </div>
          {dupSelected && (
            <p className="text-xs text-danger">Role-play 1 and 2 must be different content items.</p>
          )}
          <Input
            label="Description (optional)"
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
          />
          <div className="grid gap-3 sm:grid-cols-2">
            <Input
              label="Criteria focus (CSV, optional)"
              value={form.criteriaFocus}
              onChange={(e) => setForm({ ...form, criteriaFocus: e.target.value })}
            />
            <Input
              label="Tags (CSV, optional)"
              value={form.tags}
              onChange={(e) => setForm({ ...form, tags: e.target.value })}
            />
          </div>
          <Input
            label="Sort order"
            type="number"
            value={String(form.sortOrder)}
            onChange={(e) => setForm({ ...form, sortOrder: Number(e.target.value) || 0 })}
          />
          <div className="rounded-2xl border border-border bg-background-light p-3 text-xs text-muted">
            Published mock sets can be edited, but learner sessions already started from an older version keep their saved attempt history.
          </div>
          <div className="flex justify-end gap-2 pt-2">
            <Button type="button" variant="outline" onClick={closeEdit} disabled={savingEdit}>
              Cancel
            </Button>
            <Button type="submit" variant="primary" disabled={savingEdit}>
              {savingEdit ? 'Saving...' : 'Save changes'}
            </Button>
          </div>
        </form>
      </Modal>
    </AdminRouteWorkspace>
  );
}
