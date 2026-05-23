'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { BookOpen, Plus, Upload, Sparkles, Trash2, Edit3, Volume2, Crown } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
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
      const result = await deleteAdminVocabularyItems(itemIds);
      const removed = new Set(itemIds);
      setRows(prev => prev.filter(row => !removed.has(row.id)));
      setTotal(prev => Math.max(0, prev - result.deleted - result.archived));
      setSelectedKeys(new Set());
      setConfirmBulkDelete(false);
      const archivedMessage = result.archived > 0 ? ` ${result.archived} archived because learners reference them.` : '';
      const failedMessage = result.failed > 0 ? ` ${result.failed} failed.` : '';
      setToast({
        variant: result.failed > 0 ? 'error' : 'success',
        message: `Bulk delete complete: ${result.deleted} deleted.${archivedMessage}${failedMessage}`,
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
          <span className="font-semibold text-navy">{row.term}</span>
          <span className="text-xs text-muted line-clamp-1">{row.definition}</span>
        </div>
      ),
    },
    { key: 'category', header: 'Category', render: (row) => <span className="capitalize text-sm">{row.category.replace(/_/g, ' ')}</span> },
    { key: 'profession', header: 'Profession', render: (row) => <span className="text-sm capitalize">{row.professionId ?? '—'}</span>, hideOnMobile: true },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge variant={row.status === 'active' ? 'success' : row.status === 'draft' ? 'warning' : 'muted'}>
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
            <Edit3 className="h-4 w-4 text-muted hover:text-primary" />
          </Link>
          <button onClick={() => handleDelete(row.id, row.term)} aria-label={`Delete ${row.term}`}>
            <Trash2 className="h-4 w-4 text-muted hover:text-danger" />
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
      <AdminRouteWorkspace>
        <AdminRouteSectionHeader
          eyebrow="CMS"
          title="Vocabulary"
          description="Create, edit, import, and AI-draft OET vocabulary terms. All mutations are audit-logged."
          icon={BookOpen}
          actions={
            <div className="flex flex-wrap gap-2">
              <Button variant="secondary" size="sm" asChild>
<Link href="/admin/content/vocabulary/import"><Upload className="mr-1.5 h-4 w-4" />Import CSV</Link>
</Button>
              <Button variant="secondary" size="sm" asChild>
<Link href="/admin/content/vocabulary/ai-draft"><Sparkles className="mr-1.5 h-4 w-4" />AI draft</Link>
</Button>
              <Button variant="primary" size="sm" asChild>
<Link href="/admin/content/vocabulary/new"><Plus className="mr-1.5 h-4 w-4" />New term</Link>
</Button>
            </div>
          }
        />

        {/* TTS Configuration Info */}
        <div className="rounded-xl border border-border bg-surface/50 p-4 mb-1">
          <h3 className="flex items-center gap-2 text-sm font-semibold text-navy mb-2">
            <Volume2 className="h-4 w-4 text-primary" />
            Vocabulary Audio (TTS Configuration)
          </h3>
          <div className="grid gap-3 sm:grid-cols-2">
            <div className="flex items-start gap-3 rounded-lg border border-success/30 bg-success/5 p-3">
              <Volume2 className="mt-0.5 h-4 w-4 shrink-0 text-success" />
              <div>
                <p className="text-sm font-medium text-navy">Browser TTS</p>
                <p className="text-xs text-muted mt-0.5">Free-tier fallback via Web Speech API. Works offline, instant playback. Active for all learners by default.</p>
              </div>
              <Badge variant="success">Active</Badge>
            </div>
            <div className="flex items-start gap-3 rounded-lg border border-warning/30 bg-warning/5 p-3">
              <Crown className="mt-0.5 h-4 w-4 shrink-0 text-warning" />
              <div>
                <p className="text-sm font-medium text-navy">Premium TTS (ElevenLabs)</p>
                <p className="text-xs text-muted mt-0.5">British clinical pronunciation via AI. Requires active subscription. Managed in AI Providers settings.</p>
              </div>
              <Badge variant="warning">Premium</Badge>
            </div>
          </div>
        </div>

        <AdminRoutePanel>
          <div className="mb-4 grid gap-3 sm:grid-cols-4">
            <Input
              placeholder="Search term or definition…"
              value={search}
              onChange={(e) => { setSearch(e.target.value); setPage(1); }}
            />
            <select
              value={category}
              onChange={(e) => { setCategory(e.target.value); setPage(1); }}
              className="rounded-xl border border-border bg-surface px-3 py-2 text-sm"
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
              className="rounded-xl border border-border bg-surface px-3 py-2 text-sm"
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
              className="rounded-xl border border-border bg-surface px-3 py-2 text-sm"
            >
              <option value="">All statuses</option>
              <option value="active">Active</option>
              <option value="draft">Draft</option>
              <option value="archived">Archived</option>
            </select>
          </div>

          {recallSets.length > 0 && (
            <div className="mb-4 flex flex-wrap items-center gap-2">
              <span className="text-xs font-semibold uppercase tracking-wide text-muted">Recall set:</span>
              <button
                type="button"
                onClick={() => { setRecallSet(''); setPage(1); }}
                className={`rounded-full border px-3 py-1 text-xs font-medium transition-colors ${
                  recallSet === ''
                    ? 'border-primary bg-primary/10 text-primary'
                    : 'border-border bg-surface text-muted hover:border-border-hover hover:text-navy'
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
                      ? 'border-primary bg-primary/10 text-primary'
                      : 'border-border bg-surface text-muted hover:border-border-hover hover:text-navy'
                  }`}
                >
                  {s.shortLabel} ({s.total})
                </button>
              ))}
            </div>
          )}

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
          </AsyncStateWrapper>

          {pageStatus === 'empty' && (
            <EmptyState
              icon={<BookOpen className="h-6 w-6" />}
              title="No vocabulary terms yet"
              description="Start by adding a term, importing a CSV, or generating AI drafts for admin review."
              action={{ label: 'New term', onClick: () => router.push('/admin/content/vocabulary/new') }}
            />
          )}
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    </>
  );
}
