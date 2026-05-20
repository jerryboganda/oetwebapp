'use client';

import { useCallback, useEffect, useState } from 'react';
import { Save, ShieldCheck, Plus, Trash2 } from 'lucide-react';
import { AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-error';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Tabs } from '@/components/ui/tabs';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { apiClient } from '@/lib/api';

interface RoleConfig {
  role: string;
  model: string;
  maxTokensPerRequest: number;
  maxTokensPerDay: number;
  systemPrompt: string;
  maxIterations: number;
  enabledTools: string[];
  rateLimitPerMinute: number;
  rateLimitPerHour: number;
}

interface AssistantConfig {
  roles: RoleConfig[];
  availableModels: string[];
  availableTools: string[];
}

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const DEFAULT_ROLES = ['learner', 'expert', 'admin'];
const TABS = [
  { id: 'models', label: 'Models & Limits' },
  { id: 'tools', label: 'Tool Access' },
  { id: 'prompts', label: 'System Prompts' },
  { id: 'rate-limits', label: 'Rate Limiting' },
];

export default function AiAssistantConfigPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [config, setConfig] = useState<AssistantConfig | null>(null);
  const [activeTab, setActiveTab] = useState('models');
  const [saving, setSaving] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const loadConfig = useCallback(async () => {
    try {
      setStatus('loading');
      const data = await apiClient.get<AssistantConfig>('/v1/admin/ai-assistant/config');
      setConfig(data);
      setStatus('success');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    if (isAuthenticated && role === 'admin') {
      loadConfig();
    }
  }, [isAuthenticated, role, loadConfig]);

  const handleSave = useCallback(async () => {
    if (!config) return;
    try {
      setSaving(true);
      await apiClient.post('/v1/admin/ai-assistant/config', config);
      setToast({ variant: 'success', message: 'Configuration saved successfully' });
    } catch {
      setToast({ variant: 'error', message: 'Failed to save configuration' });
    } finally {
      setSaving(false);
    }
  }, [config]);

  const updateRoleConfig = useCallback((roleKey: string, update: Partial<RoleConfig>) => {
    setConfig((prev) => {
      if (!prev) return prev;
      return {
        ...prev,
        roles: prev.roles.map((r) => (r.role === roleKey ? { ...r, ...update } : r)),
      };
    });
  }, []);

  const toggleTool = useCallback((roleKey: string, tool: string) => {
    setConfig((prev) => {
      if (!prev) return prev;
      return {
        ...prev,
        roles: prev.roles.map((r) => {
          if (r.role !== roleKey) return r;
          const tools = r.enabledTools.includes(tool)
            ? r.enabledTools.filter((t) => t !== tool)
            : [...r.enabledTools, tool];
          return { ...r, enabledTools: tools };
        }),
      };
    });
  }, []);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <EmptyState icon={<ShieldCheck className="w-8 h-8" />} title="Admin access required" description="Sign in with an admin account to view this page." />
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AsyncStateWrapper status={status} onRetry={loadConfig}>
        {config && (
          <>
            <div className="flex items-center justify-between">
              <Tabs tabs={TABS} activeTab={activeTab} onChange={setActiveTab} />
              <Button variant="primary" onClick={handleSave} disabled={saving} className="gap-1.5">
                <Save className="h-4 w-4" />
                {saving ? 'Saving…' : 'Save Configuration'}
              </Button>
            </div>

            {activeTab === 'models' && (
              <div className="space-y-3">
                {config.roles.map((rc) => (
                  <AdminRoutePanel key={rc.role}>
                    <div className="p-4">
                      <div className="mb-3 flex items-center gap-2">
                        <Badge variant="info">{rc.role}</Badge>
                        <span className="text-sm font-bold text-admin-text">Model & Token Limits</span>
                      </div>
                      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
                        <div>
                          <label className="mb-1 block text-xs font-medium text-admin-text-muted">Model</label>
                          <Select
                            value={rc.model}
                            onChange={(e) => updateRoleConfig(rc.role, { model: e.target.value })}
                            className="h-8 text-xs"
                            options={config.availableModels.map((m) => ({ value: m, label: m }))}
                          />
                        </div>
                        <div>
                          <label className="mb-1 block text-xs font-medium text-admin-text-muted">Max Tokens/Request</label>
                          <Input
                            type="number"
                            value={rc.maxTokensPerRequest}
                            onChange={(e) => updateRoleConfig(rc.role, { maxTokensPerRequest: Number(e.target.value) })}
                            className="h-8 text-xs"
                          />
                        </div>
                        <div>
                          <label className="mb-1 block text-xs font-medium text-admin-text-muted">Max Tokens/Day</label>
                          <Input
                            type="number"
                            value={rc.maxTokensPerDay}
                            onChange={(e) => updateRoleConfig(rc.role, { maxTokensPerDay: Number(e.target.value) })}
                            className="h-8 text-xs"
                          />
                        </div>
                        <div>
                          <label className="mb-1 block text-xs font-medium text-admin-text-muted">Max Iterations</label>
                          <Input
                            type="number"
                            value={rc.maxIterations}
                            onChange={(e) => updateRoleConfig(rc.role, { maxIterations: Number(e.target.value) })}
                            className="h-8 text-xs"
                          />
                        </div>
                      </div>
                    </div>
                  </AdminRoutePanel>
                ))}
              </div>
            )}

            {activeTab === 'tools' && (
              <div className="space-y-3">
                {config.roles.map((rc) => (
                  <AdminRoutePanel key={rc.role}>
                    <div className="p-4">
                      <div className="mb-3 flex items-center gap-2">
                        <Badge variant="info">{rc.role}</Badge>
                        <span className="text-sm font-bold text-admin-text">Available Tools</span>
                      </div>
                      <div className="flex flex-wrap gap-2">
                        {config.availableTools.map((tool) => {
                          const enabled = rc.enabledTools.includes(tool);
                          return (
                            <button
                              key={tool}
                              onClick={() => toggleTool(rc.role, tool)}
                              className={`rounded-lg border px-3 py-1.5 text-xs font-medium transition-colors ${
                                enabled
                                  ? 'border-emerald-500/40 bg-emerald-500/15 text-emerald-300'
                                  : 'border-admin-border bg-admin-surface-raised text-admin-text-muted hover:border-admin-text-muted'
                              }`}
                            >
                              {tool}
                            </button>
                          );
                        })}
                      </div>
                    </div>
                  </AdminRoutePanel>
                ))}
              </div>
            )}

            {activeTab === 'prompts' && (
              <div className="space-y-3">
                {config.roles.map((rc) => (
                  <AdminRoutePanel key={rc.role}>
                    <div className="p-4">
                      <div className="mb-3 flex items-center gap-2">
                        <Badge variant="info">{rc.role}</Badge>
                        <span className="text-sm font-bold text-admin-text">System Prompt</span>
                      </div>
                      <textarea
                        value={rc.systemPrompt}
                        onChange={(e) => updateRoleConfig(rc.role, { systemPrompt: e.target.value })}
                        rows={8}
                        className="w-full rounded-lg border border-admin-border bg-admin-surface-raised p-3 text-sm text-admin-text placeholder:text-admin-text-muted focus:border-violet-500 focus:outline-none focus:ring-1 focus:ring-violet-500"
                        placeholder={`System prompt for ${rc.role} role…`}
                      />
                      <p className="mt-1 text-xs text-admin-text-muted">
                        {rc.systemPrompt.length} characters
                      </p>
                    </div>
                  </AdminRoutePanel>
                ))}
              </div>
            )}

            {activeTab === 'rate-limits' && (
              <div className="space-y-3">
                {config.roles.map((rc) => (
                  <AdminRoutePanel key={rc.role}>
                    <div className="p-4">
                      <div className="mb-3 flex items-center gap-2">
                        <Badge variant="info">{rc.role}</Badge>
                        <span className="text-sm font-bold text-admin-text">Rate Limiting</span>
                      </div>
                      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                        <div>
                          <label className="mb-1 block text-xs font-medium text-admin-text-muted">Requests per Minute</label>
                          <Input
                            type="number"
                            value={rc.rateLimitPerMinute}
                            onChange={(e) => updateRoleConfig(rc.role, { rateLimitPerMinute: Number(e.target.value) })}
                            className="h-8 text-xs"
                          />
                        </div>
                        <div>
                          <label className="mb-1 block text-xs font-medium text-admin-text-muted">Requests per Hour</label>
                          <Input
                            type="number"
                            value={rc.rateLimitPerHour}
                            onChange={(e) => updateRoleConfig(rc.role, { rateLimitPerHour: Number(e.target.value) })}
                            className="h-8 text-xs"
                          />
                        </div>
                      </div>
                    </div>
                  </AdminRoutePanel>
                ))}
              </div>
            )}
          </>
        )}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
