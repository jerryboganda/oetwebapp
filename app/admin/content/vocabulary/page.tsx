'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { BookOpen, Plus, Upload, Sparkles, Trash2, Edit3, Volume2, CheckCircle2, Archive, FileText, RefreshCw } from 'lucide-react';
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
  bulkActivateAdminVocabularyItems,
  bulkArchiveAdminVocabularyItems,
  bulkDraftAdminVocabularyItems,
  fetchAdminVocabularyAudioProgress,
  resumeAdminVocabularyAudio,
  fetchAdminVocabularyCategories,
  fetchAdminVocabularyRecallSets,
  type AdminRecallSetSummary,
  type AdminVocabularyAudioProgress,
} from '@/lib/api';

type VocabRow = {
  id: string;
  term: string;
  definition: string;
  professionId: string | null;
  category: string;
  exampleSentence: string | null;
  status: 'draft' | 'active' | 'archived';
  hasAudio?: boolean;
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
  const [confirmBulkPublish, setConfirmBulkPublish] = useState(false);
  const [confirmBulkArchive, setConfirmBulkArchive] = useState(false);
  const [bulkDeleting, setBulkDeleting] = useState(false);
  const [bulkPublishing, setBulkPublishing] = useState(false);
  const [bulkArchiving, setBulkArchiving] = useState(false);
  const [bulkDrafting, setBulkDrafting] = useState(false);
  const [audioProgress, setAudioProgress] = useState<AdminVocabularyAudioProgress | null>(null);
  const [audioStalled, setAudioStalled] = useState(false);
  const audioPollingRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const lastPendingRef = useRef<number | null>(null);
  const stalledCountRef = useRef(0);
  const [refreshKey, setRefreshKey] = useState(0);

  // ── Audio progress polling ────────────────────────────────────────────
  const pollAudioProgress = useCallback(async () => {
    try {
      const progress = await fetchAdminVocabularyAudioProgress();
      setAudioProgress(progress);
      // Stop polling when all done
      if (progress.pending === 0 && audioPollingRef.current) {
        clearInterval(audioPollingRef.current);
        audioPollingRef.current = null;
        setAudioStalled(false);
      } else if (progress.pending > 0) {
        // Detect stall: pending unchanged for 3+ consecutive polls (12+ seconds)
        if (lastPendingRef.current === progress.pending) {
          stalledCountRef.current++;
          if (stalledCountRef.current >= 3) {
            setAudioStalled(true);
          }
        } else {
          stalledCountRef.current = 0;
          setAudioStalled(false);
        }
        lastPendingRef.current = progress.pending;
      }
    } catch { /* silent */ }
  }, []);

  useEffect(() => {
    pollAudioProgress();
    // Start polling every 4 seconds
    audioPollingRef.current = setInterval(pollAudioProgress, 4000);
    return () => {
      if (audioPollingRef.current) clearInterval(audioPollingRef.current);
    };
  }, [pollAudioProgress]);

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
  }, [search, category, profession, status, recallSet, page, pageSize, refreshKey]);

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

  async function handleBulkPublish() {
    const itemIds = Array.from(selectedKeys);
    if (itemIds.length === 0) return;
    setBulkPublishing(true);
    try {
      const result = await bulkActivateAdminVocabularyItems(itemIds);
      setConfirmBulkPublish(false);
      setRefreshKey(k => k + 1);
      setPage(p => p); // triggers re-fetch
      const failMsg = result.failed > 0 ? ` ${result.failed} failed publish gate.` : '';
      const skipMsg = result.skipped > 0 ? ` ${result.skipped} already active.` : '';
      setToast({
        variant: result.failed > 0 ? 'error' : 'success',
        message: `Published ${result.activated} terms.${skipMsg}${failMsg}${result.errors?.length ? ' ' + result.errors[0] : ''}`,
      });
    } catch {
      setToast({ variant: 'error', message: 'Failed to publish selected terms.' });
    } finally {
      setBulkPublishing(false);
    }
  }

  async function handleBulkArchive() {
    const itemIds = Array.from(selectedKeys);
    if (itemIds.length === 0) return;
    setBulkArchiving(true);
    try {
      const result = await bulkArchiveAdminVocabularyItems(itemIds);
      setConfirmBulkArchive(false);
      setSelectedKeys(new Set());
      setRows(prev => prev.map(r => itemIds.includes(r.id) ? { ...r, status: 'archived' as const } : r));
      setToast({ variant: 'success', message: `Archived ${result.archived} terms.${result.skipped > 0 ? ` ${result.skipped} already archived.` : ''}` });
    } catch {
      setToast({ variant: 'error', message: 'Failed to archive selected terms.' });
    } finally {
      setBulkArchiving(false);
    }
  }

  async function handleBulkDraft() {
    const itemIds = Array.from(selectedKeys);
    if (itemIds.length === 0) return;
    setBulkDrafting(true);
    try {
      const result = await bulkDraftAdminVocabularyItems(itemIds);
      setSelectedKeys(new Set());
      setRows(prev => prev.map(r => itemIds.includes(r.id) ? { ...r, status: 'draft' as const } : r));
      setToast({ variant: 'success', message: `Set ${result.drafted} terms to draft.${result.skipped > 0 ? ` ${result.skipped} already draft.` : ''}` });
    } catch {
      setToast({ variant: 'error', message: 'Failed to set terms to draft.' });
    } finally {
      setBulkDrafting(false);
    }
  }

  async function handleRegenerateAudio() {
    try {
      const result = await resumeAdminVocabularyAudio();
      const count = (result as { enqueued?: number }).enqueued ?? 0;
      setToast({ variant: 'success', message: `Resumed: queued ${count} terms for audio generation.` });
      setAudioStalled(false);
      stalledCountRef.current = 0;
      lastPendingRef.current = null;
      // Restart polling
      if (!audioPollingRef.current) {
        audioPollingRef.current = setInterval(pollAudioProgress, 4000);
      }
    } catch {
      setToast({ variant: 'error', message: 'Failed to resume audio generation.' });
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
      key: 'audio',
      header: 'Audio',
      render: (row) => (
        row.hasAudio
          ? <Volume2 className="h-4 w-4 text-[var(--admin-success)]" aria-label="Has audio" />
          : <Volume2 className="h-4 w-4 text-admin-fg-muted opacity-30" aria-label="No audio" />
      ),
    },
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
      <BulkActionConfirmModal
        open={confirmBulkPublish}
        title="Publish selected vocabulary terms"
        description={`Publish ${selectedKeys.size} selected ${selectedKeys.size === 1 ? 'term' : 'terms'}? Terms that don't meet the publish gate (missing example sentence, category, source provenance, or audio for medical terms) will be skipped with an error.`}
        confirmLabel="Publish selected"
        loading={bulkPublishing}
        onConfirm={handleBulkPublish}
        onClose={() => setConfirmBulkPublish(false)}
      />
      <BulkActionConfirmModal
        open={confirmBulkArchive}
        title="Archive selected vocabulary terms"
        description={`Archive ${selectedKeys.size} selected ${selectedKeys.size === 1 ? 'term' : 'terms'}? Archived terms won't appear in learner practice sets.`}
        confirmLabel="Archive selected"
        loading={bulkArchiving}
        onConfirm={handleBulkArchive}
        onClose={() => setConfirmBulkArchive(false)}
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
        {/* Audio Generation Progress */}
        {audioProgress && (
          <Card>
            <CardContent className="p-4 pt-4">
              <div className="flex items-center justify-between mb-3">
                <h3 className="flex items-center gap-2 text-sm font-semibold text-admin-fg-strong">
                  <Volume2 className="h-4 w-4 text-[var(--admin-primary)]" />
                  Audio Generation Progress
                </h3>
                <div className="flex items-center gap-2">
                  {audioProgress.pending > 0 && audioStalled && (
                    <Badge variant="danger" size="sm">
                      Stalled
                    </Badge>
                  )}
                  {audioProgress.pending > 0 && !audioStalled && (
                    <Badge variant="warning" size="sm">
                      {audioProgress.pending} pending
                    </Badge>
                  )}
                  {audioProgress.pending === 0 && (
                    <Badge variant="success" size="sm">
                      All complete
                    </Badge>
                  )}
                  {audioProgress.pending > 0 && (
                    <Button variant="primary" size="sm" onClick={handleRegenerateAudio}>
                      <RefreshCw className="mr-1 h-3.5 w-3.5" />Resume generation
                    </Button>
                  )}
                </div>
              </div>
              {/* Progress bar */}
              <div className="space-y-2">
                <div className="flex items-center justify-between text-xs text-admin-fg-muted">
                  <span>{audioProgress.withAudio} of {audioProgress.total} terms have audio</span>
                  <span className="font-mono font-semibold text-admin-fg-strong">{audioProgress.percentComplete}%</span>
                </div>
                <div className="h-3 w-full overflow-hidden rounded-full bg-[var(--admin-bg-inset)]">
                  <div
                    className="h-full rounded-full transition-[width,background-color] duration-500 ease-out"
                    style={{
                      width: `${audioProgress.percentComplete}%`,
                      background: audioProgress.percentComplete === 100
                        ? 'var(--admin-success)'
                        : audioStalled
                          ? 'var(--admin-warning)'
                          : 'var(--admin-primary)',
                    }}
                  />
                </div>
                {audioProgress.pending > 0 && audioStalled && (
                  <p className="text-xs text-[var(--admin-danger)]">
                    Generation stalled. {audioProgress.pending} terms still need audio. Click &quot;Resume generation&quot; to continue.
                  </p>
                )}
                {audioProgress.pending > 0 && !audioStalled && (
                  <p className="text-xs text-admin-fg-muted animate-pulse">
                    Audio generation in progress. ElevenLabs TTS is processing {audioProgress.pending} remaining terms...
                  </p>
                )}
              </div>
            </CardContent>
          </Card>
        )}

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
                    title={`${s.description}: active ${s.active}, draft ${s.draft}, archived ${s.archived}`}
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
                totalCount={total}
                onClearSelection={() => setSelectedKeys(new Set())}
                onSelectAll={() => {
                  // Select all on current page
                  setSelectedKeys(new Set(rows.map(r => r.id)));
                }}
                actions={[
                  {
                    key: 'publish',
                    label: 'Publish',
                    icon: <CheckCircle2 className="h-4 w-4" aria-hidden="true" />,
                    variant: 'primary',
                    disabled: selectedRows.length === 0,
                    loading: bulkPublishing,
                    onClick: () => setConfirmBulkPublish(true),
                  },
                  {
                    key: 'draft',
                    label: 'Set to Draft',
                    icon: <FileText className="h-4 w-4" aria-hidden="true" />,
                    variant: 'secondary',
                    disabled: selectedRows.length === 0,
                    loading: bulkDrafting,
                    onClick: handleBulkDraft,
                  },
                  {
                    key: 'archive',
                    label: 'Archive',
                    icon: <Archive className="h-4 w-4" aria-hidden="true" />,
                    variant: 'secondary',
                    disabled: selectedRows.length === 0,
                    loading: bulkArchiving,
                    onClick: () => setConfirmBulkArchive(true),
                  },
                  {
                    key: 'delete',
                    label: 'Delete',
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
