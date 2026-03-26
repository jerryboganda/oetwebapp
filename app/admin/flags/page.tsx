'use client';

import { useState, useEffect } from 'react';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { FilterBar, FilterGroup } from '@/components/ui/filter-bar';
import { DataTable, Column } from '@/components/ui/data-table';
import { mockFlags, AdminFeatureFlag } from '@/lib/mock-admin-data';
import { Badge } from '@/components/ui/badge';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Flag } from 'lucide-react';
import { analytics } from '@/lib/analytics';

export default function FlagsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [activeFilters, setActiveFilters] = useState<Record<string, string[]>>({ type: [] });
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [pageStatus, setPageStatus] = useState<'loading' | 'success' | 'empty'>('loading');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [toggleTarget, setToggleTarget] = useState<AdminFeatureFlag | null>(null);

  useEffect(() => {
    const load = async () => {
      await new Promise(resolve => setTimeout(resolve, 300));
      setPageStatus(mockFlags.length > 0 ? 'success' : 'empty');
    };
    load();
  }, []);

  const columns: Column<AdminFeatureFlag>[] = [
    {
      key: 'name',
      header: 'Flag Name',
      render: (item) => (
        <div>
          <div className="font-medium text-slate-900">{item.name}</div>
          <div className="text-sm text-slate-500 font-mono">{item.key}</div>
        </div>
      ),
    },
    {
      key: 'description',
      header: 'Description',
      render: (item) => <div className="text-sm text-slate-600 max-w-xs truncate">{item.description}</div>,
    },
    {
      key: 'owner',
      header: 'Owner',
      render: (item) => <div className="text-sm text-slate-600">{item.owner}</div>,
    },
    {
      key: 'type',
      header: 'Type',
      render: (item) => (
        <Badge variant={item.type === 'release' ? 'info' : item.type === 'experiment' ? 'warning' : 'default'} className="capitalize">
          {item.type}
        </Badge>
      ),
    },
    {
      key: 'rollout',
      header: 'Rollout',
      render: (item) => (
        <div className="flex items-center gap-2">
          <div className="w-24 h-2 bg-slate-100 rounded-full overflow-hidden">
            <div
              className={`h-full ${item.rolloutPercentage === 100 ? 'bg-green-500' : 'bg-blue-500'}`}
              style={{ width: `${item.rolloutPercentage}%` }}
            />
          </div>
          <span className="text-sm text-slate-600 font-mono">{item.rolloutPercentage}%</span>
        </div>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (item) => (
        <Badge variant={item.enabled ? 'success' : 'muted'}>
          {item.enabled ? 'Enabled' : 'Disabled'}
        </Badge>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (item) => (
        <div className="flex justify-end">
          <Button
            variant={item.enabled ? 'destructive' : undefined}
            size="sm"
            onClick={() => setToggleTarget(item)}
          >
            {item.enabled ? 'Disable' : 'Enable'}
          </Button>
        </div>
      ),
    },
  ];

  const filterGroups: FilterGroup[] = [
    {
      id: 'type',
      label: 'Type',
      options: [
        { id: 'release', label: 'Release' },
        { id: 'experiment', label: 'Experiment' },
        { id: 'operational', label: 'Operational' },
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

  const filteredData = mockFlags.filter((item) => {
    if (activeFilters.type.length > 0) {
      return activeFilters.type.includes(item.type);
    }
    return true;
  });

  const handleCreateFlag = () => {
    try {
      analytics.track('admin_flag_changed', { action: 'create' });
      setIsModalOpen(false);
      setToast({ variant: 'success', message: 'Feature flag created successfully.' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to create feature flag.' });
    }
  };

  const handleToggleFlag = () => {
    if (!toggleTarget) return;
    try {
      analytics.track('admin_flag_changed', { flagId: toggleTarget.id, action: toggleTarget.enabled ? 'disable' : 'enable' });
      setToast({ variant: 'success', message: `"${toggleTarget.name}" ${toggleTarget.enabled ? 'disabled' : 'enabled'} successfully.` });
    } catch {
      setToast({ variant: 'error', message: 'Failed to toggle feature flag.' });
    }
    setToggleTarget(null);
  };

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <div className="space-y-6 max-w-7xl mx-auto" role="main" aria-label="Feature Flags">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">Feature Flags</h1>
          <p className="text-sm text-slate-500 mt-1">Manage rollouts and system experiments.</p>
        </div>
        <Button onClick={() => setIsModalOpen(true)}>Create Flag</Button>
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<Flag className="w-12 h-12 text-muted" />}
            title="No Feature Flags"
            description="Create your first feature flag to control rollouts."
            action={{ label: 'Create Flag', onClick: () => setIsModalOpen(true) }}
          />
        }
      >
        <div className="bg-white rounded-xl shadow-sm border border-slate-200">
          <FilterBar
            groups={filterGroups}
            selected={activeFilters}
            onChange={handleFilterChange}
            className="border-b border-slate-200 p-4"
          />
          {filteredData.length === 0 ? (
            <EmptyState
              icon={<Flag className="w-12 h-12 text-muted" />}
              title="No Matching Flags"
              description="Adjust your filters to see feature flags."
            />
          ) : (
            <DataTable columns={columns} data={filteredData} keyExtractor={(item) => item.id} />
          )}
        </div>
      </AsyncStateWrapper>

      <Modal open={isModalOpen} onClose={() => setIsModalOpen(false)} title="Create Feature Flag">
        <div className="space-y-4 py-4">
          <Input label="Flag Name" placeholder="e.g., New Speaking UI" />
          <Input label="Flag Key" placeholder="e.g., speaking_ui_v2" />
          <Input label="Description" placeholder="What this flag controls..." />
          <Input label="Owner" placeholder="e.g., Product Team" />
          <Select
            label="Type"
            options={[
              { value: 'release', label: 'Release' },
              { value: 'experiment', label: 'Experiment' },
              { value: 'operational', label: 'Operational' },
            ]}
          />
          <Input label="Initial Rollout Percentage" type="number" placeholder="0" min={0} max={100} />
          <div className="flex justify-end gap-3 pt-4 border-t border-slate-100 mt-6">
            <Button variant="outline" onClick={() => setIsModalOpen(false)}>Cancel</Button>
            <Button onClick={handleCreateFlag}>Create Flag</Button>
          </div>
        </div>
      </Modal>

      <Modal open={!!toggleTarget} onClose={() => setToggleTarget(null)} title={`${toggleTarget?.enabled ? 'Disable' : 'Enable'} Feature Flag`}>
        <div className="space-y-4 py-4">
          <p className="text-sm text-slate-600">
            Are you sure you want to {toggleTarget?.enabled ? 'disable' : 'enable'}{' '}
            <span className="font-medium">&ldquo;{toggleTarget?.name}&rdquo;</span>? This change takes effect immediately and affects all users within the configured rollout scope.
          </p>
          <div className="flex justify-end gap-3 pt-4 border-t border-slate-100 mt-6">
            <Button variant="outline" onClick={() => setToggleTarget(null)}>Cancel</Button>
            <Button
              variant={toggleTarget?.enabled ? 'destructive' : undefined}
              onClick={handleToggleFlag}
            >
              {toggleTarget?.enabled ? 'Disable Flag' : 'Enable Flag'}
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
