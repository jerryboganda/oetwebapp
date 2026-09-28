'use client';

import { useCallback, useEffect, useState } from 'react';
import { AlertTriangle, Cpu, Gauge, RefreshCw, ShieldCheck, Zap } from 'lucide-react';
import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchWritingAiProvider,
  updateWritingAiProvider,
  type WritingAiProviderMode,
  type WritingAiProviderStatus,
} from '@/lib/ai-management-api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

function fmt(n: number): string {
  return n.toLocaleString();
}
function fmtUsd(n: number): string {
  return `$${n.toFixed(4)}`;
}
function fmtPct(n: number | null): string {
  return n == null ? '—' : `${n.toFixed(1)}%`;
}
function fmtWhen(iso: string | null): string {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '—' : d.toUTCString();
}

const MODE_OPTIONS = [
  { value: 'auto', label: 'Automatic — Claude 5x primary, Codex fallback' },
  { value: 'claude', label: 'Claude Opus 5.5 High (force primary)' },
  { value: 'codex', label: 'OpenAI Sol (Codex) — force fallback' },
];

export default function WritingAiProviderPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [toast, setToast] = useState<ToastState>(null);
  const [status, setStatus] = useState<WritingAiProviderStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [mode, setMode] = useState<WritingAiProviderMode>('auto');
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const s = await fetchWritingAiProvider();
      setStatus(s);
      setMode(s.mode);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <div className="mx-auto max-w-3xl py-12">
        <EmptyState illustration={<ShieldCheck className="w-8 h-8" />} title="Admin access required" description="Sign in with an admin account to view this page." />
      </div>
    );
  }

  async function saveMode(next: WritingAiProviderMode) {
    setSaving(true);
    try {
      await updateWritingAiProvider({ mode: next });
      setMode(next);
      setToast({ variant: 'success', message: `Provider mode set to "${next}".` });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Save failed' });
    } finally {
      setSaving(false);
    }
  }

  const quota = status?.quota;
  const util = quota?.utilizationPct ?? null;
  const warnTone = status?.failoverActive
    ? 'danger'
    : util != null && status != null && util >= status.warnPct
      ? 'warning'
      : 'success';

  return (
    <>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
      <AdminOperationsLayout
        title="Writing AI Provider"
        description="Dedicated Claude Max 5x subscription (primary) with automatic failover to the Codex subscription. See docs/ops/WRITING-AI-PROVIDERS.md."
        breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Writing AI Provider' }]}
        primaryGrid={
          loading && !status ? (
            <EmptyState illustration={<Cpu className="w-8 h-8" />} title="Loading…" description="Reading provider state." />
          ) : error ? (
            <EmptyState illustration={<AlertTriangle className="w-8 h-8" />} title="Could not load provider status" description={error} />
          ) : status ? (
            <>
              {/* Banners */}
              {status.failoverActive && (
                <Card className="border-[var(--admin-danger)] bg-[var(--admin-danger-tint)]">
                  <CardContent className="flex items-center gap-3 py-3 text-sm">
                    <AlertTriangle className="w-5 h-5 text-[var(--admin-danger)]" />
                    <div>
                      <strong>Failover active.</strong> New Writing grading requests are being served by the Codex
                      fallback until the Claude weekly allowance resets
                      {status.quotaExceededUntil ? ` (${fmtWhen(status.quotaExceededUntil)})` : ''}.
                    </div>
                  </CardContent>
                </Card>
              )}
              {!status.failoverActive && util != null && util >= status.warnPct && (
                <Card className="border-[var(--admin-warning)] bg-[var(--admin-warning-tint)]">
                  <CardContent className="flex items-center gap-3 py-3 text-sm">
                    <AlertTriangle className="w-5 h-5 text-[var(--admin-warning)]" />
                    <div>
                      <strong>Approaching weekly limit.</strong> Claude 5x utilisation is {fmtPct(util)} (warns at{' '}
                      {status.warnPct}%, fails over at {status.failoverPct}%).
                    </div>
                  </CardContent>
                </Card>
              )}

              {/* KPI strip */}
              <KpiStrip>
                <KpiTile label="Graded today" value={fmt(status.gradedToday)} icon={<Gauge className="w-4 h-4" />} />
                <KpiTile label="Graded this week" value={fmt(status.gradedWeek)} icon={<Gauge className="w-4 h-4" />} />
                <KpiTile
                  label="Claude weekly utilisation"
                  value={fmtPct(util)}
                  tone={warnTone}
                  icon={<Zap className="w-4 h-4" />}
                  hint={quota?.source === 'reported' ? 'reported by sidecar' : 'estimated'}
                />
                <KpiTile
                  label="Allowance remaining"
                  value={util == null ? '—' : fmtPct(Math.max(0, 100 - util))}
                  tone={warnTone}
                  icon={<ShieldCheck className="w-4 h-4" />}
                  hint={quota?.resetsAt ? `resets ${fmtWhen(quota.resetsAt)}` : undefined}
                />
                <KpiTile
                  label="Current primary"
                  value={status.currentPrimary.model}
                  icon={<Cpu className="w-4 h-4" />}
                  hint={status.currentPrimary.provider}
                />
                <KpiTile label="Fallback calls (7d)" value={fmt(status.fallbackCountWeek)} icon={<RefreshCw className="w-4 h-4" />} />
                <KpiTile
                  label="Claude API cost (7d)"
                  value={fmtUsd(status.claudeApi?.costWeekUsd ?? 0)}
                  hint="level 2 — pay-as-you-go"
                  tone={(status.claudeApi?.costWeekUsd ?? 0) > 0 ? 'warning' : 'default'}
                />
                <KpiTile
                  label="Codex cost (7d)"
                  value={fmtUsd(status.codex.recordedCostWeekUsd)}
                  hint="level 3 — subscription, $0 marginal"
                />
              </KpiStrip>

              {/* Provider control */}
              <Card className="mt-4">
                <CardHeader>
                  <CardTitle>Provider control</CardTitle>
                  <p className="text-sm text-admin-fg-muted">
                    Automatic runs the 3-level chain: Claude Opus 5.5 (5x subscription) — retried once — then Claude
                    Opus 5.5 via the Anthropic API, then the Codex subscription. The weekly-cap failover engages at{' '}
                    {status.failoverPct}% utilisation or on a quota/rate-limit signal.
                  </p>
                </CardHeader>
                <CardContent className="flex flex-col gap-4">
                  <div className="flex flex-wrap items-end gap-3">
                    <Select
                      label="Provider mode"
                      options={MODE_OPTIONS}
                      value={mode}
                      onChange={(e) => setMode(e.target.value as WritingAiProviderMode)}
                      className="min-w-72"
                    />
                    <Button onClick={() => void saveMode(mode)} disabled={saving || mode === status.mode}>
                      {saving ? 'Saving…' : 'Apply'}
                    </Button>
                    <Button variant="secondary" onClick={() => void load()} disabled={loading}>
                      Refresh
                    </Button>
                  </div>
                  <div className="text-sm text-admin-fg-muted flex flex-wrap gap-x-6 gap-y-1">
                    <span>
                      Mode: <Badge>{status.mode}</Badge>
                    </span>
                    <span>
                      Warn at <strong>{status.warnPct}%</strong> · Failover at <strong>{status.failoverPct}%</strong>
                    </span>
                    <span>
                      Claude (7d): {fmt(status.claude.callsWeek)} calls · {fmt(status.claude.tokensWeek)} tokens
                    </span>
                    <span>
                      Codex (7d): {fmt(status.codex.callsWeek)} calls · {fmt(status.codex.tokensWeek)} tokens
                    </span>
                  </div>
                </CardContent>
              </Card>
            </>
          ) : null
        }
      />
    </>
  );
}
