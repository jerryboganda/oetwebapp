'use client';

import { useEffect, useState } from 'react';
import { Edit2, ListTree, Plus } from 'lucide-react';
import { AdminRouteSectionHeader, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getAdminTaxonomyData, getAdminTaxonomyImpactData } from '@/lib/admin';
import { archiveAdminTaxonomy, createAdminTaxonomy, updateAdminTaxonomy } from '@/lib/api';
import type { AdminTaxonomyImpact, AdminTaxonomyNode } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

export default function AdminTaxonomyPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [nodes, setNodes] = useState<AdminTaxonomyNode[]>([]);
  const [filters, setFilters] = useState<Record<string, string[]>>({});
  const [reloadNonce, setReloadNonce] = useState(0);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingNode, setEditingNode] = useState<AdminTaxonomyNode | null>(null);
  const [impact, setImpact] = useState<AdminTaxonomyImpact | null>(null);
  const [form, setForm] = useState({ label: '', code: '', status: 'active', description: '' });
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const selectedStatus = filters.status?.[0];

  useEffect(() => {
    let cancelled = false;

    async function bootstrapTaxonomy() {
      try {
        setPageStatus('loading');
        const items = await getAdminTaxonomyData({ status: selectedStatus });
        if (cancelled) return;

        setNodes(items);
        setPageStatus(items.length > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load taxonomy.' });
        }
      }
    }

    void bootstrapTaxonomy();
    return () => {
      cancelled = true;
    };
  }, [reloadNonce, selectedStatus]);

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

  const columns: Column<AdminTaxonomyNode>[] = [
    { key: 'label', header: 'Label', render: (row) => <span className="font-medium text-navy">{row.label}</span> },
    { key: 'slug', header: 'Code', render: (row) => <span className="font-mono text-xs text-muted">{row.slug}</span> },
    { key: 'contentCount', header: 'Linked Content', render: (row) => <span>{row.contentCount}</span> },
    {
      key: 'status',
      header: 'Status',
      render: (row) => <Badge variant={row.status === 'active' ? 'success' : 'muted'}>{row.status}</Badge>,
    },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div className="flex justify-end gap-2">
          <Button variant="outline" size="sm" onClick={() => void openEditor(row)} className="gap-2">
            <Edit2 className="h-4 w-4" /> Edit
          </Button>
          {row.status === 'active' ? (
            <Button variant="outline" size="sm" onClick={() => void archiveNode(row)} className="text-rose-600">
              Archive
            </Button>
          ) : null}
        </div>
      ),
    },
  ];

  function handleFilterChange(groupId: string, optionId: string) {
    setFilters((current) => {
      return {
        ...current,
        [groupId]: current[groupId]?.[0] === optionId ? [] : [optionId],
      };
    });
  }

  async function openEditor(node?: AdminTaxonomyNode) {
    setEditingNode(node ?? null);
    setImpact(node ? await getAdminTaxonomyImpactData(node.id) : null);
    setForm({
      label: node?.label ?? '',
      code: node?.slug ?? '',
      status: node?.status ?? 'active',
      description: '',
    });
    setModalOpen(true);
  }

  async function archiveNode(node: AdminTaxonomyNode) {
    try {
      const impactSummary = await getAdminTaxonomyImpactData(node.id);
      if (!impactSummary.safeToArchive) {
        setToast({
          variant: 'error',
          message: `${node.label} still has ${impactSummary.usage.contentCount} linked content items and cannot be archived safely.`,
        });
        return;
      }
      await archiveAdminTaxonomy(node.id);
      setToast({ variant: 'success', message: `${node.label} archived.` });
      setReloadNonce((current) => current + 1);
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: `Unable to archive ${node.label}.` });
    }
  }

  async function submitNode() {
    try {
      if (editingNode) {
        await updateAdminTaxonomy(editingNode.id, { label: form.label, code: form.code, status: form.status });
        setToast({ variant: 'success', message: `${form.label} updated.` });
      } else {
        await createAdminTaxonomy({ label: form.label, code: form.code });
        setToast({ variant: 'success', message: `${form.label} created.` });
      }
      setModalOpen(false);
      setReloadNonce((current) => current + 1);
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to save taxonomy changes.' });
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Profession taxonomy">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title="Profession Taxonomy"
        description="Manage the professions that drive OET content targeting, learner goals, and downstream analytics."
        actions={
          <Button onClick={() => void openEditor()} className="gap-2">
            <Plus className="h-4 w-4" /> Add Profession
          </Button>
        }
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => setReloadNonce((current) => current + 1)}
        emptyContent={<EmptyState icon={<ListTree className="h-10 w-10 text-muted" />} title="No taxonomy entries" description="Add your first profession to start structuring content." />}
      >
        <AdminRoutePanel title="Filters" description="Review active vs archived taxonomy nodes.">
          <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => setFilters({})} />
        </AdminRoutePanel>

        <AdminRoutePanel title="Professions" description="Create, update, and archive professions with live impact checks before change.">
          <DataTable columns={columns} data={nodes} keyExtractor={(row) => row.id} />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <Modal open={modalOpen} onClose={() => setModalOpen(false)} title={editingNode ? `Edit ${editingNode.label}` : 'Add Profession'}>
        <div className="space-y-4">
          <Input label="Label" value={form.label} onChange={(event) => setForm((current) => ({ ...current, label: event.target.value }))} />
          <Input label="Code" value={form.code} onChange={(event) => setForm((current) => ({ ...current, code: event.target.value }))} />
          {editingNode ? (
            <Select
              label="Status"
              value={form.status}
              onChange={(event) => setForm((current) => ({ ...current, status: event.target.value }))}
              options={[
                { value: 'active', label: 'Active' },
                { value: 'archived', label: 'Archived' },
              ]}
            />
          ) : null}
          <Textarea
            label="Operational Notes"
            value={form.description}
            onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))}
            hint="Reserved for taxonomy governance notes."
          />
          {impact ? (
            <div className="rounded-xl bg-background-light p-4 text-sm text-muted">
              <p className="font-medium text-navy">Impact preview</p>
              <p>Linked content: {impact.usage.contentCount}</p>
              <p>Linked learners: {impact.usage.learnerCount}</p>
              <p>Goal references: {impact.usage.goalCount}</p>
              <p>{impact.safeToArchive ? 'Safe to archive when needed.' : 'Not safe to archive yet.'}</p>
            </div>
          ) : null}
          <div className="flex justify-end gap-3 border-t border-gray-200 pt-4">
            <Button variant="outline" onClick={() => setModalOpen(false)}>Cancel</Button>
            <Button onClick={() => void submitNode()}>{editingNode ? 'Save Changes' : 'Create Profession'}</Button>
          </div>
        </div>
      </Modal>
    </AdminRouteWorkspace>
  );
}
