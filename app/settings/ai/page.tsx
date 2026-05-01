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
      <div className="relative min-h-[calc(100dvh-4rem)] bg-background">
        <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_top,_var(--tw-gradient-stops))] from-primary/10 via-transparent to-transparent pointer-events-none -z-10 blur-3xl opacity-70" />
        
        <main className="mx-auto max-w-4xl px-4 py-8 space-y-10 relative z-10 pb-20">
          <Button
            variant="ghost"
            onClick={() => router.push('/settings')}
            className="gap-2 rounded-full hover:bg-navy/5 font-bold mt-4"
          >
            <ArrowLeft className="w-4 h-4" /> Back to Settings
          </Button>

          <div className="bg-white/60 backdrop-blur-2xl p-2 sm:p-2 border-0 shadow-[0_8px_30px_rgb(0,0,0,0.04)] ring-1 ring-primary/5 transition-all overflow-hidden relative group rounded-3xl">
            <div className="absolute inset-0 bg-[linear-gradient(45deg,transparent,rgba(255,255,255,0.4),transparent)] -translate-x-[150%] group-hover:translate-x-[150%] transition-transform duration-1000 ease-in-out pointer-events-none" />
            <LearnerPageHero
              title="AI Settings"
              description="Bring your own AI provider key or use your platform allowance. Keys are encrypted at rest; we only ever show the last 4 characters."
              highlights={[
                { icon: Cpu, label: 'Plan', value: usage?.planName ?? (loading ? '…' : '—') },
                { icon: Shield, label: 'Mode', value: prefs?.mode ?? (loading ? '…' : '—') },
              ]}
            />
          </div>

          {error && <InlineAlert variant="error" className="shadow-sm">{error}</InlineAlert>}

          {/* Usage meter */}
          <section className="bg-white/70 backdrop-blur-2xl rounded-[2.5rem] border/50 ring-1 ring-black/5 p-6 sm:p-8 shadow-[0_8px_40px_rgb(0,0,0,0.04)] hover:shadow-[0_8px_50px_rgb(0,0,0,0.08)] transition-all duration-500 overflow-hidden relative group">
            <div className="absolute inset-0 bg-gradient-to-br from-white/40 to-transparent pointer-events-none" />
            <div className="relative z-10 space-y-4">
              <div className="flex flex-wrap items-center gap-3 justify-between">
                <div className="flex items-center gap-3">
                  <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl border border-primary/15 bg-primary/10 shadow-sm ring-1 ring-white/50 backdrop-blur-md transition-transform group-hover:scale-110 duration-500">
                    <Cpu className="h-5 w-5 text-primary drop-shadow-sm" />
                  </div>
                  <h2 className="text-xl font-black text-navy tracking-tight group-hover:text-primary transition-colors">AI credit usage</h2>
                </div>
                <Badge className="bg-primary/5 text-primary border-primary/10 rounded-full px-3 py-1 font-black text-[10px] uppercase tracking-widest shadow-sm">Quota</Badge>
              </div>

              {loading ? (
                <Skeleton className="h-24 w-full rounded-2xl bg-white/40 mt-4" />
              ) : usage ? (
                <div className="space-y-5 mt-4 bg-white/40 p-6 rounded-[2rem] border border-black/5 shadow-inner">
                  <div className="flex flex-wrap items-baseline justify-between gap-2">
                    <span className="text-sm font-black text-navy/70 tracking-tight">
                      This month: <strong className="text-navy text-lg">{usage.tokensUsedThisMonth.toLocaleString()}</strong> /
                      {' '}{usage.monthlyTokenCap > 0 ? usage.monthlyTokenCap.toLocaleString() : 'unlimited'} tokens
                    </span>
                    <span className="text-xs font-bold text-navy/70 bg-white/60 px-3 py-1.5 rounded-full shadow-sm ring-1 ring-black/5">Today: {usage.tokensUsedToday.toLocaleString()}</span>
                  </div>
                  
                  <div className="w-full h-3 bg-white/50 rounded-full overflow-hidden shadow-inner ring-1 ring-black/5 p-0.5">
                    <div
                      className={`h-full rounded-full transition-all duration-1000 ease-in-out shadow-sm relative ${monthlyPct > 85 ? 'bg-danger' : monthlyPct > 60 ? 'bg-warning' : 'bg-primary'}`}
                      style={{ width: `${monthlyPct}%` }}
                    >
                      <div className="absolute inset-0 bg-[linear-gradient(90deg,transparent,rgba(255,255,255,0.4),transparent)] -translate-x-[150%] animate-[shimmer_2s_infinite]" />
                    </div>
                  </div>
                  
                  <div className="flex flex-wrap gap-2 text-xs font-bold">
                    <Badge variant="muted" className="rounded-full bg-white/60 shadow-sm">{usage.overagePolicy}</Badge>
                    {usage.killSwitchActive && <Badge variant="danger" className="rounded-full shadow-sm">Platform AI paused</Badge>}
                    {usage.aiDisabled && <Badge variant="danger" className="rounded-full shadow-sm">Account AI disabled</Badge>}
                  </div>
                  
                  {balance && balance.tokensAvailable > 0 && (
                    <div className="mt-2 p-4 bg-primary/5 rounded-2xl border border-primary/10">
                      <p className="text-sm font-black text-primary">
                        Credits available: {balance.tokensAvailable.toLocaleString()} tokens
                      </p>
                    </div>
                  )}
                </div>
              ) : null}
            </div>
          </section>

          {/* Mode preference */}
          <section className="bg-white/70 backdrop-blur-2xl rounded-[2.5rem] border/50 ring-1 ring-black/5 p-6 sm:p-8 shadow-[0_8px_40px_rgb(0,0,0,0.04)] hover:shadow-[0_8px_50px_rgb(0,0,0,0.08)] transition-all duration-500 overflow-hidden relative group">
            <div className="absolute inset-0 bg-gradient-to-br from-white/40 to-transparent pointer-events-none" />
            <div className="relative z-10 space-y-6">
              <div className="flex items-start gap-4 mb-4">
                <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl border border-indigo-200/50 bg-indigo-50 shadow-sm ring-1 ring-white/50 backdrop-blur-md transition-transform group-hover:scale-110 duration-500">
                  <Shield className="h-5 w-5 text-indigo-600 drop-shadow-sm" />
                </div>
                <div>
                  <div className="flex items-center gap-3">
                    <h2 className="text-xl font-black text-navy tracking-tight group-hover:text-indigo-600 transition-colors">How should we route AI calls?</h2>
                    <Badge className="hidden sm:inline-flex bg-indigo-50 text-indigo-700 border-indigo-100 rounded-full px-3 py-1 font-black text-[10px] uppercase tracking-widest shadow-sm">Routing</Badge>
                  </div>
                  <p className="text-sm font-medium text-navy/70 leading-relaxed max-w-2xl mt-2">
                    Your choice applies to non-scoring features (practice, summarisation, conversation). Scoring-critical
                    features (writing grade, speaking grade, mock exam) always use the platform to keep scoring consistent.
                  </p>
                </div>
              </div>
              
              {prefs && (
                <div className="space-y-5 bg-white/40 p-6 rounded-[2rem] border border-black/5 shadow-inner">
                  <Select
                    label="Preferred credential source"
                    value={prefs.mode}
                    onChange={(e) => void savePrefs({ ...prefs, mode: e.target.value as AiCredentialMode })}
                    className="w-full rounded-2xl border border-black/5 bg-white/80 backdrop-blur-xl px-5 py-4 text-base font-bold text-navy outline-none transition-all shadow-inner focus:bg-white focus:shadow-[0_4px_20px_rgb(0,0,0,0.08)] focus:border-indigo-400 focus:ring-4 focus:ring-indigo-100 appearance-none cursor-pointer h-14 bg-no-repeat bg-[right_1rem_center] bg-[length:1.2em]"
                    options={[
                      { value: 'Auto', label: 'Auto — use my key when available, platform otherwise' },
                      { value: 'ByokOnly', label: 'My key only — refuse calls if my key is unavailable' },
                      { value: 'PlatformOnly', label: 'Platform only — ignore my stored keys' },
                    ]}
                  />
                  <label className="flex items-center gap-3 text-sm font-bold text-navy cursor-pointer group/label hover:text-indigo-600 transition-colors ml-1">
                    <div className="relative flex items-center justify-center w-6 h-6 rounded-[8px] border-2 border-black/10 bg-white/80 group-hover/label:border-indigo-400 transition-colors shadow-inner">
                      <input
                        type="checkbox"
                        className="opacity-0 absolute inset-0 cursor-pointer"
                        checked={prefs.allowPlatformFallback}
                        onChange={(e) => void savePrefs({ ...prefs, allowPlatformFallback: e.target.checked })}
                      />
                      {prefs.allowPlatformFallback && <div className="w-2.5 h-2.5 bg-indigo-500 rounded-[3px]" />}
                    </div>
                    Allow platform fallback when my key fails (recommended)
                  </label>
                </div>
              )}
            </div>
          </section>

          {/* Stored credentials */}
          <section className="bg-white/70 backdrop-blur-2xl rounded-[2.5rem] border/50 ring-1 ring-black/5 p-6 sm:p-8 shadow-[0_8px_40px_rgb(0,0,0,0.04)] hover:shadow-[0_8px_50px_rgb(0,0,0,0.08)] transition-all duration-500 overflow-hidden relative group">
            <div className="absolute inset-0 bg-gradient-to-br from-white/40 to-transparent pointer-events-none" />
            <div className="relative z-10 space-y-6">
              <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                <div className="flex items-center gap-4">
                  <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl border border-amber-200/50 bg-amber-50 shadow-sm ring-1 ring-white/50 backdrop-blur-md transition-transform group-hover:scale-110 duration-500">
                    <KeyRound className="h-5 w-5 text-amber-600 drop-shadow-sm" />
                  </div>
                  <div>
                    <h2 className="text-xl font-black text-navy tracking-tight group-hover:text-amber-600 transition-colors">Stored API keys</h2>
                    <p className="text-sm font-medium text-navy/70 mt-1">Manage your provider keys securely.</p>
                  </div>
                </div>
                <Button 
                  onClick={() => setShowAdd(true)}
                  className="bg-amber-500 hover:bg-amber-600 text-white gap-2 font-bold rounded-2xl px-6 py-5 h-auto shadow-md shadow-amber-500/20 w-full sm:w-auto transition-all hover:scale-105"
                >
                  <KeyRound className="w-4 h-4" /> Add key
                </Button>
              </div>

              <div className="bg-white/40 rounded-[2rem] border border-black/5 shadow-inner overflow-hidden">
                {loading ? (
                  <Skeleton className="h-24 w-full bg-white/40" />
                ) : credentials.length === 0 ? (
                  <div className="p-8 text-center">
                    <p className="text-sm font-bold text-navy/60">No API keys stored. Add one to route calls through your own account.</p>
                  </div>
                ) : (
                  <ul className="divide-y divide-black/5">
                    {credentials.map((c) => (
                      <li key={c.id} className="p-6 flex flex-col sm:flex-row sm:items-center gap-4 justify-between hover:bg-white/50 transition-colors">
                        <div>
                          <div className="font-black text-navy text-lg">{c.providerCode}</div>
                          <div className="text-sm text-navy/60 font-mono font-medium mt-1">{c.keyHint}</div>
                          <div className="flex items-center gap-2 mt-3">
                            <Badge variant={c.status === 'Active' ? 'success' : 'muted'} className="rounded-full px-2.5 py-0.5 text-[10px] font-black uppercase tracking-widest">
                              {c.status}
                            </Badge>
                            <span className="text-xs font-bold text-navy/50">
                              {c.lastUsedAt && ` · last used ${new Date(c.lastUsedAt).toLocaleDateString()}`}
                              {c.cooldownUntil && new Date(c.cooldownUntil) > new Date()
                                && ` · cooldown until ${new Date(c.cooldownUntil).toLocaleString()}`}
                            </span>
                          </div>
                        </div>
                        <Button variant="ghost" size="sm" onClick={() => void handleRevoke(c.id)} className="text-danger hover:bg-danger/10 hover:text-danger rounded-xl font-bold bg-white shadow-sm ring-1 ring-black/5">
                          <Trash2 className="w-4 h-4 mr-2" /> Revoke
                        </Button>
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            </div>
          </section>

          <Modal open={showAdd} onClose={() => { setShowAdd(false); setAddError(null); }} title="Add AI provider key">
            <div className="space-y-5">
              <Select
                label="Provider"
                value={newProvider}
                onChange={(e) => setNewProvider(e.target.value)}
                options={PROVIDER_PRESETS.map((p) => ({ value: p.code, label: p.name }))}
                className="w-full rounded-2xl border border-black/5 bg-surface px-5 py-4 text-base font-bold text-navy outline-none transition-all shadow-inner focus:border-amber-400 focus:ring-4 focus:ring-amber-100 appearance-none cursor-pointer h-14 bg-no-repeat bg-[right_1rem_center] bg-[length:1.2em]"
              />
              <p className="text-xs font-bold text-navy/60 bg-amber-50 border border-amber-100 p-3 rounded-xl">
                {PROVIDER_PRESETS.find((p) => p.code === newProvider)?.hint}
              </p>
              <Input
                label="API key"
                type="password"
                value={newKey}
                onChange={(e) => setNewKey(e.target.value)}
                placeholder="sk-…"
                className="w-full rounded-2xl border border-black/5 bg-surface px-5 py-4 text-base font-bold text-navy outline-none transition-all shadow-inner focus:border-amber-400 focus:ring-4 focus:ring-amber-100 h-14"
              />
              <p className="text-xs font-medium text-navy/60 leading-relaxed">
                We validate the key against the provider once, encrypt it at rest, and never show it again.
                Only the last 4 characters will be visible.
              </p>
              {addError && <InlineAlert variant="error" className="shadow-sm rounded-xl">{addError}</InlineAlert>}
              <div className="flex gap-3 justify-end pt-2">
                <Button variant="ghost" onClick={() => { setShowAdd(false); setAddError(null); }} className="rounded-xl font-bold hover:bg-navy/5">Cancel</Button>
                <Button variant="primary" onClick={() => void handleAdd()} disabled={saving || newKey.length < 16} className="rounded-xl font-bold px-6 bg-amber-500 hover:bg-amber-600 border-none shadow-md hover:scale-105 transition-transform">
                  {saving ? <Loader2 className="w-4 h-4 animate-spin mr-2" /> : null}
                  Save key
                </Button>
              </div>
            </div>
          </Modal>
        </main>
      </div>
    </LearnerDashboardShell>
  );
}
