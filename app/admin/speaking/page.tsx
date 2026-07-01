'use client';

/**
 * Unified Speaking authoring hub.
 *
 * Replaces the old three-card fragmentation (separate "Speaking authoring",
 * "Mock sets" and "Shared resources" workspaces) with a single screen: New
 * actions that launch the wizards, a tabbed managed list of role-play cards
 * (with bulk publish/archive) and mock sets, and the unchanged
 * operations/quality links.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  Archive,
  BarChart3,
  CheckCircle2,
  Check,
  Eye,
  FileSearch,
  Loader2,
  Minus,
  Pencil,
  Plus,
  Tags,
} from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { AdminHubSection, type AdminHubLink } from '@/components/admin/ui/hub-card';
import { DataTable, type Column } from '@/components/ui/data-table';
import {
  AdminManagedTable,
  type ManagedBulkAction,
  type BulkResult,
} from '@/components/admin/managed-table/admin-managed-table';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Toast } from '@/components/ui/alert';
import { canAccessAdminRoute, hasPermission, AdminPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  adminListRolePlayCards,
  bulkAdminRolePlayCards,
  type RolePlayCardSummary,
} from '@/lib/api/speaking-role-play-cards';
import {
  archiveAdminSpeakingMockSet,
  fetchAdminSpeakingMockSets,
  publishAdminSpeakingMockSet,
  type AdminSpeakingMockSetRow,
} from '@/lib/api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;
type Tab = 'cards' | 'mock-sets';

const operationsLinks: AdminHubLink[] = [
  {
    href: '/admin/speaking/result-visibility',
    title: 'Result visibility',
    description: 'Choose exactly which Speaking result fields learners can see, from submission receipt to drills and reattempts.',
    icon: <Eye className="h-5 w-5" />,
    badge: 'Policy',
    badgeVariant: 'info',
  },
  {
    href: '/admin/analytics/speaking',
    title: 'Speaking analytics',
    description: 'Review class, tutor-consistency, and content-difficulty metrics for the Speaking module.',
    icon: <BarChart3 className="h-5 w-5" />,
    badge: 'Insights',
    badgeVariant: 'success',
  },
  {
    href: '/admin/speaking/recordings/audit',
    title: 'Recording audit',
    description: 'Inspect access patterns and audit trails for learner speaking recordings.',
    icon: <FileSearch className="h-5 w-5" />,
    badge: 'Audit',
    badgeVariant: 'warning',
  },
];

export default function AdminSpeakingPage() {
  const { user } = useCurrentUser();
  const perms = user?.adminPermissions;
  const canWrite = hasPermission(perms, AdminPermission.ContentWrite);

  const [tab, setTab] = useState<Tab>('cards');
  const [toast, setToast] = useState<ToastState>(null);

  const [cards, setCards] = useState<RolePlayCardSummary[]>([]);
  const [cardsLoading, setCardsLoading] = useState(true);
  const [cardsPage, setCardsPage] = useState(1);
  const [cardsPageSize, setCardsPageSize] = useState(25);

  const [mockSets, setMockSets] = useState<AdminSpeakingMockSetRow[]>([]);
  const [mockSetsLoading, setMockSetsLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);

  const reloadCards = useCallback(async () => {
    setCardsLoading(true);
    try {
      const data = await adminListRolePlayCards({});
      const items = Array.isArray(data)
        ? (data as RolePlayCardSummary[])
        : ((data as unknown as { rolePlayCards?: RolePlayCardSummary[] }).rolePlayCards ?? []);
      setCards(items);
      setCardsPage(1);
    } catch (e) {
      setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Failed to load cards.' });
    } finally {
      setCardsLoading(false);
    }
  }, []);

  const reloadMockSets = useCallback(async () => {
    setMockSetsLoading(true);
    try {
      setMockSets(await fetchAdminSpeakingMockSets());
    } catch (e) {
      setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Failed to load mock sets.' });
    } finally {
      setMockSetsLoading(false);
    }
  }, []);

  useEffect(() => {
    void reloadCards();
    void reloadMockSets();
  }, [reloadCards, reloadMockSets]);

  // ── Card bulk actions (publish / archive) ──────────────────────────────
  const cardBulkActions = useMemo<ManagedBulkAction<RolePlayCardSummary>[]>(() => {
    if (!canWrite) return [];
    return [
      {
        key: 'publish',
        label: 'Publish selected',
        icon: <CheckCircle2 className="h-4 w-4" />,
        variant: 'primary',
        isEligible: (r) => String(r.status).toLowerCase() === 'draft' && r.hasInterlocutorScript,
        run: (ids) => bulkAdminRolePlayCards('publish', ids),
      },
      {
        key: 'archive',
        label: 'Archive selected',
        icon: <Archive className="h-4 w-4" />,
        variant: 'danger',
        isEligible: (r) => String(r.status).toLowerCase() === 'published',
        confirm: {
          title: (n) => `Archive ${n} role-play card${n === 1 ? '' : 's'}?`,
          description: () => 'Archived cards become read-only and are hidden from learners.',
          confirmLabel: 'Archive',
          destructive: true,
        },
        run: (ids) => bulkAdminRolePlayCards('archive', ids),
      },
    ];
  }, [canWrite]);

  const handleCardBulkResult = useCallback(
    (action: ManagedBulkAction<RolePlayCardSummary>, result: BulkResult) => {
      const verb = action.key === 'archive' ? 'Archived' : 'Published';
      const failed = result.failed ?? 0;
      const skipped = result.skipped ?? 0;
      const extra = [failed ? `${failed} failed` : '', skipped ? `${skipped} skipped` : ''].filter(Boolean).join(', ');
      setToast({
        variant: failed > 0 ? 'error' : 'success',
        message: `${verb} ${result.succeeded}/${result.totalRequested}${extra ? ` (${extra})` : ''}.`,
      });
      void reloadCards();
    },
    [reloadCards],
  );

  // ── Mock-set per-row actions (no bulk endpoint exists) ─────────────────
  const publishMockSet = useCallback(async (id: string) => {
    setBusyId(id);
    try {
      await publishAdminSpeakingMockSet(id);
      setToast({ variant: 'success', message: 'Mock set published.' });
      await reloadMockSets();
    } catch (e) {
      setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Publish failed.' });
    } finally {
      setBusyId(null);
    }
  }, [reloadMockSets]);

  const archiveMockSet = useCallback(async (id: string) => {
    if (!confirm('Archive this mock set?')) return;
    setBusyId(id);
    try {
      await archiveAdminSpeakingMockSet(id);
      setToast({ variant: 'success', message: 'Mock set archived.' });
      await reloadMockSets();
    } catch (e) {
      setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Archive failed.' });
    } finally {
      setBusyId(null);
    }
  }, [reloadMockSets]);

  const cardColumns = useMemo<Column<RolePlayCardSummary>[]>(() => [
    {
      key: 'scenario',
      header: 'Scenario',
      render: (row) => (
        <div className="flex flex-col">
          <span className="font-bold">{row.title || '(untitled)'}</span>
          {row.clinicalTopic ? <span className="text-xs text-muted line-clamp-1">{row.clinicalTopic}</span> : null}
        </div>
      ),
    },
    { key: 'profession', header: 'Profession', render: (row) => <span className="text-sm capitalize">{row.professionId}</span> },
    {
      key: 'script',
      header: 'Script',
      render: (row) =>
        row.hasInterlocutorScript ? <Check className="h-4 w-4 text-emerald-600" /> : <Minus className="h-4 w-4 text-muted" />,
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => {
        const s = String(row.status).toLowerCase();
        return <Badge variant={s === 'published' ? 'success' : s === 'archived' ? 'muted' : 'info'}>{row.status}</Badge>;
      },
    },
    {
      key: 'actions',
      header: 'Actions',
      render: (row) => (
        <Button asChild size="sm" variant="outline">
          <Link href={`/admin/speaking/cards/${encodeURIComponent(row.cardId)}/classification`}>
            <Pencil className="h-3.5 w-3.5" /> Edit
          </Link>
        </Button>
      ),
    },
  ], []);

  const mockSetColumns = useMemo<Column<AdminSpeakingMockSetRow>[]>(() => [
    {
      key: 'title',
      header: 'Mock set',
      render: (row) => (
        <div className="flex flex-col">
          <span className="font-bold">{row.title}</span>
          {row.description ? <span className="text-xs text-muted line-clamp-1">{row.description}</span> : null}
        </div>
      ),
    },
    {
      key: 'rolePlays',
      header: 'Role-plays',
      render: (row) => (
        <div className="flex flex-col gap-1 text-xs">
          <span className={row.rolePlay1.isSpeaking ? '' : 'text-danger'}>1. {row.rolePlay1.title}{!row.rolePlay1.isSpeaking && ' (not speaking!)'}</span>
          <span className={row.rolePlay2.isSpeaking ? '' : 'text-danger'}>2. {row.rolePlay2.title}{!row.rolePlay2.isSpeaking && ' (not speaking!)'}</span>
        </div>
      ),
    },
    { key: 'status', header: 'Status', render: (row) => <Badge variant={row.status === 'published' ? 'success' : row.status === 'archived' ? 'muted' : 'info'}>{row.status}</Badge> },
    { key: 'difficulty', header: 'Difficulty', render: (row) => <Badge variant="muted">{row.difficulty}</Badge> },
    {
      key: 'actions',
      header: 'Actions',
      render: (row) => (
        <div className="flex flex-wrap items-center gap-2">
          {row.status !== 'archived' ? (
            <Button asChild size="sm" variant="outline">
              <Link href={`/admin/speaking/mock-sets/${encodeURIComponent(row.mockSetId)}/details`}>
                <Pencil className="h-3.5 w-3.5" /> Edit
              </Link>
            </Button>
          ) : null}
          {canWrite && row.status === 'draft' ? (
            <Button size="sm" variant="primary" onClick={() => void publishMockSet(row.mockSetId)} disabled={busyId === row.mockSetId}>
              <CheckCircle2 className="h-3.5 w-3.5" /> Publish
            </Button>
          ) : null}
          {canWrite && row.status !== 'archived' ? (
            <Button size="sm" variant="outline" onClick={() => void archiveMockSet(row.mockSetId)} disabled={busyId === row.mockSetId}>
              <Archive className="h-3.5 w-3.5" /> Archive
            </Button>
          ) : null}
        </div>
      ),
    },
  ], [busyId, canWrite, publishMockSet, archiveMockSet]);

  const visibleOperations = useMemo(
    () => operationsLinks.filter((link) => canAccessAdminRoute(perms, link.href)),
    [perms],
  );

  const newActions = canWrite ? (
    <div className="flex flex-wrap gap-2">
      <Button asChild variant="primary">
        <Link href="/admin/speaking/cards/new"><Plus className="h-4 w-4" /> New role-play card</Link>
      </Button>
      <Button asChild variant="outline">
        <Link href="/admin/speaking/mock-sets/new"><Plus className="h-4 w-4" /> New mock set</Link>
      </Button>
      <Button asChild variant="outline">
        <Link href="/admin/content/speaking/card-types"><Tags className="h-4 w-4" /> Card types</Link>
      </Button>
    </div>
  ) : null;

  const tabButton = (value: Tab, label: string) => (
    <button
      type="button"
      onClick={() => setTab(value)}
      aria-current={tab === value ? 'page' : undefined}
      className={`border-b-2 px-2 pb-2 text-sm font-semibold transition-colors ${
        tab === value ? 'border-primary text-navy' : 'border-transparent text-muted hover:text-navy'
      }`}
    >
      {label}
    </button>
  );

  const pagedCards = cards.slice((cardsPage - 1) * cardsPageSize, cardsPage * cardsPageSize);

  return (
    <AdminCatalogLayout
      title="Speaking authoring"
      description="Author role-play cards and compose mock sets in one place, then manage result visibility, analytics and recording audits."
      eyebrow="Admin"
      hideViewModeToggle
      itemsClassName="flex flex-col gap-6"
      actions={newActions}
    >
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <div className="rounded-2xl border border-border bg-surface">
        <div className="flex items-center gap-4 border-b border-border px-4 pt-3">
          {tabButton('cards', 'Role-play cards')}
          {tabButton('mock-sets', 'Mock sets')}
        </div>
        <div className="p-4">
          {tab === 'cards' ? (
            cardsLoading ? (
              <p className="inline-flex items-center gap-2 text-sm text-muted"><Loader2 className="h-4 w-4 animate-spin" /> Loading cards…</p>
            ) : (
              <AdminManagedTable<RolePlayCardSummary>
                columns={cardColumns}
                data={pagedCards}
                keyExtractor={(row) => row.cardId}
                total={cards.length}
                page={cardsPage}
                pageSize={cardsPageSize}
                onPageChange={setCardsPage}
                onPageSizeChange={(s) => {
                  setCardsPageSize(s);
                  setCardsPage(1);
                }}
                itemLabel="card"
                itemLabelPlural="cards"
                bulkActions={cardBulkActions}
                onResult={handleCardBulkResult}
                onError={(_action, error) => setToast({ variant: 'error', message: error instanceof Error ? error.message : 'Bulk action failed.' })}
              />
            )
          ) : mockSetsLoading ? (
            <p className="inline-flex items-center gap-2 text-sm text-muted"><Loader2 className="h-4 w-4 animate-spin" /> Loading mock sets…</p>
          ) : (
            <DataTable<AdminSpeakingMockSetRow> columns={mockSetColumns} data={mockSets} keyExtractor={(row) => row.mockSetId} />
          )}
        </div>
      </div>

      {visibleOperations.length > 0 ? (
        <AdminHubSection
          title="Operations & quality"
          description="Release policy, performance signals, and audit tools."
          links={visibleOperations}
          columns="three"
        />
      ) : null}
    </AdminCatalogLayout>
  );
}
