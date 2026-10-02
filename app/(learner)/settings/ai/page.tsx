'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Cpu, KeyRound, Shield, Trash2 } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { Checkbox, Input, Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import {
  fetchMyAiCredentials,
  fetchMyAiPreferences,
  revokeMyAiCredential,
  saveMyAiCredential,
  updateMyAiPreferences,
  type AiCredentialItem,
  type AiCredentialMode,
} from '@/lib/ai-management-api';
import { fetchMyAiPackageCredits, type AiPackageCreditSnapshot } from '@/lib/api';

const PROVIDER_PRESETS: { code: string; name: string; hint: string }[] = [
  { code: 'openai-platform', name: 'OpenAI Platform', hint: 'platform.openai.com · keys starting sk-…' },
  { code: 'anthropic', name: 'Anthropic', hint: 'console.anthropic.com · keys starting sk-ant-…' },
  { code: 'openrouter', name: 'OpenRouter', hint: 'openrouter.ai · aggregates dozens of models' },
];

function CreditRow({ label, value }: { label: string; value: number | null }) {
  return (
    <li className="flex items-baseline justify-between gap-3 rounded-xl bg-background-light px-4 py-3">
      <span className="text-sm font-semibold text-muted">{label}</span>
      <span className={`text-sm font-bold tabular-nums ${value === null ? 'text-success-strong' : 'text-navy'}`}>
        {value === null ? 'Unlimited' : <CountUp value={value} />}
      </span>
    </li>
  );
}

export default function AiSettingsPage() {
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [credentials, setCredentials] = useState<AiCredentialItem[]>([]);
  const [prefs, setPrefs] = useState<{ mode: AiCredentialMode; allowPlatformFallback: boolean } | null>(null);
  const [aiCredits, setAiCredits] = useState<AiPackageCreditSnapshot | null>(null);

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
      const [creds, p, pkg] = await Promise.all([
        fetchMyAiCredentials(),
        fetchMyAiPreferences(),
        fetchMyAiPackageCredits().catch(() => null),
      ]);
      setCredentials(creds);
      setPrefs({ mode: p.mode, allowPlatformFallback: p.allowPlatformFallback });
      setAiCredits(pkg);
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
      const detail = (e as Error & { detail?: { message?: string; error?: string; errorCode?: string } }).detail;
      setAddError(detail?.message ?? detail?.error ?? (e as Error).message);
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

  // The initial load failed outright: nothing below would be real, so show the error with a retry.
  const loadFailed = !loading && !prefs && Boolean(error);

  return (
    <>
      <LearnerPageHero
        icon={Cpu}
        title="AI Settings"
        description="Bring your own AI provider key or use your platform allowance. Keys are encrypted at rest; we only ever show the last 4 characters."
        highlights={[
          { icon: Cpu, label: 'Plan', value: aiCredits?.writingUnlimited ? 'OET Mastery' : (loading ? '…' : 'AI packages') },
          { icon: Shield, label: 'Mode', value: prefs?.mode ?? (loading ? '…' : '-') },
        ]}
        aside={(
          <Button asChild variant="outline" size="sm">
            <Link href="/settings">
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Back to Settings
            </Link>
          </Button>
        )}
      />

      {loadFailed ? (
        <ErrorState message={error ?? undefined} onRetry={() => void load()} retryLabel="Try again" />
      ) : (
        <>
          {error && <InlineAlert variant="error">{error}</InlineAlert>}

          {/* AI credit balances — Credits / Attempts / Unlimited only. Raw
              provider token quotas are platform-level operational data and are
              never shown on candidate surfaces. */}
          <MotionSection className="space-y-4">
            <LearnerSurfaceSectionHeader icon={Cpu} title="AI credit balances" />
            {loading ? (
              <Skeleton className="h-24 w-full rounded-2xl" />
            ) : aiCredits ? (
              <Card>
                <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2">
                  <CreditRow label="Reading Credits" value={aiCredits.readingUnlimited ? null : aiCredits.readingTestsRemaining} />
                  <CreditRow label="Listening Credits" value={aiCredits.listeningUnlimited ? null : aiCredits.listeningTestsRemaining} />
                  <CreditRow label="Writing Credits" value={aiCredits.writingUnlimited ? null : aiCredits.writingOnlyCredits} />
                  <CreditRow label="Speaking Credits" value={aiCredits.speakingUnlimited ? null : aiCredits.speakingOnlyCredits} />
                  <CreditRow label="Shared Credits" value={aiCredits.sharedCredits ?? 0} />
                  {(aiCredits.flexibleCredits > 0 || (aiCredits.sharedCredits ?? 0) === 0) && (
                    <CreditRow label="Flexible W/S Credits" value={aiCredits.flexibleCredits} />
                  )}
                  {aiCredits.mockExamsRemaining > 0 && (
                    <CreditRow label="Full Mock Attempts" value={aiCredits.mockExamsRemaining} />
                  )}
                </ul>
                <p className="mt-4 text-xs text-muted">
                  Shared credits work across all four subtests: Reading 1 · Listening 1 · Writing 2 · Speaking 2.
                </p>
              </Card>
            ) : (
              <EmptyState icon={<Cpu className="h-8 w-8" />} title="No active AI credit packages." />
            )}
          </MotionSection>

          {/* Mode preference */}
          <MotionSection delayIndex={1} className="space-y-4">
            <LearnerSurfaceSectionHeader
              icon={Shield}
              title="How should we route AI calls?"
              description="Your choice applies to non-scoring features (practice, summarisation, conversation). Scoring-critical features (writing grade, speaking grade, mock exam) always use the platform to keep scoring consistent."
            />
            {loading ? (
              <Skeleton className="h-32 w-full rounded-2xl" />
            ) : prefs ? (
              <Card className="space-y-4">
                <Select
                  label="Preferred credential source"
                  value={prefs.mode}
                  onChange={(e) => void savePrefs({ ...prefs, mode: e.target.value as AiCredentialMode })}
                  className="w-full cursor-pointer"
                  options={[
                    { value: 'Auto', label: 'Auto: use my key when available, platform otherwise' },
                    { value: 'ByokOnly', label: 'My key only: refuse calls if my key is unavailable' },
                    { value: 'PlatformOnly', label: 'Platform only: ignore my stored keys' },
                  ]}
                />
                <Checkbox
                  label="Allow platform fallback when my key fails (recommended)"
                  checked={prefs.allowPlatformFallback}
                  onChange={(e) => void savePrefs({ ...prefs, allowPlatformFallback: e.target.checked })}
                />
              </Card>
            ) : null}
          </MotionSection>

          {/* Stored credentials */}
          <MotionSection delayIndex={2} className="space-y-4">
            <LearnerSurfaceSectionHeader
              icon={KeyRound}
              title="Stored API keys"
              description="Manage your provider keys securely."
              action={(
                <Button onClick={() => setShowAdd(true)} className="w-full sm:w-auto">
                  <KeyRound className="h-4 w-4" aria-hidden="true" /> Add key
                </Button>
              )}
            />
            {loading ? (
              <Skeleton className="h-24 w-full rounded-2xl" />
            ) : credentials.length === 0 ? (
              <EmptyState
                icon={<KeyRound className="h-8 w-8" />}
                title="No API keys stored."
                description="Add one to route calls through your own account."
              />
            ) : (
              <Card padding="none" className="overflow-hidden">
                <ul className="divide-y divide-border">
                  {credentials.map((c) => (
                    <li key={c.id} className="flex flex-col justify-between gap-4 p-4 sm:flex-row sm:items-center sm:p-5">
                      <div className="min-w-0">
                        <div className="text-base font-semibold text-navy">{c.providerCode}</div>
                        <div className="mt-1 font-mono text-sm text-muted">{c.keyHint}</div>
                        <div className="mt-2 flex flex-wrap items-center gap-2">
                          <Badge variant={c.status === 'Active' ? 'success' : 'muted'}>
                            {c.status}
                          </Badge>
                          <span className="text-xs tabular-nums text-muted">
                            {c.lastUsedAt && ` · last used ${new Date(c.lastUsedAt).toLocaleDateString()}`}
                            {c.cooldownUntil && new Date(c.cooldownUntil) > new Date()
                              && ` · cooldown until ${new Date(c.cooldownUntil).toLocaleString()}`}
                          </span>
                        </div>
                      </div>
                      <Button variant="outline" size="sm" onClick={() => void handleRevoke(c.id)} className="self-start text-danger-strong hover:bg-danger/10 sm:self-auto">
                        <Trash2 className="h-4 w-4" aria-hidden="true" /> Revoke
                      </Button>
                    </li>
                  ))}
                </ul>
              </Card>
            )}
          </MotionSection>
        </>
      )}

      <Modal open={showAdd} onClose={() => { setShowAdd(false); setAddError(null); }} title="Add AI provider key">
        <div className="space-y-5">
          <Select
            label="Provider"
            value={newProvider}
            onChange={(e) => setNewProvider(e.target.value)}
            options={PROVIDER_PRESETS.map((p) => ({ value: p.code, label: p.name }))}
            className="w-full cursor-pointer"
          />
          <p className="rounded-xl bg-background-light p-3 text-xs font-medium text-muted">
            {PROVIDER_PRESETS.find((p) => p.code === newProvider)?.hint}
          </p>
          <Input
            label="API key"
            type="password"
            value={newKey}
            onChange={(e) => setNewKey(e.target.value)}
            placeholder="sk-…"
            autoComplete="off"
            spellCheck={false}
            className="w-full"
          />
          <p className="text-xs leading-relaxed text-muted">
            We validate the key against the provider once, encrypt it at rest, and never show it again.
            Only the last 4 characters will be visible.
          </p>
          {addError && <InlineAlert variant="error">{addError}</InlineAlert>}
          <div className="flex justify-end gap-3 pt-2">
            <Button variant="ghost" onClick={() => { setShowAdd(false); setAddError(null); }}>Cancel</Button>
            <Button variant="primary" onClick={() => void handleAdd()} loading={saving} disabled={newKey.length < 16}>
              Save key
            </Button>
          </div>
        </div>
      </Modal>
    </>
  );
}
