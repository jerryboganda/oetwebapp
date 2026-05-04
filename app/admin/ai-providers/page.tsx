'use client';

import { useCallback, useEffect, useState } from 'react';
import { Server } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { Input, Select } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchAiProviders,
  createAiProvider,
  updateAiProvider,
  deactivateAiProvider,
  type AiProviderRow,
} from '@/lib/ai-management-api';

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

function fmtUsd(n: number): string {
  return `$${n.toFixed(2)}`;
}

const PRESETS: Record<string, Partial<AiProviderRow & { apiKey?: string }>> = {
  openai: {
    code: 'openai',
    name: 'OpenAI Platform',
    dialect: 'OpenAiCompatible',
    baseUrl: 'https://api.openai.com/v1',
    defaultModel: 'gpt-4o',
    pricePer1kPromptTokens: 0.0025,
    pricePer1kCompletionTokens: 0.01,
    retryCount: 2,
    circuitBreakerThreshold: 5,
    circuitBreakerWindowSeconds: 30,
    failoverPriority: 10,
    isActive: true,
  },
  anthropic: {
    code: 'anthropic',
    name: 'Anthropic',
    dialect: 'Anthropic',
    baseUrl: 'https://api.anthropic.com',
    defaultModel: 'claude-3-5-sonnet-20241022',
    pricePer1kPromptTokens: 0.003,
    pricePer1kCompletionTokens: 0.015,
    retryCount: 2,
    circuitBreakerThreshold: 5,
    circuitBreakerWindowSeconds: 30,
    failoverPriority: 20,
    isActive: true,
  },
  google: {
    code: 'google',
    name: 'Google Gemini (OpenAI compat)',
    dialect: 'OpenAiCompatible',
    baseUrl: 'https://generativelanguage.googleapis.com/v1beta/openai',
    defaultModel: 'gemini-2.0-flash',
    pricePer1kPromptTokens: 0.00035,
    pricePer1kCompletionTokens: 0.0014,
    retryCount: 2,
    circuitBreakerThreshold: 5,
    circuitBreakerWindowSeconds: 30,
    failoverPriority: 30,
    isActive: true,
  },
  'nvidia-nim': {
    code: 'nvidia-nim',
    name: 'NVIDIA NIM',
    dialect: 'OpenAiCompatible',
    baseUrl: 'https://integrate.api.nvidia.com/v1',
    defaultModel: 'minimaxai/minimax-m2.7',
    pricePer1kPromptTokens: 0,
    pricePer1kCompletionTokens: 0,
    retryCount: 2,
    circuitBreakerThreshold: 5,
    circuitBreakerWindowSeconds: 30,
    failoverPriority: 40,
    isActive: true,
  },
  groq: {
    code: 'groq',
    name: 'Groq',
    dialect: 'OpenAiCompatible',
    baseUrl: 'https://api.groq.com/openai/v1',
    defaultModel: 'llama-3.3-70b-versatile',
    pricePer1kPromptTokens: 0.00059,
    pricePer1kCompletionTokens: 0.00079,
    retryCount: 2,
    circuitBreakerThreshold: 5,
    circuitBreakerWindowSeconds: 30,
    failoverPriority: 50,
    isActive: true,
  },
  deepseek: {
    code: 'deepseek',
    name: 'DeepSeek',
    dialect: 'OpenAiCompatible',
    baseUrl: 'https://api.deepseek.com/v1',
    defaultModel: 'deepseek-chat',
    pricePer1kPromptTokens: 0.00027,
    pricePer1kCompletionTokens: 0.0011,
    retryCount: 2,
    circuitBreakerThreshold: 5,
    circuitBreakerWindowSeconds: 30,
    failoverPriority: 60,
    isActive: true,
  },
  together: {
    code: 'together',
    name: 'Together AI',
    dialect: 'OpenAiCompatible',
    baseUrl: 'https://api.together.xyz/v1',
    defaultModel: 'meta-llama/Llama-3.3-70B-Instruct-Turbo',
    pricePer1kPromptTokens: 0.00088,
    pricePer1kCompletionTokens: 0.00088,
    retryCount: 2,
    circuitBreakerThreshold: 5,
    circuitBreakerWindowSeconds: 30,
    failoverPriority: 70,
    isActive: true,
  },
  openrouter: {
    code: 'openrouter',
    name: 'OpenRouter',
    dialect: 'OpenAiCompatible',
    baseUrl: 'https://openrouter.ai/api/v1',
    defaultModel: 'openai/gpt-4o',
    pricePer1kPromptTokens: 0,
    pricePer1kCompletionTokens: 0,
    retryCount: 2,
    circuitBreakerThreshold: 5,
    circuitBreakerWindowSeconds: 30,
    failoverPriority: 80,
    isActive: true,
  },
  azure: {
    code: 'azure-openai',
    name: 'Azure OpenAI',
    dialect: 'OpenAiCompatible',
    baseUrl: 'https://{your-resource}.openai.azure.com/openai/deployments/{deployment-id}/chat/completions?api-version=2024-08-01-preview',
    defaultModel: 'gpt-4o',
    pricePer1kPromptTokens: 0.005,
    pricePer1kCompletionTokens: 0.015,
    retryCount: 2,
    circuitBreakerThreshold: 5,
    circuitBreakerWindowSeconds: 30,
    failoverPriority: 90,
    isActive: true,
  },
  'cloudflare-workers-ai': {
    code: 'cloudflare-workers-ai',
    name: 'Cloudflare Workers AI (native)',
    dialect: 'Cloudflare',
    // Replace {ACCOUNT_ID} with your Cloudflare account id. The path stops
    // at /ai — the provider appends /run/{model} per call.
    baseUrl: 'https://api.cloudflare.com/client/v4/accounts/{ACCOUNT_ID}/ai',
    defaultModel: '@cf/meta/llama-3.1-8b-instruct',
    // Workers AI pricing (Apr 2025): $0.011 / 1k input tokens, $0.011 / 1k
    // output tokens for Llama 3.1 8B; varies per model. Free tier: 10k
    // neurons/day. Adjust per the model you set as default.
    pricePer1kPromptTokens: 0.011,
    pricePer1kCompletionTokens: 0.011,
    retryCount: 2,
    circuitBreakerThreshold: 5,
    circuitBreakerWindowSeconds: 30,
    failoverPriority: 100,
    isActive: true,
  },
  'cloudflare-openai-compat': {
    code: 'cloudflare-openai-compat',
    name: 'Cloudflare Workers AI (OpenAI compat)',
    dialect: 'OpenAiCompatible',
    // OpenAI-compatible endpoint — works through the existing registry
    // dispatch path. Use the native preset above for full CF feature access.
    baseUrl: 'https://api.cloudflare.com/client/v4/accounts/{ACCOUNT_ID}/ai/v1',
    defaultModel: '@cf/meta/llama-3.1-8b-instruct',
    pricePer1kPromptTokens: 0.011,
    pricePer1kCompletionTokens: 0.011,
    retryCount: 2,
    circuitBreakerThreshold: 5,
    circuitBreakerWindowSeconds: 30,
    failoverPriority: 110,
    isActive: true,
  },
};

export default function AiProvidersPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<AiProviderRow[]>([]);
  const [editing, setEditing] = useState<(AiProviderRow & { apiKey?: string }) | null>(null);
  const [creating, setCreating] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const load = useCallback(async () => {
    try { setRows(await fetchAiProviders()); setStatus('success'); } catch { setStatus('error'); }
  }, []);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const save = async () => {
    if (!editing) return;
    try {
      const payload = { ...editing } as Record<string, unknown>;
      if (creating) await createAiProvider(payload);
      else await updateAiProvider(editing.id, payload);
      setToast({ variant: 'success', message: creating ? 'Provider registered.' : 'Provider updated.' });
      setEditing(null); setCreating(false); await load();
    } catch (e) { setToast({ variant: 'error', message: `Save failed: ${(e as Error).message}` }); }
  };

  const deactivate = async (id: string) => {
    try { await deactivateAiProvider(id); setToast({ variant: 'success', message: 'Provider deactivated.' }); await load(); }
    catch (e) { setToast({ variant: 'error', message: `Failed: ${(e as Error).message}` }); }
  };

  const applyPreset = (key: string) => {
    const preset = PRESETS[key];
    if (!preset) return;
    setEditing((prev) =>
      prev
        ? {
            ...prev,
            code: preset.code ?? prev.code,
            name: preset.name ?? prev.name,
            dialect: (preset.dialect as AiProviderRow['dialect']) ?? prev.dialect,
            baseUrl: preset.baseUrl ?? prev.baseUrl,
            defaultModel: preset.defaultModel ?? prev.defaultModel,
            pricePer1kPromptTokens: preset.pricePer1kPromptTokens ?? prev.pricePer1kPromptTokens,
            pricePer1kCompletionTokens: preset.pricePer1kCompletionTokens ?? prev.pricePer1kCompletionTokens,
            retryCount: preset.retryCount ?? prev.retryCount,
            circuitBreakerThreshold: preset.circuitBreakerThreshold ?? prev.circuitBreakerThreshold,
            circuitBreakerWindowSeconds: preset.circuitBreakerWindowSeconds ?? prev.circuitBreakerWindowSeconds,
            failoverPriority: preset.failoverPriority ?? prev.failoverPriority,
            isActive: preset.isActive ?? prev.isActive,
          }
        : null,
    );
  };

  const columns: Column<AiProviderRow>[] = [
    { key: 'code', header: 'Code', render: (p) => <span className="font-mono">{p.code}</span> },
    { key: 'name', header: 'Name', render: (p) => p.name },
    { key: 'd', header: 'Dialect', render: (p) => p.dialect },
    { key: 'u', header: 'Base URL', render: (p) => <span className="text-xs text-muted">{p.baseUrl}</span> },
    { key: 'k', header: 'Key', render: (p) => <span className="font-mono text-xs">{p.apiKeyHint}</span> },
    { key: 'pr', header: 'Price 1k in/out', render: (p) => `${fmtUsd(p.pricePer1kPromptTokens)}/${fmtUsd(p.pricePer1kCompletionTokens)}` },
    { key: 'pri', header: 'Priority', render: (p) => p.failoverPriority },
    { key: 'a', header: 'Active', render: (p) => <Badge variant={p.isActive ? 'success' : 'muted'}>{p.isActive ? 'Yes' : 'No'}</Badge> },
    {
      key: 'acts', header: 'Actions', render: (p) => (
        <div className="flex gap-2">
          <Button variant="ghost" size="sm" onClick={() => { setEditing({ ...p, apiKey: '' }); setCreating(false); }}>Edit</Button>
          {p.isActive && <Button variant="ghost" size="sm" onClick={() => void deactivate(p.id)}>Deactivate</Button>}
        </div>
      ),
    },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <EmptyState icon={<Server className="w-8 h-8" />} title="Admin access required" description="Sign in with an admin account to view this page." />
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        icon={<Server className="w-6 h-6" />}
        title="AI Providers"
        description="Register, rotate, and manage platform AI provider credentials. OpenAI-compatible endpoints (NVIDIA NIM, Groq, DeepSeek, Together, OpenRouter, Azure, …) are supported via the same dialect."
      />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AsyncStateWrapper status={status}>
        <div className="flex justify-end mt-4">
          <Button variant="primary" onClick={() => {
            setCreating(true);
            setEditing({
              id: '', code: '', name: '', dialect: 'OpenAiCompatible',
              baseUrl: '', apiKeyHint: '', defaultModel: '', allowedModelsCsv: '',
              pricePer1kPromptTokens: 0, pricePer1kCompletionTokens: 0,
              retryCount: 2, circuitBreakerThreshold: 5, circuitBreakerWindowSeconds: 30,
              failoverPriority: 100, isActive: true,
              createdAt: '', updatedAt: '', apiKey: '',
            });
          }}>
            + Register provider
          </Button>
        </div>
        <AdminRoutePanel title="Registered providers">
          <DataTable data={rows} columns={columns} keyExtractor={(p) => p.id || p.code} />
        </AdminRoutePanel>
      </AsyncStateWrapper>

      {editing && (
        <Modal open={true} onClose={() => { setEditing(null); setCreating(false); }} title={creating ? 'Register provider' : `Edit ${editing.code}`}>
          <div className="space-y-4">
            {creating && (
              <div className="flex flex-wrap gap-2">
                <span className="text-sm text-muted mr-1">Quick preset:</span>
                {Object.entries(PRESETS).map(([key, preset]) => (
                  <Button key={key} variant="outline" size="sm" onClick={() => applyPreset(key)} title={preset.name}>
                    {preset.name}
                  </Button>
                ))}
              </div>
            )}
            <div className="grid grid-cols-2 gap-4">
              <Input label="Code" value={editing.code} onChange={(e) => setEditing({ ...editing, code: e.target.value })} />
              <Input label="Name" value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} />
              <Select label="Dialect" value={editing.dialect}
                onChange={(e) => setEditing({ ...editing, dialect: e.target.value as AiProviderRow['dialect'] })}
                options={[
                  { value: 'OpenAiCompatible', label: 'OpenAI-compatible' },
                  { value: 'Anthropic', label: 'Anthropic (native)' },
                  { value: 'Cloudflare', label: 'Cloudflare Workers AI (native)' },
                  { value: 'Mock', label: 'Mock (dev only)' },
                ]} />
              <Input label="Base URL" value={editing.baseUrl} onChange={(e) => setEditing({ ...editing, baseUrl: e.target.value })} />
              <Input label={creating ? 'API key' : 'API key (leave blank to keep)'} type="password" value={editing.apiKey ?? ''} onChange={(e) => setEditing({ ...editing, apiKey: e.target.value })} />
              <Input label="Default model" value={editing.defaultModel} onChange={(e) => setEditing({ ...editing, defaultModel: e.target.value })} />
              <Select label="Reasoning effort" value={editing.reasoningEffort ?? ''}
                onChange={(e) => setEditing({ ...editing, reasoningEffort: e.target.value || null })}
                options={[
                  { value: '', label: 'Inherit env default (AI__ReasoningEffort)' },
                  { value: 'low', label: 'low' },
                  { value: 'medium', label: 'medium' },
                  { value: 'high', label: 'high' },
                ]} />
              <Input label="Price / 1k prompt tokens (USD)" type="number" step="0.0001" value={editing.pricePer1kPromptTokens} onChange={(e) => setEditing({ ...editing, pricePer1kPromptTokens: Number(e.target.value) })} />
              <Input label="Price / 1k completion tokens (USD)" type="number" step="0.0001" value={editing.pricePer1kCompletionTokens} onChange={(e) => setEditing({ ...editing, pricePer1kCompletionTokens: Number(e.target.value) })} />
              <Input label="Retry count" type="number" value={editing.retryCount} onChange={(e) => setEditing({ ...editing, retryCount: Number(e.target.value) })} />
              <Input label="Failover priority" type="number" value={editing.failoverPriority} onChange={(e) => setEditing({ ...editing, failoverPriority: Number(e.target.value) })} />
              <label className="col-span-2 flex items-center gap-2">
                <input type="checkbox" checked={editing.isActive} onChange={(e) => setEditing({ ...editing, isActive: e.target.checked })} />
                Active
              </label>
            </div>
            <div className="flex gap-3 mt-4">
              <Button variant="primary" onClick={() => void save()}>Save</Button>
              <Button variant="ghost" onClick={() => { setEditing(null); setCreating(false); }}>Cancel</Button>
            </div>
          </div>
        </Modal>
      )}
    </AdminRouteWorkspace>
  );
}
