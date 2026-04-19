'use client';

import { useEffect, useMemo, useState } from 'react';
import { Edit3, Flag, Plus, Power } from 'lucide-react';
import { AdminRouteSummaryCard, AdminRouteSectionHeader, AdminRoutePanel, AdminRouteWorkspace, AdminRoutePanelFooter } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { activateAdminFlag, createAdminFlag, deactivateAdminFlag, updateAdminFlag } from '@/lib/api';
import { getAdminFlagData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminFlag } from '@/lib/types/admin';

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
          <p className="font-medium text-navy">{flag.name}</p>
          <p className="font-mono text-xs text-muted">{flag.key}</p>
        </div>
      ),
    },
    {
      key: 'description',
      header: 'Description',
      render: (flag) => <span className="text-sm text-muted">{flag.description || 'No rollout description yet.'}</span>,
    },
    {
      key: 'owner',
      header: 'Owner',
      render: (flag) => <span className="text-sm text-muted">{flag.owner || 'Unassigned'}</span>,
    },
    {
      key: 'type',
      header: 'Type',
      render: (flag) => (
        <Badge variant={flag.type === 'experiment' ? 'warning' : flag.type === 'operational' ? 'info' : 'muted'}>
          {flag.type}
        </Badge>
      ),
    },
    {
      key: 'rollout',
      header: 'Rollout',
      render: (flag) => (
        <div className="min-w-[140px] space-y-2">
          <div className="h-2 rounded-full bg-background-light">
            <div className="h-2 rounded-full bg-primary" style={{ width: `${flag.rolloutPercentage}%` }} />
          </div>
          <p className="font-mono text-xs text-muted">{flag.rolloutPercentage}%</p>
        </div>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (flag) => (
        <Badge variant={flag.enabled ? 'success' : 'muted'}>
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
          <Button variant="outline" size="sm" onClick={() => openEditModal(flag)}>
            <Edit3 className="h-3.5 w-3.5" />
            Edit
          </Button>
          <Button
            size="sm"
            variant={flag.enabled ? 'destructive' : 'primary'}
            onClick={() => handleToggleFlag(flag)}
            loading={togglingId === flag.id}
          >
            <Power className="h-3.5 w-3.5" />
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
          <p className="truncate font-semibold text-navy">{flag.name}</p>
          <p className="truncate text-xs uppercase tracking-[0.12em] text-muted">{flag.key}</p>
        </div>
        <div className="flex flex-col items-end gap-1">
          <Badge variant={flag.type === 'experiment' ? 'warning' : flag.type === 'operational' ? 'info' : 'muted'}>
            {flag.type}
          </Badge>
          <Badge variant={flag.enabled ? 'success' : 'muted'}>
            {flag.enabled ? 'enabled' : 'disabled'}
          </Badge>
        </div>
      </div>

      <p className="text-sm text-muted">{flag.description || 'No rollout description yet.'}</p>

      <div className="grid grid-cols-2 gap-3 text-sm">
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Owner</p>
          <p className="mt-1 font-medium text-navy">{flag.owner || 'Unassigned'}</p>
        </div>
        <div className="rounded-2xl bg-background-light px-3 py-2">
          <p className="text-[11px] uppercase tracking-[0.12em] text-muted">Rollout</p>
          <p className="mt-1 font-medium text-navy">{flag.rolloutPercentage}%</p>
        </div>
      </div>

      <div className="flex flex-col gap-2 sm:flex-row">
        <Button variant="outline" size="sm" className="w-full sm:flex-1" onClick={() => openEditModal(flag)}>
          <Edit3 className="h-3.5 w-3.5" />
          Edit
        </Button>
        <Button
          size="sm"
          className="w-full sm:flex-1"
          variant={flag.enabled ? 'destructive' : 'primary'}
          onClick={() => handleToggleFlag(flag)}
          loading={togglingId === flag.id}
        >
          <Power className="h-3.5 w-3.5" />
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

      <AdminRouteSectionHeader
        title="Feature Flags"
        description="Manage production rollouts, experiments, and operational controls with real activate and deactivate actions."
        actions={
          <Button onClick={openCreateModal} className="gap-2">
            <Plus className="h-4 w-4" />
            Create Flag
          </Button>
        }
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<Flag className="h-10 w-10 text-muted" />}
            title="No feature flags yet"
            description="Create the first rollout control so experiments and launches are traceable from admin."
            action={{ label: 'Create Flag', onClick: openCreateModal }}
          />
        }
      >
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
          <AdminRouteSummaryCard label="Enabled Flags" value={metrics.enabled} icon={<Power className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Experiments" value={metrics.experiments} icon={<Flag className="h-5 w-5" />} tone={metrics.experiments > 0 ? 'warning' : 'default'} />
          <AdminRouteSummaryCard label="Operational Controls" value={metrics.operational} icon={<Flag className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="100% Rollout" value={metrics.fullRollout} icon={<Flag className="h-5 w-5" />} />
        </div>

        <AdminRoutePanel title="Rollout Registry" description="All visible enable, disable, and edit controls are backed by the admin feature flag endpoints and audit events.">
          <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => setFilters({ type: [] })} />
          <DataTable density="compact" columns={columns} data={flags} keyExtractor={(flag) => flag.id} mobileCardRender={mobileCardRender} />
          <AdminRoutePanelFooter source="Feature flag registry" />
        </AdminRoutePanel>
      </AsyncStateWrapper>

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

          <div className="flex justify-end gap-3 border-t border-border pt-4">
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
