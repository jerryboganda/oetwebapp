'use client';

import { useCallback, useEffect, useState } from 'react';
import { AlertTriangle, Cpu, Gauge, RefreshCw, ShieldCheck, Zap } from 'lucide-react';
import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Button } from '@/components/ui/button';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { fetchWritingAiProvider, type WritingAiProviderStatus } from '@/lib/ai-management-api';

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

// Owner hard rule MAX-ALWAYS-ON (2 Oct 2026): there is no provider mode,
// threshold or marker control — the Claude Max route can never be switched off.
export default function WritingAiProviderPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<WritingAiProviderStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setStatus(await fetchWritingAiProvider());
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

  const quota = status?.quota;
  const util = quota?.utilizationPct ?? null;
  const warnTone = util != null && status != null && util >= status.warnPct ? 'warning' : 'success';

  return (
    <AdminOperationsLayout
      title="Writing AI Provider"
      description="Claude Max 5x subscription first on every grade; the Anthropic API and the Codex subscription only as in-grade fallbacks. See docs/ops/WRITING-AI-PROVIDERS.md."
      breadcrumbs={[{ label: 'Admin', href: '/admin' }, { label: 'Writing AI Provider' }]}
      primaryGrid={
        loading && !status ? (
          <EmptyState illustration={<Cpu className="w-8 h-8" />} title="Loading…" description="Reading provider state." />
        ) : error ? (
          <EmptyState illustration={<AlertTriangle className="w-8 h-8" />} title="Could not load provider status" description={error} />
        ) : status ? (
          <>
            {util != null && util >= status.warnPct && (
              <Card className="border-[var(--admin-warning)] bg-[var(--admin-warning-tint)]">
                <CardContent className="flex items-center gap-3 py-3 text-sm">
                  <AlertTriangle className="w-5 h-5 text-[var(--admin-warning)]" />
                  <div>
                    <strong>High weekly usage estimate.</strong> Claude Max usage is estimated at {fmtPct(util)} of
                    the weekly allowance. Information only: grading stays on Claude Max and moves down the chain
                    only when a call actually fails.
                  </div>
                </CardContent>
              </Card>
            )}

            {/* KPI strip — information only */}
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

            <Card className="mt-4">
              <CardHeader>
                <CardTitle>Provider chain</CardTitle>
                <p className="text-sm text-admin-fg-muted">
                  Claude Max subscription — always tried first (hard rule). Codex and the Anthropic API are only used
                  inside a single grade after Max actually errors.
                </p>
              </CardHeader>
              <CardContent className="flex flex-col gap-4">
                <div>
                  <Button variant="secondary" onClick={() => void load()} disabled={loading}>
                    Refresh
                  </Button>
                </div>
                <div className="text-sm text-admin-fg-muted flex flex-wrap gap-x-6 gap-y-1">
                  <span>
                    Usage warning at <strong>{status.warnPct}%</strong> (information only)
                  </span>
                  {status.quotaExceededUntil && (
                    <span>Last Claude quota refusal recorded until {fmtWhen(status.quotaExceededUntil)} (information only)</span>
                  )}
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
  );
}
