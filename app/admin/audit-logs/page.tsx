'use client';

import { useState, useEffect, useMemo } from 'react';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { FilterBar, FilterGroup } from '@/components/ui/filter-bar';
import { DataTable, Column } from '@/components/ui/data-table';
import { mockAuditLogs, AdminAuditLog } from '@/lib/mock-admin-data';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { FileText, Search, Download } from 'lucide-react';
import { analytics } from '@/lib/analytics';

export default function AuditLogsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [activeFilters, setActiveFilters] = useState<Record<string, string[]>>({ action: [], actor: [] });
  const [searchQuery, setSearchQuery] = useState('');
  const [pageStatus, setPageStatus] = useState<'loading' | 'success' | 'empty'>('loading');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    const load = async () => {
      await new Promise(resolve => setTimeout(resolve, 300));
      setPageStatus(mockAuditLogs.length > 0 ? 'success' : 'empty');
      analytics.track('admin_audit_log_viewed', {});
    };
    load();
  }, []);

  const columns: Column<AdminAuditLog>[] = [
    {
      key: 'timestamp',
      header: 'Timestamp',
      render: (item) => (
        <div className="text-sm text-slate-600 whitespace-nowrap">
          {new Date(item.timestamp).toLocaleString()}
        </div>
      ),
    },
    {
      key: 'actor',
      header: 'Actor',
      render: (item) => <div className="font-medium text-slate-900">{item.actor}</div>,
    },
    {
      key: 'action',
      header: 'Action',
      render: (item) => <div className="text-slate-900">{item.action}</div>,
    },
    {
      key: 'resource',
      header: 'Resource',
      render: (item) => <div className="text-sm text-slate-500 font-mono">{item.resource}</div>,
    },
    {
      key: 'details',
      header: 'Details',
      render: (item) => <div className="text-sm text-slate-600">{item.details}</div>,
    },
  ];

  // Build unique action types and actors from data for dynamic filters
  const uniqueActions = useMemo(() => [...new Set(mockAuditLogs.map(l => l.action))], []);
  const uniqueActors = useMemo(() => [...new Set(mockAuditLogs.map(l => l.actor))], []);

  const filterGroups: FilterGroup[] = [
    {
      id: 'action',
      label: 'Action',
      options: uniqueActions.map(a => ({ id: a, label: a })),
    },
    {
      id: 'actor',
      label: 'Actor',
      options: uniqueActors.map(a => ({ id: a, label: a })),
    },
  ];

  const handleFilterChange = (groupId: string, optionId: string) => {
    setActiveFilters((prev) => {
      const current = prev[groupId] || [];
      const updated = current.includes(optionId)
        ? current.filter((id) => id !== optionId)
        : [...current, optionId];
      return { ...prev, [groupId]: updated };
    });
  };

  const filteredData = useMemo(() => {
    const q = searchQuery.toLowerCase();
    return mockAuditLogs.filter((item) => {
      const matchesSearch = !q || item.action.toLowerCase().includes(q) || item.actor.toLowerCase().includes(q) || item.resource.toLowerCase().includes(q) || item.details.toLowerCase().includes(q);
      const matchesAction = activeFilters.action.length === 0 || activeFilters.action.includes(item.action);
      const matchesActor = activeFilters.actor.length === 0 || activeFilters.actor.includes(item.actor);
      return matchesSearch && matchesAction && matchesActor;
    });
  }, [searchQuery, activeFilters]);

  const handleExport = () => {
    try {
      analytics.track('admin_audit_log_viewed', { action: 'export_csv' });
      setToast({ variant: 'success', message: 'CSV export started. Download will begin shortly.' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to export CSV.' });
    }
  };

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <div className="space-y-6 max-w-7xl mx-auto" role="main" aria-label="Audit Logs">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">Audit Logs</h1>
          <p className="text-sm text-slate-500 mt-1">Security and operational event tracking.</p>
        </div>
        <Button variant="outline" onClick={handleExport}>
          <Download className="w-4 h-4 mr-2" />
          Export CSV
        </Button>
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<FileText className="w-12 h-12 text-muted" />}
            title="No Audit Logs"
            description="Audit events will appear here as actions are taken in the system."
          />
        }
      >
        <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden">
          <div className="p-4 border-b border-slate-200">
            <div className="relative">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400 z-10" />
              <Input
                placeholder="Search logs by action, actor, resource, or details..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="pl-10"
              />
            </div>
          </div>
          <FilterBar
            groups={filterGroups}
            selected={activeFilters}
            onChange={handleFilterChange}
            className="border-b border-slate-200 p-4"
          />
          {filteredData.length === 0 ? (
            <EmptyState
              icon={<FileText className="w-12 h-12 text-muted" />}
              title="No Matching Logs"
              description="Adjust your search or filters to see audit events."
              action={{ label: 'Clear Filters', onClick: () => { setSearchQuery(''); setActiveFilters({ action: [], actor: [] }); } }}
            />
          ) : (
            <DataTable columns={columns} data={filteredData} keyExtractor={(item) => item.id} />
          )}
        </div>
      </AsyncStateWrapper>
    </div>
  );
}
