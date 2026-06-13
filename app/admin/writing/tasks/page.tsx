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
import { CheckCircle2, Archive as ArchiveIcon, Trash2, AlertTriangle } from 'lucide-react';

import {
  AdminSettingsLayout,
  SettingsSection,
} from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { type Column } from '@/components/ui/data-table';
import {
  AdminManagedTable,
  type ManagedBulkAction,
  type BulkResult,
} from '@/components/admin/managed-table/admin-managed-table';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { hasPermission, AdminPermission } from '@/lib/admin-permissions';
import {
  listWritingTasks,
  cloneWritingTask,
  publishWritingTask,
  archiveWritingTask,
  exportWritingTask,
  bulkWritingTasks,
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
  WritingTaskImportDialog,
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
  const [importOpen, setImportOpen] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

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
      setPage(1);
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

  const bulkActions = useMemo<ManagedBulkAction<WritingTaskDto>[]>(() => {
    if (!canWrite) return [];
    return [
      {
        key: 'publish',
        label: 'Publish selected',
        icon: <CheckCircle2 className="h-4 w-4" />,
        variant: 'primary',
        isEligible: (t) => t.status !== 'published',
        run: (ids) => bulkWritingTasks('publish', ids),
      },
      {
        key: 'archive',
        label: 'Archive selected',
        icon: <ArchiveIcon className="h-4 w-4" />,
        variant: 'danger',
        isEligible: (t) => t.status !== 'archived',
        confirm: {
          title: (n) => `Archive ${n} writing task${n === 1 ? '' : 's'}?`,
          description: () => 'Learners will no longer see them.',
          confirmLabel: 'Archive',
          destructive: true,
        },
        run: (ids) => bulkWritingTasks('archive', ids),
      },
      {
        key: 'delete',
        label: 'Delete selected',
        icon: <Trash2 className="h-4 w-4" />,
        variant: 'danger',
        // No status gate (per decision) — permanent delete is allowed on any
        // status. The backend skips any task that has learner submissions.
        confirm: {
          title: (n) => `Permanently delete ${n} writing task${n === 1 ? '' : 's'}?`,
          description: () =>
            'The tasks and all their authoring content are permanently removed. This cannot be undone. Tasks with learner submissions are skipped.',
          confirmLabel: 'Delete permanently',
          destructive: true,
        },
        run: (ids) => bulkWritingTasks('delete', ids),
      },
      {
        key: 'force-delete',
        label: 'Force delete',
        icon: <AlertTriangle className="h-4 w-4" />,
        variant: 'danger',
        // Like Delete but ALSO purges every learner submission, grade, appeal,
        // annotation, moderation record, and attempt event tied to the task.
        confirm: {
          title: (n) => `Force-delete ${n} writing task${n === 1 ? '' : 's'} and all learner data?`,
          description: () =>
            'The tasks are permanently removed along with every learner submission, grade, appeal, annotation, and attempt event tied to them. This destroys learner history and cannot be undone.',
          confirmLabel: 'Force delete',
          destructive: true,
        },
        run: (ids) => bulkWritingTasks('force-delete', ids),
      },
    ];
  }, [canWrite]);

  const handleBulkResult = useCallback(
    (action: ManagedBulkAction<WritingTaskDto>, result: BulkResult) => {
      const verb =
        action.key === 'archive'
          ? 'Archived'
          : action.key === 'delete' || action.key === 'force-delete'
            ? 'Deleted'
            : 'Published';
      const failed = result.failed ?? 0;
      const skipped = result.skipped ?? 0;
      const parts = [`${verb} ${result.succeeded} of ${result.totalRequested}`];
      if (skipped > 0) parts.push(`${skipped} skipped`);
      if (failed > 0) parts.push(`${failed} failed`);
      setToast({
        variant: failed > 0 ? 'error' : 'success',
        message: `${parts.join(', ')}.`,
      });
      void load();
    },
    [load],
  );

  const handleBulkError = useCallback(
    (_action: ManagedBulkAction<WritingTaskDto>, error: unknown) => {
      setToast({
        message: error instanceof Error ? error.message : 'Bulk action failed',
        variant: 'error',
      });
    },
    [],
  );

  const pagedTasks = useMemo(
    () => tasks.slice((page - 1) * pageSize, page * pageSize),
    [tasks, page, pageSize],
  );

  const columns = useMemo<Column<WritingTaskDto>[]>(
    () => [
      {
        key: 'task',
        header: 'Task',
        render: (task) => (
          <div>
            <button
              type="button"
              onClick={() => router.push(`/admin/writing/tasks/${task.id}/edit`)}
              className="text-left font-medium text-admin-fg-strong outline-none transition-colors duration-150 hover:text-[var(--admin-primary)] focus-visible:text-[var(--admin-primary)] focus-visible:underline motion-reduce:transition-none"
            >
              {task.title || 'Untitled task'}
            </button>
            <div className="mt-0.5 flex flex-wrap items-center gap-x-2 gap-y-0.5 text-xs text-admin-fg-muted">
              {task.internalCode && <span className="font-mono">{task.internalCode}</span>}
              <span
                className={
                  task.stimulusPdfDownloadPath
                    ? 'text-[var(--admin-success)]'
                    : 'text-[var(--admin-fg-muted)]'
                }
              >
                {task.stimulusPdfDownloadPath ? 'Stimulus PDF ✓' : 'No stimulus PDF'}
              </span>
            </div>
          </div>
        ),
      },
      {
        key: 'profession',
        header: 'Profession',
        render: (task) => WRITING_PROFESSION_LABELS[task.profession],
      },
      {
        key: 'type',
        header: 'Type',
        render: (task) => WRITING_LETTER_TYPE_LABELS[task.letterType],
      },
      {
        key: 'difficulty',
        header: 'Diff.',
        render: (task) => <span className="tabular-nums">{task.difficulty}</span>,
      },
      {
        key: 'mode',
        header: 'Mode',
        render: (task) => WRITING_SIMULATION_MODE_LABELS[task.simulationModes],
      },
      {
        key: 'status',
        header: 'Status',
        render: (task) => <StatusBadge status={task.status} />,
      },
      {
        key: 'updated',
        header: 'Updated',
        render: (task) => (
          <span className="text-xs text-admin-fg-muted">{formatDate(task.updatedAt)}</span>
        ),
      },
      {
        key: 'actions',
        header: 'Actions',
        render: (task) => (
          <div className="flex flex-wrap items-center justify-end gap-1.5">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => router.push(`/admin/writing/tasks/${task.id}/edit`)}
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
        ),
      },
    ],
    [canWrite, canPublish, rowBusyId, router, handleClone, handlePublish, handleArchive, handleExport],
  );

  const headerActions = canWrite ? (
    <>
      <Button variant="secondary" size="sm" onClick={() => setImportOpen(true)}>
        Import JSON
      </Button>
      <Button size="sm" onClick={() => router.push('/admin/writing/tasks/new')}>
        + New task
      </Button>
    </>
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
          <AdminManagedTable
            columns={columns}
            data={pagedTasks}
            keyExtractor={(t) => t.id}
            total={tasks.length}
            page={page}
            pageSize={pageSize}
            onPageChange={setPage}
            onPageSizeChange={(s) => {
              setPageSize(s);
              setPage(1);
            }}
            itemLabel="task"
            itemLabelPlural="tasks"
            bulkActions={bulkActions}
            onResult={handleBulkResult}
            onError={handleBulkError}
          />
        )}
      </SettingsSection>

      <WritingTaskImportDialog
        open={importOpen}
        onClose={() => setImportOpen(false)}
        onError={(message) => setToast({ message, variant: 'error' })}
        onSuccess={(message) => setToast({ message, variant: 'success' })}
      />

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
