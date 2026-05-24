'use client';

import { useEffect, useMemo, useState } from 'react';
import { Edit3, Flag, Plus, Power } from 'lucide-react';
import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { activateAdminFlag, createAdminFlag, deactivateAdminFlag, updateAdminFlag } from '@/lib/api';
import { getAdminFlagData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminFlag } from '@/lib/types/admin';
import { BulkActionBar } from '@/components/ui/bulk-action-bar';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { EmptyState } from '@/components/admin/ui/empty-state';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface FlagFormState {
  id: string | null;
  name: string;
  key: string;
  description: string;
  owner: string;
  type: string;
  rolloutPercentage: string;
  enabled: boolean;
}

const defaultFormState: FlagFormState = {
  id: null,
  name: '',
  key: '',
  description: '',
  owner: '',
  type: 'release',
  rolloutPercentage: '0',
  enabled: false,
};

export default function FlagsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({ type: [] });
  const [flags, setFlags] = useState<AdminFlag[]>([]);
  const [form, setForm] = useState<FlagFormState>(defaultFormState);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const [togglingId, setTogglingId] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());

  const selectedType = filters.type?.[0];

  useEffect(() => {
    let cancelled = false;

    async function loadFlags() {
      setPageStatus('loading');
      try {
        const items = await getAdminFlagData({ type: selectedType });
        if (cancelled) return;

        setFlags(items);
        setPageStatus(items.length > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load feature flags.' });
        }
      }
    }

    loadFlags();
    return () => {
      cancelled = true;
    };
  }, [selectedType]);

  const metrics = useMemo(() => {
    const enabled = flags.filter((flag) => flag.enabled).length;
    const experiments = flags.filter((flag) => flag.type === 'experiment').length;
    const operational = flags.filter((flag) => flag.type === 'operational').length;
    const fullRollout = flags.filter((flag) => flag.rolloutPercentage === 100).length;
    return { enabled, experiments, operational, fullRollout };
  }, [flags]);

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

  const columns: Column<AdminFlag>[] = [
    {
      key: 'name',
      header: 'Flag',
      render: (flag) => (
        <div className="space-y-1">
          <p className="font-medium text-admin-fg-strong">{flag.name}</p>
          <p className="font-mono text-xs text-admin-fg-muted">{flag.key}</p>
        </div>
      ),
    },
    {
      key: 'description',
      header: 'Description',
      render: (flag) => <span className="text-sm text-admin-fg-muted">{flag.description || 'No rollout description yet.'}</span>,
    },
    {
      key: 'owner',
      header: 'Owner',
      render: (flag) => <span className="text-sm text-admin-fg-muted">{flag.owner || 'Unassigned'}</span>,
    },
    {
      key: 'type',
      header: 'Type',
      render: (flag) => (
        <Badge variant={flag.type === 'experiment' ? 'warning' : flag.type === 'operational' ? 'info' : 'default'}>
          {flag.type}
        </Badge>
      ),
    },
    {
      key: 'rollout',
      header: 'Rollout',
      render: (flag) => (
        <div className="min-w-[140px] space-y-2">
          <div className="h-2 rounded-full bg-admin-bg-subtle">
            <div className="h-2 rounded-full bg-[var(--admin-primary)]" style={{ width: `${flag.rolloutPercentage}%` }} />
          </div>
          <p className="font-mono text-xs text-admin-fg-muted">{flag.rolloutPercentage}%</p>
        </div>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (flag) => (
        <Badge variant={flag.enabled ? 'success' : 'default'}>
          {flag.enabled ? 'enabled' : 'disabled'}
        </Badge>
      ),
    },
    {
      key: 'actions',
      header: '',
      className: 'w-56',
      render: (flag) => (
        <div className="flex justify-end gap-2">
          <Button variant="outline" size="sm" onClick={() => openEditModal(flag)} startIcon={<Edit3 className="h-3.5 w-3.5" />}>
            Edit
          </Button>
          <Button
            size="sm"
            variant={flag.enabled ? 'destructive' : 'primary'}
            onClick={() => handleToggleFlag(flag)}
            loading={togglingId === flag.id}
            startIcon={<Power className="h-3.5 w-3.5" />}
          >
            {flag.enabled ? 'Disable' : 'Enable'}
          </Button>
        </div>
      ),
    },
  ];

  const mobileCardRender = (flag: AdminFlag) => (
    <div className="space-y-3">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="truncate font-semibold text-admin-fg-strong">{flag.name}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-admin-fg-muted">{flag.key}</p>
        </div>
        <div className="flex flex-col items-end gap-1">
          <Badge variant={flag.type === 'experiment' ? 'warning' : flag.type === 'operational' ? 'info' : 'default'}>
            {flag.type}
          </Badge>
          <Badge variant={flag.enabled ? 'success' : 'default'}>
            {flag.enabled ? 'enabled' : 'disabled'}
          </Badge>
        </div>
      </div>

      <p className="text-sm text-admin-fg-muted">{flag.description || 'No rollout description yet.'}</p>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-admin bg-admin-bg-subtle px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-admin-fg-muted">Owner</p>
          <p className="mt-1 font-medium text-admin-fg-strong">{flag.owner || 'Unassigned'}</p>
        </div>
        <div className="rounded-admin bg-admin-bg-subtle px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-admin-fg-muted">Rollout</p>
          <p className="mt-1 font-medium text-admin-fg-strong">{flag.rolloutPercentage}%</p>
        </div>
      </div>

      <div className="flex flex-col gap-2 sm:flex-row">
        <Button variant="outline" size="sm" className="w-full sm:flex-1" onClick={() => openEditModal(flag)} startIcon={<Edit3 className="h-3.5 w-3.5" />}>
          Edit
        </Button>
        <Button
          size="sm"
          className="w-full sm:flex-1"
          variant={flag.enabled ? 'destructive' : 'primary'}
          onClick={() => handleToggleFlag(flag)}
          loading={togglingId === flag.id}
          startIcon={<Power className="h-3.5 w-3.5" />}
        >
          {flag.enabled ? 'Disable' : 'Enable'}
        </Button>
      </div>
    </div>
  );

  function handleFilterChange(groupId: string, optionId: string) {
    setFilters((current) => ({
      ...current,
      [groupId]: current[groupId]?.includes(optionId) ? [] : [optionId],
    }));
  }

  function openCreateModal() {
    setForm(defaultFormState);
    setIsModalOpen(true);
  }

  function openEditModal(flag: AdminFlag) {
    setForm({
      id: flag.id,
      name: flag.name,
      key: flag.key,
      description: flag.description,
      owner: flag.owner,
      type: flag.type,
      rolloutPercentage: String(flag.rolloutPercentage),
      enabled: flag.enabled,
    });
    setIsModalOpen(true);
  }

  async function reloadFlags() {
    const items = await getAdminFlagData({ type: selectedType });
    setFlags(items);
    setPageStatus(items.length > 0 ? 'success' : 'empty');
  }

  async function handleSaveFlag() {
    setIsSaving(true);
    try {
      const payload = {
        name: form.name,
        key: form.key,
        description: form.description,
        owner: form.owner,
        flagType: form.type,
        rolloutPercentage: Number(form.rolloutPercentage || 0),
        enabled: form.enabled,
      };

      if (form.id) {
        await updateAdminFlag(form.id, payload);
      } else {
        await createAdminFlag(payload);
      }

      await reloadFlags();
      setIsModalOpen(false);
      setToast({
        variant: 'success',
        message: form.id ? 'Feature flag updated.' : 'Feature flag created.',
      });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to save this feature flag.' });
    } finally {
      setIsSaving(false);
    }
  }

  async function handleToggleFlag(flag: AdminFlag) {
    setTogglingId(flag.id);
    try {
      if (flag.enabled) {
        await deactivateAdminFlag(flag.id);
      } else {
        await activateAdminFlag(flag.id);
      }

      await reloadFlags();
      setToast({
        variant: 'success',
        message: `${flag.name} ${flag.enabled ? 'disabled' : 'enabled'} successfully.`,
      });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to update that flag right now.' });
    } finally {
      setTogglingId(null);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Feature flags">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminTableLayout
        title="Feature Flags"
        description="Manage production rollouts, experiments, and operational controls with real activate and deactivate actions."
        breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Feature Flags' }]}
        actions={
          <Button onClick={openCreateModal} startIcon={<Plus className="h-4 w-4" />}>
            Create Flag
          </Button>
        }
        banner={
          <div className="space-y-6">
            <KpiStrip>
              <KpiTile label="Enabled Flags" value={metrics.enabled} icon={<Power className="h-4 w-4" />} tone="primary" />
              <KpiTile label="Experiments" value={metrics.experiments} icon={<Flag className="h-4 w-4" />} tone={metrics.experiments > 0 ? 'warning' : 'default'} />
              <KpiTile label="Operational Controls" value={metrics.operational} icon={<Flag className="h-4 w-4" />} tone="info" />
              <KpiTile label="100% Rollout" value={metrics.fullRollout} icon={<Flag className="h-4 w-4" />} tone="success" />
            </KpiStrip>
            <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => setFilters({ type: [] })} />
          </div>
        }
      >
        <AsyncStateWrapper
          status={pageStatus}
          onRetry={() => window.location.reload()}
          emptyContent={
            <div className="p-6">
              <EmptyState
                illustration={<Flag />}
                title="No feature flags yet"
                description="Create the first rollout control so experiments and launches are traceable from admin."
                primaryAction={{ label: 'Create Flag', onClick: openCreateModal }}
              />
            </div>
          }
        >
          <DataTable columns={columns} data={flags} keyExtractor={(flag) => flag.id} mobileCardRender={mobileCardRender} selectable selectedKeys={selectedKeys} onSelectionChange={setSelectedKeys} />
          <BulkActionBar
            selectedCount={selectedKeys.size}
            onClearSelection={() => setSelectedKeys(new Set())}
            actions={[
              { key: 'enable', label: 'Enable selected', onClick: () => setToast({ variant: 'error', message: 'Bulk enable coming soon.' }) },
              { key: 'disable', label: 'Disable selected', variant: 'danger', onClick: () => setToast({ variant: 'error', message: 'Bulk disable coming soon.' }) },
            ]}
          />
        </AsyncStateWrapper>
      </AdminTableLayout>

      <Modal open={isModalOpen} onClose={() => setIsModalOpen(false)} title={form.id ? 'Edit Feature Flag' : 'Create Feature Flag'}>
        <div className="space-y-4 py-2">
          <Input label="Flag Name" value={form.name} onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))} />
          <Input label="Flag Key" value={form.key} onChange={(event) => setForm((current) => ({ ...current, key: event.target.value }))} />
          <Input label="Owner" value={form.owner} onChange={(event) => setForm((current) => ({ ...current, owner: event.target.value }))} />
          <Input label="Description" value={form.description} onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))} />
          <Select
            label="Type"
            value={form.type}
            onChange={(event) => setForm((current) => ({ ...current, type: event.target.value }))}
            options={[
              { value: 'release', label: 'Release' },
              { value: 'experiment', label: 'Experiment' },
              { value: 'operational', label: 'Operational' },
            ]}
          />
          <Input
            label="Rollout Percentage"
            type="number"
            min={0}
            max={100}
            value={form.rolloutPercentage}
            onChange={(event) => setForm((current) => ({ ...current, rolloutPercentage: event.target.value }))}
          />
          <Select
            label="Initial State"
            value={form.enabled ? 'enabled' : 'disabled'}
            onChange={(event) => setForm((current) => ({ ...current, enabled: event.target.value === 'enabled' }))}
            options={[
              { value: 'disabled', label: 'Disabled' },
              { value: 'enabled', label: 'Enabled' },
            ]}
          />

          <div className="flex justify-end gap-3 border-t border-admin-border pt-4">
            <Button variant="outline" onClick={() => setIsModalOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleSaveFlag} loading={isSaving}>
              Save Flag
            </Button>
          </div>
        </div>
      </Modal>
    </AdminRouteWorkspace>
  );
}
