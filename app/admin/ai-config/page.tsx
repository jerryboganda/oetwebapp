'use client';

import { useState, useEffect } from 'react';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { FilterBar, FilterGroup } from '@/components/ui/filter-bar';
import { DataTable, Column } from '@/components/ui/data-table';
import { mockAIConfigs, AdminAIConfig } from '@/lib/mock-admin-data';
import { Badge } from '@/components/ui/badge';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Cpu } from 'lucide-react';
import { analytics } from '@/lib/analytics';

export default function AIConfigPage() {
  const { isAuthenticated, role } = useAdminAuth();
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

  const columns: Column<AdminAIConfig>[] = [
    {
      key: 'model',
      header: 'Model & Provider',
      render: (item) => (
        <div>
          <div className="font-medium text-slate-900">{item.model}</div>
          <div className="text-sm text-slate-500">{item.provider}</div>
        </div>
      ),
    },
    {
      key: 'taskType',
      header: 'Task Type',
      render: (item) => <div className="text-slate-600">{item.taskType}</div>,
    },
    {
      key: 'promptLabel',
      header: 'Prompt / Config Label',
      render: (item) => <div className="text-slate-600 font-mono text-xs">{item.promptLabel}</div>,
    },
    {
      key: 'confidenceThreshold',
      header: 'Threshold',
      render: (item) => <div className="text-slate-600 font-mono">{(item.confidenceThreshold * 100).toFixed(0)}%</div>,
    },
    {
      key: 'routingRule',
      header: 'Confidence Routing',
      render: (item) => <div className="text-slate-600 text-sm">{item.routingRule}</div>,
    },
    {
      key: 'experimentFlag',
      header: 'Experiment Flag',
      render: (item) => item.experimentFlag ? (
        <Badge variant="warning" className="font-mono text-xs">{item.experimentFlag}</Badge>
      ) : (
        <span className="text-slate-400 text-sm">—</span>
      ),
    },
    {
      key: 'accuracy',
      header: 'Accuracy',
      render: (item) => (
        <div className="text-slate-600 font-mono">{(item.accuracy * 100).toFixed(1)}%</div>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (item) => (
        <Badge variant={item.status === 'active' ? 'success' : item.status === 'testing' ? 'warning' : 'muted'}>
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
        { id: 'testing', label: 'Testing' },
        { id: 'deprecated', label: 'Deprecated' },
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

  const filteredData = mockAIConfigs.filter((item) => {
    if (activeFilters.status.length > 0) {
      return activeFilters.status.includes(item.status);
    }
    return true;
  });

  const handleSaveConfig = () => {
    analytics.track('admin_ai_config_changed', { action: 'create' });
    setToast({ variant: 'success', message: 'AI configuration saved.' });
    setIsModalOpen(false);
  };

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <div className="space-y-6 max-w-7xl mx-auto" role="main" aria-label="AI Configuration">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold text-slate-900">AI Configuration</h1>
          <p className="text-sm text-slate-500 mt-1">Manage AI models, prompts, thresholds, and confidence routing.</p>
        </div>
        <Button onClick={() => setIsModalOpen(true)}>
          New Configuration
        </Button>
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<Cpu className="w-12 h-12 text-muted" />}
            title="No AI Configurations"
            description="Add your first AI evaluation configuration."
            action={{ label: 'New Configuration', onClick: () => setIsModalOpen(true) }}
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
              icon={<Cpu className="w-12 h-12 text-muted" />}
              title="No Matching Configurations"
              description="Adjust your filters to see AI configurations."
              action={{ label: 'Clear Filters', onClick: () => setActiveFilters({ status: [] }) }}
            />
          ) : (
            <DataTable columns={columns} data={filteredData} keyExtractor={(item) => item.id} />
          )}
        </div>
      </AsyncStateWrapper>

      <Modal open={isModalOpen} onClose={() => setIsModalOpen(false)} title="New AI Configuration">
        <div className="space-y-4 py-4">
          <Input label="Model Name" placeholder="e.g., gpt-4o" />
          <Select 
            label="Provider" 
            options={[
              { value: 'openai', label: 'OpenAI' },
              { value: 'anthropic', label: 'Anthropic' },
              { value: 'google', label: 'Google' }
            ]} 
          />
          <Select 
            label="Task Target" 
            options={[
              { value: 'writing', label: 'Writing Grading' },
              { value: 'speaking', label: 'Speaking Analysis' },
              { value: 'stt', label: 'Speech-to-Text' }
            ]} 
          />
          <Input label="Confidence Threshold" type="number" placeholder="0.85" min={0} max={1} />
          <Input label="Prompt / Config Label" placeholder="e.g., writing-grade-v4.2" />
          <Select 
            label="Initial Status" 
            options={[
              { value: 'testing', label: 'Testing' },
              { value: 'active', label: 'Active (Production)' }
            ]} 
          />
          
          <div className="flex justify-end gap-3 pt-4 border-t border-slate-100 mt-6">
            <Button variant="outline" onClick={() => setIsModalOpen(false)}>Cancel</Button>
            <Button onClick={handleSaveConfig}>Save Configuration</Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
