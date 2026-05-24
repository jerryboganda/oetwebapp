'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Plus, Search, BookOpen, FileCheck2, ArrowRight, Trash2 } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { EmptyState } from '@/components/ui/empty-error';
import { Pagination } from '@/components/ui/pagination';
import { getAdminContentLibraryData } from '@/lib/admin';
import { archiveContentPaper } from '@/lib/content-upload-api';
import type { AdminContentRow } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

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
        const result = await getAdminContentLibraryData({
          page,
          pageSize,
          search: search.trim() || undefined,
          type: 'reading_task',
          status: status || undefined,
        });
        if (cancelled) return;
        setRows(result.items);
        setTotal(result.total);
        setPageStatus(result.items.length > 0 ? 'success' : 'empty');
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
          className="font-semibold text-navy hover:text-primary transition-colors line-clamp-1"
        >
          {row.title}
        </Link>
      ),
    },
    {
      key: 'profession',
      header: 'Profession',
      render: (row) => (
        <span className="text-sm capitalize">{row.profession || 'All'}</span>
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
      render: (row) => <span className="text-sm text-muted-foreground">{row.author}</span>,
      hideOnMobile: true,
    },
    {
      key: 'updatedAt',
      header: 'Updated',
      render: (row) => (
        <span className="text-sm text-muted-foreground">{formatDate(row.updatedAt)}</span>
      ),
      hideOnMobile: true,
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div className="flex items-center justify-end gap-2">
          <Link href={`/admin/content/reading/${row.id}`} aria-label={`Edit ${row.title}`}>
            <Button variant="ghost" size="sm">
              <ArrowRight className="h-4 w-4" />
            </Button>
          </Link>
          <Button
            variant="ghost"
            size="sm"
            aria-label={`Archive ${row.title}`}
            onClick={() => setArchiveTarget(row)}
          >
            <Trash2 className="h-4 w-4 text-danger" />
          </Button>
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
          <div className="bg-surface rounded-lg p-6 shadow-xl max-w-sm w-full mx-4 border border-border">
            <h3 className="text-base font-semibold text-foreground mb-2">Archive Paper?</h3>
            <p className="text-sm text-muted-foreground mb-4">
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

      <AdminRouteWorkspace>
        <AdminRouteSectionHeader
          eyebrow="CMS"
          title="Reading Papers"
          description="Manage OET reading papers. Author, review, and publish Part A / B / C papers with exact-match grading."
          icon={BookOpen}
          actions={
            <Button variant="primary" size="sm" asChild>
              <Link href="/admin/content/reading/new">
                <Plus className="mr-1.5 h-4 w-4" />New Paper
              </Link>
            </Button>
          }
        />

        <AdminRoutePanel>
          <div className="mb-4 grid gap-3 sm:grid-cols-3">
            <div className="relative sm:col-span-2">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground pointer-events-none" />
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
            <DataTable
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
        </AdminRoutePanel>
      </AdminRouteWorkspace>
    </>
  );
}
