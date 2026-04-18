'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { FileText, Plus, Archive as ArchiveIcon } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { DEFAULT_CONTENT_SOURCE_PROVENANCE } from '@/lib/content-upload-defaults';
import {
  archiveContentPaper,
  createContentPaper,
  listContentPapers,
  type ContentPaperDto,
} from '@/lib/content-upload-api';

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const SUBTESTS = [
  { value: '', label: 'All subtests' },
  { value: 'listening', label: 'Listening' },
  { value: 'reading', label: 'Reading' },
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
];

const STATUSES = [
  { value: '', label: 'Any status' },
  { value: 'Draft', label: 'Draft' },
  { value: 'InReview', label: 'In review' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

const PAGE_SIZE = 50;

export default function ContentPapersListPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<ContentPaperDto[]>([]);
  const [toast, setToast] = useState<ToastState>(null);

  const [filterSubtest, setFilterSubtest] = useState('');
  const [filterStatus, setFilterStatus] = useState('');
  const [searchInput, setSearchInput] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [hasMore, setHasMore] = useState(false);

  const [showCreate, setShowCreate] = useState(false);
  const [newSubtest, setNewSubtest] = useState<'listening' | 'reading' | 'writing' | 'speaking'>('listening');
  const [newTitle, setNewTitle] = useState('');
  const [newApplyAll, setNewApplyAll] = useState(true);
  const [newProfession, setNewProfession] = useState('medicine');
  const [newProvenance, setNewProvenance] = useState(DEFAULT_CONTENT_SOURCE_PROVENANCE);
  const [saving, setSaving] = useState(false);
  const requestSeqRef = useRef(0);
  const skipNextLoadRef = useRef(false);

  useEffect(() => {
    const timeout = window.setTimeout(() => {
      setSearch(searchInput.trim());
    }, 250);

    return () => window.clearTimeout(timeout);
  }, [searchInput]);

  const load = useCallback(async (pageNumber: number) => {
    const requestSeq = ++requestSeqRef.current;
    setStatus('loading');
    try {
      const data = await listContentPapers({
        subtest: filterSubtest || undefined,
        status: filterStatus || undefined,
        search: search || undefined,
        page: pageNumber,
        pageSize: PAGE_SIZE,
      });
      if (requestSeq !== requestSeqRef.current) return;
      setRows(data);
      setHasMore(data.length === PAGE_SIZE);
      setStatus('success');
    } catch (e) {
      if (requestSeq !== requestSeqRef.current) return;
      setStatus('error');
      setToast({ variant: 'error', message: `Failed to load papers: ${(e as Error).message}` });
    }
  }, [filterSubtest, filterStatus, search]);

  useEffect(() => {
    skipNextLoadRef.current = true;
    setStatus('loading');
    setPage(1);
    setHasMore(false);
  }, [filterSubtest, filterStatus, search]);

  useEffect(() => {
    if (skipNextLoadRef.current && page !== 1) return;
    skipNextLoadRef.current = false;
    void load(page);
  }, [load, page]);

  const createNow = async () => {
    setSaving(true);
    try {
      const paper = await createContentPaper({
        subtestCode: newSubtest,
        title: newTitle.trim(),
        appliesToAllProfessions: newApplyAll,
        professionId: newApplyAll ? null : newProfession,
        estimatedDurationMinutes: newSubtest === 'listening' ? 40 : newSubtest === 'reading' ? 60 : 45,
        priority: 0,
        sourceProvenance: newProvenance.trim() || DEFAULT_CONTENT_SOURCE_PROVENANCE,
      });
      setToast({ variant: 'success', message: `Created paper "${paper.title}"` });
      setShowCreate(false);
      setNewTitle('');
      setNewProvenance(DEFAULT_CONTENT_SOURCE_PROVENANCE);
      await load(page);
    } catch (e) {
      const detail = (e as Error & { detail?: { error?: string } }).detail;
      setToast({ variant: 'error', message: detail?.error ?? (e as Error).message });
    } finally { setSaving(false); }
  };

  const archive = useCallback(async (id: string) => {
    try {
      await archiveContentPaper(id);
      setToast({ variant: 'success', message: 'Paper archived.' });
      await load(page);
    } catch (e) {
      setToast({ variant: 'error', message: `Archive failed: ${(e as Error).message}` });
    }
  }, [load, page]);

  const columns: Column<ContentPaperDto>[] = useMemo(() => [
    { key: 'st', header: 'Subtest', render: (p) => <Badge variant="info">{p.subtestCode}</Badge> },
    {
      key: 't', header: 'Title', render: (p) => (
        <Link href={`/admin/content-papers/${p.id}`} className="font-medium hover:text-primary">
          {p.title}
        </Link>
      ),
    },
    { key: 'sl', header: 'Slug', render: (p) => <span className="font-mono text-xs">{p.slug}</span> },
    {
      key: 'sc', header: 'Scope', render: (p) => p.appliesToAllProfessions
        ? <Badge variant="muted">All professions</Badge>
        : <Badge variant="info">{p.professionId ?? '—'}</Badge>,
    },
    { key: 'cl', header: 'Card / Letter', render: (p) => p.cardType ?? p.letterType ?? '—' },
    {
      key: 'st2', header: 'Status', render: (p) => <Badge variant={
        p.status === 'Published' ? 'success'
          : p.status === 'Archived' ? 'muted'
          : p.status === 'InReview' ? 'warning' : 'default'
      }>{p.status}</Badge>,
    },
    { key: 'u', header: 'Updated', render: (p) => new Date(p.updatedAt).toLocaleString() },
    {
      key: 'a', header: 'Actions', render: (p) => (
        <div className="flex gap-2">
          <Link href={`/admin/content-papers/${p.id}`}>
            <Button variant="ghost" size="sm">Edit</Button>
          </Link>
          {p.status !== 'Archived' && (
            <Button variant="ghost" size="sm" onClick={() => void archive(p.id)}>
              <ArchiveIcon className="w-4 h-4" /> Archive
            </Button>
          )}
        </div>
      ),
    },
  ], [archive]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <p className="text-sm text-muted">Admin access required.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        icon={<FileText className="w-6 h-6" />}
        title="Content Papers"
        description="Author and publish Listening, Reading, Writing, and Speaking papers. Uploaded files are stored content-addressed and dedup automatically."
      />

      <AdminRoutePanel title="Filters">
        <div className="grid grid-cols-1 md:grid-cols-4 gap-3">
          <Select label="Subtest" value={filterSubtest} onChange={(e) => setFilterSubtest(e.target.value)} options={SUBTESTS} />
          <Select label="Status" value={filterStatus} onChange={(e) => setFilterStatus(e.target.value)} options={STATUSES} />
          <Input label="Search" value={searchInput} onChange={(e) => setSearchInput(e.target.value)} placeholder="Title or slug" />
          <div className="flex items-end gap-2">
            <Button variant="primary" onClick={() => {
              setNewProvenance(DEFAULT_CONTENT_SOURCE_PROVENANCE);
              setShowCreate(true);
            }}>
              <Plus className="w-4 h-4 mr-1" /> New paper
            </Button>
            <Link href="/admin/content-papers/import">
              <Button variant="ghost">Bulk import</Button>
            </Link>
          </div>
        </div>
      </AdminRoutePanel>

      <AsyncStateWrapper status={status}>
        <AdminRoutePanel title={`Papers (${rows.length})`}>
          <div className="space-y-4">
            <DataTable data={rows} columns={columns} keyExtractor={(p) => p.id} />
            <div className="flex flex-col gap-3 border-t border-gray-100 pt-4 sm:flex-row sm:items-center sm:justify-between">
              <p className="text-sm text-muted">
                {rows.length === 0 ? 'No papers found.' : `Showing ${rows.length} papers on page ${page}.`}
              </p>
              <div className="flex items-center gap-2">
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => setPage((current) => Math.max(1, current - 1))}
                  disabled={page === 1 || status === 'loading'}
                >
                  Previous
                </Button>
                <span className="text-sm text-muted">Page {page}</span>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => setPage((current) => current + 1)}
                  disabled={!hasMore || status === 'loading'}
                >
                  Next
                </Button>
              </div>
            </div>
          </div>
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <Modal open={showCreate} onClose={() => setShowCreate(false)} title="Create paper">
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          <Select
            label="Subtest"
            value={newSubtest}
            onChange={(e) => setNewSubtest(e.target.value as typeof newSubtest)}
            options={[
              { value: 'listening', label: 'Listening' },
              { value: 'reading', label: 'Reading' },
              { value: 'writing', label: 'Writing' },
              { value: 'speaking', label: 'Speaking' },
            ]}
          />
          <Input label="Title" value={newTitle} onChange={(e) => setNewTitle(e.target.value)} placeholder="e.g. Listening Sample 1" />
          <label className="flex items-center gap-2 md:col-span-2">
            <input type="checkbox" checked={newApplyAll} onChange={(e) => setNewApplyAll(e.target.checked)} />
            Applies to all professions
          </label>
          {!newApplyAll && (
            <Input label="Profession ID" value={newProfession} onChange={(e) => setNewProfession(e.target.value)} />
          )}
          <div className="md:col-span-2">
            <Input
              label="Source provenance"
              value={newProvenance}
              onChange={(e) => setNewProvenance(e.target.value)}
              placeholder={DEFAULT_CONTENT_SOURCE_PROVENANCE}
            />
          </div>
        </div>
        <div className="flex gap-3 mt-4 justify-end">
          <Button variant="ghost" onClick={() => setShowCreate(false)}>Cancel</Button>
          <Button variant="primary" onClick={() => void createNow()} loading={saving} disabled={!newTitle.trim()}>Create</Button>
        </div>
      </Modal>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
