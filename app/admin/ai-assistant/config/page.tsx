'use client';

import { useCallback, useEffect, useState } from 'react';
import { Save, ShieldCheck } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
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
      // Only the model and tool grants have storage in this schema; the
      // response reports exactly what was applied so the toast never implies
      // that token caps or rate limits were persisted when they were not.
      const result = await apiClient.post<{ applied?: string[]; message?: string }>(
        '/v1/admin/ai-assistant/config',
        config
      );
      setToast({
        variant: 'success',
        message: result?.message ?? 'Configuration saved successfully',
      });
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

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'AI Assistant', href: '/admin' },
    { label: 'Configuration' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="AI Assistant Configuration" eyebrow="AI Assistant" breadcrumbs={breadcrumbs}>
        <EmptyState
          title="Admin access required"
          description="Sign in with an admin account to view this page."
          illustration={<ShieldCheck className="h-8 w-8" />}
        />
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      title="AI Assistant Configuration"
      description="Configure per-role model selection, token limits, tool access, system prompts, and rate limiting."
      eyebrow="AI Assistant"
      breadcrumbs={breadcrumbs}
      actions={
        <Button variant="primary" onClick={handleSave} disabled={saving || !config} size="sm">
          <Save className="h-4 w-4" />
          {saving ? 'Saving…' : 'Save Configuration'}
        </Button>
      }
    >
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AsyncStateWrapper
        status={status}
        errorMessage="AI Assistant configuration isn't available yet. This admin surface hasn't been built on the backend, so there's nothing to load or save here. Use the AI Assistant monitoring and safety pages in the meantime."
      >
        {config && (
          <>
            <SettingsSection title="Section" description="Choose what to configure.">
              <Tabs tabs={TABS} activeTab={activeTab} onChange={setActiveTab} />
            </SettingsSection>

            {activeTab === 'models' && (
              <div className="space-y-4">
                {config.roles.map((rc) => (
                  <Card key={rc.role}>
                    <CardContent className="p-4">
                      <div className="mb-3 flex items-center gap-2">
                        <Badge variant="primary" intensity="tinted">{rc.role}</Badge>
                        <span className="text-sm font-bold text-admin-fg-strong">Model & Token Limits</span>
                      </div>
                      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
                        <div>
                          <label className="mb-1 block text-xs font-medium text-admin-fg-muted">Model</label>
                          <Select
                            value={rc.model}
                            onChange={(e) => updateRoleConfig(rc.role, { model: e.target.value })}
                            className="h-8 text-xs"
                            options={config.availableModels.map((m) => ({ value: m, label: m }))}
                          />
                        </div>
                        <div>
                          <label className="mb-1 block text-xs font-medium text-admin-fg-muted">Max Tokens/Request</label>
                          <Input
                            type="number"
                            value={rc.maxTokensPerRequest}
                            onChange={(e) => updateRoleConfig(rc.role, { maxTokensPerRequest: Number(e.target.value) })}
                            className="h-8 text-xs"
                          />
                        </div>
                        <div>
                          <label className="mb-1 block text-xs font-medium text-admin-fg-muted">Max Tokens/Day</label>
                          <Input
                            type="number"
                            value={rc.maxTokensPerDay}
                            onChange={(e) => updateRoleConfig(rc.role, { maxTokensPerDay: Number(e.target.value) })}
                            className="h-8 text-xs"
                          />
                        </div>
                        <div>
                          <label className="mb-1 block text-xs font-medium text-admin-fg-muted">Max Iterations</label>
                          <Input
                            type="number"
                            value={rc.maxIterations}
                            onChange={(e) => updateRoleConfig(rc.role, { maxIterations: Number(e.target.value) })}
                            className="h-8 text-xs"
                          />
                        </div>
                      </div>
                    </CardContent>
                  </Card>
                ))}
              </div>
            )}

            {activeTab === 'tools' && (
              <div className="space-y-4">
                {config.roles.map((rc) => (
                  <Card key={rc.role}>
                    <CardContent className="p-4">
                      <div className="mb-3 flex items-center gap-2">
                        <Badge variant="primary" intensity="tinted">{rc.role}</Badge>
                        <span className="text-sm font-bold text-admin-fg-strong">Available Tools</span>
                      </div>
                      <div className="flex flex-wrap gap-2">
                        {config.availableTools.map((tool) => {
                          const enabled = rc.enabledTools.includes(tool);
                          return (
                            <button
                              key={tool}
                              onClick={() => toggleTool(rc.role, tool)}
                              className={`rounded-admin border px-3 py-1.5 text-xs font-medium transition-colors ${
                                enabled
                                  ? 'border-[var(--admin-success)] bg-[var(--admin-success-tint)] text-[var(--admin-success)]'
                                  : 'border-admin-border bg-admin-bg-subtle text-admin-fg-muted hover:border-admin-fg-muted'
                              }`}
                            >
                              {tool}
                            </button>
                          );
                        })}
                      </div>
                    </CardContent>
                  </Card>
                ))}
              </div>
            )}

            {activeTab === 'prompts' && (
              <div className="space-y-4">
                {config.roles.map((rc) => (
                  <Card key={rc.role}>
                    <CardContent className="p-4">
                      <div className="mb-3 flex items-center gap-2">
                        <Badge variant="primary" intensity="tinted">{rc.role}</Badge>
                        <span className="text-sm font-bold text-admin-fg-strong">System Prompt</span>
                      </div>
                      <textarea
                        value={rc.systemPrompt}
                        onChange={(e) => updateRoleConfig(rc.role, { systemPrompt: e.target.value })}
                        rows={8}
                        className="w-full rounded-admin border border-admin-border bg-admin-bg-surface p-3 text-sm text-admin-fg-strong placeholder:text-admin-fg-muted focus:border-[var(--admin-primary)] focus:outline-none focus:ring-1 focus:ring-[var(--admin-primary)]"
                        placeholder={`System prompt for ${rc.role} role…`}
                      />
                      <p className="mt-1 text-xs text-admin-fg-muted">{rc.systemPrompt.length} characters</p>
                    </CardContent>
                  </Card>
                ))}
              </div>
            )}

            {activeTab === 'rate-limits' && (
              <div className="space-y-4">
                {config.roles.map((rc) => (
                  <Card key={rc.role}>
                    <CardContent className="p-4">
                      <div className="mb-3 flex items-center gap-2">
                        <Badge variant="primary" intensity="tinted">{rc.role}</Badge>
                        <span className="text-sm font-bold text-admin-fg-strong">Rate Limiting</span>
                      </div>
                      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                        <div>
                          <label className="mb-1 block text-xs font-medium text-admin-fg-muted">Requests per Minute</label>
                          <Input
                            type="number"
                            value={rc.rateLimitPerMinute}
                            onChange={(e) => updateRoleConfig(rc.role, { rateLimitPerMinute: Number(e.target.value) })}
                            className="h-8 text-xs"
                          />
                        </div>
                        <div>
                          <label className="mb-1 block text-xs font-medium text-admin-fg-muted">Requests per Hour</label>
                          <Input
                            type="number"
                            value={rc.rateLimitPerHour}
                            onChange={(e) => updateRoleConfig(rc.role, { rateLimitPerHour: Number(e.target.value) })}
                            className="h-8 text-xs"
                          />
                        </div>
                      </div>
                    </CardContent>
                  </Card>
                ))}
              </div>
            )}
          </>
        )}
      </AsyncStateWrapper>
    </AdminSettingsLayout>
  );
}
