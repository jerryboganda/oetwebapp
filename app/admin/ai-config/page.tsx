'use client';

import { useEffect, useMemo, useState } from 'react';
import { Bot, Cpu, Edit3, PlayCircle, Plus, ShieldAlert, Trash2 } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { FilterBar, type FilterGroup } from '@/components/ui/filter-bar';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { activateAdminAIConfig, createAdminAIConfig, updateAdminAIConfig, deleteAdminAIConfig } from '@/lib/api';
import { getAdminAIConfigData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminAIConfig } from '@/lib/types/admin';
import { fetchAiProviders, type AiProviderRow } from '@/lib/ai-management-api';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface AIConfigFormState {
  id: string | null;
  model: string;
  provider: string;
  taskType: string;
  status: AdminAIConfig['status'];
  accuracy: string;
  confidenceThreshold: string;
  routingRule: string;
  experimentFlag: string;
  promptLabel: string;
  confidencePolicyBand: string;
  confidencePolicyMin: string;
  confidencePolicyMax: string;
  confidencePolicyHumanReview: boolean;
  confidencePolicyLearnerLabel: string;
  confidencePolicyProvenanceLabel: string;
  confidencePolicyDisclaimer: string;
}

const defaultFormState: AIConfigFormState = {
  id: null,
  model: '',
  provider: 'openai',
  taskType: 'writing',
  status: 'testing',
  accuracy: '0.85',
  confidenceThreshold: '0.8',
  routingRule: 'Escalate to tutor review below threshold',
  experimentFlag: '',
  promptLabel: '',
  confidencePolicyBand: 'medium',
  confidencePolicyMin: '0.6',
  confidencePolicyMax: '0.8',
  confidencePolicyHumanReview: true,
  confidencePolicyLearnerLabel: 'Medium confidence practice estimate',
  confidencePolicyProvenanceLabel: 'AI-assisted practice evaluation',
  confidencePolicyDisclaimer: 'This is a practice estimate only and not an official exam score.',
};

export default function AIConfigPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [filters, setFilters] = useState<Record<string, string[]>>({ status: [] });
  const [configs, setConfigs] = useState<AdminAIConfig[]>([]);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [form, setForm] = useState<AIConfigFormState>(defaultFormState);
  const [activatingId, setActivatingId] = useState<string | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const [providerOptions, setProviderOptions] = useState<Array<{ value: string; label: string }>>([
    { value: 'openai', label: 'OpenAI' },
    { value: 'anthropic', label: 'Anthropic' },
    { value: 'google', label: 'Google' },
  ]);

  const selectedStatus = filters.status?.[0];

  useEffect(() => {
    let cancelled = false;

    async function loadConfigs() {
      setPageStatus('loading');
      try {
        const [items, providers] = await Promise.all([
          getAdminAIConfigData({ status: selectedStatus }),
          fetchAiProviders().catch(() => [] as AiProviderRow[]),
        ]);
        if (cancelled) return;

        setConfigs(items);
        setPageStatus(items.length > 0 ? 'success' : 'empty');

        // Merge registered providers into the dropdown so custom
        // OpenAI-compatible endpoints (NVIDIA NIM, Groq, …) appear.
        const base = [
          { value: 'openai', label: 'OpenAI' },
          { value: 'anthropic', label: 'Anthropic' },
          { value: 'google', label: 'Google' },
        ];
        const registered = providers.map((p) => ({ value: p.code, label: `${p.name} (${p.code})` }));
        const merged = [...base, ...registered.filter((r) => !base.some((b) => b.value === r.value))];
        setProviderOptions(merged);
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load AI configurations.' });
        }
      }
    }

    loadConfigs();
    return () => {
      cancelled = true;
    };
  }, [selectedStatus]);

  const metrics = useMemo(() => {
    const active = configs.filter((config) => config.status === 'active').length;
    const testing = configs.filter((config) => config.status === 'testing').length;
    const deprecated = configs.filter((config) => config.status === 'deprecated').length;
    const averageAccuracy = configs.length > 0
      ? Math.round((configs.reduce((sum, config) => sum + config.accuracy, 0) / configs.length) * 100)
      : 0;
    const totalEvaluations = configs.reduce((sum, config) => sum + (config.escalationStats?.totalEvaluations ?? 0), 0);
    const totalEscalations = configs.reduce((sum, config) => sum + (config.escalationStats?.escalationsTriggered ?? 0), 0);
    const overallEscalationRate = totalEvaluations > 0 ? (totalEscalations / totalEvaluations) * 100 : 0;

    return { active, testing, deprecated, averageAccuracy, totalEvaluations, totalEscalations, overallEscalationRate };
  }, [configs]);

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

  const columns: Column<AdminAIConfig>[] = [
    {
      key: 'model',
      header: 'Model',
      render: (config) => (
        <div className="space-y-1">
          <p className="font-medium text-navy">{config.model}</p>
          <p className="text-sm text-muted">{config.providerName ?? config.provider}</p>
        </div>
      ),
    },
    {
      key: 'taskType',
      header: 'Task Type',
      render: (config) => <span className="capitalize text-muted">{config.taskType}</span>,
    },
    {
      key: 'promptLabel',
      header: 'Prompt Label',
      render: (config) => <span className="font-mono text-xs text-muted">{config.promptLabel || 'Not labeled'}</span>,
    },
    {
      key: 'routingRule',
      header: 'Routing Rule',
      render: (config) => <span className="text-sm text-muted">{config.routingRule || 'No routing override'}</span>,
    },
    {
      key: 'metrics',
      header: 'Confidence / Accuracy',
      render: (config) => (
        <div className="space-y-1 text-sm text-muted">
          <p>{Math.round(config.confidenceThreshold * 100)}% threshold</p>
          <p>{Math.round(config.accuracy * 100)}% accuracy</p>
        </div>
      ),
    },
    {
      key: 'escalation',
      header: 'Escalation Rate',
      render: (config) => {
        const stats = config.escalationStats;
        if (!stats) {
          return <span className="text-xs text-muted">No data</span>;
        }
        const rate = stats.escalationRatePercent;
        return (
          <div className="space-y-1">
            <Badge variant={rate < 5 ? 'success' : rate < 15 ? 'warning' : 'danger'} size="sm">
              {rate.toFixed(1)}%
            </Badge>
            <p className="text-xs text-muted">{stats.escalationsTriggered} / {stats.totalEvaluations} evals</p>
          </div>
        );
      },
    },
    {
      key: 'status',
      header: 'Status',
      render: (config) => (
        <Badge variant={config.status === 'active' ? 'success' : config.status === 'testing' ? 'warning' : 'muted'}>
          {config.status}
        </Badge>
      ),
    },
    {
      key: 'actions',
      header: '',
      className: 'w-56',
      render: (config) => (
        <div className="flex justify-end gap-2">
          <Button variant="outline" size="sm" onClick={() => openEditModal(config)}>
            <Edit3 className="h-3.5 w-3.5" />
            Edit
          </Button>
          {config.status !== 'active' ? (
            <Button size="sm" onClick={() => handleActivate(config.id)} loading={activatingId === config.id}>
              <PlayCircle className="h-3.5 w-3.5" />
              Activate
            </Button>
          ) : null}
          {config.status === 'deprecated' ? (
            <Button variant="ghost" size="sm" className="text-danger" onClick={() => handleDelete(config.id)}>
              <Trash2 className="h-3.5 w-3.5" />
            </Button>
          ) : null}
        </div>
      ),
    },
  ];

  function openCreateModal() {
    setForm(defaultFormState);
    setIsModalOpen(true);
  }

  function openEditModal(config: AdminAIConfig) {
    const policy = config.confidencePolicy;
    setForm({
      id: config.id,
      model: config.model,
      provider: config.provider,
      taskType: config.taskType,
      status: config.status,
      accuracy: String(config.accuracy),
      confidenceThreshold: String(config.confidenceThreshold),
      routingRule: config.routingRule,
      experimentFlag: config.experimentFlag,
      promptLabel: config.promptLabel,
      confidencePolicyBand: policy?.band ?? 'medium',
      confidencePolicyMin: policy ? String(policy.minThreshold) : '0.6',
      confidencePolicyMax: policy ? String(policy.maxThreshold) : '0.8',
      confidencePolicyHumanReview: policy?.recommendsHumanReview ?? true,
      confidencePolicyLearnerLabel: policy?.learnerLabel ?? 'Medium confidence practice estimate',
      confidencePolicyProvenanceLabel: policy?.provenanceLabel ?? 'AI-assisted practice evaluation',
      confidencePolicyDisclaimer: policy?.disclaimer ?? 'This is a practice estimate only and not an official exam score.',
    });
    setIsModalOpen(true);
  }

  function handleFilterChange(groupId: string, optionId: string) {
    setFilters((current) => ({
      ...current,
      [groupId]: current[groupId]?.includes(optionId) ? [] : [optionId],
    }));
  }

  async function reloadConfigs() {
    const items = await getAdminAIConfigData({ status: selectedStatus });
    setConfigs(items);
    setPageStatus(items.length > 0 ? 'success' : 'empty');
  }

  async function handleActivate(configId: string) {
    setActivatingId(configId);
    try {
      await activateAdminAIConfig(configId);
      await reloadConfigs();
      setToast({ variant: 'success', message: 'AI configuration activated.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to activate this configuration.' });
    } finally {
      setActivatingId(null);
    }
  }

  async function handleDelete(configId: string) {
    if (!window.confirm('Delete this AI configuration permanently?')) return;
    try {
      await deleteAdminAIConfig(configId);
      await reloadConfigs();
      setToast({ variant: 'success', message: 'AI configuration deleted.' });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to delete this configuration.' });
    }
  }

  async function handleSaveConfig() {
    setIsSaving(true);
    try {
      const payload = {
        model: form.model,
        provider: form.provider,
        taskType: form.taskType,
        status: form.status,
        accuracy: Number(form.accuracy || 0),
        confidenceThreshold: Number(form.confidenceThreshold || 0),
        routingRule: form.routingRule,
        experimentFlag: form.experimentFlag,
        promptLabel: form.promptLabel,
        confidencePolicy: {
          band: form.confidencePolicyBand,
          minThreshold: Number(form.confidencePolicyMin || 0),
          maxThreshold: Number(form.confidencePolicyMax || 0),
          recommendsHumanReview: form.confidencePolicyHumanReview,
          learnerLabel: form.confidencePolicyLearnerLabel,
          provenanceLabel: form.confidencePolicyProvenanceLabel,
          disclaimer: form.confidencePolicyDisclaimer,
        },
      };

      if (form.id) {
        await updateAdminAIConfig(form.id, payload);
      } else {
        await createAdminAIConfig(payload);
      }

      await reloadConfigs();
      setIsModalOpen(false);
      setToast({
        variant: 'success',
        message: form.id ? 'AI configuration updated.' : 'AI configuration created.',
      });
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to save this AI configuration.' });
    } finally {
      setIsSaving(false);
    }
  }

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="AI evaluation config">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title="AI Evaluation Config"
        description="Control live grading models, routing thresholds, and experiment linkages with real activation rules."
        actions={
          <Button onClick={openCreateModal} className="gap-2">
            <Plus className="h-4 w-4" />
            New Configuration
          </Button>
        }
      />

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<Cpu className="h-10 w-10 text-muted" />}
            title="No AI configurations yet"
            description="Create the first evaluation configuration so routing, experimentation, and activation are controlled from admin."
            action={{ label: 'New Configuration', onClick: openCreateModal }}
          />
        }
      >
        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
          <AdminRouteSummaryCard label="Active Configs" value={metrics.active} icon={<PlayCircle className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Testing Configs" value={metrics.testing} icon={<Bot className="h-5 w-5" />} tone={metrics.testing > 0 ? 'warning' : 'default'} />
          <AdminRouteSummaryCard label="Deprecated" value={metrics.deprecated} icon={<Cpu className="h-5 w-5" />} />
          <AdminRouteSummaryCard label="Average Accuracy" value={`${metrics.averageAccuracy}%`} icon={<Cpu className="h-5 w-5" />} />
          <AdminRouteSummaryCard
            label="Escalation Rate"
            value={metrics.totalEvaluations > 0 ? `${metrics.overallEscalationRate.toFixed(1)}%` : 'N/A'}
            icon={<ShieldAlert className="h-5 w-5" />}
            tone={metrics.overallEscalationRate > 15 ? 'danger' : metrics.overallEscalationRate > 5 ? 'warning' : 'default'}
          />
        </div>

        <AdminRoutePanel title="Configuration Registry" description="Live model metadata, thresholds, routing rules, and activation controls all come from the admin AI config endpoints.">
          <FilterBar groups={filterGroups} selected={filters} onChange={handleFilterChange} onClear={() => setFilters({ status: [] })} />
          <DataTable columns={columns} data={configs} keyExtractor={(config) => config.id} />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      <Modal open={isModalOpen} onClose={() => setIsModalOpen(false)} title={form.id ? 'Edit AI Configuration' : 'New AI Configuration'}>
        <div className="space-y-4 py-2">
          <Input label="Model Name" value={form.model} onChange={(event) => setForm((current) => ({ ...current, model: event.target.value }))} />
          <div className="flex items-end gap-2">
            <div className="flex-1">
              <Select
                label="Provider"
                value={form.provider}
                onChange={(event) => setForm((current) => ({ ...current, provider: event.target.value }))}
                options={providerOptions}
              />
            </div>
            <Button variant="outline" size="sm" className="mb-0.5" onClick={() => window.open('/admin/ai-providers', '_blank')}>
              Manage providers
            </Button>
          </div>
          <Select
            label="Task Type"
            value={form.taskType}
            onChange={(event) => setForm((current) => ({ ...current, taskType: event.target.value }))}
            options={[
              { value: 'writing', label: 'Writing' },
              { value: 'speaking', label: 'Speaking' },
              { value: 'reading', label: 'Reading' },
              { value: 'listening', label: 'Listening' },
              { value: 'stt', label: 'Speech to Text' },
            ]}
          />
          <div className="grid gap-4 md:grid-cols-2">
            <Input
              label="Confidence Threshold"
              type="number"
              min={0}
              max={1}
              step="0.01"
              value={form.confidenceThreshold}
              onChange={(event) => setForm((current) => ({ ...current, confidenceThreshold: event.target.value }))}
            />
            <Input
              label="Accuracy"
              type="number"
              min={0}
              max={1}
              step="0.01"
              value={form.accuracy}
              onChange={(event) => setForm((current) => ({ ...current, accuracy: event.target.value }))}
            />
          </div>
          <Input label="Prompt Label" value={form.promptLabel} onChange={(event) => setForm((current) => ({ ...current, promptLabel: event.target.value }))} />
          <Input label="Routing Rule" value={form.routingRule} onChange={(event) => setForm((current) => ({ ...current, routingRule: event.target.value }))} />
          <Input label="Experiment Flag" value={form.experimentFlag} onChange={(event) => setForm((current) => ({ ...current, experimentFlag: event.target.value }))} />
          <Select
            label="Status"
            value={form.status}
            onChange={(event) => setForm((current) => ({ ...current, status: event.target.value as AdminAIConfig['status'] }))}
            options={[
              { value: 'testing', label: 'Testing' },
              { value: 'active', label: 'Active' },
              { value: 'deprecated', label: 'Deprecated' },
            ]}
          />

          <div className="border-t border-border pt-4">
            <h4 className="text-sm font-bold text-navy mb-3">Confidence Policy</h4>
            <div className="grid gap-4 md:grid-cols-2">
              <Select
                label="Band"
                value={form.confidencePolicyBand}
                onChange={(event) => setForm((current) => ({ ...current, confidencePolicyBand: event.target.value }))}
                options={[
                  { value: 'high', label: 'High' },
                  { value: 'medium', label: 'Medium' },
                  { value: 'low', label: 'Low' },
                  { value: 'unknown', label: 'Unknown' },
                ]}
              />
              <div className="flex items-center gap-2">
                <input
                  id="humanReview"
                  type="checkbox"
                  checked={form.confidencePolicyHumanReview}
                  onChange={(e) => setForm((current) => ({ ...current, confidencePolicyHumanReview: e.target.checked }))}
                  className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
                />
                <label htmlFor="humanReview" className="text-sm text-navy cursor-pointer">
                  Recommend human review in this band
                </label>
              </div>
              <Input
                label="Min Threshold"
                type="number"
                min={0}
                max={1}
                step="0.01"
                value={form.confidencePolicyMin}
                onChange={(event) => setForm((current) => ({ ...current, confidencePolicyMin: event.target.value }))}
              />
              <Input
                label="Max Threshold"
                type="number"
                min={0}
                max={1}
                step="0.01"
                value={form.confidencePolicyMax}
                onChange={(event) => setForm((current) => ({ ...current, confidencePolicyMax: event.target.value }))}
              />
              <Input
                label="Learner Label"
                value={form.confidencePolicyLearnerLabel}
                onChange={(event) => setForm((current) => ({ ...current, confidencePolicyLearnerLabel: event.target.value }))}
              />
              <Input
                label="Provenance Label"
                value={form.confidencePolicyProvenanceLabel}
                onChange={(event) => setForm((current) => ({ ...current, confidencePolicyProvenanceLabel: event.target.value }))}
              />
            </div>
            <Input
              label="Disclaimer"
              value={form.confidencePolicyDisclaimer}
              onChange={(event) => setForm((current) => ({ ...current, confidencePolicyDisclaimer: event.target.value }))}
              className="mt-3"
            />
          </div>

          <div className="flex justify-end gap-3 border-t border-border pt-4">
            <Button variant="outline" onClick={() => setIsModalOpen(false)}>
              Cancel
            </Button>
            <Button onClick={handleSaveConfig} loading={isSaving}>
              Save Configuration
            </Button>
          </div>
        </div>
      </Modal>
    </AdminRouteWorkspace>
  );
}
