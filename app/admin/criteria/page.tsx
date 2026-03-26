'use client';

import { useState, useEffect } from 'react';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { FilterBar, FilterGroup } from '@/components/ui/filter-bar';
import { DataTable, Column } from '@/components/ui/data-table';
import { mockCriteria, AdminCriteria } from '@/lib/mock-admin-data';
import { Badge } from '@/components/ui/badge';
import { Tabs, TabPanel } from '@/components/ui/tabs';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Target } from 'lucide-react';
import { analytics } from '@/lib/analytics';

export default function CriteriaPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [activeTab, setActiveTab] = useState('writing');
  const [activeFilters, setActiveFilters] = useState<Record<string, string[]>>({ status: [] });
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [pageStatus, setPageStatus] = useState<'loading' | 'success'>('loading');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    const load = async () => {
      await new Promise(resolve => setTimeout(resolve, 300));
      setPageStatus('success');
    };
    load();
  }, []);

  const columns: Column<AdminCriteria>[] = [
    {
      key: 'name',
      header: 'Name',
      render: (item: AdminCriteria) => (
        <div>
          <div className="font-medium text-slate-900">{item.name}</div>
          <div className="text-sm text-slate-500">{item.description}</div>
        </div>
      ),
    },
    {
      key: 'weight',
      header: 'Weight',
      render: (item: AdminCriteria) => <div className="text-slate-600">{item.weight} pts</div>,
    },
    {
      key: 'status',
      header: 'Status',
      render: (item: AdminCriteria) => (
        <Badge variant={item.status === 'active' ? 'success' : 'muted'}>
          {item.status.charAt(0).toUpperCase() + item.status.slice(1)}
        </Badge>
      ),
    },
  ];

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

  const handleFilterChange = (groupId: string, optionId: string) => {
    setActiveFilters((prev) => {
      const current = prev[groupId] || [];
      const updated = current.includes(optionId)
        ? current.filter((id) => id !== optionId)
        : [...current, optionId];
      return { ...prev, [groupId]: updated };
    });
  };

  const getFilteredData = (type: 'writing' | 'speaking') => {
    return mockCriteria.filter((item: AdminCriteria) => {
      if (item.type !== type) return false;
      if (activeFilters.status.length > 0) {
        return activeFilters.status.includes(item.status);
      }
      return true;
    });
  };

  const handleAddCriterion = () => {
    analytics.track('admin_criteria_changed', { action: 'create' });
    setToast({ variant: 'success', message: 'Criterion saved successfully.' });
    setIsModalOpen(false);
  };

  if (!isAuthenticated || role !== 'admin') return null;

  const writingData = getFilteredData('writing');
  const speakingData = getFilteredData('speaking');

  return (
    <div className="space-y-6 max-w-7xl mx-auto" role="main" aria-label="Marking Criteria">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">Marking Criteria</h1>
          <p className="text-sm text-slate-500 mt-1">Manage grading rubrics for writing and speaking tests.</p>
        </div>
        <Button onClick={() => setIsModalOpen(true)}>
          Add Criterion
        </Button>
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<Target className="w-12 h-12 text-muted" />}
            title="No Criteria Defined"
            description="Add your first criterion to start configuring rubrics."
            action={{ label: 'Add Criterion', onClick: () => setIsModalOpen(true) }}
          />
        }
      >
        <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden">
          <Tabs 
            tabs={[
              { id: 'writing', label: 'Writing Criteria', count: mockCriteria.filter((c: AdminCriteria) => c.type === 'writing').length },
              { id: 'speaking', label: 'Speaking Criteria', count: mockCriteria.filter((c: AdminCriteria) => c.type === 'speaking').length }
            ]} 
            activeTab={activeTab} 
            onChange={setActiveTab}
            className="px-4 pt-2 bg-slate-50"
          />
          
          <FilterBar 
            groups={filterGroups} 
            selected={activeFilters}
            onChange={handleFilterChange} 
            className="border-b border-slate-200 p-4" 
          />

          <TabPanel id="writing" activeTab={activeTab}>
            {writingData.length === 0 ? (
              <EmptyState title="No Writing Criteria" description="No criteria match the current filter." />
            ) : (
              <DataTable columns={columns} data={writingData} keyExtractor={(item: AdminCriteria) => item.id} />
            )}
          </TabPanel>
          
          <TabPanel id="speaking" activeTab={activeTab}>
            {speakingData.length === 0 ? (
              <EmptyState title="No Speaking Criteria" description="No criteria match the current filter." />
            ) : (
              <DataTable columns={columns} data={speakingData} keyExtractor={(item: AdminCriteria) => item.id} />
            )}
          </TabPanel>
        </div>
      </AsyncStateWrapper>

      <Modal open={isModalOpen} onClose={() => setIsModalOpen(false)} title="Add Criterion">
        <div className="space-y-4 py-4">
          <Input label="Criterion Name" placeholder="e.g., Purpose" />
          <Select 
            label="Type" 
            options={[
              { value: 'writing', label: 'Writing Task' },
              { value: 'speaking', label: 'Speaking Roleplay' }
            ]} 
          />
          <Input label="Weight (Points)" type="number" placeholder="7" min={1} max={10} />
          <Textarea label="Description" placeholder="Briefly describe what this criterion evaluates..." />
          
          <div className="flex justify-end gap-3 pt-4 border-t border-slate-100 mt-6">
            <Button variant="outline" onClick={() => setIsModalOpen(false)}>Cancel</Button>
            <Button onClick={handleAddCriterion}>Save Criterion</Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
