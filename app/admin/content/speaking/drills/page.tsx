'use client';

/**
 * OET Speaking - Phase 11 P11.5 - admin drill bank list page.
 *
 * Lists drills curated by status (draft / in-review / published / archived)
 * and lets the admin create / publish / archive / delete. Detail editing
 * lives at `[id]/page.tsx`.
 */
import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Sparkles, ArrowRight, Upload, Archive, Trash2 } from 'lucide-react';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';

import { InlineAlert, Toast } from '@/components/ui/alert';
import { Button as LegacyButton } from '@/components/ui/button';
import { type Column } from '@/components/ui/data-table';
import {
  AdminManagedTable,
  type ManagedBulkAction,
  type BulkResult,
} from '@/components/admin/managed-table/admin-managed-table';
import { Input, Select } from '@/components/ui/form-controls';
import {
  bulkAdminDrills,
  createAdminDrill,
  listAdminDrills,
  SPEAKING_DRILL_KINDS,
  type AdminDrillCreateInput,
  type AdminDrillSummary,
  type SpeakingDrillKind,
} from '@/lib/api/speaking-drills';

const STATUS_VARIANT: Record<string, 'default' | 'success' | 'warning' | 'info'> = {
  draft: 'default',
  inreview: 'warning',
  published: 'success',
  archived: 'default',
};

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Drill bank' },
];

function summarizeResult(verb: string, result: BulkResult): string {
  const n = result.succeeded;
  const noun = n === 1 ? 'drill' : 'drills';
  return `${verb} ${n} ${noun} (${result.skipped ?? 0} skipped, ${result.failed ?? 0} failed)`;
}

export default function AdminSpeakingDrillsPage() {
  const [drills, setDrills] = useState<AdminDrillSummary[]>([]);
  const [statusFilter, setStatusFilter] = useState<string>('');
  const [kindFilter, setKindFilter] = useState<string>('');
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const [draft, setDraft] = useState<AdminDrillCreateInput>({
    drillKind: 'fluency_relay',
    title: '',
    instructionText: '',
    targetCriteria: [],
    professionId: null,
  });

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await listAdminDrills({
        status: (statusFilter || undefined) as 'draft' | 'inreview' | 'published' | 'archived' | undefined,
        drillKind: kindFilter || undefined,
      });
      setDrills(res.drills ?? []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load drills.');
    } finally {
      setLoading(false);
    }
  }, [statusFilter, kindFilter]);

  useEffect(() => {
    reload();
  }, [reload]);

  // Reset to the first page whenever the filtered set changes.
  useEffect(() => {
    setPage(1);
  }, [statusFilter, kindFilter]);

  // listAdminDrills returns the full list with no server-side pagination, so we
  // slice client-side for the current page while passing the full count as total.
  const pageRows = useMemo(
    () => drills.slice((page - 1) * pageSize, page * pageSize),
    [drills, page, pageSize],
  );

  const columns = useMemo<Column<AdminDrillSummary>[]>(() => [
    {
      key: 'title',
      header: 'Title',
      render: (row) => (
        <Link
          href={`/admin/content/speaking/drills/${encodeURIComponent(row.drillId)}`}
          className="font-medium text-admin-fg-strong hover:underline line-clamp-1"
        >
          {row.title}
        </Link>
      ),
    },
    {
      key: 'drillKind',
      header: 'Kind',
      render: (row) => (
        <Badge variant="default" intensity="tinted">{String(row.drillKind)}</Badge>
      ),
      hideOnMobile: true,
    },
    {
      key: 'profession',
      header: 'Profession',
      render: (row) => (
        <span className="text-sm text-admin-fg-default">{row.professionId ?? 'All'}</span>
      ),
      hideOnMobile: true,
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => {
        const variant = STATUS_VARIANT[row.status] ?? 'default';
        return (
          <Badge variant={variant as any} intensity="tinted">{row.status}</Badge>
        );
      },
    },
    {
      key: 'instruction',
      header: 'Instruction',
      render: (row) => (
        <span className="text-sm text-admin-fg-muted line-clamp-1">{row.instructionText}</span>
      ),
      hideOnMobile: true,
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div className="flex items-center justify-end gap-2">
          <Link
            href={`/admin/content/speaking/drills/${encodeURIComponent(row.drillId)}`}
            aria-label={`Edit ${row.title}`}
          >
            <LegacyButton variant="ghost" size="sm">
              <ArrowRight className="h-4 w-4" />
            </LegacyButton>
          </Link>
        </div>
      ),
    },
  ], []);

  const bulkActions = useMemo<ManagedBulkAction<AdminDrillSummary>[]>(() => [
    {
      key: 'publish',
      label: 'Publish',
      icon: <Upload className="h-4 w-4" />,
      variant: 'primary',
      isEligible: (row) => row.status !== 'published',
      run: (ids) => bulkAdminDrills('publish', ids),
    },
    {
      key: 'archive',
      label: 'Archive',
      icon: <Archive className="h-4 w-4" />,
      variant: 'danger',
      isEligible: (row) => row.status !== 'archived',
      confirm: {
        title: (n) => `Archive ${n} ${n === 1 ? 'drill' : 'drills'}?`,
        description: (n) =>
          `${n} ${n === 1 ? 'drill' : 'drills'} will be archived. This can be undone by an admin later.`,
        confirmLabel: 'Archive',
        destructive: true,
      },
      run: (ids) => bulkAdminDrills('archive', ids),
    },
    {
      key: 'delete',
      label: 'Delete',
      icon: <Trash2 className="h-4 w-4" />,
      variant: 'danger',
      confirm: {
        title: (n) => `Delete ${n} ${n === 1 ? 'drill' : 'drills'}?`,
        description: (n) =>
          `${n} ${n === 1 ? 'drill' : 'drills'} will be deleted (soft-archived) and removed from the bank.`,
        confirmLabel: 'Delete',
        destructive: true,
      },
      run: (ids) => bulkAdminDrills('delete', ids),
    },
  ], []);

  function handleResult(action: ManagedBulkAction<AdminDrillSummary>, result: BulkResult) {
    const verbs: Record<string, string> = {
      publish: 'Published',
      archive: 'Archived',
      delete: 'Deleted',
    };
    const verb = verbs[action.key] ?? 'Updated';
    setToast({ variant: 'success', message: summarizeResult(verb, result) });
    void reload();
  }

  function handleError() {
    setToast({ variant: 'error', message: 'Bulk action failed.' });
  }

  async function submitDraft(e: React.FormEvent) {
    e.preventDefault();
    if (!draft.title.trim() || !draft.instructionText.trim()) return;
    setCreating(true);
    setError(null);
    try {
      const targetCriteria = draft.targetCriteria
        .map((s) => s.trim())
        .filter((s) => s.length > 0);
      await createAdminDrill({ ...draft, targetCriteria });
      setDraft({
        drillKind: 'fluency_relay',
        title: '',
        instructionText: '',
        targetCriteria: [],
        professionId: null,
      });
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not create drill.');
    } finally {
      setCreating(false);
    }
  }

  const filtersNode = (
    <div className="flex flex-wrap items-end gap-2">
      <div className="w-40">
        <Select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          options={[
            { value: '', label: 'All statuses' },
            { value: 'draft', label: 'Draft' },
            { value: 'inreview', label: 'In review' },
            { value: 'published', label: 'Published' },
            { value: 'archived', label: 'Archived' },
          ]}
        />
      </div>
      <div className="w-40">
        <Select
          value={kindFilter}
          onChange={(e) => setKindFilter(e.target.value)}
          options={[
            { value: '', label: 'All kinds' },
            ...SPEAKING_DRILL_KINDS.map((k) => ({ value: k, label: k })),
          ]}
        />
      </div>
      <Button variant="outline" size="sm" onClick={reload} disabled={loading}>
        Refresh
      </Button>
    </div>
  );

  return (
    <>
    {toast && (
      <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
    )}
    <AdminCatalogLayout
      title="Speaking drill bank"
      description="Curate the post-session remediation drills that the speaking analytics console recommends to learners after low-scoring criteria."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Content"
      hideViewModeToggle
      filters={filtersNode}
      actions={
        <Button asChild variant="outline">
          <Link href="/admin/content/speaking/drills/ai-draft">AI-assisted draft</Link>
        </Button>
      }
    >
      {error && (
        <div className="col-span-full">
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      )}

      <Card className="col-span-full">
        <CardHeader>
          <CardTitle>Create drill (manual)</CardTitle>
        </CardHeader>
        <CardContent>
          <form className="grid gap-3 sm:grid-cols-2" onSubmit={submitDraft}>
            <Input
              placeholder="Title"
              value={draft.title}
              onChange={(e) => setDraft({ ...draft, title: e.target.value })}
              required
            />
            <Select
              value={draft.drillKind}
              onChange={(e) =>
                setDraft({ ...draft, drillKind: e.target.value as SpeakingDrillKind })
              }
              options={SPEAKING_DRILL_KINDS.map((k) => ({ value: k, label: k }))}
            />
            <Input
              placeholder="Profession id (optional)"
              value={draft.professionId ?? ''}
              onChange={(e) =>
                setDraft({ ...draft, professionId: e.target.value || null })
              }
            />
            <Input
              placeholder="Target criteria (comma-separated)"
              value={draft.targetCriteria.join(', ')}
              onChange={(e) =>
                setDraft({
                  ...draft,
                  targetCriteria: e.target.value.split(','),
                })
              }
            />
            <textarea
              className="rounded-admin border border-admin-border bg-admin-bg-surface p-2 text-sm sm:col-span-2"
              rows={3}
              placeholder="Instruction text shown to the learner"
              value={draft.instructionText}
              onChange={(e) => setDraft({ ...draft, instructionText: e.target.value })}
              required
            />
            <div className="sm:col-span-2 flex justify-end">
              <Button type="submit" disabled={creating}>
                {creating ? 'Creating...' : 'Create drill'}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>

      {loading ? (
        <div className="col-span-full">
          <Skeleton className="h-48 w-full" />
        </div>
      ) : (
        <div className="col-span-full">
          <AdminManagedTable
            columns={columns}
            data={pageRows}
            keyExtractor={(d) => d.drillId}
            total={drills.length}
            loading={loading}
            page={page}
            pageSize={pageSize}
            onPageChange={setPage}
            onPageSizeChange={(s) => { setPageSize(s); setPage(1); }}
            pageSizeOptions={[10, 25, 50, 100]}
            itemLabel="drill"
            itemLabelPlural="drills"
            bulkActions={bulkActions}
            onResult={handleResult}
            onError={handleError}
            emptyState={
              <EmptyState
                illustration={<Sparkles />}
                title="No drills match the current filters"
                description="Adjust filters or create a new drill above."
              />
            }
          />
        </div>
      )}
    </AdminCatalogLayout>
    </>
  );
}
