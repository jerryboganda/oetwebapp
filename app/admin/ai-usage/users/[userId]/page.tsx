'use client';

import { use, useCallback, useEffect, useState } from 'react';
import { CreditCard, Shield, SlidersHorizontal, Trash2 } from 'lucide-react';
import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchUserCredits,
  grantUserCredits,
  fetchAiUserOverride,
  upsertAiUserOverride,
  removeAiUserOverride,
  fetchAiPlans,
  type AiCreditBalance,
  type AiCreditLedgerRow,
  type AiQuotaPlan,
  type AiUserQuotaOverride,
} from '@/lib/ai-management-api';

type PageStatus = 'loading' | 'success' | 'error';

export default function AdminUserAiPage({ params }: { params: Promise<{ userId: string }> }) {
  const { userId } = use(params);
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [balance, setBalance] = useState<AiCreditBalance | null>(null);
  const [entries, setEntries] = useState<AiCreditLedgerRow[]>([]);
  const [error, setError] = useState<string | null>(null);

  const [tokens, setTokens] = useState(10_000);
  const [source, setSource] = useState<'promo' | 'purchase' | 'admin'>('promo');
  const [description, setDescription] = useState('');
  const [expiresDays, setExpiresDays] = useState(30);
  const [granting, setGranting] = useState(false);

  const [override, setOverride] = useState<AiUserQuotaOverride | null>(null);
  const [plans, setPlans] = useState<AiQuotaPlan[]>([]);
  const [monthlyCap, setMonthlyCap] = useState<string>('');
  const [dailyCap, setDailyCap] = useState<string>('');
  const [forcePlan, setForcePlan] = useState<string>('');
  const [aiDisabled, setAiDisabled] = useState<boolean>(false);
  const [overrideReason, setOverrideReason] = useState<string>('');
  const [overrideExpiresDays, setOverrideExpiresDays] = useState<number>(0);
  const [savingOverride, setSavingOverride] = useState<boolean>(false);

  const load = useCallback(async () => {
    setStatus('loading');
    setError(null);
    try {
      const [credits, ov, pl] = await Promise.all([
        fetchUserCredits(userId),
        fetchAiUserOverride(userId).catch(() => null),
        fetchAiPlans().catch(() => []),
      ]);
      setBalance(credits.balance);
      setEntries(credits.entries);
      setOverride(ov ?? null);
      setPlans(pl ?? []);
      if (ov) {
        setMonthlyCap(ov.monthlyTokenCapOverride != null ? String(ov.monthlyTokenCapOverride) : '');
        setDailyCap(ov.dailyTokenCapOverride != null ? String(ov.dailyTokenCapOverride) : '');
        setForcePlan(ov.forcePlanCode ?? '');
        setAiDisabled(Boolean(ov.aiDisabled));
        setOverrideReason(ov.reason ?? '');
      }
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setError(`Failed to load: ${(e as Error).message}`);
    }
  }, [userId]);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'AI Usage', href: '/admin/ai-usage' },
    { label: userId },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminOperationsLayout title="User AI" eyebrow="AI Usage" breadcrumbs={breadcrumbs}
        primaryGrid={<InlineAlert variant="error">Admin access required.</InlineAlert>}
      />
    );
  }

  const grant = async () => {
    setGranting(true);
    try {
      const expiresAt = expiresDays > 0
        ? new Date(Date.now() + expiresDays * 86400_000).toISOString()
        : undefined;
      await grantUserCredits(userId, {
        tokens, costUsd: 0, source, description: description || undefined, expiresAt,
      });
      setDescription('');
      await load();
    } catch (e) {
      setError(`Grant failed: ${(e as Error).message}`);
    } finally { setGranting(false); }
  };

  const saveOverride = async () => {
    setSavingOverride(true);
    try {
      const expiresAt = overrideExpiresDays > 0
        ? new Date(Date.now() + overrideExpiresDays * 86400_000).toISOString()
        : null;
      await upsertAiUserOverride(userId, {
        monthlyTokenCapOverride: monthlyCap.trim() === '' ? null : Number(monthlyCap),
        dailyTokenCapOverride: dailyCap.trim() === '' ? null : Number(dailyCap),
        forcePlanCode: forcePlan.trim() === '' ? null : forcePlan.trim(),
        aiDisabled,
        reason: overrideReason.trim() === '' ? null : overrideReason.trim(),
        expiresAt,
      });
      await load();
    } catch (e) {
      setError(`Override save failed: ${(e as Error).message}`);
    } finally { setSavingOverride(false); }
  };

  const clearOverride = async () => {
    if (!override) return;
    if (!confirm('Remove quota override for this user? They will revert to their plan defaults.')) return;
    setSavingOverride(true);
    try {
      await removeAiUserOverride(userId);
      setOverride(null);
      setMonthlyCap(''); setDailyCap(''); setForcePlan(''); setAiDisabled(false);
      setOverrideReason(''); setOverrideExpiresDays(0);
      await load();
    } catch (e) {
      setError(`Override remove failed: ${(e as Error).message}`);
    } finally { setSavingOverride(false); }
  };

  const columns: Column<AiCreditLedgerRow>[] = [
    { key: 't', header: 'When', render: (r) => new Date(r.createdAt).toLocaleString() },
    { key: 's', header: 'Source', render: (r) => <Badge variant="primary" intensity="tinted" size="sm">{r.source}</Badge> },
    {
      key: 'd', header: 'Tokens', render: (r) => (
        <span className={r.tokensDelta > 0 ? 'text-[var(--admin-success)]' : 'text-[var(--admin-danger)]'}>
          {r.tokensDelta > 0 ? '+' : ''}{r.tokensDelta.toLocaleString()}
        </span>
      ),
    },
    { key: 'ex', header: 'Expires', render: (r) => r.expiresAt ? new Date(r.expiresAt).toLocaleDateString() : '—' },
    { key: 'x', header: 'State', render: (r) => r.expiredByEntryId ? <Badge variant="default" intensity="tinted" size="sm">Expired</Badge> : <Badge variant="success" intensity="tinted" size="sm">Active</Badge> },
    { key: 'desc', header: 'Description', render: (r) => <span className="text-xs text-admin-fg-muted">{r.description ?? ''}</span> },
  ];

  return (
    <AdminOperationsLayout
      title={`User AI: ${userId}`}
      description="Credits ledger, quota overrides, and forced-plan assignment for this learner."
      eyebrow="AI Usage"
      breadcrumbs={breadcrumbs}
      kpis={
        balance ? (
          <KpiStrip>
            <KpiTile label="Available tokens" value={balance.tokensAvailable.toLocaleString()} icon={<CreditCard className="h-4 w-4" />} tone="primary" />
            <KpiTile label="Granted lifetime" value={balance.tokensGrantedLifetime.toLocaleString()} icon={<Shield className="h-4 w-4" />} tone="success" />
            <KpiTile label="Consumed lifetime" value={balance.tokensConsumedLifetime.toLocaleString()} icon={<Shield className="h-4 w-4" />} tone="info" />
          </KpiStrip>
        ) : null
      }
      primaryGrid={
        <div className="space-y-6">
          {error && <InlineAlert variant="error">{error}</InlineAlert>}

          <AsyncStateWrapper status={status}>
            <Card>
              <CardHeader><CardTitle>Grant credits</CardTitle></CardHeader>
              <CardContent>
                <div className="grid grid-cols-1 items-end gap-4 md:grid-cols-4">
                  <Input type="number" label="Tokens" value={tokens} onChange={(e) => setTokens(Number(e.target.value))} />
                  <Select
                    label="Source"
                    value={source}
                    onChange={(e) => setSource(e.target.value as typeof source)}
                    options={[
                      { value: 'promo', label: 'Promotional' },
                      { value: 'purchase', label: 'Manual purchase' },
                      { value: 'admin', label: 'Admin adjustment' },
                    ]}
                  />
                  <Input type="number" label="Expires in (days, 0 = never)" value={expiresDays} onChange={(e) => setExpiresDays(Number(e.target.value))} />
                  <Input label="Description" value={description} onChange={(e) => setDescription(e.target.value)} placeholder="e.g. Q2 promo" />
                  <Button variant="primary" onClick={() => void grant()} disabled={granting || tokens <= 0}>
                    {granting ? 'Granting…' : 'Grant'}
                  </Button>
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <CardTitle>Quota / policy override</CardTitle>
                <CardDescription className="flex items-center gap-2">
                  <SlidersHorizontal className="h-4 w-4" />
                  {override
                    ? <>Override active since {new Date(override.createdAt).toLocaleDateString()}{override.expiresAt ? `, expires ${new Date(override.expiresAt).toLocaleDateString()}` : ''}.</>
                    : <>No override. User is on their plan defaults.</>}
                </CardDescription>
              </CardHeader>
              <CardContent>
                <div className="grid grid-cols-1 items-end gap-4 md:grid-cols-3">
                  <Input label="Monthly token cap override" type="number" placeholder="blank = plan default" value={monthlyCap} onChange={(e) => setMonthlyCap(e.target.value)} />
                  <Input label="Daily token cap override" type="number" placeholder="blank = plan default" value={dailyCap} onChange={(e) => setDailyCap(e.target.value)} />
                  <Select
                    label="Force plan"
                    value={forcePlan}
                    onChange={(e) => setForcePlan(e.target.value)}
                    options={[
                      { value: '', label: 'None' },
                      ...plans.map((p) => ({ value: p.code, label: `${p.code} (${p.name})` })),
                    ]}
                  />
                  <label className="flex items-center gap-2 text-sm text-admin-fg-default">
                    <input type="checkbox" checked={aiDisabled} onChange={(e) => setAiDisabled(e.target.checked)} />
                    Disable AI entirely for this user
                  </label>
                  <Input label="Expires in (days, 0 = never)" type="number" value={overrideExpiresDays} onChange={(e) => setOverrideExpiresDays(Number(e.target.value))} />
                  <Input label="Reason" value={overrideReason} onChange={(e) => setOverrideReason(e.target.value)} placeholder="e.g. support escalation" />
                </div>
                <div className="mt-4 flex items-center gap-2">
                  <Button variant="primary" onClick={() => void saveOverride()} disabled={savingOverride}>
                    {savingOverride ? 'Saving…' : 'Save override'}
                  </Button>
                  {override && (
                    <Button variant="secondary" onClick={() => void clearOverride()} disabled={savingOverride}>
                      <Trash2 className="h-4 w-4" /> Remove override
                    </Button>
                  )}
                </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle>Ledger history</CardTitle></CardHeader>
              <CardContent className="p-0">
                <DataTable data={entries} columns={columns} keyExtractor={(r) => r.id} />
              </CardContent>
            </Card>
          </AsyncStateWrapper>
        </div>
      }
    />
  );
}
