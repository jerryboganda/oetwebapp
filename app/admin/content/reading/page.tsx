'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Plus, Search, BookOpen, FileCheck2, ArrowRight, Trash2 } from 'lucide-react';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable as LegacyDataTable, type Column } from '@/components/ui/data-table';
import { Badge } from '@/components/ui/badge';
import { Button as LegacyButton } from '@/components/ui/button';
import { Button } from '@/components/admin/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { Pagination } from '@/components/ui/pagination';
import { archiveContentPaper, listContentPapers, type ContentPaperDto, type ContentStatus } from '@/lib/content-upload-api';
import type { AdminContentRow } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

function toQueryStatus(value: string): ContentStatus | undefined {
  if (value === 'draft') return 'Draft';
  if (value === 'published') return 'Published';
  if (value === 'archived') return 'Archived';
  return undefined;
}

function toRowStatus(status: ContentStatus): AdminContentRow['status'] {
  if (status === 'Published') return 'published';
  if (status === 'Archived') return 'archived';
  return 'draft';
}

function toContentRow(paper: ContentPaperDto): AdminContentRow {
  return {
    id: paper.id,
    title: paper.title,
    type: paper.subtestCode,
    profession: paper.appliesToAllProfessions ? 'All' : paper.professionId ?? 'All',
    status: toRowStatus(paper.status),
    updatedAt: paper.updatedAt,
    author: 'System',
    revisionCount: paper.publishedRevisionId ? 1 : 0,
  };
}

export default function AdminReadingPapersPage() {
  const router = useRouter();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<AdminContentRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<string>('');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    let cancelled = false;
    const t = setTimeout(async () => {
      setPageStatus('loading');
      try {
        const papers = await listContentPapers({
          page: 1,
          pageSize: 500,
          subtest: 'reading',
          search: search.trim() || undefined,
          status: toQueryStatus(status),
        });
        if (cancelled) return;
        const resultRows = papers.map(toContentRow);
        const pageStart = (page - 1) * pageSize;
        setRows(resultRows.slice(pageStart, pageStart + pageSize));
        setTotal(resultRows.length);
        setPageStatus(resultRows.length > 0 ? 'success' : 'empty');
      } catch {
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Failed to load reading papers.' });
        }
      }
    }, 300);
    return () => { cancelled = true; clearTimeout(t); };
  }, [search, status, page, pageSize]);

  function formatDate(iso: string): string {
    try {
      return new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' });
    } catch {
      return iso;
    }
  }

  const [archiveTarget, setArchiveTarget] = useState<AdminContentRow | null>(null);

  async function handleArchive(row: AdminContentRow) {
    try {
      await archiveContentPaper(row.id);
      setRows((prev) => prev.filter((r) => r.id !== row.id));
      setTotal((prev) => prev - 1);
      setToast({ variant: 'success', message: `"${row.title}" archived.` });
    } catch {
      setToast({ variant: 'error', message: 'Archive failed.' });
    } finally {
      setArchiveTarget(null);
    }
  }

  const columns = useMemo<Column<AdminContentRow>[]>(() => [
    {
      key: 'title',
      header: 'Title',
      render: (row) => (
        <Link
          href={`/admin/content/reading/${row.id}`}
          className="font-semibold text-admin-fg-strong hover:text-[var(--admin-primary)] transition-colors line-clamp-1"
        >
          {row.title}
        </Link>
      ),
    },
    {
      key: 'profession',
      header: 'Profession',
      render: (row) => (
        <span className="text-sm capitalize text-admin-fg-default">{row.profession || 'All'}</span>
      ),
      hideOnMobile: true,
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge variant={row.status === 'published' ? 'success' : row.status === 'draft' ? 'muted' : 'warning'}>
          {row.status}
        </Badge>
      ),
    },
    {
      key: 'author',
      header: 'Author',
      render: (row) => <span className="text-sm text-admin-fg-muted">{row.author}</span>,
      hideOnMobile: true,
    },
    {
      key: 'updatedAt',
      header: 'Updated',
      render: (row) => (
        <span className="text-sm text-admin-fg-muted">{formatDate(row.updatedAt)}</span>
      ),
      hideOnMobile: true,
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div className="flex items-center justify-end gap-2">
          <Link href={`/admin/content/reading/${row.id}`} aria-label={`Edit ${row.title}`}>
            <LegacyButton variant="ghost" size="sm">
              <ArrowRight className="h-4 w-4" />
            </LegacyButton>
          </Link>
          <LegacyButton
            variant="ghost"
            size="sm"
            aria-label={`Archive ${row.title}`}
            onClick={() => setArchiveTarget(row)}
          >
            <Trash2 className="h-4 w-4 text-[var(--admin-danger)]" />
          </LegacyButton>
        </div>
      ),
    },
  ], []);

  return (
    <>
      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}

      {/* Archive Confirm Dialog */}
      {archiveTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
          <div className="bg-admin-bg-surface rounded-admin-lg p-6 shadow-admin-lg max-w-sm w-full mx-4 border border-admin-border">
            <h3 className="text-base font-semibold text-admin-fg-strong mb-2">Archive Paper?</h3>
            <p className="text-sm text-admin-fg-muted mb-4">
              &ldquo;{archiveTarget.title}&rdquo; will be archived. This can be undone by an admin later.
            </p>
            <div className="flex justify-end gap-2">
              <Button variant="ghost" size="sm" onClick={() => setArchiveTarget(null)}>
                Cancel
              </Button>
              <Button variant="destructive" size="sm" onClick={() => handleArchive(archiveTarget)}>
                Archive
              </Button>
            </div>
          </div>
        </div>
      )}

      <AdminTableLayout
        title="Reading Papers"
        description="Manage OET reading papers. Author, review, and publish Part A / B / C papers with exact-match grading."
        eyebrow="CMS"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Reading' },
        ]}
        actions={
          <Button asChild size="sm" startIcon={<Plus className="h-4 w-4" />}>
            <Link href="/admin/content/reading/new">New Paper</Link>
          </Button>
        }
        banner={
          <div className="grid gap-3 sm:grid-cols-3 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-3 sm:p-4 shadow-admin-sm">
            <div className="relative sm:col-span-2">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-admin-fg-muted pointer-events-none" />
              <Input
                placeholder="Search papers by title…"
                value={search}
                onChange={(e) => { setSearch(e.target.value); setPage(1); }}
                className="pl-9"
              />
            </div>
            <Select
              label="Status"
              value={status}
              onChange={(e) => { setStatus(e.target.value); setPage(1); }}
              options={[
                { value: '', label: 'All statuses' },
                { value: 'draft', label: 'Draft' },
                { value: 'published', label: 'Published' },
                { value: 'archived', label: 'Archived' },
              ]}
            />
          </div>
        }
      >
        <div className="p-4 sm:p-5">
          <AsyncStateWrapper
            status={pageStatus}
            emptyContent={
              <EmptyState
                icon={<FileCheck2 className="h-6 w-6" />}
                title="No reading papers found"
                description="Create your first reading paper to start building the content library."
                action={{ label: 'Create Paper', onClick: () => router.push('/admin/content/reading/new') }}
              />
            }
          >
            <LegacyDataTable
              columns={columns}
              data={rows}
              keyExtractor={(r) => r.id}
            />
            <Pagination
              page={page}
              pageSize={pageSize}
              total={total}
              onPageChange={setPage}
              onPageSizeChange={setPageSize}
              pageSizeOptions={[10, 25, 50, 100]}
              itemLabel="paper"
              itemLabelPlural="papers"
            />
          </AsyncStateWrapper>
        </div>
      </AdminTableLayout>
    </>
  );
}

// Silence unused-import lint after icon prop is now passed as element rather than ref.
void BookOpen;
