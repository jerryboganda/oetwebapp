'use client';

import { useCallback, useEffect, useState } from 'react';
import { AlertTriangle, RefreshCw } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Button } from '@/components/ui/button';
import { Select } from '@/components/ui/form-controls';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { listDunningCampaigns, type DunningCampaignDto } from '@/lib/api';

const STATUS_OPTIONS = ['', 'active', 'paused', 'recovered', 'cancelled'].map((s) => ({ value: s, label: s || 'all' }));

function statusVariant(status: string) {
  if (status === 'recovered') return 'success' as const;
  if (status === 'cancelled') return 'danger' as const;
  if (status === 'paused') return 'warning' as const;
  return 'info' as const;
}

export default function AdminDunningPage() {
  const [status, setStatus] = useState('active');
  const [rows, setRows] = useState<DunningCampaignDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await listDunningCampaigns(status || undefined));
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load.');
    } finally {
      setLoading(false);
    }
  }, [status]);

  useEffect(() => { void load(); }, [load]);

  const columns: Column<DunningCampaignDto>[] = [
    { key: 'started', header: 'Started', render: (c) => new Date(c.startedAt).toLocaleString() },
    { key: 'sub', header: 'Subscription', render: (c) => c.subscriptionId.slice(0, 12) + '…' },
    { key: 'user', header: 'User', render: (c) => c.userId },
    { key: 'attempts', header: 'Attempts', render: (c) => c.attemptCount.toString() },
    { key: 'next', header: 'Next attempt', render: (c) => new Date(c.nextAttemptAt).toLocaleString() },
    { key: 'failure', header: 'Last failure', render: (c) => c.lastFailureCode ?? c.lastFailureReason ?? '—' },
    { key: 'steps', header: 'Steps done', render: (c) => c.stepsCompletedCsv.split(',').filter(Boolean).length.toString() },
    { key: 'status', header: 'Status', render: (c) => <Badge variant={statusVariant(c.status)}>{c.status}</Badge> },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader title="Dunning campaigns" description="Failed renewal recovery — Day 0→21 retry schedule + access state." />

      <AdminRoutePanel>
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="mb-4 flex items-center gap-3">
          <Select label="Status" value={status} options={STATUS_OPTIONS} onChange={(e) => setStatus(e.target.value)} />
          <Button variant="ghost" onClick={() => void load()}>
            <RefreshCw className="h-4 w-4" />
          </Button>
        </div>
        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : rows.length === 0 ? (
          <div className="flex items-center gap-2 rounded-lg bg-emerald-50 p-4 text-sm text-emerald-900 dark:bg-emerald-950 dark:text-emerald-100">
            <AlertTriangle className="h-4 w-4" />
            No active dunning campaigns. 🎉
          </div>
        ) : (
          <DataTable data={rows} columns={columns} keyExtractor={(r) => r.id} emptyMessage="No campaigns." />
        )}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
