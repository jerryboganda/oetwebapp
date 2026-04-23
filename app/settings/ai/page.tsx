'use client';

import { useCallback, useEffect, useState } from 'react';
import { ArrowLeft, Cpu, KeyRound, Loader2, Shield, Trash2 } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { Skeleton } from '@/components/ui/skeleton';
import {
  fetchMyAiCredentials,
  fetchMyAiPreferences,
  fetchMyAiUsage,
  fetchMyAiCredits,
  revokeMyAiCredential,
  saveMyAiCredential,
  updateMyAiPreferences,
  type AiCredentialItem,
  type AiCredentialMode,
  type AiUserPolicySnapshot,
  type AiCreditBalance,
} from '@/lib/ai-management-api';

const PROVIDER_PRESETS: { code: string; name: string; hint: string }[] = [
  { code: 'openai-platform', name: 'OpenAI Platform', hint: 'platform.openai.com · keys starting sk-…' },
  { code: 'anthropic', name: 'Anthropic', hint: 'console.anthropic.com · keys starting sk-ant-…' },
  { code: 'openrouter', name: 'OpenRouter', hint: 'openrouter.ai · aggregates dozens of models' },
];

export default function AiSettingsPage() {
  const router = useRouter();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [credentials, setCredentials] = useState<AiCredentialItem[]>([]);
  const [prefs, setPrefs] = useState<{ mode: AiCredentialMode; allowPlatformFallback: boolean } | null>(null);
  const [usage, setUsage] = useState<AiUserPolicySnapshot | null>(null);
  const [balance, setBalance] = useState<AiCreditBalance | null>(null);

  // New credential form
  const [showAdd, setShowAdd] = useState(false);
  const [newProvider, setNewProvider] = useState(PROVIDER_PRESETS[0].code);
  const [newKey, setNewKey] = useState('');
  const [saving, setSaving] = useState(false);
  const [addError, setAddError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [creds, p, u, c] = await Promise.all([
        fetchMyAiCredentials(),
        fetchMyAiPreferences(),
        fetchMyAiUsage(),
        fetchMyAiCredits(),
      ]);
      setCredentials(creds);
      setPrefs({ mode: p.mode, allowPlatformFallback: p.allowPlatformFallback });
      setUsage(u);
      setBalance(c.balance);
    } catch (e) {
      setError(`Failed to load AI settings: ${(e as Error).message}`);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const savePrefs = async (next: { mode: AiCredentialMode; allowPlatformFallback: boolean }) => {
    setPrefs(next);
    try {
      await updateMyAiPreferences(next);
    } catch (e) {
      setError(`Preferences save failed: ${(e as Error).message}`);
    }
  };

  const handleAdd = async () => {
    setSaving(true);
    setAddError(null);
    try {
      await saveMyAiCredential({ providerCode: newProvider, apiKey: newKey });
      setNewKey('');
      setShowAdd(false);
      await load();
    } catch (e) {
      const detail = (e as Error & { detail?: { error?: string; errorCode?: string } }).detail;
      setAddError(detail?.error ?? (e as Error).message);
    } finally {
      setSaving(false);
    }
  };

  const handleRevoke = async (id: string) => {
    try {
      await revokeMyAiCredential(id);
      await load();
    } catch (e) {
      setError(`Revoke failed: ${(e as Error).message}`);
    }
  };

  const monthlyPct = usage && usage.monthlyTokenCap > 0
    ? Math.min(100, Math.round((usage.tokensUsedThisMonth / usage.monthlyTokenCap) * 100))
    : 0;

  return (
    <LearnerDashboardShell>
      <main className="mx-auto max-w-4xl px-4 py-8 space-y-6">
        <button
          type="button"
          onClick={() => router.push('/settings')}
          className="inline-flex items-center gap-2 text-sm text-muted hover:text-navy transition-colors"
        >
          <ArrowLeft className="w-4 h-4" /> Back to Settings
        </button>

        <LearnerPageHero
          title="AI Settings"
          description="Bring your own AI provider key or use your platform allowance. Keys are encrypted at rest; we only ever show the last 4 characters."
          highlights={[
            { icon: Cpu, label: 'Plan', value: usage?.planName ?? (loading ? '…' : '—') },
            { icon: Shield, label: 'Mode', value: prefs?.mode ?? (loading ? '…' : '—') },
          ]}
        />

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {/* Usage meter */}
        <section className="bg-surface rounded-2xl border border-border p-6 space-y-3">
          <h2 className="text-lg font-semibold">AI credit usage</h2>
          {loading ? (
            <Skeleton className="h-20 w-full" />
          ) : usage ? (
            <>
              <div className="flex items-baseline justify-between">
                <span className="text-sm text-muted">
                  This month: <strong>{usage.tokensUsedThisMonth.toLocaleString()}</strong> /
                  {' '}{usage.monthlyTokenCap > 0 ? usage.monthlyTokenCap.toLocaleString() : 'unlimited'} tokens
                </span>
                <span className="text-sm text-muted">Today: {usage.tokensUsedToday.toLocaleString()}</span>
              </div>
              <div className="w-full h-2 bg-background-light rounded-full overflow-hidden">
                <div
                  className={`h-full transition-all ${monthlyPct > 85 ? 'bg-danger' : monthlyPct > 60 ? 'bg-warning' : 'bg-success'}`}
                  style={{ width: `${monthlyPct}%` }}
                />
              </div>
              <div className="flex flex-wrap gap-2 text-xs text-muted">
                <Badge variant="muted">{usage.overagePolicy}</Badge>
                {usage.killSwitchActive && <Badge variant="danger">Platform AI paused</Badge>}
                {usage.aiDisabled && <Badge variant="danger">Account AI disabled</Badge>}
              </div>
              {balance && balance.tokensAvailable > 0 && (
                <p className="text-xs text-muted">
                  Credits available: {balance.tokensAvailable.toLocaleString()} tokens.
                </p>
              )}
            </>
          ) : null}
        </section>

        {/* Mode preference */}
        <section className="bg-surface rounded-2xl border border-border p-6 space-y-4">
          <h2 className="text-lg font-semibold">How should we route AI calls?</h2>
          <p className="text-sm text-muted">
            Your choice applies to non-scoring features (practice, summarisation, conversation). Scoring-critical
            features (writing grade, speaking grade, mock exam) always use the platform to keep scoring consistent.
          </p>
          {prefs && (
            <div className="space-y-3">
              <Select
                label="Preferred credential source"
                value={prefs.mode}
                onChange={(e) => void savePrefs({ ...prefs, mode: e.target.value as AiCredentialMode })}
                options={[
                  { value: 'Auto', label: 'Auto — use my key when available, platform otherwise' },
                  { value: 'ByokOnly', label: 'My key only — refuse calls if my key is unavailable' },
                  { value: 'PlatformOnly', label: 'Platform only — ignore my stored keys' },
                ]}
              />
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={prefs.allowPlatformFallback}
                  onChange={(e) => void savePrefs({ ...prefs, allowPlatformFallback: e.target.checked })}
                />
                Allow platform fallback when my key fails (recommended)
              </label>
            </div>
          )}
        </section>

        {/* Stored credentials */}
        <section className="bg-surface rounded-2xl border border-border p-6 space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="text-lg font-semibold">Stored API keys</h2>
            <Button variant="primary" onClick={() => setShowAdd(true)}>
              <KeyRound className="w-4 h-4 mr-2" /> Add key
            </Button>
          </div>
          {loading ? (
            <Skeleton className="h-24 w-full" />
          ) : credentials.length === 0 ? (
            <p className="text-sm text-muted">No API keys stored. Add one to route calls through your own account.</p>
          ) : (
            <ul className="divide-y divide-border">
              {credentials.map((c) => (
                <li key={c.id} className="py-3 flex items-center justify-between">
                  <div>
                    <div className="font-medium">{c.providerCode}</div>
                    <div className="text-xs text-muted font-mono">{c.keyHint}</div>
                    <div className="text-xs text-muted">
                      {c.status === 'Active' ? 'Active' : c.status}
                      {c.lastUsedAt && ` · last used ${new Date(c.lastUsedAt).toLocaleDateString()}`}
                      {c.cooldownUntil && new Date(c.cooldownUntil) > new Date()
                        && ` · cooldown until ${new Date(c.cooldownUntil).toLocaleString()}`}
                    </div>
                  </div>
                  <Button variant="ghost" size="sm" onClick={() => void handleRevoke(c.id)}>
                    <Trash2 className="w-4 h-4" /> Revoke
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </section>

        <Modal open={showAdd} onClose={() => { setShowAdd(false); setAddError(null); }} title="Add AI provider key">
          <div className="space-y-4">
            <Select
              label="Provider"
              value={newProvider}
              onChange={(e) => setNewProvider(e.target.value)}
              options={PROVIDER_PRESETS.map((p) => ({ value: p.code, label: p.name }))}
            />
            <p className="text-xs text-muted">
              {PROVIDER_PRESETS.find((p) => p.code === newProvider)?.hint}
            </p>
            <Input
              label="API key"
              type="password"
              value={newKey}
              onChange={(e) => setNewKey(e.target.value)}
              placeholder="sk-…"
            />
            <p className="text-xs text-muted">
              We validate the key against the provider once, encrypt it at rest, and never show it again.
              Only the last 4 characters will be visible.
            </p>
            {addError && <InlineAlert variant="error">{addError}</InlineAlert>}
            <div className="flex gap-3 justify-end">
              <Button variant="ghost" onClick={() => { setShowAdd(false); setAddError(null); }}>Cancel</Button>
              <Button variant="primary" onClick={() => void handleAdd()} disabled={saving || newKey.length < 16}>
                {saving ? <Loader2 className="w-4 h-4 animate-spin mr-2" /> : null}
                Save key
              </Button>
            </div>
          </div>
        </Modal>
      </main>
    </LearnerDashboardShell>
  );
}
