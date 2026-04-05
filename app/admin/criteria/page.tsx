'use client';

import { useEffect, useMemo, useState } from 'react';
import { Edit3, Plus, Target } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Tabs } from '@/components/ui/tabs';
import { createAdminCriterion, updateAdminCriterion } from '@/lib/api';
import { getAdminCriteriaData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminCriterion } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface CriterionFormState {
  id: string | null;
  name: string;
  subtestCode: string;
  weight: string;
  description: string;
  status: 'active' | 'archived';
}

const subtestTabs = [
  { id: 'writing', label: 'Writing' },
  { id: 'speaking', label: 'Speaking' },
  { id: 'reading', label: 'Reading' },
  { id: 'listening', label: 'Listening' },
];

const defaultFormState: CriterionFormState = {
  id: null,
  name: '',
  subtestCode: 'writing',
  weight: '1',
  description: '',
  status: 'active',
};

export default function CriteriaPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [activeTab, setActiveTab] = useState('writing');
  const [filters, setFilters] = useState<Record<string, string[]>>({ status: [] });
  const [criteria, setCriteria] = useState<AdminCriterion[]>([]);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [form, setForm] = useState<CriterionFormState>(defaultFormState);
  const [isSaving, setIsSaving] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const selectedStatus = filters.status?.[0];

  useEffect(() => {
    let cancelled = false;

    async function loadCriteria() {
      setPageStatus('loading');
      try {
        const items = await getAdminCriteriaData({
          subtest: activeTab,
          status: selectedStatus,
        });

        if (cancelled) return;

        setCriteria(items);
        setPageStatus(items.length > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load criteria right now.' });
        }
      }
    }

    loadCriteria();
    return () => {
      cancelled = true;
    };
  }, [activeTab, selectedStatus]);

  const filterGroups: FilterGroup[] = [
    {
      id: 'status',
      label: 'Status',
      options: [
        { id: 'active', label: 'Active' },
        { id: 'archived', label: 'Archived' },
      ],
    },
  ];

  const columns: Column<AdminCriterion>[] = useMemo(
    () => [
      {
        key: 'name',
        header: 'Criterion',
        render: (criterion) => (
          <div className="space-y-1">
            <p className="font-medium text-navy">{criterion.name}</p>
            <p className="max-w-2xl text-sm text-muted">{criterion.description || 'No editorial description yet.'}</p>
          </div>
        ),
      },
      {
        key: 'weight',
        header: 'Weight',
        render: (criterion) => <span className="font-mono text-sm text-muted">{criterion.weight}</span>,
        className: 'w-24',
      },
      {
        key: 'status',
        header: 'Status',
        render: (criterion) => (
          <Badge variant={criterion.status === 'active' ? 'success' : 'muted'}>
            {criterion.status}
          </Badge>
        ),
        className: 'w-32',
      },
      {
        key: 'actions',
        header: '',
        className: 'w-28',
        render: (criterion) => (
          <div className="flex justify-end">
            <Button variant="outline" size="sm" onClick={() => openEditModal(criterion)}>
              <Edit3 className="h-3.5 w-3.5" />
              Edit
            </Button>
          </div>
        ),
      },
    ],
    [],
  );

  function openCreateModal() {
    setForm({
      ...defaultFormState,
      subtestCode: activeTab,
    });
    setIsModalOpen(true);
  }

  function openEditModal(criterion: AdminCriterion) {
    setForm({
      id: criterion.id,
      name: criterion.name,
      subtestCode: criterion.type,
      weight: String(criterion.weight),
      description: criterion.description,
      status: criterion.status,
    });
    setIsModalOpen(true);
  }

  function handleFilterChange(groupId: string, optionId: string) {
    setFilters((current) => ({
      ...current,
      [groupId]: current[groupId]?.includes(optionId) ? [] : [optionId],
    }));
  }

  async function reloadCriteria() {
    const items = await getAdminCriteriaData({
      subtest: activeTab,
      status: selectedStatus,
    });
    setCriteria(items);
    setPageStatus(items.length > 0 ? 'success' : 'empty');
  }

  async function handleSaveCriterion() {
    setIsSaving(true);
    try {
      const weight = Number(form.weight || 1);
      if (form.id) {
        await updateAdminCriterion(form.id, {
          name: form.name,
          description: form.description,
          weight,
          status: form.status,
        });
      } else {
        const created = await createAdminCriterion({
          name: form.name,
          subtestCode: form.subtestCode,
          description: form.description,
          weight,
        });

        await updateAdminCriterion(String(created.id), {
          description: form.description,
          weight,
          status: form.status,
        });
      }

      await reloadCriteria();
      setIsModalOpen(false);
      setToast({
        variant: 'success',
        message: form.id ? 'Criterion updated successfully.' : 'Criterion created successfully.',
      });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to save this criterion.' });
    } finally {
      setIsSaving(false);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Rubrics and criteria">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title="Rubrics & Criteria"
        description="Manage live scoring criteria for each OET subtest with real weight, status, and editorial descriptions."
        actions={
          <Button onClick={openCreateModal} className="gap-2">
            <Plus className="h-4 w-4" />
            Add Criterion
          </Button>
        }
      />

      <Tabs tabs={subtestTabs} activeTab={activeTab} onChange={setActiveTab} />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<Target className="h-10 w-10 text-muted" />}
            title={`No ${activeTab} criteria yet`}
            description="Create the first scoring criterion for this subtest so editors and evaluators share the same rubric language."
            action={{ label: 'Add Criterion', onClick: openCreateModal }}
          />
        }
      >
        <AdminRoutePanel title="Criteria Library" description="Every row below is backed by the live admin criteria endpoint and persists status changes.">
          <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => setFilters({ status: [] })} />
          <DataTable columns={columns} data={criteria} keyExtractor={(criterion) => criterion.id} />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <Modal
        open={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        title={form.id ? 'Edit Criterion' : 'Add Criterion'}
      >
        <div className="space-y-4 py-2">
          <Input label="Criterion Name" value={form.name} onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))} />
          <Select
            label="Subtest"
            value={form.subtestCode}
            onChange={(event) => setForm((current) => ({ ...current, subtestCode: event.target.value }))}
            options={subtestTabs.map((tab) => ({ value: tab.id, label: tab.label }))}
            disabled={Boolean(form.id)}
          />
          <Input
            label="Weight"
            type="number"
            min={1}
            max={20}
            value={form.weight}
            onChange={(event) => setForm((current) => ({ ...current, weight: event.target.value }))}
          />
          <Textarea
            label="Editorial Description"
            value={form.description}
            onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))}
            className="min-h-[120px]"
          />
          <Select
            label="Status"
            value={form.status}
            onChange={(event) => setForm((current) => ({ ...current, status: event.target.value as CriterionFormState['status'] }))}
            options={[
              { value: 'active', label: 'Active' },
              { value: 'archived', label: 'Archived' },
            ]}
          />

          <div className="flex justify-end gap-3 border-t border-gray-200 pt-4">
            <Button variant="outline" onClick={() => setIsModalOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleSaveCriterion} loading={isSaving}>
              Save Criterion
            </Button>
          </div>
        </div>
      </Modal>
    </AdminRouteWorkspace>
  );
}
