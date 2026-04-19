'use client';

import { use, useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, User } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteStatRow,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchUserCredits,
  grantUserCredits,
  type AiCreditBalance,
  type AiCreditLedgerRow,
} from '@/lib/ai-management-api';

type PageStatus = 'loading' | 'success' | 'error';

export default function AdminUserAiPage({ params }: { params: Promise<{ userId: string }> }) {
  const { userId } = use(params);
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [balance, setBalance] = useState<AiCreditBalance | null>(null);
  const [entries, setEntries] = useState<AiCreditLedgerRow[]>([]);
  const [error, setError] = useState<string | null>(null);

  // Grant form
  const [tokens, setTokens] = useState(10_000);
  const [source, setSource] = useState<'promo' | 'purchase' | 'admin'>('promo');
  const [description, setDescription] = useState('');
  const [expiresDays, setExpiresDays] = useState(30);
  const [granting, setGranting] = useState(false);

  const load = useCallback(async () => {
    setStatus('loading');
    setError(null);
    try {
      const data = await fetchUserCredits(userId);
      setBalance(data.balance);
      setEntries(data.entries);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setError(`Failed to load credits: ${(e as Error).message}`);
    }
  }, [userId]);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminRouteWorkspace>
        <InlineAlert variant="error">Admin access required.</InlineAlert>
      </AdminRouteWorkspace>
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

  const columns: Column<AiCreditLedgerRow>[] = [
    { key: 't', header: 'When', render: (r) => new Date(r.createdAt).toLocaleString() },
    { key: 's', header: 'Source', render: (r) => <Badge variant="info">{r.source}</Badge> },
    {
      key: 'd', header: 'Tokens', render: (r) => (
        <span className={r.tokensDelta > 0 ? 'text-success' : 'text-danger'}>
          {r.tokensDelta > 0 ? '+' : ''}{r.tokensDelta.toLocaleString()}
        </span>
      ),
    },
    { key: 'ex', header: 'Expires', render: (r) => r.expiresAt ? new Date(r.expiresAt).toLocaleDateString() : '—' },
    { key: 'x', header: 'State', render: (r) => r.expiredByEntryId ? <Badge variant="muted">Expired</Badge> : <Badge variant="success">Active</Badge> },
    { key: 'desc', header: 'Description', render: (r) => <span className="text-xs text-muted">{r.description ?? ''}</span> },
  ];

  return (
    <AdminRouteWorkspace>
      <Link href="/admin/ai-usage" className="inline-flex items-center gap-2 text-sm text-muted hover:text-navy">
        <ArrowLeft className="w-4 h-4" /> Back to AI Usage
      </Link>
      <AdminRouteSectionHeader
        icon={<User className="w-6 h-6" />}
        title={`User credits: ${userId}`}
        description="Grant promo / refund credits, inspect ledger history, and correct mis-postings."
      />

      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      <AsyncStateWrapper status={status}>
        {balance && (
          <AdminRoutePanel eyebrow="Credit balance" title={`Standing for ${userId}`}>
            <AdminRouteStatRow
              items={[
                {
                  label: 'Available tokens',
                  value: balance.tokensAvailable.toLocaleString(),
                  hint: balance.tokensAvailable > 0 ? 'Spendable' : 'None available',
                  tone: balance.tokensAvailable === 0 ? 'warning' : 'success',
                },
                {
                  label: 'Granted lifetime',
                  value: balance.tokensGrantedLifetime.toLocaleString(),
                  hint: 'Cumulative grants',
                },
                {
                  label: 'Consumed lifetime',
                  value: balance.tokensConsumedLifetime.toLocaleString(),
                  hint: 'Debits to date',
                },
              ]}
            />
          </AdminRoutePanel>
        )}

        <AdminRoutePanel
          eyebrow="Top up"
          title="Grant credits"
          description="Add tokens to this user's balance. Promo grants respect the expiry window; Admin adjustments are treated as corrections in the ledger."
        >
          <div className="grid grid-cols-1 md:grid-cols-4 gap-4 items-end">
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
            <Button variant="primary" onClick={() => void grant()} loading={granting} disabled={tokens <= 0}>
              Grant
            </Button>
          </div>
        </AdminRoutePanel>

        <AdminRoutePanel eyebrow="Audit trail" title="Ledger history" dense>
          <DataTable data={entries} columns={columns} keyExtractor={(r) => r.id} />
        </AdminRoutePanel>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
