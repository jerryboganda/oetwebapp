'use client';

/**
 * Admin · Writing · Task list (canonical).
 *
 * Table of OET Writing tasks via `listWritingTasks`, with profession /
 * letter-type / status filters, "New task", and per-row actions (edit, clone,
 * publish / archive, export JSON). Authoring actions are gated on ContentWrite;
 * publish on ContentPublish.
 *
 * Spec §3 (task catalogue), §18 (export). The list returns full
 * `WritingTaskDto` rows (the contract exposes no slim list-item type), so we
 * derive summary chips (key-content count, model-answer presence) on the client.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';

import {
  AdminSettingsLayout,
  SettingsSection,
} from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { hasPermission, AdminPermission } from '@/lib/admin-permissions';
import {
  listWritingTasks,
  cloneWritingTask,
  publishWritingTask,
  archiveWritingTask,
  exportWritingTask,
  type WritingTaskListQuery,
} from '@/lib/writing/exam-api';
import {
  WRITING_PROFESSIONS,
  WRITING_PROFESSION_LABELS,
  type WritingProfession,
  type WritingLetterType,
  type WritingTaskDto,
} from '@/lib/writing/types';
import {
  WRITING_LETTER_TYPES,
  WRITING_LETTER_TYPE_LABELS,
  WRITING_SIMULATION_MODE_LABELS,
} from '@/components/domain/writing/admin';

const STATUS_TABS = ['all', 'draft', 'published', 'archived'] as const;
type StatusTab = (typeof STATUS_TABS)[number];

type ToastVariant = 'success' | 'error' | 'info' | 'warning';
type ToastState = { message: string; variant: ToastVariant } | null;

const PROFESSION_FILTER_OPTIONS = [
  { value: 'all', label: 'All professions' },
  ...WRITING_PROFESSIONS.map((p) => ({ value: p, label: WRITING_PROFESSION_LABELS[p] })),
];
const LETTER_TYPE_FILTER_OPTIONS = [
  { value: 'all', label: 'All letter types' },
  ...WRITING_LETTER_TYPES.map((lt) => ({ value: lt, label: WRITING_LETTER_TYPE_LABELS[lt] })),
];

export default function WritingTasksPage() {
  const router = useRouter();
  const { user } = useCurrentUser();
  useAdminAuth(); // enforces admin access + redirect
  const canWrite = useMemo(
    () => hasPermission(user?.adminPermissions, AdminPermission.ContentWrite),
    [user?.adminPermissions],
  );
  const canPublish = useMemo(
    () => hasPermission(user?.adminPermissions, AdminPermission.ContentPublish),
    [user?.adminPermissions],
  );

  const [tasks, setTasks] = useState<WritingTaskDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [rowBusyId, setRowBusyId] = useState<string | null>(null);

  const [search, setSearch] = useState('');
  const [professionFilter, setProfessionFilter] = useState<WritingProfession | 'all'>('all');
  const [letterTypeFilter, setLetterTypeFilter] = useState<WritingLetterType | 'all'>('all');
  const [statusTab, setStatusTab] = useState<StatusTab>('all');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const query: WritingTaskListQuery = { pageSize: 200 };
      if (professionFilter !== 'all') query.profession = professionFilter;
      if (letterTypeFilter !== 'all') query.letterType = letterTypeFilter;
      if (statusTab !== 'all') query.status = statusTab;
      if (search.trim()) query.search = search.trim();
      const res = await listWritingTasks(query);
      setTasks(res.items);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load tasks');
    } finally {
      setLoading(false);
    }
  }, [professionFilter, letterTypeFilter, statusTab, search]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleClone = useCallback(
    async (id: string) => {
      setRowBusyId(id);
      try {
        const dto = await cloneWritingTask(id);
        router.push(`/admin/writing/tasks/${dto.id}/edit`);
      } catch (err) {
        setToast({
          message: err instanceof Error ? err.message : 'Clone failed',
          variant: 'error',
        });
        setRowBusyId(null);
      }
    },
    [router],
  );

  const handlePublish = useCallback(
    async (id: string) => {
      setRowBusyId(id);
      try {
        await publishWritingTask(id);
        setToast({ message: 'Task published', variant: 'success' });
        await load();
      } catch (err) {
        setToast({
          message:
            err instanceof Error
              ? `Publish failed: ${err.message}`
              : 'Publish failed — open the task to see issues',
          variant: 'error',
        });
      } finally {
        setRowBusyId(null);
      }
    },
    [load],
  );

  const handleArchive = useCallback(
    async (id: string) => {
      setRowBusyId(id);
      try {
        await archiveWritingTask(id);
        setToast({ message: 'Task archived', variant: 'success' });
        await load();
      } catch (err) {
        setToast({
          message: err instanceof Error ? err.message : 'Archive failed',
          variant: 'error',
        });
      } finally {
        setRowBusyId(null);
      }
    },
    [load],
  );

  const handleExport = useCallback(async (task: WritingTaskDto) => {
    setRowBusyId(task.id);
    try {
      const json = await exportWritingTask(task.id);
      const blob = new Blob([JSON.stringify(json, null, 2)], {
        type: 'application/json',
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      const slug =
        (task.internalCode || task.title || 'writing-task')
          .toLowerCase()
          .replace(/[^a-z0-9]+/g, '-')
          .replace(/^-+|-+$/g, '') || 'writing-task';
      a.download = `${slug}.json`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (err) {
      setToast({
        message: err instanceof Error ? err.message : 'Export failed',
        variant: 'error',
      });
    } finally {
      setRowBusyId(null);
    }
  }, []);

  const headerActions = canWrite ? (
    <Button size="sm" onClick={() => router.push('/admin/writing/tasks/new')}>
      + New task
    </Button>
  ) : undefined;

  return (
    <AdminSettingsLayout
      title="Writing tasks"
      description="Author and manage OET Writing tasks."
      eyebrow="Writing"
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Writing', href: '/admin/writing' },
        { label: 'Tasks' },
      ]}
      actions={headerActions}
    >
      <SettingsSection
        title="Filters"
        description="Narrow by profession, letter type, or status."
      >
        <div className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-3">
            <Input
              label="Search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Title or code…"
            />
            <Select
              label="Profession"
              value={professionFilter}
              onChange={(e) =>
                setProfessionFilter(e.target.value as WritingProfession | 'all')
              }
              options={PROFESSION_FILTER_OPTIONS}
            />
            <Select
              label="Letter type"
              value={letterTypeFilter}
              onChange={(e) =>
                setLetterTypeFilter(e.target.value as WritingLetterType | 'all')
              }
              options={LETTER_TYPE_FILTER_OPTIONS}
            />
          </div>

          <div
            className="inline-flex rounded-lg border border-admin-border bg-admin-bg-subtle p-0.5"
            role="tablist"
            aria-label="Filter by status"
          >
            {STATUS_TABS.map((tab) => {
              const active = statusTab === tab;
              return (
                <button
                  key={tab}
                  type="button"
                  role="tab"
                  aria-selected={active}
                  onClick={() => setStatusTab(tab)}
                  className={`rounded-md px-3 py-1.5 text-sm font-medium capitalize outline-none transition-colors duration-150 focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] motion-reduce:transition-none ${
                    active
                      ? 'bg-admin-bg-surface text-[var(--admin-primary)] shadow-admin-sm'
                      : 'text-admin-fg-muted hover:text-admin-fg-default'
                  }`}
                >
                  {tab}
                </button>
              );
            })}
          </div>
        </div>
      </SettingsSection>

      <SettingsSection
        title="Tasks"
        description={
          loading ? 'Loading…' : `${tasks.length} task${tasks.length === 1 ? '' : 's'}`
        }
      >
        {error ? (
          <div className="space-y-3">
            <p className="text-sm text-admin-danger">{error}</p>
            <Button variant="secondary" size="sm" onClick={() => void load()}>
              Retry
            </Button>
          </div>
        ) : loading ? (
          <p className="text-sm text-admin-fg-muted">Loading tasks…</p>
        ) : tasks.length === 0 ? (
          <div className="rounded-lg border border-dashed border-admin-border bg-admin-bg-subtle px-4 py-10 text-center">
            <p className="text-sm text-admin-fg-muted">No tasks match these filters.</p>
            {canWrite && (
              <Button
                size="sm"
                className="mt-3"
                onClick={() => router.push('/admin/writing/tasks/new')}
              >
                + Create the first task
              </Button>
            )}
          </div>
        ) : (
          <div className="-mx-4 overflow-x-auto sm:-mx-5">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-admin-border text-left text-xs font-medium uppercase tracking-wide text-admin-fg-muted">
                  <th className="px-4 py-2.5 sm:px-5">Task</th>
                  <th className="px-3 py-2.5">Profession</th>
                  <th className="px-3 py-2.5">Type</th>
                  <th className="px-3 py-2.5">Diff.</th>
                  <th className="px-3 py-2.5">Mode</th>
                  <th className="px-3 py-2.5">Status</th>
                  <th className="px-3 py-2.5">Updated</th>
                  <th className="px-4 py-2.5 text-right sm:px-5">Actions</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-admin-border">
                {tasks.map((task) => (
                  <tr key={task.id} className="align-top">
                    <td className="px-4 py-3 sm:px-5">
                      <button
                        type="button"
                        onClick={() =>
                          router.push(`/admin/writing/tasks/${task.id}/edit`)
                        }
                        className="text-left font-medium text-admin-fg-strong outline-none transition-colors duration-150 hover:text-[var(--admin-primary)] focus-visible:text-[var(--admin-primary)] focus-visible:underline motion-reduce:transition-none"
                      >
                        {task.title || 'Untitled task'}
                      </button>
                      <div className="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-admin-fg-muted">
                        {task.internalCode && (
                          <span className="font-mono">{task.internalCode}</span>
                        )}
                        <span>{task.keyContentChecklist?.length ?? 0} key points</span>
                        <span
                          className={
                            task.modelAnswerText
                              ? 'text-[var(--admin-success)]'
                              : 'text-[var(--admin-warning)]'
                          }
                        >
                          {task.modelAnswerText ? 'Model answer ✓' : 'No model answer'}
                        </span>
                      </div>
                    </td>
                    <td className="px-3 py-3 text-admin-fg-default">
                      {WRITING_PROFESSION_LABELS[task.profession]}
                    </td>
                    <td className="px-3 py-3 text-admin-fg-default">
                      {WRITING_LETTER_TYPE_LABELS[task.letterType]}
                    </td>
                    <td className="px-3 py-3 tabular-nums text-admin-fg-default">
                      {task.difficulty}
                    </td>
                    <td className="px-3 py-3 text-admin-fg-default">
                      {WRITING_SIMULATION_MODE_LABELS[task.simulationModes]}
                    </td>
                    <td className="px-3 py-3">
                      <StatusBadge status={task.status} />
                    </td>
                    <td className="px-3 py-3 text-xs text-admin-fg-muted">
                      {formatDate(task.updatedAt)}
                    </td>
                    <td className="px-4 py-3 sm:px-5">
                      <div className="flex flex-wrap items-center justify-end gap-1.5">
                        <Button
                          variant="secondary"
                          size="sm"
                          onClick={() =>
                            router.push(`/admin/writing/tasks/${task.id}/edit`)
                          }
                        >
                          Edit
                        </Button>
                        {canWrite && (
                          <Button
                            variant="secondary"
                            size="sm"
                            onClick={() => handleClone(task.id)}
                            loading={rowBusyId === task.id}
                            disabled={rowBusyId === task.id}
                          >
                            Clone
                          </Button>
                        )}
                        {canPublish && task.status !== 'published' && (
                          <Button
                            size="sm"
                            onClick={() => handlePublish(task.id)}
                            loading={rowBusyId === task.id}
                            disabled={rowBusyId === task.id}
                          >
                            Publish
                          </Button>
                        )}
                        {canWrite && task.status !== 'archived' && (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => handleArchive(task.id)}
                            disabled={rowBusyId === task.id}
                          >
                            Archive
                          </Button>
                        )}
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => handleExport(task)}
                          disabled={rowBusyId === task.id}
                        >
                          Export
                        </Button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </SettingsSection>

      {toast && (
        <Toast
          message={toast.message}
          variant={toast.variant}
          onClose={() => setToast(null)}
        />
      )}
    </AdminSettingsLayout>
  );
}

function StatusBadge({ status }: { status: WritingTaskDto['status'] }) {
  const variant =
    status === 'published' ? 'success' : status === 'archived' ? 'warning' : 'default';
  return (
    <Badge variant={variant} size="sm" className="capitalize">
      {status}
    </Badge>
  );
}

function formatDate(iso: string): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}
