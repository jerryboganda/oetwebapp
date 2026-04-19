'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { AlertCircle, Cpu, Gauge, Power, RefreshCw, Server, ShieldCheck } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteStatRow,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { Input, Select } from '@/components/ui/form-controls';
import { Switch } from '@/components/ui/switch';
import { Modal } from '@/components/ui/modal';
import { Tabs } from '@/components/ui/tabs';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchAiAnomalies,
  fetchAiGlobalPolicy,
  fetchAiPlans,
  fetchAiProviders,
  fetchAiUsage,
  fetchAiUsageSummary,
  fetchAiUsageTrend,
  toggleAiKillSwitch,
  updateAiGlobalPolicy,
  createAiPlan,
  updateAiPlan,
  deactivateAiPlan,
  createAiProvider,
  updateAiProvider,
  deactivateAiProvider,
  type AiGlobalPolicy,
  type AiProviderRow,
  type AiQuotaPlan,
  type AiUsagePage,
  type AiUsageSummaryRow,
} from '@/lib/ai-management-api';

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const TABS = [
  { id: 'usage', label: 'Usage', icon: <Gauge className="w-4 h-4" /> },
  { id: 'budget', label: 'Budget & Kill-switch', icon: <Power className="w-4 h-4" /> },
  { id: 'plans', label: 'Quota Plans', icon: <ShieldCheck className="w-4 h-4" /> },
  { id: 'providers', label: 'Providers', icon: <Server className="w-4 h-4" /> },
  { id: 'anomalies', label: 'Anomalies', icon: <AlertCircle className="w-4 h-4" /> },
];

function fmt(n: number): string {
  return n.toLocaleString();
}
function fmtUsd(n: number): string {
  return `$${n.toFixed(2)}`;
}

export default function AiUsagePage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [activeTab, setActiveTab] = useState('usage');
  const [toast, setToast] = useState<ToastState>(null);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <EmptyState icon={<ShieldCheck className="w-8 h-8" />} title="Admin access required" description="Sign in with an admin account to view this page." />
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        icon={<Cpu className="w-6 h-6" />}
        title="AI Usage & Budget"
        description="Platform-wide control over AI spend, quota plans, providers, and per-user credentials. See docs/AI-USAGE-POLICY.md."
      />

      <Tabs tabs={TABS} activeTab={activeTab} onChange={setActiveTab} />

      {activeTab === 'usage' && <UsagePanel onToast={setToast} />}
      {activeTab === 'budget' && <BudgetPanel onToast={setToast} />}
      {activeTab === 'plans' && <PlansPanel onToast={setToast} />}
      {activeTab === 'providers' && <ProvidersPanel onToast={setToast} />}
      {activeTab === 'anomalies' && <AnomaliesPanel onToast={setToast} />}

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}

// ═════════════════════════════════════════════════════════════════════════
function UsagePanel({ onToast }: { onToast: (t: ToastState) => void }) {
  const [status, setStatus] = useState<PageStatus>('loading');
  const [summary, setSummary] = useState<AiUsageSummaryRow[]>([]);
  const [log, setLog] = useState<AiUsagePage | null>(null);
  const [trend, setTrend] = useState<{ day: string; totalTokens: number; calls: number }[]>([]);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const [sum, page, tr] = await Promise.all([
        fetchAiUsageSummary(undefined, 'feature'),
        fetchAiUsage({ page: 1, pageSize: 25 }),
        fetchAiUsageTrend(),
      ]);
      setSummary(sum.rows);
      setLog(page);
      setTrend(tr.rows);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      onToast({ variant: 'error', message: `Failed to load usage data: ${(e as Error).message}` });
    }
  }, [onToast]);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const totals = useMemo(() => {
    const tokens = summary.reduce((acc, r) => acc + r.totalTokens, 0);
    const calls = summary.reduce((acc, r) => acc + r.calls, 0);
    return { tokens, calls };
  }, [summary]);

  const summaryColumns: Column<AiUsageSummaryRow>[] = [
    { key: 'key', header: 'Feature', render: (r) => <span className="font-mono text-sm">{r.key}</span> },
    { key: 'calls', header: 'Calls', render: (r) => fmt(r.calls) },
    { key: 'tokens', header: 'Tokens', render: (r) => fmt(r.totalTokens) },
    {
      key: 'ok', header: 'Success rate', render: (r) => {
        const total = (r.successes ?? 0) + (r.failures ?? 0);
        const pct = total === 0 ? 100 : Math.round(((r.successes ?? 0) / total) * 100);
        return <Badge variant={pct >= 95 ? 'success' : pct >= 80 ? 'warning' : 'danger'}>{pct}%</Badge>;
      },
    },
    { key: 'cost', header: 'Est. cost', render: (r) => fmtUsd(r.costEstimateUsd ?? 0) },
  ];

  const logColumns: Column<AiUsagePage['rows'][number]>[] = [
    { key: 't', header: 'Time', render: (r) => new Date(r.createdAt).toLocaleString() },
    { key: 'f', header: 'Feature', render: (r) => <span className="font-mono text-xs">{r.featureCode}</span> },
    { key: 'p', header: 'Provider', render: (r) => r.providerId ?? '—' },
    { key: 'k', header: 'Key', render: (r) => <Badge variant={r.keySource === 'Byok' ? 'info' : 'muted'}>{r.keySource}</Badge> },
    { key: 'tk', header: 'Tokens', render: (r) => fmt(r.totalTokens) },
    { key: 'o', header: 'Outcome', render: (r) => <Badge variant={r.outcome === 'Success' ? 'success' : 'danger'}>{r.outcome}</Badge> },
    { key: 'l', header: 'Latency', render: (r) => `${r.latencyMs}ms` },
    { key: 'tr', header: 'Policy trace', render: (r) => <span className="text-xs text-muted truncate max-w-xs">{r.policyTrace ?? ''}</span> },
  ];

  return (
    <AsyncStateWrapper status={status}>
      {/* Quick-signals summary strip. Replaces the 3 vanity cards
          (calls/tokens/trend-days) that were the same info at different
          granularity. Now uses AdminRouteStatRow for a calmer rhythm. */}
      <AdminRoutePanel
        eyebrow="This month"
        title="Usage at a glance"
        actions={
          <Button variant="ghost" size="sm" onClick={() => void load()}>
            <RefreshCw className="h-3.5 w-3.5" aria-hidden /> Refresh
          </Button>
        }
      >
        <AdminRouteStatRow
          items={[
            { label: 'Calls', value: fmt(totals.calls), hint: 'Total invocations' },
            { label: 'Tokens', value: fmt(totals.tokens), hint: 'Prompt + completion' },
            {
              label: 'Active days',
              value: fmt(trend.length),
              hint: `${trend.length > 0 ? 'With activity' : 'No recent activity'}`,
              tone: trend.length === 0 ? 'warning' : 'info',
            },
          ]}
        />
      </AdminRoutePanel>

      <AdminRoutePanel eyebrow="Features" title="By feature">
        <DataTable density="compact" data={summary} columns={summaryColumns} keyExtractor={(r) => r.key} />
      </AdminRoutePanel>

      {log && (
        <AdminRoutePanel
          eyebrow="Recent activity"
          title={`Recent calls (${log.total} total)`}
          dense
        >
          <DataTable density="compact" data={log.rows} columns={logColumns} keyExtractor={(r) => r.id} />
        </AdminRoutePanel>
      )}
    </AsyncStateWrapper>
  );
}

// ═════════════════════════════════════════════════════════════════════════
function BudgetPanel({ onToast }: { onToast: (t: ToastState) => void }) {
  const [status, setStatus] = useState<PageStatus>('loading');
  const [policy, setPolicy] = useState<AiGlobalPolicy | null>(null);
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    try { setPolicy(await fetchAiGlobalPolicy()); setStatus('success'); } catch { setStatus('error'); }
  }, []);
  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const save = async () => {
    if (!policy) return;
    setSaving(true);
    try {
      const updated = await updateAiGlobalPolicy(policy);
      setPolicy(updated);
      onToast({ variant: 'success', message: 'Global policy updated.' });
    } catch (e) {
      onToast({ variant: 'error', message: `Save failed: ${(e as Error).message}` });
    } finally { setSaving(false); }
  };

  const toggleKill = async (enabled: boolean) => {
    if (!policy) return;
    try {
      const updated = await toggleAiKillSwitch(enabled, policy.killSwitchScope, policy.killSwitchReason ?? undefined);
      setPolicy(updated);
      onToast({ variant: 'success', message: enabled ? 'Kill-switch ENGAGED.' : 'Kill-switch disengaged.' });
    } catch (e) { onToast({ variant: 'error', message: `Toggle failed: ${(e as Error).message}` }); }
  };

  return (
    <AsyncStateWrapper status={status}>
      {policy && (
        <div className="space-y-6 mt-4">
          <AdminRoutePanel
            eyebrow="Platform controls"
            title="Kill-switch"
            description="Engage to immediately stop all platform-keyed AI calls. Learners' BYOK keys still function unless scope is set to All calls."
          >
            <div className="flex flex-wrap items-end gap-4">
              <Badge variant={policy.killSwitchEnabled ? 'danger' : 'success'}>
                {policy.killSwitchEnabled ? 'ENGAGED' : 'Off'}
              </Badge>
              <Select
                label="Scope"
                value={policy.killSwitchScope}
                onChange={(e) => setPolicy({ ...policy, killSwitchScope: e.target.value as AiGlobalPolicy['killSwitchScope'] })}
                options={[
                  { value: 'PlatformKeysOnly', label: 'Platform keys only (BYOK continues)' },
                  { value: 'AllCalls', label: 'All calls (hard stop)' },
                ]}
              />
              <Input
                label="Reason (shown to learners)"
                value={policy.killSwitchReason ?? ''}
                onChange={(e) => setPolicy({ ...policy, killSwitchReason: e.target.value })}
              />
              <Button variant={policy.killSwitchEnabled ? 'secondary' : 'primary'} onClick={() => toggleKill(!policy.killSwitchEnabled)}>
                {policy.killSwitchEnabled ? 'Disengage' : 'Engage'} kill-switch
              </Button>
            </div>
          </AdminRoutePanel>

          <AdminRoutePanel
            eyebrow="Spend"
            title="Monthly budget"
            description="Protects the platform from runaway usage by auto-disengaging platform-keyed AI when the monthly spend crosses the hard-kill threshold."
          >
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <Input type="number" step="0.01" label="Monthly budget (USD)" value={policy.monthlyBudgetUsd}
                onChange={(e) => setPolicy({ ...policy, monthlyBudgetUsd: Number(e.target.value) })} />
              <Input type="number" label="Soft-warn at %" value={policy.softWarnPct}
                onChange={(e) => setPolicy({ ...policy, softWarnPct: Number(e.target.value) })} />
              <Input type="number" label="Hard-kill at %" value={policy.hardKillPct}
                onChange={(e) => setPolicy({ ...policy, hardKillPct: Number(e.target.value) })} />
            </div>
            {(() => {
              const pct = policy.monthlyBudgetUsd > 0
                ? Math.round((policy.currentSpendUsd / policy.monthlyBudgetUsd) * 100)
                : 0;
              const barTone = pct >= policy.hardKillPct ? 'bg-danger'
                : pct >= policy.softWarnPct ? 'bg-amber-500'
                : 'bg-success';
              return (
                <div className="space-y-2">
                  <div className="flex items-baseline justify-between text-sm">
                    <span className="text-muted">
                      Current spend: <span className="font-semibold text-navy">{fmtUsd(policy.currentSpendUsd)}</span>
                    </span>
                    <span className="text-muted">
                      {pct}% of {fmtUsd(policy.monthlyBudgetUsd)} budget
                    </span>
                  </div>
                  <div className="h-1.5 w-full rounded-full bg-background-light overflow-hidden">
                    <div
                      className={`h-full ${barTone} transition-all`}
                      style={{ width: `${Math.min(100, pct)}%` }}
                    />
                  </div>
                </div>
              );
            })()}
          </AdminRoutePanel>

          <AdminRoutePanel
            eyebrow="Credentials"
            title="BYOK policy"
            description="Controls whether learners' own API keys are accepted for various feature classes."
          >
            <div className="space-y-2">
              <Switch
                checked={policy.allowByokOnScoringFeatures}
                onCheckedChange={(next) => setPolicy({ ...policy, allowByokOnScoringFeatures: next })}
                label="Allow BYOK on scoring-critical features"
                description="Default: off. Keeps platform keys in the scoring path for grade integrity."
              />
              <Switch
                checked={policy.allowByokOnNonScoringFeatures}
                onCheckedChange={(next) => setPolicy({ ...policy, allowByokOnNonScoringFeatures: next })}
                label="Allow BYOK on practice / conversation / summarisation features"
              />
              <Input label="Default platform provider code" value={policy.defaultPlatformProviderId}
                onChange={(e) => setPolicy({ ...policy, defaultPlatformProviderId: e.target.value })} />
            </div>
          </AdminRoutePanel>

          <AdminRoutePanel
            eyebrow="Safety"
            title="Anomaly detection"
            description="Flag users whose daily token usage jumps above their trailing 7-day median. Helps catch runaway agents or abuse early."
          >
            <div className="flex flex-wrap items-end gap-4">
              <Switch
                checked={policy.anomalyDetectionEnabled}
                onCheckedChange={(next) => setPolicy({ ...policy, anomalyDetectionEnabled: next })}
                label="Anomaly detection enabled"
              />
              <Input type="number" step="0.5" label="Flag users at N× median" value={policy.anomalyMultiplierX}
                onChange={(e) => setPolicy({ ...policy, anomalyMultiplierX: Number(e.target.value) })} />
            </div>
          </AdminRoutePanel>

          <div className="flex gap-3">
            <Button variant="primary" onClick={save} loading={saving}>Save changes</Button>
            <Button variant="ghost" onClick={() => void load()}>Reset</Button>
          </div>
        </div>
      )}
    </AsyncStateWrapper>
  );
}

// ═════════════════════════════════════════════════════════════════════════
function PlansPanel({ onToast }: { onToast: (t: ToastState) => void }) {
  const [status, setStatus] = useState<PageStatus>('loading');
  const [plans, setPlans] = useState<AiQuotaPlan[]>([]);
  const [editing, setEditing] = useState<AiQuotaPlan | null>(null);
  const [creating, setCreating] = useState(false);

  const load = useCallback(async () => {
    try { setPlans(await fetchAiPlans()); setStatus('success'); } catch { setStatus('error'); }
  }, []);
  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const save = async (plan: AiQuotaPlan) => {
    try {
      if (creating) await createAiPlan(plan);
      else await updateAiPlan(plan.id, plan);
      onToast({ variant: 'success', message: creating ? 'Plan created.' : 'Plan updated.' });
      setEditing(null); setCreating(false); await load();
    } catch (e) { onToast({ variant: 'error', message: `Save failed: ${(e as Error).message}` }); }
  };

  const deactivate = async (id: string) => {
    try { await deactivateAiPlan(id); onToast({ variant: 'success', message: 'Plan deactivated.' }); await load(); }
    catch (e) { onToast({ variant: 'error', message: `Failed: ${(e as Error).message}` }); }
  };

  const columns: Column<AiQuotaPlan>[] = [
    { key: 'code', header: 'Code', render: (p) => <span className="font-mono">{p.code}</span> },
    { key: 'name', header: 'Name', render: (p) => p.name },
    { key: 'm', header: 'Monthly cap', render: (p) => fmt(p.monthlyTokenCap) },
    { key: 'd', header: 'Daily cap', render: (p) => fmt(p.dailyTokenCap) },
    { key: 'ro', header: 'Rollover', render: (p) => p.rolloverPolicy },
    { key: 'ov', header: 'Overage', render: (p) => p.overagePolicy },
    { key: 'a', header: 'Active', render: (p) => <Badge variant={p.isActive ? 'success' : 'muted'}>{p.isActive ? 'Yes' : 'No'}</Badge> },
    {
      key: 'acts', header: 'Actions', render: (p) => (
        <div className="flex gap-2">
          <Button variant="ghost" size="sm" onClick={() => { setEditing(p); setCreating(false); }}>Edit</Button>
          {p.isActive && <Button variant="ghost" size="sm" onClick={() => void deactivate(p.id)}>Deactivate</Button>}
        </div>
      ),
    },
  ];

  return (
    <AsyncStateWrapper status={status}>
      <div className="flex justify-end mt-4">
        <Button variant="primary" onClick={() => {
          setCreating(true);
          setEditing({
            id: '', code: '', name: '', description: '',
            period: 'Monthly', monthlyTokenCap: 100_000, dailyTokenCap: 25_000, maxConcurrentRequests: 0,
            rolloverPolicy: 'Expire', rolloverCapPct: 20,
            overagePolicy: 'Deny', overageRatePer1kTokens: null,
            autoUpgradeTargetPlanCode: null, degradeModel: null,
            allowedFeaturesCsv: '', allowedModelsCsv: '',
            isActive: true, displayOrder: 0,
            createdAt: '', updatedAt: '',
          });
        }}>+ New plan</Button>
      </div>
      <AdminRoutePanel eyebrow="Plans" title="Quota plans" dense>
        <DataTable density="compact" data={plans} columns={columns} keyExtractor={(p) => p.id || p.code} />
      </AdminRoutePanel>
      {editing && (
        <Modal open={true} onClose={() => { setEditing(null); setCreating(false); }} title={creating ? 'New plan' : `Edit ${editing.code}`}>
          <PlanEditor value={editing} onChange={setEditing} />
          <div className="flex gap-3 mt-4">
            <Button variant="primary" onClick={() => void save(editing)}>Save</Button>
            <Button variant="ghost" onClick={() => { setEditing(null); setCreating(false); }}>Cancel</Button>
          </div>
        </Modal>
      )}
    </AsyncStateWrapper>
  );
}

function PlanEditor({ value, onChange }: { value: AiQuotaPlan; onChange: (p: AiQuotaPlan) => void }) {
  return (
    <div className="grid grid-cols-2 gap-4">
      <Input label="Code" value={value.code} onChange={(e) => onChange({ ...value, code: e.target.value })} />
      <Input label="Name" value={value.name} onChange={(e) => onChange({ ...value, name: e.target.value })} />
      <Input type="number" label="Monthly token cap" value={value.monthlyTokenCap} onChange={(e) => onChange({ ...value, monthlyTokenCap: Number(e.target.value) })} />
      <Input type="number" label="Daily token cap" value={value.dailyTokenCap} onChange={(e) => onChange({ ...value, dailyTokenCap: Number(e.target.value) })} />
      <Select label="Period" value={value.period}
        onChange={(e) => onChange({ ...value, period: e.target.value as AiQuotaPlan['period'] })}
        options={[
          { value: 'Monthly', label: 'Monthly' },
          { value: 'Daily', label: 'Daily' },
          { value: 'Weekly', label: 'Weekly' },
          { value: 'Rolling30d', label: 'Rolling 30 days' },
          { value: 'NeverExpire', label: 'Never expire' },
        ]} />
      <Select label="Rollover" value={value.rolloverPolicy}
        onChange={(e) => onChange({ ...value, rolloverPolicy: e.target.value as AiQuotaPlan['rolloverPolicy'] })}
        options={[
          { value: 'Expire', label: 'Expire at period end' },
          { value: 'RolloverCapped', label: 'Rollover (capped %)' },
          { value: 'RolloverFull', label: 'Rollover (full)' },
        ]} />
      <Input type="number" label="Rollover cap %" value={value.rolloverCapPct} onChange={(e) => onChange({ ...value, rolloverCapPct: Number(e.target.value) })} />
      <Select label="Overage policy" value={value.overagePolicy}
        onChange={(e) => onChange({ ...value, overagePolicy: e.target.value as AiQuotaPlan['overagePolicy'] })}
        options={[
          { value: 'Deny', label: 'Deny (recommended)' },
          { value: 'AllowWithCharge', label: 'Allow with charge' },
          { value: 'AutoUpgrade', label: 'Auto-upgrade plan' },
          { value: 'DegradeToSmallerModel', label: 'Degrade to smaller model' },
        ]} />
      <Input label="Allowed features CSV" value={value.allowedFeaturesCsv} onChange={(e) => onChange({ ...value, allowedFeaturesCsv: e.target.value })} />
      <Input label="Allowed models CSV" value={value.allowedModelsCsv} onChange={(e) => onChange({ ...value, allowedModelsCsv: e.target.value })} />
      <div className="col-span-2">
        <Switch
          checked={value.isActive}
          onCheckedChange={(next) => onChange({ ...value, isActive: next })}
          label="Plan active"
        />
      </div>
    </div>
  );
}

// ═════════════════════════════════════════════════════════════════════════
function ProvidersPanel({ onToast }: { onToast: (t: ToastState) => void }) {
  const [status, setStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<AiProviderRow[]>([]);
  const [editing, setEditing] = useState<(AiProviderRow & { apiKey?: string }) | null>(null);
  const [creating, setCreating] = useState(false);

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
      onToast({ variant: 'success', message: creating ? 'Provider registered.' : 'Provider updated.' });
      setEditing(null); setCreating(false); await load();
    } catch (e) { onToast({ variant: 'error', message: `Save failed: ${(e as Error).message}` }); }
  };

  const deactivate = async (id: string) => {
    try { await deactivateAiProvider(id); onToast({ variant: 'success', message: 'Provider deactivated.' }); await load(); }
    catch (e) { onToast({ variant: 'error', message: `Failed: ${(e as Error).message}` }); }
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

  return (
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
        }}>+ Register provider</Button>
      </div>
      <AdminRoutePanel eyebrow="Providers" title="AI providers" dense>
        <DataTable density="compact" data={rows} columns={columns} keyExtractor={(p) => p.id || p.code} />
      </AdminRoutePanel>
      {editing && (
        <Modal open={true} onClose={() => { setEditing(null); setCreating(false); }} title={creating ? 'Register provider' : `Edit ${editing.code}`}>
          <div className="grid grid-cols-2 gap-4">
            <Input label="Code" value={editing.code} onChange={(e) => setEditing({ ...editing, code: e.target.value })} />
            <Input label="Name" value={editing.name} onChange={(e) => setEditing({ ...editing, name: e.target.value })} />
            <Select label="Dialect" value={editing.dialect}
              onChange={(e) => setEditing({ ...editing, dialect: e.target.value as AiProviderRow['dialect'] })}
              options={[
                { value: 'OpenAiCompatible', label: 'OpenAI-compatible' },
                { value: 'Anthropic', label: 'Anthropic' },
                { value: 'Mock', label: 'Mock (dev only)' },
              ]} />
            <Input label="Base URL" value={editing.baseUrl} onChange={(e) => setEditing({ ...editing, baseUrl: e.target.value })} />
            <Input label={creating ? 'API key' : 'API key (leave blank to keep)'} type="password" value={editing.apiKey ?? ''} onChange={(e) => setEditing({ ...editing, apiKey: e.target.value })} />
            <Input label="Default model" value={editing.defaultModel} onChange={(e) => setEditing({ ...editing, defaultModel: e.target.value })} />
            <Input label="Price / 1k prompt tokens (USD)" type="number" step="0.0001" value={editing.pricePer1kPromptTokens} onChange={(e) => setEditing({ ...editing, pricePer1kPromptTokens: Number(e.target.value) })} />
            <Input label="Price / 1k completion tokens (USD)" type="number" step="0.0001" value={editing.pricePer1kCompletionTokens} onChange={(e) => setEditing({ ...editing, pricePer1kCompletionTokens: Number(e.target.value) })} />
            <Input label="Retry count" type="number" value={editing.retryCount} onChange={(e) => setEditing({ ...editing, retryCount: Number(e.target.value) })} />
            <Input label="Failover priority" type="number" value={editing.failoverPriority} onChange={(e) => setEditing({ ...editing, failoverPriority: Number(e.target.value) })} />
            <div className="col-span-2">
              <Switch
                checked={editing.isActive}
                onCheckedChange={(next) => setEditing({ ...editing, isActive: next })}
                label="Provider active"
              />
            </div>
          </div>
          <div className="flex gap-3 mt-4">
            <Button variant="primary" onClick={() => void save()}>Save</Button>
            <Button variant="ghost" onClick={() => { setEditing(null); setCreating(false); }}>Cancel</Button>
          </div>
        </Modal>
      )}
    </AsyncStateWrapper>
  );
}

// ═════════════════════════════════════════════════════════════════════════
function AnomaliesPanel({ onToast }: { onToast: (t: ToastState) => void }) {
  const [status, setStatus] = useState<PageStatus>('loading');
  const [data, setData] = useState<Awaited<ReturnType<typeof fetchAiAnomalies>> | null>(null);

  const load = useCallback(async () => {
    try { setData(await fetchAiAnomalies()); setStatus('success'); }
    catch (e) { setStatus('error'); onToast({ variant: 'error', message: `${(e as Error).message}` }); }
  }, [onToast]);
  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  return (
    <AsyncStateWrapper status={status}>
      {data && !data.enabled && (
        <EmptyState icon={<AlertCircle className="w-8 h-8" />} title="Anomaly detection is disabled" description="Enable it on the Budget tab." />
      )}
      {data && data.enabled && (
        <AdminRoutePanel
          eyebrow="Anomalies"
          title={`Flagged users (≥ ${data.multiplier}× trailing 7-day median)`}
          dense
        >
          {data.rows.length === 0
            ? <EmptyState icon={<ShieldCheck className="w-6 h-6" />} title="No anomalies detected" description="Nothing in the last 24h crossed the threshold." />
            : (
              <DataTable
                density="compact"
                data={data.rows}
                keyExtractor={(r) => r.userId}
                columns={[
                  { key: 'u', header: 'User', render: (r) => <span className="font-mono">{r.userId}</span> },
                  { key: 't', header: 'Tokens today', render: (r) => fmt(r.tokensToday) },
                  { key: 'm', header: '7d median', render: (r) => fmt(Math.round(r.median7d)) },
                ]}
              />
            )}
        </AdminRoutePanel>
      )}
    </AsyncStateWrapper>
  );
}
