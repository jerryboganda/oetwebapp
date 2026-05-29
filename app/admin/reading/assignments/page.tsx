'use client';

import { useCallback, useEffect, useState } from 'react';
import { Loader2, Plus, Trash2 } from 'lucide-react';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { PageHeader } from '@/components/admin/ui/page-header';
import { listContentPapers, type ContentPaperDto } from '@/lib/content-upload-api';
import {
  cancelReadingAssignment,
  createReadingAssignment,
  listReadingAssignments,
  type ReadingAssignmentCreateInput,
  type ReadingAssignmentDto,
} from '@/lib/reading-tutor-api';

const KINDS = [
  { value: 'retake', label: 'Full retake' },
  { value: 'part_a', label: 'Part A practice' },
  { value: 'part_bc', label: 'Parts B & C practice' },
  { value: 'drill', label: 'Targeted drill' },
  { value: 'full', label: 'Full reading exam' },
] as const;

const KIND_LABELS: Record<string, string> = Object.fromEntries(
  KINDS.map((k) => [k.value, k.label]),
);

function formatDate(iso: string | null): string {
  if (!iso) return '-';
  try {
    return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
  } catch {
    return iso;
  }
}

export default function AdminReadingAssignmentsPage() {
  const [papers, setPapers] = useState<ContentPaperDto[]>([]);

  const [assignedToUserId, setAssignedToUserId] = useState('');
  const [paperId, setPaperId] = useState('');
  const [kind, setKind] = useState<string>('retake');
  const [note, setNote] = useState('');
  const [dueAt, setDueAt] = useState('');

  const [creating, setCreating] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [formSuccess, setFormSuccess] = useState<string | null>(null);

  const [items, setItems] = useState<ReadingAssignmentDto[]>([]);
  const [listUserId, setListUserId] = useState('');
  const [listLoading, setListLoading] = useState(false);
  const [listError, setListError] = useState<string | null>(null);
  const [cancellingId, setCancellingId] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const data = await listContentPapers({ subtest: 'reading', pageSize: 500 });
        if (!cancelled) setPapers(data);
      } catch {
        /* papers picker degrades gracefully to an empty list */
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const loadAssignments = useCallback(async (userId: string) => {
    const trimmed = userId.trim();
    if (!trimmed) {
      setItems([]);
      return;
    }
    setListLoading(true);
    setListError(null);
    try {
      const data = await listReadingAssignments(trimmed, 'admin');
      setItems(data);
      setListUserId(trimmed);
    } catch {
      setListError('Failed to load assignments.');
    } finally {
      setListLoading(false);
    }
  }, []);

  async function handleCreate(event: React.FormEvent) {
    event.preventDefault();
    setFormError(null);
    setFormSuccess(null);

    if (!assignedToUserId.trim()) {
      setFormError('A learner id is required.');
      return;
    }
    if (!paperId) {
      setFormError('Select a reading paper.');
      return;
    }

    const body: ReadingAssignmentCreateInput = {
      assignedToUserId: assignedToUserId.trim(),
      paperId,
      kind,
      note: note.trim() || null,
      dueAt: dueAt ? new Date(dueAt).toISOString() : null,
    };

    setCreating(true);
    try {
      const created = await createReadingAssignment(body, 'admin');
      setFormSuccess('Assignment created.');
      setNote('');
      setDueAt('');
      // Refresh the list if it is showing the same learner.
      if (listUserId === body.assignedToUserId) {
        setItems((prev) => [created, ...prev]);
      }
    } catch {
      setFormError('Failed to create the assignment.');
    } finally {
      setCreating(false);
    }
  }

  async function handleCancel(id: string) {
    setCancellingId(id);
    setListError(null);
    try {
      await cancelReadingAssignment(id, 'admin');
      setItems((prev) =>
        prev.map((item) => (item.id === id ? { ...item, status: 'cancelled' } : item)),
      );
    } catch {
      setListError('Failed to cancel the assignment.');
    } finally {
      setCancellingId(null);
    }
  }

  return (
    <AdminPageShell mainAriaLabel="Reading assignments">
      <PageHeader
        eyebrow="Reading"
        title="Assignments"
        description="Assign reading work to a learner and track its status."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Reading', href: '/admin/content/reading' },
          { label: 'Assignments' },
        ]}
      />

      <form
        onSubmit={handleCreate}
        aria-label="Create assignment"
        className="space-y-4 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-4 sm:p-5"
      >
        <div className="flex items-center gap-2">
          <Plus className="h-4 w-4 text-admin-fg-muted" aria-hidden="true" />
          <h2 className="text-sm font-semibold text-admin-fg-strong">New assignment</h2>
        </div>

        <div className="grid gap-4 md:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <label htmlFor="assign-user" className="text-sm font-medium text-admin-fg-default">
              Learner id
            </label>
            <input
              id="assign-user"
              type="text"
              value={assignedToUserId}
              disabled={creating}
              onChange={(event) => setAssignedToUserId(event.target.value)}
              className="rounded-admin-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <label htmlFor="assign-paper" className="text-sm font-medium text-admin-fg-default">
              Reading paper
            </label>
            <select
              id="assign-paper"
              value={paperId}
              disabled={creating}
              onChange={(event) => setPaperId(event.target.value)}
              className="rounded-admin-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
            >
              <option value="">Select a paper…</option>
              {papers.map((paper) => (
                <option key={paper.id} value={paper.id}>
                  {paper.title}
                </option>
              ))}
            </select>
          </div>

          <div className="flex flex-col gap-1.5">
            <label htmlFor="assign-kind" className="text-sm font-medium text-admin-fg-default">
              Kind
            </label>
            <select
              id="assign-kind"
              value={kind}
              disabled={creating}
              onChange={(event) => setKind(event.target.value)}
              className="rounded-admin-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
            >
              {KINDS.map((k) => (
                <option key={k.value} value={k.value}>
                  {k.label}
                </option>
              ))}
            </select>
          </div>

          <div className="flex flex-col gap-1.5">
            <label htmlFor="assign-due" className="text-sm font-medium text-admin-fg-default">
              Due date <span className="text-admin-fg-muted">(optional)</span>
            </label>
            <input
              id="assign-due"
              type="date"
              value={dueAt}
              disabled={creating}
              onChange={(event) => setDueAt(event.target.value)}
              className="rounded-admin-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
            />
          </div>
        </div>

        <div className="flex flex-col gap-1.5">
          <label htmlFor="assign-note" className="text-sm font-medium text-admin-fg-default">
            Note <span className="text-admin-fg-muted">(optional)</span>
          </label>
          <textarea
            id="assign-note"
            rows={2}
            value={note}
            disabled={creating}
            onChange={(event) => setNote(event.target.value)}
            className="rounded-admin-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
          />
        </div>

        {formError ? (
          <p role="alert" className="text-sm text-[var(--admin-danger)]">
            {formError}
          </p>
        ) : null}
        {formSuccess ? (
          <p role="status" className="text-sm text-[var(--admin-success)]">
            {formSuccess}
          </p>
        ) : null}

        <button
          type="submit"
          disabled={creating}
          className="inline-flex items-center gap-2 rounded-admin-lg bg-[var(--admin-primary)] px-4 py-2 text-sm font-medium text-[var(--admin-primary-fg)] transition-colors hover:bg-[var(--admin-primary-hover)] disabled:opacity-50"
        >
          {creating ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : null}
          Create assignment
        </button>
      </form>

      <section
        aria-label="Existing assignments"
        className="space-y-4 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-4 sm:p-5"
      >
        <h2 className="text-sm font-semibold text-admin-fg-strong">Learner assignments</h2>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            void loadAssignments(listUserId);
          }}
          className="flex flex-wrap items-end gap-3"
        >
          <div className="flex flex-1 flex-col gap-1.5">
            <label htmlFor="list-user" className="text-sm font-medium text-admin-fg-default">
              Learner id
            </label>
            <input
              id="list-user"
              type="text"
              value={listUserId}
              onChange={(event) => setListUserId(event.target.value)}
              className="rounded-admin-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-default focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
            />
          </div>
          <button
            type="submit"
            disabled={listLoading}
            className="inline-flex items-center gap-2 rounded-admin-lg border border-admin-border px-4 py-2 text-sm font-medium text-admin-fg-default transition-colors hover:bg-admin-bg-subtle disabled:opacity-50"
          >
            {listLoading ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : null}
            Load
          </button>
        </form>

        {listError ? (
          <p role="alert" className="text-sm text-[var(--admin-danger)]">
            {listError}
          </p>
        ) : null}

        {items.length === 0 ? (
          <p className="rounded-admin-lg border border-dashed border-admin-border px-4 py-6 text-center text-sm text-admin-fg-muted">
            {listLoading ? 'Loading…' : 'No assignments to show. Load a learner to view their assignments.'}
          </p>
        ) : (
          <div className="overflow-x-auto rounded-admin-lg border border-admin-border">
            <table className="w-full border-collapse text-sm">
              <thead>
                <tr className="border-b border-admin-border bg-admin-bg-subtle text-left text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
                  <th scope="col" className="px-4 py-2.5">Kind</th>
                  <th scope="col" className="px-4 py-2.5">Status</th>
                  <th scope="col" className="px-4 py-2.5">Due</th>
                  <th scope="col" className="px-4 py-2.5">Created</th>
                  <th scope="col" className="px-4 py-2.5 text-right">Action</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => {
                  const cancelled = item.status === 'cancelled';
                  const completed = item.status === 'completed';
                  return (
                    <tr key={item.id} className="border-b border-admin-border last:border-b-0">
                      <th scope="row" className="px-4 py-2.5 text-left font-medium text-admin-fg-default">
                        {KIND_LABELS[item.kind] ?? item.kind}
                      </th>
                      <td className="px-4 py-2.5 capitalize text-admin-fg-default">{item.status}</td>
                      <td className="px-4 py-2.5 text-admin-fg-muted">{formatDate(item.dueAt)}</td>
                      <td className="px-4 py-2.5 text-admin-fg-muted">{formatDate(item.createdAt)}</td>
                      <td className="px-4 py-2.5 text-right">
                        {cancelled || completed ? (
                          <span className="text-xs text-admin-fg-muted">-</span>
                        ) : (
                          <button
                            type="button"
                            onClick={() => handleCancel(item.id)}
                            disabled={cancellingId === item.id}
                            className="inline-flex items-center gap-1.5 rounded-admin-lg border border-admin-border px-3 py-1.5 text-xs font-medium text-[var(--admin-danger)] transition-colors hover:bg-[var(--admin-danger-tint)] disabled:opacity-50"
                          >
                            {cancellingId === item.id ? (
                              <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                            ) : (
                              <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                            )}
                            Cancel
                          </button>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </AdminPageShell>
  );
}
