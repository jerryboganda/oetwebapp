'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { BookOpen, Plus, Upload, Sparkles, Trash2, Edit3, Volume2, Crown } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Input } from '@/components/admin/ui/input';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Toast } from '@/components/ui/alert';
import { Pagination } from '@/components/ui/pagination';
import { BulkActionBar } from '@/components/ui/bulk-action-bar';
import { BulkActionConfirmModal } from '@/components/ui/bulk-action-confirm-modal';
import {
  fetchAdminVocabularyItems,
  deleteAdminVocabularyItem,
  deleteAdminVocabularyItems,
  fetchAdminVocabularyCategories,
  fetchAdminVocabularyRecallSets,
  type AdminRecallSetSummary,
} from '@/lib/api';

type VocabRow = {
  id: string;
  term: string;
  definition: string;
  professionId: string | null;
  category: string;
  exampleSentence: string | null;
  status: 'draft' | 'active' | 'archived';
};

type ListResponse = {
  total: number;
  page: number;
  pageSize: number;
  items: VocabRow[];
};


export default function AdminVocabularyPage() {
  const router = useRouter();
  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState<VocabRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState<string>('');
  const [profession, setProfession] = useState<string>('');
  const [status, setStatus] = useState<string>('');
  const [recallSet, setRecallSet] = useState<string>('');
  const [categories, setCategories] = useState<Array<{ category: string; total: number }>>([]);
  const [recallSets, setRecallSets] = useState<AdminRecallSetSummary[]>([]);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const [confirmBulkDelete, setConfirmBulkDelete] = useState(false);
  const [bulkDeleting, setBulkDeleting] = useState(false);

  useEffect(() => {
    fetchAdminVocabularyCategories()
      .then(d => {
        const c = (d as { categories?: Array<{ category: string; total: number }> }).categories ?? [];
        setCategories(c);
      })
      .catch(() => {});
    fetchAdminVocabularyRecallSets()
      .then(d => setRecallSets(d.sets ?? []))
      .catch(() => {});
  }, []);

  useEffect(() => {
    let cancelled = false;
    const t = setTimeout(async () => {
      setLoading(true);
      try {
        const res = await fetchAdminVocabularyItems({
          profession: profession || undefined,
          category: category || undefined,
          status: status || undefined,
          search: search || undefined,
          recallSet: recallSet || undefined,
          page,
          pageSize,
        });
        if (cancelled) return;
        const data = res as ListResponse;
        setRows(data.items ?? []);
        setTotal(data.total ?? 0);
        setSelectedKeys(new Set());
      } catch {
        if (!cancelled) setToast({ variant: 'error', message: 'Failed to load vocabulary items.' });
      } finally {
        if (!cancelled) setLoading(false);
      }
    }, 300);
    return () => { cancelled = true; clearTimeout(t); };
  }, [search, category, profession, status, recallSet, page, pageSize]);

  async function handleDelete(id: string, term: string) {
    if (!confirm(`Delete "${term}"? Referenced terms will be archived instead.`)) return;
    try {
      await deleteAdminVocabularyItem(id);
      setToast({ variant: 'success', message: `Deleted "${term}".` });
      setRows(prev => prev.filter(r => r.id !== id));
    } catch {
      setToast({ variant: 'error', message: `Failed to delete "${term}".` });
    }
  }

  async function handleBulkDelete() {
    const itemIds = Array.from(selectedKeys);
    if (itemIds.length === 0) return;

    setBulkDeleting(true);
    try {
      // Backend limits to 1000 per request — chunk large selections
      const CHUNK_SIZE = 1000;
      let totalDeleted = 0;
      let totalArchived = 0;
      let totalFailed = 0;

      for (let i = 0; i < itemIds.length; i += CHUNK_SIZE) {
        const chunk = itemIds.slice(i, i + CHUNK_SIZE);
        const result = await deleteAdminVocabularyItems(chunk);
        totalDeleted += result.deleted;
        totalArchived += result.archived;
        totalFailed += result.failed;
      }

      const removed = new Set(itemIds);
      setRows(prev => prev.filter(row => !removed.has(row.id)));
      setTotal(prev => Math.max(0, prev - totalDeleted - totalArchived));
      setSelectedKeys(new Set());
      setConfirmBulkDelete(false);
      const archivedMessage = totalArchived > 0 ? ` ${totalArchived} archived because learners reference them.` : '';
      const failedMessage = totalFailed > 0 ? ` ${totalFailed} failed.` : '';
      setToast({
        variant: totalFailed > 0 ? 'error' : 'success',
        message: `Bulk delete complete: ${totalDeleted} deleted.${archivedMessage}${failedMessage}`,
      });
    } catch {
      setToast({ variant: 'error', message: 'Failed to delete selected vocabulary terms.' });
    } finally {
      setBulkDeleting(false);
    }
  }

  const columns = useMemo<Column<VocabRow>[]>(() => [
    {
      key: 'term',
      header: 'Term',
      render: (row) => (
        <div className="flex flex-col">
          <span className="font-semibold text-admin-fg-strong">{row.term}</span>
          <span className="text-xs text-admin-fg-muted line-clamp-1">{row.definition}</span>
        </div>
      ),
    },
    { key: 'category', header: 'Category', render: (row) => <span className="capitalize text-sm">{row.category.replace(/_/g, ' ')}</span> },
    { key: 'profession', header: 'Profession', render: (row) => <span className="text-sm capitalize">{row.professionId ?? '—'}</span>, hideOnMobile: true },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge variant={row.status === 'active' ? 'success' : row.status === 'draft' ? 'warning' : 'secondary'} size="sm">
          {row.status}
        </Badge>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div className="flex items-center justify-end gap-2">
          <Link href={`/admin/content/vocabulary/${row.id}`} aria-label={`Edit ${row.term}`}>
            <Edit3 className="h-4 w-4 text-admin-fg-muted hover:text-[var(--admin-primary)]" />
          </Link>
          <button onClick={() => handleDelete(row.id, row.term)} aria-label={`Delete ${row.term}`}>
            <Trash2 className="h-4 w-4 text-admin-fg-muted hover:text-[var(--admin-danger)]" />
          </button>
        </div>
      ),
    },
  ], []);

  const selectedRows = useMemo(
    () => rows.filter(row => selectedKeys.has(row.id)),
    [rows, selectedKeys],
  );

  const pageStatus = loading ? 'loading' : rows.length === 0 ? 'empty' : 'success';

  const filters = (
    <div className="grid gap-3 sm:grid-cols-4 w-full">
      <Input
        placeholder="Search term or definition…"
        value={search}
        onChange={(e) => { setSearch(e.target.value); setPage(1); }}
      />
      <select
        value={category}
        onChange={(e) => { setCategory(e.target.value); setPage(1); }}
        className="h-10 rounded-[var(--admin-radius-lg)] border border-[var(--admin-border)] bg-[var(--admin-bg-surface)] px-3 text-sm text-[var(--admin-fg-default)]"
      >
        <option value="">All categories ({total})</option>
        {categories.map(c => (
          <option key={c.category} value={c.category}>
            {c.category.replace(/_/g, ' ')} ({c.total})
          </option>
        ))}
      </select>
      <select
        value={profession}
        onChange={(e) => { setProfession(e.target.value); setPage(1); }}
        className="h-10 rounded-[var(--admin-radius-lg)] border border-[var(--admin-border)] bg-[var(--admin-bg-surface)] px-3 text-sm text-[var(--admin-fg-default)]"
      >
        <option value="">All professions</option>
        <option value="medicine">Medicine</option>
        <option value="nursing">Nursing</option>
        <option value="dentistry">Dentistry</option>
        <option value="pharmacy">Pharmacy</option>
      </select>
      <select
        value={status}
        onChange={(e) => { setStatus(e.target.value); setPage(1); }}
        className="h-10 rounded-[var(--admin-radius-lg)] border border-[var(--admin-border)] bg-[var(--admin-bg-surface)] px-3 text-sm text-[var(--admin-fg-default)]"
      >
        <option value="">All statuses</option>
        <option value="active">Active</option>
        <option value="draft">Draft</option>
        <option value="archived">Archived</option>
      </select>
    </div>
  );

  return (
    <>
      {toast && (
        <Toast variant={toast.variant === 'error' ? 'error' : 'success'} message={toast.message} onClose={() => setToast(null)} />
      )}
      <BulkActionConfirmModal
        open={confirmBulkDelete}
        title="Delete selected vocabulary terms"
        description={`Delete ${selectedKeys.size} selected vocabulary ${selectedKeys.size === 1 ? 'term' : 'terms'}? Terms referenced by learners will be archived instead of permanently deleted.`}
        confirmLabel="Delete selected"
        destructive
        loading={bulkDeleting}
        onConfirm={handleBulkDelete}
        onClose={() => setConfirmBulkDelete(false)}
      />
      <AdminCatalogLayout
        eyebrow="CMS"
        title="Vocabulary"
        description="Create, edit, import, and AI-draft OET vocabulary terms. All mutations are audit-logged."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Vocabulary' },
        ]}
        actions={
          <>
            <Button variant="secondary" size="sm" asChild>
              <Link href="/admin/content/vocabulary/import"><Upload className="mr-1.5 h-4 w-4" />Import CSV</Link>
            </Button>
            <Button variant="secondary" size="sm" asChild>
              <Link href="/admin/content/vocabulary/ai-draft"><Sparkles className="mr-1.5 h-4 w-4" />AI draft</Link>
            </Button>
            <Button variant="primary" size="sm" asChild>
              <Link href="/admin/content/vocabulary/new"><Plus className="mr-1.5 h-4 w-4" />New term</Link>
            </Button>
          </>
        }
        filters={filters}
        hideViewModeToggle
        itemsClassName="flex flex-col gap-4"
      >
        {/* TTS Configuration Info */}
        <Card>
          <CardContent className="p-4 pt-4">
            <h3 className="flex items-center gap-2 text-sm font-semibold text-admin-fg-strong mb-2">
              <Volume2 className="h-4 w-4 text-[var(--admin-primary)]" />
              Vocabulary Audio (TTS Configuration)
            </h3>
            <div className="grid gap-3 sm:grid-cols-2">
              <div className="flex items-start gap-3 rounded-admin-lg border border-[var(--admin-success-tint-strong)] bg-[var(--admin-success-tint)] p-3">
                <Volume2 className="mt-0.5 h-4 w-4 shrink-0 text-[var(--admin-success)]" />
                <div className="flex-1">
                  <p className="text-sm font-medium text-admin-fg-strong">Browser TTS</p>
                  <p className="text-xs text-admin-fg-muted mt-0.5">Free-tier fallback via Web Speech API. Works offline, instant playback. Active for all learners by default.</p>
                </div>
                <Badge variant="success" size="sm">Active</Badge>
              </div>
              <div className="flex items-start gap-3 rounded-admin-lg border border-[var(--admin-warning-tint-strong)] bg-[var(--admin-warning-tint)] p-3">
                <Crown className="mt-0.5 h-4 w-4 shrink-0 text-[var(--admin-warning)]" />
                <div className="flex-1">
                  <p className="text-sm font-medium text-admin-fg-strong">Premium TTS (ElevenLabs)</p>
                  <p className="text-xs text-admin-fg-muted mt-0.5">British clinical pronunciation via AI. Requires active subscription. Managed in AI Providers settings.</p>
                </div>
                <Badge variant="warning" size="sm">Premium</Badge>
              </div>
            </div>
          </CardContent>
        </Card>

        {recallSets.length > 0 && (
          <Card>
            <CardContent className="p-4 pt-4">
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">Recall set:</span>
                <button
                  type="button"
                  onClick={() => { setRecallSet(''); setPage(1); }}
                  className={`rounded-full border px-3 py-1 text-xs font-medium transition-colors ${
                    recallSet === ''
                      ? 'border-[var(--admin-primary)] bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]'
                      : 'border-admin-border bg-admin-bg-surface text-admin-fg-muted hover:border-admin-border-strong hover:text-admin-fg-strong'
                  }`}
                >
                  All
                </button>
                {recallSets.map((s) => (
                  <button
                    key={s.code}
                    type="button"
                    onClick={() => { setRecallSet(s.code); setPage(1); }}
                    title={`${s.description} — active ${s.active}, draft ${s.draft}, archived ${s.archived}`}
                    className={`rounded-full border px-3 py-1 text-xs font-medium transition-colors ${
                      recallSet === s.code
                        ? 'border-[var(--admin-primary)] bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]'
                        : 'border-admin-border bg-admin-bg-surface text-admin-fg-muted hover:border-admin-border-strong hover:text-admin-fg-strong'
                    }`}
                  >
                    {s.shortLabel} ({s.total})
                  </button>
                ))}
              </div>
            </CardContent>
          </Card>
        )}

        <Card>
          <CardContent className="p-0 pt-0">
            <AsyncStateWrapper status={pageStatus}>
              <DataTable
                columns={columns}
                data={rows}
                keyExtractor={(r) => r.id}
                selectable
                selectedKeys={selectedKeys}
                onSelectionChange={setSelectedKeys}
              />
              <BulkActionBar
                selectedCount={selectedKeys.size}
                totalCount={rows.length}
                onClearSelection={() => setSelectedKeys(new Set())}
                actions={[
                  {
                    key: 'delete',
                    label: 'Delete selected',
                    icon: <Trash2 className="h-4 w-4" aria-hidden="true" />,
                    variant: 'danger',
                    disabled: selectedRows.length === 0,
                    loading: bulkDeleting,
                    onClick: () => setConfirmBulkDelete(true),
                  },
                ]}
              />
              <div className="p-4">
                <Pagination
                  page={page}
                  pageSize={pageSize}
                  total={total}
                  onPageChange={setPage}
                  onPageSizeChange={setPageSize}
                  pageSizeOptions={[10, 25, 50, 100, 500]}
                  itemLabel="term"
                  itemLabelPlural="terms"
                />
              </div>
            </AsyncStateWrapper>

            {pageStatus === 'empty' && (
              <EmptyState
                illustration={<BookOpen />}
                title="No vocabulary terms yet"
                description="Start by adding a term, importing a CSV, or generating AI drafts for admin review."
                primaryAction={{ label: 'New term', onClick: () => router.push('/admin/content/vocabulary/new') }}
              />
            )}
            {pageStatus === 'loading' && (
              <div className="space-y-3 p-4">
                {Array.from({ length: 5 }).map((_, i) => (<Skeleton key={i} className="h-12 rounded-admin-lg" />))}
              </div>
            )}
          </CardContent>
        </Card>
      </AdminCatalogLayout>
    </>
  );
}
