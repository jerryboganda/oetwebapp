'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { MessageSquare, Archive, Eye, ShieldCheck } from 'lucide-react';
import { AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { apiClient } from '@/lib/api';
import { BulkActionBar } from '@/components/ui/bulk-action-bar';

interface ThreadRow {
  id: string;
  userId: string;
  userName: string;
  userRole: string;
  title: string;
  messageCount: number;
  createdAt: string;
  lastActiveAt: string;
  status: 'active' | 'archived' | 'terminated';
}

interface ThreadsResponse {
  items: ThreadRow[];
  totalCount: number;
  page: number;
  pageSize: number;
}

type PageStatus = 'loading' | 'success' | 'error';

function statusBadgeVariant(status: ThreadRow['status']): 'success' | 'danger' | 'warning' | 'default' {
  switch (status) {
    case 'active': return 'success';
    case 'archived': return 'default';
    case 'terminated': return 'danger';
    default: return 'default';
  }
}

export default function AiAssistantThreadsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [threads, setThreads] = useState<ThreadRow[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);

  // Filters
  const [filterRole, setFilterRole] = useState('');
  const [filterUser, setFilterUser] = useState('');
  const [filterDateFrom, setFilterDateFrom] = useState('');
  const [filterDateTo, setFilterDateTo] = useState('');

  // Archive modal
  const [archiveTarget, setArchiveTarget] = useState<ThreadRow | null>(null);
  const [archiving, setArchiving] = useState(false);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());

  const loadThreads = useCallback(async () => {
    try {
      setStatus('loading');
      const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
      if (filterRole) params.set('role', filterRole);
      if (filterUser) params.set('user', filterUser);
      if (filterDateFrom) params.set('from', filterDateFrom);
      if (filterDateTo) params.set('to', filterDateTo);

      const data = await apiClient.get<ThreadsResponse>(`/v1/admin/ai-assistant/threads?${params.toString()}`);
      setThreads(data.items);
      setTotalCount(data.totalCount);
      setStatus('success');
    } catch {
      setStatus('error');
    }
  }, [page, pageSize, filterRole, filterUser, filterDateFrom, filterDateTo]);

  useEffect(() => {
    if (isAuthenticated && role === 'admin') {
      loadThreads();
    }
  }, [isAuthenticated, role, loadThreads]);

  const handleArchive = useCallback(async () => {
    if (!archiveTarget) return;
    try {
      setArchiving(true);
      await apiClient.delete(`/v1/admin/ai-assistant/threads/${archiveTarget.id}`);
      setArchiveTarget(null);
      loadThreads();
    } catch {
      // Error handled silently; could add toast
    } finally {
      setArchiving(false);
    }
  }, [archiveTarget, loadThreads]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <EmptyState icon={<ShieldCheck className="w-8 h-8" />} title="Admin access required" description="Sign in with an admin account to view this page." />
      </AdminRouteWorkspace>
    );
  }

  const columns: Column<ThreadRow>[] = [
    {
      key: 'userName',
      header: 'User',
      render: (row) => (
        <div className="min-w-0">
          <p className="truncate text-sm font-medium text-admin-text">{row.userName}</p>
          <p className="truncate text-xs text-admin-text-muted">{row.userId}</p>
        </div>
      ),
    },
    {
      key: 'userRole',
      header: 'Role',
      render: (row) => <Badge variant="info">{row.userRole}</Badge>,
    },
    {
      key: 'title',
      header: 'Title',
      render: (row) => <span className="text-sm text-admin-text">{row.title || 'Untitled'}</span>,
    },
    {
      key: 'messageCount',
      header: 'Messages',
      render: (row) => <span className="text-sm tabular-nums text-admin-text">{row.messageCount}</span>,
    },
    {
      key: 'createdAt',
      header: 'Created',
      render: (row) => <span className="text-xs text-admin-text-muted">{new Date(row.createdAt).toLocaleDateString()}</span>,
    },
    {
      key: 'lastActiveAt',
      header: 'Last Active',
      render: (row) => <span className="text-xs text-admin-text-muted">{new Date(row.lastActiveAt).toLocaleString()}</span>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (row) => <Badge variant={statusBadgeVariant(row.status)}>{row.status}</Badge>,
    },
    {
      key: 'actions' as keyof ThreadRow,
      header: 'Actions',
      render: (row) => (
        <div className="flex items-center gap-1">
          <Button asChild variant="ghost" className="h-7 w-7 p-0">
            <Link href={`/admin/ai-assistant/threads/${row.id}`} aria-label="View thread">
              <Eye className="h-3.5 w-3.5" />
            </Link>
          </Button>
          {row.status === 'active' && (
            <Button variant="ghost" className="h-7 w-7 p-0 text-red-400 hover:text-red-300" onClick={() => setArchiveTarget(row)}>
              <Archive className="h-3.5 w-3.5" />
            </Button>
          )}
        </div>
      ),
    },
  ];

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <AdminRouteWorkspace>
      <AdminRoutePanel>
        <div className="p-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:justify-between">
            <h2 className="text-sm font-bold text-admin-text">
              <MessageSquare className="mr-2 inline-block h-4 w-4 text-admin-text-muted" />
              All Threads ({totalCount})
            </h2>
            <div className="flex flex-wrap items-center gap-2">
              <Select
                value={filterRole}
                onChange={(e) => { setFilterRole(e.target.value); setPage(1); }}
                className="h-8 w-32 text-xs"
                options={[
                  { value: '', label: 'All roles' },
                  { value: 'learner', label: 'Learner' },
                  { value: 'expert', label: 'Expert' },
                  { value: 'admin', label: 'Admin' },
                ]}
              />
              <Input
                placeholder="Filter by user…"
                value={filterUser}
                onChange={(e) => { setFilterUser(e.target.value); setPage(1); }}
                className="h-8 w-40 text-xs"
              />
              <Input
                type="date"
                value={filterDateFrom}
                onChange={(e) => { setFilterDateFrom(e.target.value); setPage(1); }}
                className="h-8 w-32 text-xs"
              />
              <Input
                type="date"
                value={filterDateTo}
                onChange={(e) => { setFilterDateTo(e.target.value); setPage(1); }}
                className="h-8 w-32 text-xs"
              />
            </div>
          </div>
        </div>
      </AdminRoutePanel>

      <AsyncStateWrapper status={status} onRetry={loadThreads}>
        <AdminRoutePanel>
          <DataTable columns={columns} data={threads} keyExtractor={(row) => row.id} emptyMessage="No threads found" selectable selectedKeys={selectedKeys} onSelectionChange={setSelectedKeys} />
          <BulkActionBar
            selectedCount={selectedKeys.size}
            onClearSelection={() => setSelectedKeys(new Set())}
            actions={[
              { key: 'archive', label: 'Archive selected', variant: 'danger', onClick: () => {} },
            ]}
          />
          {totalPages > 1 && (
            <div className="flex items-center justify-between border-t border-admin-border px-4 py-3">
              <p className="text-xs text-admin-text-muted">
                Page {page} of {totalPages} ({totalCount} total)
              </p>
              <div className="flex gap-1">
                <Button variant="ghost" disabled={page <= 1} onClick={() => setPage((p) => p - 1)} className="h-7 px-2 text-xs">
                  Previous
                </Button>
                <Button variant="ghost" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)} className="h-7 px-2 text-xs">
                  Next
                </Button>
              </div>
            </div>
          )}
        </AdminRoutePanel>
      </AsyncStateWrapper>

      {archiveTarget && (
        <Modal open onClose={() => setArchiveTarget(null)} title="Archive Thread">
          <div className="p-4">
            <p className="text-sm text-admin-text">
              Are you sure you want to archive the thread <span className="font-semibold">&ldquo;{archiveTarget.title || 'Untitled'}&rdquo;</span> by {archiveTarget.userName}?
            </p>
            <p className="mt-2 text-xs text-admin-text-muted">
              This will terminate any active session and mark the thread as archived.
            </p>
            <div className="mt-4 flex justify-end gap-2">
              <Button variant="ghost" onClick={() => setArchiveTarget(null)}>Cancel</Button>
              <Button variant="destructive" onClick={handleArchive} disabled={archiving}>
                {archiving ? 'Archiving…' : 'Archive Thread'}
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </AdminRouteWorkspace>
  );
}
