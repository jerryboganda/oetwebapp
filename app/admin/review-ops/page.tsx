'use client';

import { useState, useEffect } from 'react';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { FilterBar, FilterGroup } from '@/components/ui/filter-bar';
import { DataTable, Column } from '@/components/ui/data-table';
import { mockReviewOps, mockReviewOpsKPIs, AdminReviewOp } from '@/lib/mock-admin-data';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Inbox, AlertTriangle, Clock, CheckCircle2, BarChart3 } from 'lucide-react';
import { analytics } from '@/lib/analytics';

export default function ReviewOpsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [activeFilters, setActiveFilters] = useState<Record<string, string[]>>({ status: [], priority: [] });
  const [pageStatus, setPageStatus] = useState<'loading' | 'success'>('loading');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    const load = async () => {
      await new Promise(resolve => setTimeout(resolve, 300));
      setPageStatus('success');
    };
    load();
  }, []);

  const kpis = mockReviewOpsKPIs;

  const columns: Column<AdminReviewOp>[] = [
    {
      key: 'id',
      header: 'Review ID',
      render: (item) => <div className="font-medium text-slate-900">{item.id}</div>,
    },
    {
      key: 'taskId',
      header: 'Task & Learner',
      render: (item) => (
        <div>
          <div className="text-sm text-slate-900">{item.taskId}</div>
          <div className="text-xs text-slate-500">Learner: {item.learnerId}</div>
        </div>
      ),
    },
    {
      key: 'expertId',
      header: 'Assigned Expert',
      render: (item) => <div className="text-slate-600">{item.expertId}</div>,
    },
    {
      key: 'priority',
      header: 'Priority',
      render: (item) => (
        <Badge variant={item.priority === 'high' ? 'danger' : item.priority === 'normal' ? 'default' : 'muted'}>
          {item.priority.charAt(0).toUpperCase() + item.priority.slice(1)}
        </Badge>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (item) => (
        <Badge variant={item.status === 'completed' ? 'success' : item.status === 'in_progress' ? 'warning' : 'muted'}>
          {item.status.replace('_', ' ').replace(/\b\w/g, (c) => c.toUpperCase())}
        </Badge>
      ),
    },
  ];

  const filterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Status',
      options: [
        { id: 'pending', label: 'Pending' },
        { id: 'in_progress', label: 'In Progress' },
        { id: 'completed', label: 'Completed' },
      ],
    },
    {
      id: 'priority',
      label: 'Priority',
      options: [
        { id: 'high', label: 'High' },
        { id: 'normal', label: 'Normal' },
        { id: 'low', label: 'Low' },
      ],
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

  const filteredData = mockReviewOps.filter((item) => {
    let matches = true;
    if (activeFilters.status.length > 0) {
      matches = matches && activeFilters.status.includes(item.status);
    }
    if (activeFilters.priority.length > 0) {
      matches = matches && activeFilters.priority.includes(item.priority);
    }
    return matches;
  });

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <div className="space-y-6 max-w-7xl mx-auto" role="main" aria-label="Review Operations">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">Review Operations</h1>
          <p className="text-sm text-slate-500 mt-1">Manage expert assignments and monitor grading queues.</p>
        </div>
        <Button onClick={() => { analytics.track('admin_review_ops_action', { action: 'assign_reviews' }); setToast({ variant: 'success', message: 'Review assignment initiated.' }); }}>Assign Reviews</Button>
      </div>

      <AsyncStateWrapper status={pageStatus} onRetry={() => window.location.reload()}>
        {/* KPI Cards — §6.7.4 */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 flex items-center gap-4">
            <div className="w-10 h-10 rounded-lg bg-orange-50 text-orange-600 flex items-center justify-center">
              <Inbox className="w-5 h-5" />
            </div>
            <div>
              <div className="text-2xl font-semibold text-slate-900">{kpis.backlog}</div>
              <div className="text-sm text-slate-500">Backlog</div>
            </div>
          </div>
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 flex items-center gap-4">
            <div className="w-10 h-10 rounded-lg bg-red-50 text-red-600 flex items-center justify-center">
              <AlertTriangle className="w-5 h-5" />
            </div>
            <div>
              <div className="text-2xl font-semibold text-slate-900">{kpis.overdue}</div>
              <div className="text-sm text-slate-500">Overdue</div>
            </div>
          </div>
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 flex items-center gap-4">
            <div className="w-10 h-10 rounded-lg bg-amber-50 text-amber-600 flex items-center justify-center">
              <Clock className="w-5 h-5" />
            </div>
            <div>
              <div className="text-2xl font-semibold text-slate-900">{kpis.slaRisk}</div>
              <div className="text-sm text-slate-500">SLA Risk</div>
            </div>
          </div>
          <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 flex items-center gap-4">
            <div className="w-10 h-10 rounded-lg bg-green-50 text-green-600 flex items-center justify-center">
              <CheckCircle2 className="w-5 h-5" />
            </div>
            <div>
              <div className="text-2xl font-semibold text-slate-900">{kpis.statusDistribution.completed}</div>
              <div className="text-sm text-slate-500">Completed</div>
            </div>
          </div>
        </div>

        {/* Status Distribution Bar */}
        <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5">
          <h3 className="text-sm font-medium text-slate-700 mb-3 flex items-center gap-2">
            <BarChart3 className="w-4 h-4" /> Status Distribution
          </h3>
          <div className="flex h-3 rounded-full overflow-hidden bg-slate-100">
            <div className="bg-slate-400 transition-all" style={{ width: `${(kpis.statusDistribution.pending / (kpis.statusDistribution.pending + kpis.statusDistribution.inProgress + kpis.statusDistribution.completed)) * 100}%` }} title={`Pending: ${kpis.statusDistribution.pending}`} />
            <div className="bg-amber-400 transition-all" style={{ width: `${(kpis.statusDistribution.inProgress / (kpis.statusDistribution.pending + kpis.statusDistribution.inProgress + kpis.statusDistribution.completed)) * 100}%` }} title={`In Progress: ${kpis.statusDistribution.inProgress}`} />
            <div className="bg-green-500 transition-all" style={{ width: `${(kpis.statusDistribution.completed / (kpis.statusDistribution.pending + kpis.statusDistribution.inProgress + kpis.statusDistribution.completed)) * 100}%` }} title={`Completed: ${kpis.statusDistribution.completed}`} />
          </div>
          <div className="flex justify-between mt-2 text-xs text-slate-500">
            <span>Pending: {kpis.statusDistribution.pending}</span>
            <span>In Progress: {kpis.statusDistribution.inProgress}</span>
            <span>Completed: {kpis.statusDistribution.completed}</span>
          </div>
        </div>

        <div className="bg-white rounded-xl shadow-sm border border-slate-200">
          <FilterBar 
            groups={filterGroups} 
            selected={activeFilters}
            onChange={handleFilterChange} 
            className="border-b border-slate-200 p-4" 
          />
          {filteredData.length === 0 ? (
            <EmptyState
              icon={<Inbox className="w-12 h-12 text-muted" />}
              title="No Matching Reviews"
              description="Adjust your filters to see review operations."
            />
          ) : (
            <DataTable columns={columns} data={filteredData} keyExtractor={(item) => item.id} />
          )}
        </div>
      </AsyncStateWrapper>
    </div>
  );
}
