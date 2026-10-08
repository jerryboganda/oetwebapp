'use client';

/**
 * AI Pipelines: the one page that orders, switches and checks the five AI stages (Writing grading, Writing reviewer,
 * Speaking grading, Speaking reviewer, Speaking live voice). Phase 1 of the AI Pipeline Control Center
 * (owner directive 2026-10-09). The saved order is authoritative and versioned; every save is audited and can be
 * rolled back. Keys, benchmarks, usage and cost sections arrive in the next phases on this same page.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { AlertTriangle, ArrowDown, ArrowUp, GripVertical, History, Plus, RefreshCw, RotateCcw, ShieldCheck } from 'lucide-react';
import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Input } from '@/components/admin/ui/input';
import { NativeSelect } from '@/components/admin/ui/native-select';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Switch } from '@/components/admin/ui/switch';
import { toast } from '@/components/ui/toaster';
import {
  fetchPipelineHistory,
  fetchPipelines,
  restorePipelineDefault,
  rollbackPipelineStage,
  runPipelineSelfCheck,
  savePipelineStage,
  type PipelineHopInput,
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

export default function AiPipelinesPage() {
  const [data, setData] = useState<PipelinesResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [busy, setBusy] = useState<string | null>(null);
  const [history, setHistory] = useState<Record<string, PipelineRevision[] | undefined>>({});
  const [selfCheck, setSelfCheck] = useState<SelfCheckResponse | null>(null);

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
  }, [load]);

  const providerByCode = useMemo(() => {
    const map = new Map<string, PipelineProvider>();
    for (const p of data?.providers ?? []) map.set(p.code, p);
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

  const layoutProps = {
    title: 'AI Pipelines',
    description: 'Order, switch and check the AI providers behind Writing, Speaking and live voice. Changes apply to the next grade or session on every server, with no redeploy.',
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
        id="providers"
        title="Providers"
        description="Connection state of every text provider. Adding keys, testing and choosing models arrive in the next phase on this page; use AI Providers until then."
      >
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-start text-2xs uppercase tracking-wider text-admin-fg-muted">
                <th className="px-3 py-2 text-start">Provider</th>
                <th className="px-3 py-2 text-start">Model</th>
                <th className="px-3 py-2 text-start">State</th>
                <th className="px-3 py-2 text-start">Last test</th>
              </tr>
            </thead>
            <tbody>
              {data.providers.map((p) => (
                <tr key={p.code} className="border-t border-admin-border">
                  <td className="px-3 py-2">{p.name}<span className="block text-2xs text-admin-fg-muted">{p.code}</span></td>
                  <td className="px-3 py-2">{p.defaultModel || '-'}</td>
                  <td className="px-3 py-2">
                    <span className="flex flex-wrap gap-1">
                      {p.isActive ? <Badge variant="success">Active</Badge> : <Badge variant="muted">Inactive</Badge>}
                      {p.isSubscriptionBridge ? <Badge variant="muted">Subscription</Badge> : p.hasKey ? <Badge variant="info">Key set</Badge> : <Badge variant="warning">No key</Badge>}
                    </span>
                  </td>
                  <td className="px-3 py-2">{p.lastTestStatus ?? 'never'}{p.lastTestedAt ? `, ${new Date(p.lastTestedAt).toLocaleString()}` : ''}</td>
                </tr>
              ))}
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
