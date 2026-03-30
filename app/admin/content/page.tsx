'use client';

import { useDeferredValue, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ChevronLeft, ChevronRight, FileText, History, Plus, Search } from 'lucide-react';
import { AdminPageHeader, AdminSectionPanel } from '@/components/domain/admin-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getAdminContentLibraryData } from '@/lib/admin';
import type { AdminContentRow } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
const PAGE_SIZE = 25;

export default function AdminContentLibraryPage() {
  const router = useRouter();
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<AdminContentRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [searchQuery, setSearchQuery] = useState('');
  const [filters, setFilters] = useState<Record<string, string[]>>({});
  const [reloadNonce, setReloadNonce] = useState(0);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const deferredSearchQuery = useDeferredValue(searchQuery.trim());
  const selectedType = filters.type?.[0];
  const selectedProfession = filters.profession?.[0];
  const selectedStatus = filters.status?.[0];

  useEffect(() => {
    let cancelled = false;

    async function load() {
      setPageStatus('loading');
      try {
        const result = await getAdminContentLibraryData({
          page,
          pageSize: PAGE_SIZE,
          search: deferredSearchQuery || undefined,
          type: selectedType,
          profession: selectedProfession,
          status: selectedStatus,
        });
        if (cancelled) return;
        if (result.items.length === 0 && result.total > 0 && page > 1) {
          setPage((current) => Math.max(1, current - 1));
          return;
        }
        setRows(result.items);
        setTotal(result.total);
        setPageStatus(result.items.length > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Failed to load content library.' });
        }
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [deferredSearchQuery, page, reloadNonce, selectedProfession, selectedStatus, selectedType]);

  const filterGroups: FilterGroup[] = [
    {
      id: 'type',
      label: 'Type',
      options: [
        { id: 'writing_task', label: 'Writing' },
        { id: 'speaking_task', label: 'Speaking' },
        { id: 'reading_task', label: 'Reading' },
        { id: 'listening_task', label: 'Listening' },
      ],
    },
    {
      id: 'profession',
      label: 'Profession',
      options: [
        { id: 'medicine', label: 'Medicine' },
        { id: 'nursing', label: 'Nursing' },
        { id: 'dentistry', label: 'Dentistry' },
        { id: 'pharmacy', label: 'Pharmacy' },
        { id: 'physiotherapy', label: 'Physiotherapy' },
      ],
    },
    {
      id: 'status',
      label: 'Status',
      options: [
        { id: 'draft', label: 'Draft' },
        { id: 'published', label: 'Published' },
        { id: 'archived', label: 'Archived' },
      ],
    },
  ];

  const columns: Column<AdminContentRow>[] = [
    { key: 'id', header: 'ID', render: (row) => <span className="font-mono text-xs">{row.id}</span> },
    {
      key: 'title',
      header: 'Title',
      render: (row) => (
        <button className="text-left font-medium text-blue-600 hover:underline" onClick={() => router.push(`/admin/content/${row.id}`)}>
          {row.title}
        </button>
      ),
    },
    { key: 'type', header: 'Type', render: (row) => <span className="capitalize">{row.type.replace('_', ' ')}</span> },
    { key: 'profession', header: 'Profession', render: (row) => <span className="capitalize">{row.profession || 'All'}</span> },
    { key: 'author', header: 'Author', render: (row) => <span className="text-slate-500">{row.author}</span> },
    {
      key: 'status',
      header: 'Status',
      render: (row) => (
        <Badge variant={row.status === 'published' ? 'success' : row.status === 'archived' ? 'muted' : 'warning'}>
          {row.status}
        </Badge>
      ),
    },
    {
      key: 'revisions',
      header: 'Revisions',
      render: (row) => (
        <button
          className="flex items-center gap-1 text-xs text-slate-500 hover:text-slate-900"
          onClick={() => router.push(`/admin/content/${row.id}/revisions`)}
        >
          <History className="h-3.5 w-3.5" />
          {row.revisionCount}
        </button>
      ),
    },
    { key: 'updatedAt', header: 'Updated', render: (row) => <span className="text-xs text-slate-500">{new Date(row.updatedAt).toLocaleString()}</span> },
  ];

  function handleFilterChange(groupId: string, optionId: string) {
    setPage(1);
    setFilters((current) => {
      return {
        ...current,
        [groupId]: current[groupId]?.[0] === optionId ? [] : [optionId],
      };
    });
  }

  const hasPreviousPage = page > 1;
  const hasNextPage = page * PAGE_SIZE < total;
  const visibleStart = total === 0 ? 0 : (page - 1) * PAGE_SIZE + 1;
  const visibleEnd = total === 0 ? 0 : Math.min(page * PAGE_SIZE, total);

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <div className="max-w-7xl space-y-6">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminPageHeader
        title="Content Library"
        description="Manage the live OET content catalog, editorial revisions, and publish-ready practice material."
        actions={
          <Button onClick={() => router.push('/admin/content/new')} className="gap-2">
            <Plus className="h-4 w-4" /> New Content
          </Button>
        }
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => setReloadNonce((current) => current + 1)}
        emptyContent={
          <EmptyState
            icon={<FileText className="h-10 w-10 text-slate-400" />}
            title="No content items yet"
            description="Create the first publishable content item to start the library."
            action={{ label: 'Create Content', onClick: () => router.push('/admin/content/new') }}
          />
        }
      >
        <AdminSectionPanel title="Filters" description="Filter by subtest, profession, or publication state.">
          <div className="max-w-sm">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
              <Input
                placeholder="Search title, ID, or author"
                value={searchQuery}
                onChange={(event) => {
                  setSearchQuery(event.target.value);
                  setPage(1);
                }}
                className="pl-9"
              />
            </div>
          </div>
          <FilterBar
            groups={filterGroups}
            selected={filters}
            onChange={handleFilterChange}
            onClear={() => {
              setFilters({});
              setSearchQuery('');
              setPage(1);
            }}
          />
        </AdminSectionPanel>

        <AdminSectionPanel
          title="Content Items"
          description={`Every visible row is backed by the live admin content endpoint. Showing ${visibleStart}-${visibleEnd} of ${total}.`}
        >
          {rows.length === 0 ? (
            <EmptyState
              icon={<Search className="h-10 w-10 text-slate-400" />}
              title="No matching content"
              description="Adjust your search or filters to find a content item."
              action={{
                label: 'Clear Filters',
                onClick: () => {
                  setFilters({});
                  setSearchQuery('');
                  setPage(1);
                },
              }}
            />
          ) : (
            <div className="space-y-4">
              <DataTable columns={columns} data={rows} keyExtractor={(row) => row.id} />
              <div className="flex flex-col gap-3 border-t border-slate-100 pt-4 text-sm text-slate-500 sm:flex-row sm:items-center sm:justify-between">
                <p>
                  Page {page} of {Math.max(1, Math.ceil(total / PAGE_SIZE))}
                </p>
                <div className="flex items-center gap-2">
                  <Button variant="outline" size="sm" onClick={() => setPage((current) => Math.max(1, current - 1))} disabled={!hasPreviousPage}>
                    <ChevronLeft className="h-4 w-4" /> Previous
                  </Button>
                  <Button variant="outline" size="sm" onClick={() => setPage((current) => current + 1)} disabled={!hasNextPage}>
                    Next <ChevronRight className="h-4 w-4" />
                  </Button>
                </div>
              </div>
            </div>
          )}
        </AdminSectionPanel>
      </AsyncStateWrapper>
    </div>
  );
}
