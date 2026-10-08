'use client';

/**
 * AI Pipelines: the one page that orders, switches, funds and checks the five AI stages (Writing grading,
 * Writing reviewer, Speaking grading, Speaking reviewer, Speaking live voice) — owner directive 2026-10-09.
 * Phase 1: the saved order is authoritative and versioned; every save is audited and can be rolled back.
 * Phase 2: usage & cost windows (today / 7d / 30d / all-time, per provider and per stage), cost per graded
 * unit, subscription gauges (Claude Max + ChatGPT/Codex reviewer), credit grants with computed remaining
 * balance, and API key management — all on this same page.
 *
 * Honesty rule: every USD figure here is INTERNALLY TRACKED (our rate-card estimate over AiUsageRecords)
 * and every subscription figure comes from our own sidecar counters. Neither Anthropic nor OpenAI exposes
 * a balance/usage API for consumer subscriptions or promo credits (verified 2026-10-09), so nothing on this
 * page may be labelled provider-verified.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AlertTriangle, ArrowDown, ArrowUp, GripVertical, History, KeyRound, Plus, RefreshCw, RotateCcw, ShieldCheck, Trash2 } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Input } from '@/components/admin/ui/input';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { NativeSelect } from '@/components/admin/ui/native-select';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Switch } from '@/components/admin/ui/switch';
import { toast } from '@/components/ui/toaster';
import { Tabs } from '@/components/ui/tabs';
import {
  fetchAiProviders,
  discoverAiProviderModels,
  testAiProvider,
  updateAiProvider,
  type AiProviderRow,
} from '@/lib/ai-management-api';
import {
  createCreditGrant,
  deleteCreditGrant,
  fetchPipelineHistory,
  fetchPipelineOverview,
  fetchPipelines,
  restorePipelineDefault,
  rollbackPipelineStage,
  runPipelineSelfCheck,
  savePipelineStage,
  type CreditGrantInput,
  type OverviewWindow,
  type PipelineHopInput,
  type PipelineOverview,
  type PipelineProvider,
  type PipelineRevision,
  type PipelineStage,
  type PipelinesResponse,
  type SelfCheckResponse,
} from '@/lib/api/ai-pipelines';

const BREADCRUMBS = [{ label: 'Admin', href: '/admin' }, { label: 'AI Pipelines' }];
const MAX_CODE = 'writing-claude-sub';
const LIVE_PROVIDERS = [
  { code: 'openai', name: 'OpenAI GPT Live' },
  { code: 'gemini', name: 'Gemini Live' },
];

type DraftHop = PipelineHopInput;
interface Draft {
  /** The saved version this draft was started from; a draft is dropped when the saved version moves on. */
  baseVersion: number;
  stageEnabled: boolean;
  hops: DraftHop[];
  reason: string;
  confirmation: string;
}

function errorText(err: unknown): string {
  const e = err as { userMessage?: string; message?: string };
  return e?.userMessage || e?.message || 'Something went wrong.';
}

function toDraft(stage: PipelineStage): Draft {
  return {
    baseVersion: stage.version,
    stageEnabled: stage.stageEnabled,
    hops: stage.hops.map((h) => ({
      provider: h.provider,
      model: h.model,
      enabled: h.enabled,
      attempts: h.attempts,
      budgetSeconds: h.budgetSeconds,
      benchmarkRunId: null,
    })),
    reason: '',
    confirmation: '',
  };
}

function signature(stageEnabled: boolean, hops: DraftHop[]): string {
  return JSON.stringify({ stageEnabled, hops: hops.map((h) => [h.provider, h.model ?? '', h.enabled, h.attempts, h.budgetSeconds]) });
}

function statusBadge(status: string) {
  if (status === 'ready') return <Badge variant="success">Ready</Badge>;
  if (status === 'disabled') return <Badge variant="muted">Off</Badge>;
  return <Badge variant="warning">{status.replace(/^[^:]+:\s*/, '')}</Badge>;
}

function fmtUsd(n: number): string {
  const abs = Math.abs(n);
  const digits = abs >= 100 ? 0 : abs >= 1 ? 2 : 4;
  return `$${n.toFixed(digits)}`;
}

function fmtTokens(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}k`;
  return String(n);
}

const OVERVIEW_WINDOWS: Array<{ id: OverviewWindow; label: string }> = [
  { id: 'today', label: 'Today' },
  { id: '7d', label: 'Last 7 days' },
  { id: '30d', label: 'Last 30 days' },
  { id: 'all', label: 'All time' },
];

/** Mirrors AiUsageStageBuckets labels server-side; unknown keys fall back to the raw bucket id. */
const BUCKET_LABELS: Record<string, string> = {
  'writing-grade': 'Writing grading',
  'writing-review': 'Writing reviewer',
  'speaking-grade': 'Speaking grading',
  'speaking-review': 'Speaking reviewer',
  'speaking-audio': 'Speaking audio model',
  other: 'Other AI features',
};

export default function AiPipelinesPage() {
  const [data, setData] = useState<PipelinesResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [busy, setBusy] = useState<string | null>(null);
  const [history, setHistory] = useState<Record<string, PipelineRevision[] | undefined>>({});
  const [selfCheck, setSelfCheck] = useState<SelfCheckResponse | null>(null);

  // Phase 2 state: usage/cost overview, provider rows for key management, credit grants.
  const [overview, setOverview] = useState<PipelineOverview | null>(null);
  const [overviewWindow, setOverviewWindow] = useState<OverviewWindow>('7d');
  const [overviewError, setOverviewError] = useState<string | null>(null);
  const [providerRows, setProviderRows] = useState<AiProviderRow[]>([]);
  const [keyDraft, setKeyDraft] = useState<{ id: string; apiKey: string; model: string; isActive: boolean } | null>(null);
  const [discoveredModels, setDiscoveredModels] = useState<Record<string, string[]>>({});
  const [rowBusy, setRowBusy] = useState<string | null>(null);
  const [grantForm, setGrantForm] = useState<{ providerCode: string; amount: string; note: string }>({ providerCode: '', amount: '', note: '' });
  const [grantBusy, setGrantBusy] = useState(false);

  const loadOverview = useCallback(async (window: OverviewWindow) => {
    try {
      setOverview(await fetchPipelineOverview(window));
      setOverviewError(null);
    } catch (err) {
      setOverviewError(errorText(err));
    }
  }, []);

  const loadProviderRows = useCallback(async () => {
    try {
      setProviderRows(await fetchAiProviders());
    } catch {
      // The keys section degrades to the stage-page provider list; the rest of the page still works.
    }
  }, []);

  const load = useCallback(async () => {
    try {
      const next = await fetchPipelines();
      setData(next);
      setError(null);
      setDrafts((prev) => {
        const merged: Record<string, Draft> = {};
        for (const stage of next.stages) {
          const existing = prev[stage.stageKey];
          // Keep an unsaved draft only while it is still based on the same saved version.
          merged[stage.stageKey] = existing && existing.baseVersion === stage.version ? existing : toDraft(stage);
        }
        return merged;
      });
    } catch (err) {
      setError(errorText(err));
    }
  }, []);

  useEffect(() => {
    void load();
    void loadOverview(overviewWindow);
    void loadProviderRows();
  }, [load, loadOverview, loadProviderRows, overviewWindow]);

  const providerByCode = useMemo(() => {
    const map = new Map<string, PipelineProvider>();
    for (const p of data?.providers ?? []) map.set(p.code, p);
    return map;
  }, [data]);

  // Which stages reference each provider — powers the "used in" chips in the keys section.
  const stagesByProvider = useMemo(() => {
    const map = new Map<string, string[]>();
    for (const stage of data?.stages ?? []) {
      for (const hop of stage.hops) {
        map.set(hop.provider, [...(map.get(hop.provider) ?? []), stage.label]);
      }
    }
    return map;
  }, [data]);

  const nameOf = (stageKey: string, code: string) =>
    stageKey === 'speaking.live_voice'
      ? LIVE_PROVIDERS.find((p) => p.code === code)?.name ?? code
      : providerByCode.get(code)?.name ?? code;

  const dropDraft = (key: string) =>
    setDrafts((prev) => {
      const next = { ...prev };
      delete next[key];
      return next;
    });

  const patchDraft = (key: string, fn: (d: Draft) => Draft) =>
    setDrafts((prev) => (prev[key] ? { ...prev, [key]: fn(prev[key]) } : prev));

  const moveHop = (key: string, from: number, to: number) =>
    patchDraft(key, (d) => {
      if (to < 0 || to >= d.hops.length || from === to) return d;
      const hops = d.hops.slice();
      const [item] = hops.splice(from, 1);
      hops.splice(to, 0, item);
      return { ...d, hops };
    });

  async function save(stage: PipelineStage) {
    const draft = drafts[stage.stageKey];
    if (!draft) return;
    setBusy(stage.stageKey);
    try {
      await savePipelineStage(stage.stageKey, {
        stageEnabled: draft.stageEnabled,
        hops: draft.hops,
        expectedVersion: stage.version,
        reason: draft.reason || undefined,
        confirmation: draft.confirmation || undefined,
      });
      toast.success(`${stage.label} saved. The next run uses the new order.`);
      dropDraft(stage.stageKey);
      setHistory((prev) => ({ ...prev, [stage.stageKey]: undefined }));
      await load();
    } catch (err) {
      toast.error(errorText(err));
      // A version conflict means the draft is based on an old order: reload the saved one.
      if ((err as { code?: string })?.code === 'ai_pipeline_version_conflict') {
        dropDraft(stage.stageKey);
        await load();
      }
    } finally {
      setBusy(null);
    }
  }

  async function restoreBuiltIn(stage: PipelineStage) {
    setBusy(stage.stageKey);
    try {
      await restorePipelineDefault(stage.stageKey, stage.version, 'Restored the built-in order from the Pipeline page.');
      toast.success(`${stage.label}: built-in order restored.`);
      dropDraft(stage.stageKey);
      setHistory((prev) => ({ ...prev, [stage.stageKey]: undefined }));
      await load();
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setBusy(null);
    }
  }

  async function showHistory(stage: PipelineStage) {
    if (history[stage.stageKey]) {
      setHistory((prev) => ({ ...prev, [stage.stageKey]: undefined }));
      return;
    }
    try {
      const rows = await fetchPipelineHistory(stage.stageKey);
      setHistory((prev) => ({ ...prev, [stage.stageKey]: rows }));
    } catch (err) {
      toast.error(errorText(err));
    }
  }

  async function rollback(stage: PipelineStage, toVersion: number) {
    setBusy(stage.stageKey);
    try {
      await rollbackPipelineStage(stage.stageKey, toVersion, stage.version);
      toast.success(`${stage.label}: restored version ${toVersion} as a new version.`);
      dropDraft(stage.stageKey);
      setHistory((prev) => ({ ...prev, [stage.stageKey]: undefined }));
      await load();
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setBusy(null);
    }
  }

  async function check(live: boolean) {
    setBusy('self-check');
    try {
      setSelfCheck(await runPipelineSelfCheck(live));
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setBusy(null);
    }
  }

  // ── Phase 2 actions: credits + key management ──────────────────────────
  async function addGrant() {
    const amount = Number(grantForm.amount);
    if (!grantForm.providerCode || !Number.isFinite(amount) || amount <= 0) {
      toast.error('Choose a provider and a positive USD amount.');
      return;
    }
    setGrantBusy(true);
    try {
      const input: CreditGrantInput = { providerCode: grantForm.providerCode, grantUsd: amount };
      if (grantForm.note.trim()) input.note = grantForm.note.trim();
      await createCreditGrant(input);
      toast.success('Credit grant recorded. Remaining updates as grading spend accrues.');
      setGrantForm({ providerCode: '', amount: '', note: '' });
      await loadOverview(overviewWindow);
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setGrantBusy(false);
    }
  }

  async function removeGrant(id: string) {
    setGrantBusy(true);
    try {
      await deleteCreditGrant(id);
      toast.success('Credit grant removed.');
      await loadOverview(overviewWindow);
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setGrantBusy(false);
    }
  }

  async function saveKeyRow(p: AiProviderRow) {
    if (!keyDraft || keyDraft.id !== p.id) return;
    setRowBusy(p.id);
    try {
      const payload = { ...p, defaultModel: keyDraft.model || p.defaultModel, isActive: keyDraft.isActive } as Record<string, unknown>;
      if (keyDraft.apiKey.trim()) payload.apiKey = keyDraft.apiKey.trim();
      await updateAiProvider(p.id, payload);
      toast.success(`${p.name} saved.`);
      setKeyDraft(null);
      await Promise.all([loadProviderRows(), load()]);
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setRowBusy(null);
    }
  }

  async function toggleProviderActive(p: AiProviderRow, next: boolean) {
    setRowBusy(p.id);
    try {
      await updateAiProvider(p.id, { ...p, isActive: next } as Record<string, unknown>);
      toast.success(`${p.name} is now ${next ? 'active' : 'inactive'}.`);
      await Promise.all([loadProviderRows(), load()]);
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setRowBusy(null);
    }
  }

  async function testRow(p: AiProviderRow) {
    setRowBusy(p.id);
    try {
      const result = await testAiProvider(p.code);
      if (result.status === 'ok') toast.success(`${p.name}: connection ok (${result.latencyMs}ms).`);
      else toast.error(`${p.name}: ${result.status}${result.errorMessage ? ` — ${result.errorMessage}` : ''}`);
      await loadProviderRows();
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setRowBusy(null);
    }
  }

  async function discoverRow(p: AiProviderRow) {
    setRowBusy(p.id);
    try {
      const { models } = await discoverAiProviderModels(p.code);
      setDiscoveredModels((prev) => ({ ...prev, [p.id]: models }));
      if (models.length === 0) toast.info(`${p.name} exposes no model list. Type the model id instead.`);
    } catch (err) {
      toast.error(errorText(err));
    } finally {
      setRowBusy(null);
    }
  }

  const layoutProps = {
    title: 'AI Pipelines',
    description: 'One page for the whole AI infrastructure: provider order and ON/OFF per stage, subscription and API usage with costs, credit grants and key management. Changes apply to the next grade or session on every server, with no redeploy.',
    breadcrumbs: BREADCRUMBS,
    actions: (
      <Button variant="outline" size="sm" onClick={() => void load()} aria-label="Refresh pipelines">
        <RefreshCw className="h-4 w-4" aria-hidden="true" />
        Refresh
      </Button>
    ),
  };

  if (!data) {
    return (
      <AdminSettingsLayout {...layoutProps}>
        {error ? (
          <Card surface="tinted-danger">
            <CardContent className="p-4">
              <div role="alert" className="flex items-start gap-2 text-sm text-admin-danger">
                <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
                <span>{error}</span>
              </div>
            </CardContent>
          </Card>
        ) : (
          <>
            <Skeleton className="h-32 w-full" />
            <Skeleton className="h-64 w-full" />
          </>
        )}
      </AdminSettingsLayout>
    );
  }

  const maxBanners = data.stages.filter((s) => s.maxNotFirst || s.maxDisabled);

  return (
    <AdminSettingsLayout {...layoutProps}>
      {maxBanners.length > 0 && (
        <Card surface="tinted-danger">
          <CardContent className="space-y-1 p-4 text-sm" role="status">
            <p className="flex items-center gap-2 font-semibold text-admin-danger">
              <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
              Claude Max is not first in every grading stage
            </p>
            {maxBanners.map((s) => (
              <p key={s.stageKey} className="text-admin-fg-default">
                {s.label}: {s.maxDisabled ? 'Claude Max is switched off' : 'Claude Max is not the first step'}
                {s.lastChange ? ` (version ${s.lastChange.version} by ${s.lastChange.changedBy ?? 'unknown'}, ${new Date(s.lastChange.at).toLocaleString()})` : ''}.
                Use &ldquo;Restore built-in order&rdquo; to put it back first.
              </p>
            ))}
          </CardContent>
        </Card>
      )}

      {data.stages.map((stage) => {
        const draft = drafts[stage.stageKey] ?? toDraft(stage);
        const dirty = signature(draft.stageEnabled, draft.hops) !== signature(stage.stageEnabled, stage.hops);
        const live = stage.kind === 'voice';
        const usedCodes = new Set(draft.hops.map((h) => h.provider));
        const addable = live
          ? LIVE_PROVIDERS.filter((p) => !usedCodes.has(p.code)).map((p) => ({ value: p.code, label: p.name }))
          : data.providers.filter((p) => !usedCodes.has(p.code)).map((p) => ({ value: p.code, label: p.name }));
        const disablesMax =
          stage.kind === 'grading' &&
          stage.hops.some((h) => h.provider === MAX_CODE && h.enabled) &&
          !draft.hops.some((h) => h.provider === MAX_CODE && h.enabled);
        const savedHops = new Set(stage.hops.filter((h) => h.enabled).map((h) => `${h.provider}|${h.model ?? ''}`));
        const revisions = history[stage.stageKey];

        return (
          <SettingsSection
            key={stage.stageKey}
            id={stage.stageKey}
            title={stage.label}
            description={
              stage.kind === 'grading'
                ? 'Steps are tried in this order. If a step fails, the same grade moves to the next enabled step, so the student never loses work.'
                : stage.kind === 'reviewer'
                  ? 'The second reader of a finished grade. It has its own switch and order, independent of grading.'
                  : 'New sessions use this order. Sessions already running are not interrupted.'
            }
            actions={
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant="muted">v{stage.version}</Badge>
                {stage.source !== 'Saved' && <Badge variant="warning">{stage.source === 'LastKnownGood' ? 'Last known good' : 'Initial default'}</Badge>}
                {dirty && <Badge variant="info">Unsaved</Badge>}
              </div>
            }
          >
            <dl className="mb-4 grid gap-3 text-sm sm:grid-cols-2">
              <div>
                <dt className="text-2xs font-semibold uppercase tracking-wider text-admin-fg-muted">Next run starts on</dt>
                <dd className="mt-1 font-medium text-admin-fg-strong">{stage.nextRunStartsOn ? nameOf(stage.stageKey, stage.nextRunStartsOn) : 'Nothing usable'}</dd>
              </div>
              {!live && (
                <div>
                  <dt className="text-2xs font-semibold uppercase tracking-wider text-admin-fg-muted">Last served by</dt>
                  <dd className="mt-1 text-admin-fg-default">
                    {stage.lastServed
                      ? `${nameOf(stage.stageKey, stage.lastServed.providerId ?? '')}${stage.lastServed.model ? ` (${stage.lastServed.model})` : ''}, ${new Date(stage.lastServed.createdAt).toLocaleString()}`
                      : 'No successful call recorded yet'}
                  </dd>
                </div>
              )}
            </dl>

            {stage.kind === 'reviewer' && (
              <div className="mb-4 flex items-center gap-3">
                <Switch
                  checked={draft.stageEnabled}
                  onCheckedChange={(checked) => patchDraft(stage.stageKey, (d) => ({ ...d, stageEnabled: checked }))}
                  aria-label={`${stage.label} on or off`}
                />
                <span className="text-sm text-admin-fg-default">
                  {draft.stageEnabled ? 'Reviewer is on' : 'Reviewer is off: grades are published on the first grader alone'}
                </span>
              </div>
            )}

            <ol className="space-y-2" aria-label={`${stage.label} order`}>
              {draft.hops.map((hop, index) => {
                const saved = stage.hops.find((h) => h.provider === hop.provider);
                const provider = providerByCode.get(hop.provider);
                const newlyEnabled = hop.enabled && !savedHops.has(`${hop.provider}|${hop.model ?? ''}`) && !live;
                return (
                  <HopRow
                    key={hop.provider}
                    index={index}
                    count={draft.hops.length}
                    onMove={(to) => moveHop(stage.stageKey, index, to)}
                  >
                    <div className="min-w-0 flex-1 space-y-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-medium text-admin-fg-strong">{index + 1}. {nameOf(stage.stageKey, hop.provider)}</span>
                        {saved ? statusBadge(saved.status) : <Badge variant="info">New</Badge>}
                        {provider?.isSubscriptionBridge && <Badge variant="muted">Subscription</Badge>}
                        {provider && !provider.hasKey && !provider.isSubscriptionBridge && <Badge variant="warning">No key</Badge>}
                      </div>
                      {!live && (
                        <div className="grid gap-2 sm:grid-cols-3">
                          <Input
                            label="Model"
                            value={hop.model ?? ''}
                            placeholder={provider?.defaultModel || 'provider default'}
                            onChange={(e) => patchDraft(stage.stageKey, (d) => ({ ...d, hops: d.hops.map((h, i) => (i === index ? { ...h, model: e.target.value || null } : h)) }))}
                          />
                          <Input
                            label="Attempts"
                            type="number"
                            min={1}
                            max={4}
                            value={hop.attempts}
                            onChange={(e) => patchDraft(stage.stageKey, (d) => ({ ...d, hops: d.hops.map((h, i) => (i === index ? { ...h, attempts: Number(e.target.value) || 1 } : h)) }))}
                          />
                          <Input
                            label="Seconds per attempt"
                            type="number"
                            min={10}
                            max={1500}
                            value={hop.budgetSeconds}
                            onChange={(e) => patchDraft(stage.stageKey, (d) => ({ ...d, hops: d.hops.map((h, i) => (i === index ? { ...h, budgetSeconds: Number(e.target.value) || 10 } : h)) }))}
                          />
                        </div>
                      )}
                      {newlyEnabled && (
                        <Input
                          label="Benchmark run id (required for a model that leaves Claude)"
                          value={hop.benchmarkRunId ?? ''}
                          onChange={(e) => patchDraft(stage.stageKey, (d) => ({ ...d, hops: d.hops.map((h, i) => (i === index ? { ...h, benchmarkRunId: e.target.value || null } : h)) }))}
                        />
                      )}
                    </div>
                    <div className="flex items-center gap-2">
                      <Switch
                        checked={hop.enabled}
                        onCheckedChange={(checked) => patchDraft(stage.stageKey, (d) => ({ ...d, hops: d.hops.map((h, i) => (i === index ? { ...h, enabled: checked } : h)) }))}
                        aria-label={`${nameOf(stage.stageKey, hop.provider)} on or off`}
                      />
                      <Button
                        variant="ghost"
                        size="sm"
                        aria-label={`Remove ${nameOf(stage.stageKey, hop.provider)} from this stage`}
                        onClick={() => patchDraft(stage.stageKey, (d) => ({ ...d, hops: d.hops.filter((_, i) => i !== index) }))}
                      >
                        Remove
                      </Button>
                    </div>
                  </HopRow>
                );
              })}
            </ol>

            {addable.length > 0 && (
              <div className="mt-3 flex flex-wrap items-end gap-2">
                <NativeSelect
                  label="Add a step"
                  placeholder="Choose a provider"
                  value=""
                  options={addable}
                  onChange={(e) => {
                    const code = e.target.value;
                    if (!code) return;
                    const p = providerByCode.get(code);
                    patchDraft(stage.stageKey, (d) => ({
                      ...d,
                      hops: [...d.hops, { provider: code, model: null, enabled: false, attempts: live ? 1 : 1, budgetSeconds: live ? 0 : 300, benchmarkRunId: null }],
                    }));
                    if (p && !p.isActive) toast.info(`${p.name} is not active yet. Activate it under Providers before enabling the step.`);
                  }}
                />
                <Plus className="mb-3 h-4 w-4 text-admin-fg-muted" aria-hidden="true" />
              </div>
            )}

            {disablesMax && (
              <div className="mt-4 rounded-admin-lg border border-admin-danger p-3">
                <p className="mb-2 text-sm text-admin-fg-default">
                  You are switching Claude Max off in this stage. Type <strong>{data.disableMaxConfirmation}</strong> to confirm.
                </p>
                <Input
                  label="Confirmation"
                  value={draft.confirmation}
                  onChange={(e) => patchDraft(stage.stageKey, (d) => ({ ...d, confirmation: e.target.value }))}
                />
              </div>
            )}

            <div className="mt-4 grid gap-3 sm:grid-cols-[1fr_auto] sm:items-end">
              <Input
                label="Reason (kept in the history)"
                value={draft.reason}
                onChange={(e) => patchDraft(stage.stageKey, (d) => ({ ...d, reason: e.target.value }))}
              />
              <div className="flex flex-wrap gap-2">
                <Button onClick={() => void save(stage)} disabled={!dirty || busy === stage.stageKey}>
                  Save order
                </Button>
                <Button
                  variant="outline"
                  disabled={!dirty}
                  onClick={() => dropDraft(stage.stageKey)}
                >
                  Discard
                </Button>
              </div>
            </div>

            <div className="mt-3 flex flex-wrap gap-2">
              <Button variant="ghost" size="sm" disabled={busy === stage.stageKey} onClick={() => void restoreBuiltIn(stage)}>
                <RotateCcw className="h-4 w-4" aria-hidden="true" />
                Restore built-in order
              </Button>
              <Button variant="ghost" size="sm" onClick={() => void showHistory(stage)}>
                <History className="h-4 w-4" aria-hidden="true" />
                {revisions ? 'Hide history' : 'History'}
              </Button>
            </div>

            {revisions && (
              <ul className="mt-3 space-y-2 text-sm" aria-label={`${stage.label} history`}>
                {revisions.map((r) => (
                  <li key={r.version} className="flex flex-wrap items-center justify-between gap-2 rounded-admin-lg border border-admin-border p-2">
                    <span className="min-w-0 text-admin-fg-default">
                      <strong>v{r.version}</strong> {r.kind}, {r.changedBy ?? 'system'}, {new Date(r.at).toLocaleString()}
                      <span className="block text-admin-fg-muted">
                        {r.hops.map((h) => `${nameOf(stage.stageKey, h.provider)}${h.enabled ? '' : ' (off)'}`).join(' > ')}
                        {r.reason ? ` | ${r.reason}` : ''}
                      </span>
                    </span>
                    {r.version !== stage.version && (
                      <Button variant="outline" size="sm" disabled={busy === stage.stageKey} onClick={() => void rollback(stage, r.version)}>
                        Restore this
                      </Button>
                    )}
                  </li>
                ))}
              </ul>
            )}
          </SettingsSection>
        );
      })}

      <SettingsSection
        id="usage-cost"
        title="Usage & cost"
        description="Every AI call is recorded with its provider, stage and a rate-card cost estimate. Figures are internally tracked — exact call counts, estimated USD — and cover the pipeline plus every other AI feature on the platform."
        actions={
          <Tabs
            tabs={OVERVIEW_WINDOWS}
            activeTab={overviewWindow}
            onChange={(id) => setOverviewWindow(id as OverviewWindow)}
            ariaLabel="Usage window"
          />
        }
      >
        {overviewError ? (
          <Card surface="tinted-danger">
            <CardContent className="p-4 text-sm text-admin-danger" role="alert">{overviewError}</CardContent>
          </Card>
        ) : !overview ? (
          <Skeleton className="h-40 w-full" />
        ) : (
          <div className="space-y-4">
            <div className="grid gap-3 sm:grid-cols-3">
              <KpiTile
                label="Per Writing letter"
                value={overview.writingLetter.avgUsd !== null ? fmtUsd(overview.writingLetter.avgUsd) : '—'}
                hint={`${overview.writingLetter.count} graded · ${fmtUsd(overview.writingLetter.totalUsd)} total`}
              />
              <KpiTile
                label="Per Speaking assessment"
                value={overview.speakingAssessment.avgUsd !== null ? fmtUsd(overview.speakingAssessment.avgUsd) : '—'}
                hint={`${overview.speakingAssessment.count} graded · ${fmtUsd(overview.speakingAssessment.totalUsd)} total (grade + audio model)`}
              />
              <KpiTile
                label="Per reviewer run"
                value={overview.reviewerRun.avgUsd !== null ? fmtUsd(overview.reviewerRun.avgUsd) : '—'}
                hint={`${overview.reviewerRun.count} reviews · ${fmtUsd(overview.reviewerRun.totalUsd)} total`}
              />
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-start text-2xs uppercase tracking-wider text-admin-fg-muted">
                    <th className="px-3 py-2 text-start">Provider</th>
                    <th className="px-3 py-2 text-start">Calls</th>
                    <th className="px-3 py-2 text-start">Tokens</th>
                    <th className="px-3 py-2 text-start">USD (est.)</th>
                    <th className="px-3 py-2 text-start">By stage</th>
                  </tr>
                </thead>
                <tbody>
                  {overview.providers.length === 0 && (
                    <tr><td colSpan={5} className="px-3 py-3 text-admin-fg-muted">No AI calls in this window.</td></tr>
                  )}
                  {overview.providers.map((p) => (
                    <tr key={p.providerId} className="border-t border-admin-border align-top">
                      <td className="px-3 py-2 font-medium text-admin-fg-strong">
                        {providerByCode.get(p.providerId)?.name ?? p.providerId}
                        <span className="block text-2xs text-admin-fg-muted">{p.providerId}</span>
                      </td>
                      <td className="px-3 py-2">
                        {p.calls.toLocaleString()}
                        <span className="block text-2xs text-admin-fg-muted">{p.successes.toLocaleString()} ok · {p.failures.toLocaleString()} failed</span>
                      </td>
                      <td className="px-3 py-2">
                        {fmtTokens(p.promptTokens + p.completionTokens)}
                        <span className="block text-2xs text-admin-fg-muted">{fmtTokens(p.promptTokens)} in · {fmtTokens(p.completionTokens)} out</span>
                      </td>
                      <td className="px-3 py-2 tabular-nums">{fmtUsd(p.costUsd)}</td>
                      <td className="px-3 py-2">
                        <span className="flex flex-wrap gap-1">
                          {p.stages.map((s) => (
                            <Badge key={s.bucket} variant={s.bucket === 'other' ? 'muted' : 'info'}>
                              {BUCKET_LABELS[s.bucket] ?? s.bucket}: {s.calls.toLocaleString()} · {fmtUsd(s.costUsd)}
                            </Badge>
                          ))}
                          {p.stages.length === 0 && <span className="text-2xs text-admin-fg-muted">—</span>}
                        </span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <p className="text-2xs text-admin-fg-muted">
              Live voice sessions in this window:{' '}
              {overview.liveVoiceSessions.length === 0
                ? 'none'
                : overview.liveVoiceSessions.map((lv) => `${lv.provider === 'openai' ? 'OpenAI' : lv.provider === 'gemini' ? 'Gemini' : lv.provider}: ${lv.sessions.toLocaleString()}`).join(', ')}
              . Live voice audio does not flow through the usage ledger, so sessions are counted but no USD is estimated for them.
              {' '}All USD figures are internally tracked rate-card estimates, not provider billing.
            </p>
          </div>
        )}
      </SettingsSection>

      <SettingsSection
        id="subscriptions"
        title="Subscriptions"
        description="Consumer subscriptions expose no official usage API. Claude Max figures come from our sidecar's weekly counter (or a local estimate), Codex reviewer figures from its sidecar plus live queue counters — all internally tracked."
      >
        {!overview ? (
          <Skeleton className="h-32 w-full" />
        ) : (
          <div className="grid gap-4 lg:grid-cols-2">
            <Card>
              <CardContent className="space-y-3 p-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <p className="font-semibold text-admin-fg-strong">Claude Max 5x (grading)</p>
                  <UsageSourceBadge source={overview.claudeMax.source} />
                </div>
                <QuotaBar
                  pct={overview.claudeMax.utilizationPct}
                  used={overview.claudeMax.weeklyTokensUsed}
                  cap={overview.claudeMax.weeklyTokenCap}
                  unitLabel="tokens this week"
                />
                <dl className="grid gap-2 text-sm sm:grid-cols-2">
                  <div>
                    <dt className="text-2xs uppercase tracking-wider text-admin-fg-muted">Resets</dt>
                    <dd>{overview.claudeMax.resetsAt ? new Date(overview.claudeMax.resetsAt).toLocaleString() : 'rolling week'}</dd>
                  </div>
                  <div>
                    <dt className="text-2xs uppercase tracking-wider text-admin-fg-muted">Last 7 days split</dt>
                    <dd>Writing {fmtTokens(overview.claudeMax.writingTokens7d)} · Speaking {fmtTokens(overview.claudeMax.speakingTokens7d)}</dd>
                  </div>
                </dl>
                <p className="text-2xs text-admin-fg-muted">
                  &ldquo;{overview.claudeMax.source === 'reported' ? 'Reported by our bridge' : overview.claudeMax.source === 'estimated' ? 'Estimated internally from usage rows' : 'Unknown — bridge unreachable'}&rdquo;.
                  The weekly cap is the operator-set safety budget, not an Anthropic-published number.
                </p>
              </CardContent>
            </Card>

            <Card>
              <CardContent className="space-y-3 p-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <p className="font-semibold text-admin-fg-strong">ChatGPT / Codex (reviewer)</p>
                  <UsageSourceBadge source={overview.codexReviewer.source} />
                </div>
                <QuotaBar
                  pct={overview.codexReviewer.utilizationPct}
                  used={overview.codexReviewer.inputTokensThisWeek + overview.codexReviewer.outputTokensThisWeek}
                  cap={null}
                  unitLabel="tokens this week"
                />
                <dl className="grid gap-2 text-sm sm:grid-cols-2">
                  <div>
                    <dt className="text-2xs uppercase tracking-wider text-admin-fg-muted">Requests this week</dt>
                    <dd>{overview.codexReviewer.requestsThisWeek.toLocaleString()}</dd>
                  </div>
                  <div>
                    <dt className="text-2xs uppercase tracking-wider text-admin-fg-muted">Week started</dt>
                    <dd>{overview.codexReviewer.weekStartedAt ? new Date(overview.codexReviewer.weekStartedAt).toLocaleString() : '—'}</dd>
                  </div>
                </dl>
                <div className="space-y-2">
                  {overview.codexReviewer.queue.length === 0 ? (
                    <p className="text-2xs text-admin-fg-muted">No reviewer activity recorded yet in this process.</p>
                  ) : (
                    overview.codexReviewer.queue.map((q) => (
                      <div key={q.assessmentType} className="rounded-admin-lg border border-admin-border p-2 text-sm">
                        <p className="font-medium capitalize text-admin-fg-strong">{q.assessmentType}</p>
                        <p className="text-2xs text-admin-fg-muted">
                          in flight {q.inFlight} · reviews {q.completed} · codex ok {q.codexSuccess}/{q.codexTotal} · quota hits {q.codexQuota} · timeouts {q.codexTimeout} · API fallbacks {q.apiFallbacks}
                          {q.lastFallbackReason ? ` · last fallback: ${q.lastFallbackReason}` : ''}
                        </p>
                      </div>
                    ))
                  )}
                </div>
              </CardContent>
            </Card>
          </div>
        )}
      </SettingsSection>

      <SettingsSection
        id="credits"
        title="Credits & grants"
        description="Enter promotional credits or top-ups when they arrive (e.g. $200 Anthropic API credits). Remaining = grant minus internally-tracked spend on that provider since the grant start. Providers do not expose prepaid balances over an API, so this figure is computed, not read from the provider."
        actions={
          <div className="flex flex-wrap items-end gap-2">
            <NativeSelect
              label="Provider"
              placeholder="Choose provider"
              value={grantForm.providerCode}
              options={(providerRows.length > 0 ? providerRows.map((p) => ({ value: p.code, label: p.name })) : (data?.providers ?? []).map((p) => ({ value: p.code, label: p.name })))}
              onChange={(e) => setGrantForm((f) => ({ ...f, providerCode: e.target.value }))}
            />
            <Input
              label="Amount (USD)"
              type="number"
              min={0}
              step="0.01"
              value={grantForm.amount}
              onChange={(e) => setGrantForm((f) => ({ ...f, amount: e.target.value }))}
            />
            <Input
              label="Note"
              value={grantForm.note}
              placeholder="e.g. promotional credits"
              onChange={(e) => setGrantForm((f) => ({ ...f, note: e.target.value }))}
            />
            <Button size="sm" disabled={grantBusy || !grantForm.providerCode} onClick={() => void addGrant()}>
              <Plus className="h-4 w-4" aria-hidden="true" />
              Add grant
            </Button>
          </div>
        }
      >
        {!overview ? (
          <Skeleton className="h-24 w-full" />
        ) : overview.credits.length === 0 ? (
          <p className="text-sm text-admin-fg-muted">No credit grants recorded. Add one when promotional credits land.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="text-start text-2xs uppercase tracking-wider text-admin-fg-muted">
                  <th className="px-3 py-2 text-start">Provider</th>
                  <th className="px-3 py-2 text-start">Grant</th>
                  <th className="px-3 py-2 text-start">Spent since start</th>
                  <th className="px-3 py-2 text-start">Remaining</th>
                  <th className="px-3 py-2 text-start">Since</th>
                  <th className="px-3 py-2 text-start">Note</th>
                  <th className="px-3 py-2 text-start"><span className="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                {overview.credits.map((c) => {
                  const exhausted = c.remainingUsd !== null && c.remainingUsd <= 0;
                  return (
                    <tr key={c.id} className="border-t border-admin-border">
                      <td className="px-3 py-2">{providerByCode.get(c.providerCode)?.name ?? c.providerCode}</td>
                      <td className="px-3 py-2 tabular-nums">{fmtUsd(c.grantUsd)}</td>
                      <td className="px-3 py-2 tabular-nums">{fmtUsd(c.spentSinceStartUsd)}</td>
                      <td className="px-3 py-2 tabular-nums">
                        {exhausted ? <Badge variant="warning">Exhausted</Badge> : <span>{fmtUsd(c.remainingUsd ?? 0)}</span>}
                      </td>
                      <td className="px-3 py-2">{new Date(c.startsAt).toLocaleDateString()}</td>
                      <td className="px-3 py-2 text-admin-fg-muted">{c.note ?? '—'}</td>
                      <td className="px-3 py-2">
                        <Button variant="ghost" size="sm" aria-label={`Remove ${c.providerCode} grant`} disabled={grantBusy} onClick={() => void removeGrant(c.id)}>
                          <Trash2 className="h-4 w-4" aria-hidden="true" />
                        </Button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </SettingsSection>

      <SettingsSection
        id="providers"
        title="Keys & providers"
        description="Add or rotate API keys (encrypted server-side, never returned to the browser), test connections, discover models and switch providers on or off — effective on the next call, no redeploy. Subscription bridges have no API key; their auth lives in the sidecar containers. Full per-provider settings remain on the AI Providers page."
        actions={
          <Button variant="outline" size="sm" onClick={() => void loadProviderRows()} aria-label="Refresh providers">
            <RefreshCw className="h-4 w-4" aria-hidden="true" />
            Refresh
          </Button>
        }
      >
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-start text-2xs uppercase tracking-wider text-admin-fg-muted">
                <th className="px-3 py-2 text-start">Provider</th>
                <th className="px-3 py-2 text-start">Key</th>
                <th className="px-3 py-2 text-start">Model</th>
                <th className="px-3 py-2 text-start">Active</th>
                <th className="px-3 py-2 text-start">Connection</th>
                <th className="px-3 py-2 text-start">Used in</th>
                <th className="px-3 py-2 text-start"><span className="sr-only">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              {providerRows.length === 0 && (
                <tr><td colSpan={7} className="px-3 py-3 text-admin-fg-muted">
                  {data.providers.length === 0 ? 'No providers registered yet.' : 'Loading full provider details…'}
                </td></tr>
              )}
              {providerRows.map((p) => {
                const isBridge = p.apiKeyHint === 'claude-max-5x' || p.apiKeyHint === 'codex-chatgpt';
                const editing = keyDraft?.id === p.id;
                const models = discoveredModels[p.id];
                const usedIn = stagesByProvider.get(p.code) ?? [];
                return (
                  <ProviderKeyRow
                    key={p.id}
                    provider={p}
                    isBridge={isBridge}
                    usedIn={usedIn}
                    models={models}
                    busy={rowBusy === p.id}
                    editing={editing}
                    draft={keyDraft}
                    onEdit={() => setKeyDraft(editing ? null : { id: p.id, apiKey: '', model: p.defaultModel, isActive: p.isActive })}
                    onCancel={() => setKeyDraft(null)}
                    onDraftChange={(patch) => setKeyDraft((d) => (d && d.id === p.id ? { ...d, ...patch } : d))}
                    onSave={() => void saveKeyRow(p)}
                    onToggle={(next) => void toggleProviderActive(p, next)}
                    onTest={() => void testRow(p)}
                    onDiscover={() => void discoverRow(p)}
                  />
                );
              })}
            </tbody>
          </table>
        </div>
      </SettingsSection>

      <SettingsSection
        id="self-check"
        title="Pipeline self-check"
        description="Production evidence on demand: the next run follows the saved order, disabled steps were never called, and what actually served since the last change. The live probe makes one cheap connection test per enabled step."
        actions={
          <div className="flex flex-wrap gap-2">
            <Button variant="outline" size="sm" disabled={busy === 'self-check'} onClick={() => void check(false)}>
              <ShieldCheck className="h-4 w-4" aria-hidden="true" />
              Run check
            </Button>
            <Button variant="outline" size="sm" disabled={busy === 'self-check'} onClick={() => void check(true)}>
              Run with live probe
            </Button>
          </div>
        }
      >
        {selfCheck ? (
          <div className="space-y-3 text-sm">
            <p className="text-admin-fg-muted">Ran {new Date(selfCheck.ranAt).toLocaleString()}{selfCheck.live ? ' with live probe' : ''}.</p>
            {selfCheck.results.map((r) => (
              <div key={r.stageKey} className="rounded-admin-lg border border-admin-border p-3">
                <p className="font-medium text-admin-fg-strong">{r.label} <span className="text-admin-fg-muted">v{r.version}</span></p>
                <ul className="mt-2 space-y-1">
                  {r.checks.map((c) => (
                    <li key={c.name} className="flex flex-wrap items-start gap-2">
                      {c.ok ? <Badge variant="success">Pass</Badge> : <Badge variant="danger">Fail</Badge>}
                      <span className="min-w-0 flex-1 text-admin-fg-default">
                        {c.name.replace(/_/g, ' ')}
                        {c.detail ? `: ${c.detail}` : ''}
                        {c.violations?.map((v) => ` | ${v.provider}: ${v.callsAfterGrace} calls after the change window, ${v.callsInsideGrace} inside it`).join('')}
                        {c.served?.length ? ` | ${c.served.map((s) => `${s.providerId ?? 'none'} ${s.outcome} x${s.calls}`).join(', ')}` : ''}
                        {c.probes?.map((p) => ` | ${p.provider}: ${p.status} ${p.latencyMs}ms`).join('')}
                      </span>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        ) : (
          <p className="text-sm text-admin-fg-muted">Not run yet.</p>
        )}
      </SettingsSection>
    </AdminSettingsLayout>
  );
}

/**
 * One step row. The grip drags with pointer events (mouse, pen and touch); the up and down buttons do the same job
 * for keyboards and screen readers.
 */
function HopRow({
  index,
  count,
  onMove,
  children,
}: {
  index: number;
  count: number;
  onMove: (to: number) => void;
  children: React.ReactNode;
}) {
  const rowRef = useRef<HTMLLIElement | null>(null);
  const dragging = useRef(false);

  function onPointerDown(e: React.PointerEvent<HTMLButtonElement>) {
    dragging.current = true;
    e.currentTarget.setPointerCapture(e.pointerId);
  }

  function onPointerMove(e: React.PointerEvent<HTMLButtonElement>) {
    if (!dragging.current || !rowRef.current) return;
    const list = rowRef.current.parentElement;
    if (!list) return;
    const rows = Array.from(list.children) as HTMLElement[];
    for (let i = 0; i < rows.length; i += 1) {
      const rect = rows[i].getBoundingClientRect();
      if (e.clientY >= rect.top && e.clientY <= rect.bottom) {
        if (i !== index) onMove(i);
        return;
      }
    }
  }

  function endDrag(e: React.PointerEvent<HTMLButtonElement>) {
    dragging.current = false;
    if (e.currentTarget.hasPointerCapture(e.pointerId)) e.currentTarget.releasePointerCapture(e.pointerId);
  }

  return (
    <li ref={rowRef} className="flex flex-col gap-3 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-3 sm:flex-row sm:items-start">
      <div className="flex items-center gap-1 sm:flex-col">
        <button
          type="button"
          aria-label="Drag to reorder"
          className="flex h-9 w-9 cursor-grab touch-none items-center justify-center rounded-admin-md text-admin-fg-muted hover:bg-admin-bg-subtle active:cursor-grabbing"
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={endDrag}
          onPointerCancel={endDrag}
        >
          <GripVertical className="h-4 w-4" aria-hidden="true" />
        </button>
        <Button variant="ghost" size="sm" aria-label="Move up" disabled={index === 0} onClick={() => onMove(index - 1)}>
          <ArrowUp className="h-4 w-4" aria-hidden="true" />
        </Button>
        <Button variant="ghost" size="sm" aria-label="Move down" disabled={index === count - 1} onClick={() => onMove(index + 1)}>
          <ArrowDown className="h-4 w-4" aria-hidden="true" />
        </Button>
      </div>
      {children}
    </li>
  );
}

/** Source label for subscription gauges: honest about what produced the number. */
function UsageSourceBadge({ source }: { source: string }) {
  if (source === 'reported') return <Badge variant="success">Reported by our bridge</Badge>;
  if (source === 'estimated') return <Badge variant="warning">Estimated internally</Badge>;
  return <Badge variant="muted">Unknown</Badge>;
}

/** Weekly allowance bar: percentage when a cap exists, raw counter otherwise. */
function QuotaBar({ pct, used, cap, unitLabel }: { pct: number | null; used: number; cap: number | null; unitLabel: string }) {
  const clamped = pct !== null ? Math.min(100, Math.max(0, pct)) : null;
  return (
    <div className="space-y-1">
      <div className="flex items-center justify-between text-sm">
        <span className="text-admin-fg-default">{fmtTokens(used)} {unitLabel}{cap ? ` of ${fmtTokens(cap)}` : ''}</span>
        {clamped !== null && <span className="tabular-nums text-admin-fg-muted">{clamped.toFixed(0)}%</span>}
      </div>
      <div className="h-2 w-full overflow-hidden rounded-full bg-admin-bg-subtle" role="progressbar" aria-valuenow={clamped ?? undefined} aria-valuemin={0} aria-valuemax={100}>
        {clamped !== null && (
          <div
            className={`h-full rounded-full ${clamped >= 90 ? 'bg-admin-danger' : clamped >= 80 ? 'bg-amber-500' : 'bg-emerald-500'}`}
            style={{ width: `${clamped}%` }}
          />
        )}
      </div>
    </div>
  );
}

/**
 * One provider row of the keys table plus its inline editor. The editor appears directly under the
 * row (a second <tr>) so key rotation stays a two-click affair on this page.
 */
function ProviderKeyRow({
  provider: p,
  isBridge,
  usedIn,
  models,
  busy,
  editing,
  draft,
  onEdit,
  onCancel,
  onDraftChange,
  onSave,
  onToggle,
  onTest,
  onDiscover,
}: {
  provider: AiProviderRow;
  isBridge: boolean;
  usedIn: string[];
  models: string[] | undefined;
  busy: boolean;
  editing: boolean;
  draft: { id: string; apiKey: string; model: string; isActive: boolean } | null;
  onEdit: () => void;
  onCancel: () => void;
  onDraftChange: (patch: Partial<{ apiKey: string; model: string; isActive: boolean }>) => void;
  onSave: () => void;
  onToggle: (next: boolean) => void;
  onTest: () => void;
  onDiscover: () => void;
}) {
  return (
    <>
      <tr className="border-t border-admin-border align-top">
        <td className="px-3 py-2 font-medium text-admin-fg-strong">
          {p.name}
          <span className="block text-2xs text-admin-fg-muted">{p.code} · {p.dialect}</span>
        </td>
        <td className="px-3 py-2">
          {isBridge ? (
            <Badge variant="muted">Subscription bridge</Badge>
          ) : p.apiKeyHint ? (
            <Badge variant="info">{p.apiKeyHint}</Badge>
          ) : (
            <Badge variant="warning">No key</Badge>
          )}
        </td>
        <td className="px-3 py-2">{p.defaultModel || '—'}</td>
        <td className="px-3 py-2">
          <Switch checked={p.isActive} onCheckedChange={onToggle} disabled={busy} aria-label={`${p.name} active`} />
        </td>
        <td className="px-3 py-2">
          {p.lastTestStatus ? (
            <span className="flex flex-col">
              <Badge variant={p.lastTestStatus === 'ok' ? 'success' : p.lastTestStatus === 'auth' ? 'danger' : p.lastTestStatus === 'rate_limited' ? 'warning' : 'muted'}>
                {p.lastTestStatus}
              </Badge>
              {p.lastTestedAt && <span className="text-2xs text-admin-fg-muted">{new Date(p.lastTestedAt).toLocaleString()}</span>}
            </span>
          ) : (
            <span className="text-2xs text-admin-fg-muted">never tested</span>
          )}
        </td>
        <td className="px-3 py-2">
          <span className="flex flex-wrap gap-1">
            {usedIn.length === 0 ? <span className="text-2xs text-admin-fg-muted">not in any stage</span>
              : usedIn.map((label) => <Badge key={label} variant="muted">{label}</Badge>)}
          </span>
        </td>
        <td className="px-3 py-2">
          <span className="flex flex-wrap justify-end gap-2">
            <Button variant="ghost" size="sm" disabled={busy} onClick={onTest}>
              <ShieldCheck className="h-4 w-4" aria-hidden="true" />
              Test
            </Button>
            {!isBridge && (
              <Button variant="outline" size="sm" disabled={busy} onClick={onEdit}>
                <KeyRound className="h-4 w-4" aria-hidden="true" />
                {editing ? 'Close' : p.apiKeyHint ? 'Update key' : 'Add key'}
              </Button>
            )}
          </span>
        </td>
      </tr>
      {editing && draft && (
        <tr className="border-t border-admin-border bg-admin-bg-subtle">
          <td colSpan={7} className="px-3 py-3">
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4 sm:items-end">
              <Input
                label="API key (paste to rotate; stored encrypted, never shown again)"
                type="password"
                autoComplete="off"
                value={draft.apiKey}
                placeholder={p.apiKeyHint || 'sk-…'}
                onChange={(e) => onDraftChange({ apiKey: e.target.value })}
              />
              <div className="space-y-1">
                <Input
                  label="Default model"
                  value={draft.model}
                  placeholder={p.defaultModel || 'provider default'}
                  onChange={(e) => onDraftChange({ model: e.target.value })}
                />
                {models && models.length > 0 && (
                  <NativeSelect
                    label="Discovered models"
                    placeholder="Pick a model"
                    value=""
                    options={models.slice(0, 200).map((m) => ({ value: m, label: m }))}
                    onChange={(e) => { if (e.target.value) onDraftChange({ model: e.target.value }); }}
                  />
                )}
              </div>
              <div className="flex items-center gap-2 pb-2">
                <Switch checked={draft.isActive} onCheckedChange={(v) => onDraftChange({ isActive: v })} aria-label="Active after save" />
                <span className="text-sm text-admin-fg-default">Active after save</span>
              </div>
              <div className="flex flex-wrap gap-2 pb-1">
                <Button size="sm" disabled={busy} onClick={onSave}>Save</Button>
                <Button variant="outline" size="sm" disabled={busy} onClick={onDiscover}>
                  Discover models
                </Button>
                <Button variant="ghost" size="sm" disabled={busy} onClick={onCancel}>Cancel</Button>
              </div>
            </div>
            <p className="mt-2 text-2xs text-admin-fg-muted">
              Saving rotates the key and applies the model/active change to every server on the next call.
              The key is encrypted with server-side Data Protection and is never returned by any API or log.
            </p>
          </td>
        </tr>
      )}
    </>
  );
}
