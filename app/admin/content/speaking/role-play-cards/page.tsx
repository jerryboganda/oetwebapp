'use client';

/**
 * Phase 1 (C.2) of the OET Speaking module roadmap.
 *
 * Admin list view for role-play cards. Mirrors the structure of the other
 * admin content lists (listening/reading/writing/mocks):
 *   - AdminCatalogLayout (filters + actions)
 *   - Filter row (profession / difficulty / status)
 *   - AdminManagedTable (selectable table + bulk publish/archive bar)
 *     with per-row edit / interlocutor / preview / duplicate actions.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  Archive as ArchiveIcon,
  CheckCircle2,
  Copy,
  Plus,
  RotateCcw,
  Sparkles,
  Upload,
  XCircle,
} from 'lucide-react';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { EmptyState } from '@/components/admin/ui/empty-state';
import {
  AdminManagedTable,
  type ManagedBulkAction,
  type BulkResult,
} from '@/components/admin/managed-table/admin-managed-table';

import { type Column } from '@/components/ui/data-table';
import { Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import {
  DIFFICULTY_OPTIONS,
  PROFESSION_OPTIONS,
  adminDuplicateRolePlayCard,
  adminListRolePlayCards,
  bulkAdminRolePlayCards,
  type RolePlayCardSummary,
} from '@/lib/api/speaking-role-play-cards';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'All statuses' },
  { value: 'Draft', label: 'Draft' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Role-play cards' },
];

const cardHref = (cardId: string, suffix = '') =>
  `/admin/content/speaking/role-play-cards/${encodeURIComponent(cardId)}${suffix}`;

export default function AdminSpeakingRolePlayCardsPage() {
  const router = useRouter();
  const [rows, setRows] = useState<RolePlayCardSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const [profession, setProfession] = useState('');
  const [difficulty, setDifficulty] = useState('');
  const [status, setStatus] = useState('');

  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListRolePlayCards({
        professionId: profession || undefined,
        difficulty: difficulty || undefined,
        status: status || undefined,
      });
      // Some backends return { rolePlayCards: [...] }, others return [...].
      const items = Array.isArray(data)
        ? (data as RolePlayCardSummary[])
        : ((data as unknown as { rolePlayCards?: RolePlayCardSummary[] })
            .rolePlayCards ?? []);
      setRows(items);
      setPage(1);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setLoading(false);
    }
  }, [profession, difficulty, status]);

  useEffect(() => {
    void reload();
  }, [reload]);

  const handleDuplicate = useCallback(
    async (row: RolePlayCardSummary) => {
      setBusyId(row.cardId);
      try {
        const created = await adminDuplicateRolePlayCard(row.cardId);
        setToast({ variant: 'success', message: `Duplicated as "${created.scenarioTitle}".` });
        await reload();
      } catch (e) {
        setToast({ variant: 'error', message: (e as Error).message });
      } finally {
        setBusyId(null);
      }
    },
    [reload],
  );

  const bulkActions: ManagedBulkAction<RolePlayCardSummary>[] = useMemo(
    () => [
      {
        key: 'publish',
        label: 'Publish selected',
        icon: <CheckCircle2 className="h-4 w-4" />,
        variant: 'primary',
        // Gate on Draft AND interlocutor-ready (the DTO exposes the flag). The
        // backend re-checks per card and reports any that slip through as failed.
        isEligible: (r) => r.status === 'Draft' && r.hasInterlocutorScript,
        run: (ids) => bulkAdminRolePlayCards('publish', ids),
      },
      {
        key: 'archive',
        label: 'Archive selected',
        icon: <ArchiveIcon className="h-4 w-4" />,
        variant: 'danger',
        isEligible: (r) => r.status === 'Published',
        confirm: {
          title: (n) => `Archive ${n} role-play card${n === 1 ? '' : 's'}?`,
          description: () => 'Archived cards become read-only and are hidden from learners.',
          confirmLabel: 'Archive',
          destructive: true,
        },
        run: (ids) => bulkAdminRolePlayCards('archive', ids),
      },
    ],
    [],
  );

  const handleBulkResult = useCallback(
    (action: ManagedBulkAction<RolePlayCardSummary>, result: BulkResult) => {
      const verb = action.key === 'archive' ? 'Archived' : 'Published';
      const failed = result.failed ?? 0;
      const skipped = result.skipped ?? 0;
      const parts = [`${verb} ${result.succeeded} of ${result.totalRequested}`];
      if (skipped > 0) parts.push(`${skipped} skipped`);
      if (failed > 0) {
        // Publish fails per-card for cards missing an interlocutor script.
        parts.push(
          action.key === 'publish'
            ? `${failed} failed (missing interlocutor script or not publishable)`
            : `${failed} failed`,
        );
      }
      setToast({
        variant: failed > 0 ? 'error' : 'success',
        message: `${parts.join(', ')}.`,
      });
      void reload();
    },
    [reload],
  );

  const handleBulkError = useCallback(
    (action: ManagedBulkAction<RolePlayCardSummary>, error: unknown) => {
      const verb = action.key === 'archive' ? 'Archive' : 'Publish';
      setToast({ variant: 'error', message: `${verb} failed: ${(error as Error).message}` });
    },
    [],
  );

  const filterCount = useMemo(
    () => [profession, difficulty, status].filter(Boolean).length,
    [profession, difficulty, status],
  );

  const pagedRows = useMemo(
    () => rows.slice((page - 1) * pageSize, page * pageSize),
    [rows, page, pageSize],
  );

  const columns: Column<RolePlayCardSummary>[] = useMemo(
    () => [
      {
        key: 'title',
        header: 'Title',
        render: (r) => (
          <Link href={cardHref(r.cardId)} className="font-semibold hover:text-[var(--admin-primary)]">
            {r.title}
          </Link>
        ),
      },
      {
        key: 'profession',
        header: 'Profession',
        render: (r) => <Badge variant="default" intensity="tinted">{r.professionId}</Badge>,
      },
      {
        key: 'difficulty',
        header: 'Difficulty',
        render: (r) => <Badge variant="default" intensity="tinted">{r.difficulty}</Badge>,
      },
      {
        key: 'status',
        header: 'Status',
        render: (r) => (
          <Badge variant={statusVariant(r.status)} intensity="tinted">{r.status}</Badge>
        ),
      },
      {
        key: 'interlocutor',
        header: 'Interlocutor',
        render: (r) =>
          r.hasInterlocutorScript ? (
            <span className="inline-flex items-center gap-1 text-xs font-semibold text-[var(--admin-success)]">
              <CheckCircle2 className="h-3.5 w-3.5" /> Ready
            </span>
          ) : (
            <span className="inline-flex items-center gap-1 text-xs font-semibold text-[var(--admin-warning)]">
              <XCircle className="h-3.5 w-3.5" /> Missing
            </span>
          ),
      },
      {
        key: 'published',
        header: 'Published',
        hideOnMobile: true,
        render: (r) =>
          r.publishedAt ? (
            <span className="text-xs text-admin-fg-muted">
              {new Date(r.publishedAt).toLocaleDateString()}
            </span>
          ) : (
            <span className="text-xs text-admin-fg-muted">-</span>
          ),
      },
      {
        key: 'actions',
        header: 'Actions',
        render: (r) => (
          <div className="flex flex-wrap items-center gap-2">
            <Link href={cardHref(r.cardId)} className="text-sm font-semibold text-admin-primary hover:underline">
              Edit
            </Link>
            <Link href={cardHref(r.cardId, '/interlocutor')} className="text-sm font-semibold text-admin-primary hover:underline">
              Interlocutor
            </Link>
            <Link href={cardHref(r.cardId, '/preview')} className="text-sm font-semibold text-admin-primary hover:underline">
              Preview
            </Link>
            <Button
              size="sm"
              variant="ghost"
              onClick={() => void handleDuplicate(r)}
              disabled={busyId === r.cardId}
              title="Duplicate as draft"
            >
              <Copy className="h-4 w-4" />
            </Button>
          </div>
        ),
      },
    ],
    [busyId, handleDuplicate],
  );

  const filtersNode = (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-4">
      <Select
        label="Profession"
        value={profession}
        onChange={(e) => setProfession(e.target.value)}
        options={[{ value: '', label: 'All professions' }, ...PROFESSION_OPTIONS]}
      />
      <Select
        label="Difficulty"
        value={difficulty}
        onChange={(e) => setDifficulty(e.target.value)}
        options={[{ value: '', label: 'All difficulties' }, ...DIFFICULTY_OPTIONS]}
      />
      <Select
        label="Status"
        value={status}
        onChange={(e) => setStatus(e.target.value)}
        options={STATUS_OPTIONS}
      />
      <div className="flex items-end">
        <Button variant="outline" onClick={() => void reload()}>
          <RotateCcw className="mr-1 h-4 w-4" /> Refresh
        </Button>
      </div>
    </div>
  );

  return (
    <AdminCatalogLayout
      title="Speaking role-play cards"
      description="Author the two-card OET Speaking scenarios: candidate card plus hidden interlocutor script. Publish only after both sides are written."
      breadcrumbs={BREADCRUMBS}
      eyebrow="CMS"
      hideViewModeToggle
      filters={filtersNode}
      actions={
        <div className="flex items-center gap-2">
          <Button variant="outline" onClick={() => router.push('/admin/content/speaking/role-play-cards/import')}>
            <Upload className="mr-1 h-4 w-4" /> Import PDF
          </Button>
          <Button onClick={() => router.push('/admin/content/speaking/role-play-cards/new')}>
            <Plus className="mr-1 h-4 w-4" /> New role-play card
          </Button>
        </div>
      }
    >
      <p className="col-span-full text-xs text-admin-fg-muted">
        {rows.length} card(s){filterCount > 0 ? ` matching ${filterCount} filter(s)` : ''}.
      </p>

      <div className="col-span-full">
        <AdminManagedTable
          columns={columns}
          data={pagedRows}
          keyExtractor={(r) => r.cardId}
          total={rows.length}
          loading={loading}
          page={page}
          pageSize={pageSize}
          onPageChange={setPage}
          onPageSizeChange={(s) => {
            setPageSize(s);
            setPage(1);
          }}
          itemLabel="card"
          itemLabelPlural="cards"
          bulkActions={bulkActions}
          onResult={handleBulkResult}
          onError={handleBulkError}
          emptyState={
            !loading && rows.length === 0 ? (
              <EmptyState
                illustration={<Sparkles />}
                title="No role-play cards yet"
                description="Click 'New role-play card' to author the first one."
              />
            ) : undefined
          }
        />
      </div>

      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}
    </AdminCatalogLayout>
  );
}

function statusVariant(status: string): 'success' | 'default' | 'warning' {
  switch ((status ?? '').toLowerCase()) {
    case 'published':
      return 'success';
    case 'draft':
      return 'default';
    case 'archived':
      return 'warning';
    default:
      return 'default';
  }
}
