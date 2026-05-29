'use client';

import { useCallback, useEffect, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { CheckCircle2, RefreshCw } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { DataTable } from '@/components/admin/ui/data-table';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Label } from '@/components/admin/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { InlineAlert } from '@/components/ui/alert';
import { listDunningCampaigns, type DunningCampaignDto } from '@/lib/api';

const STATUS_OPTIONS = [
  { value: '__all', label: 'All' },
  { value: 'active', label: 'Active' },
  { value: 'paused', label: 'Paused' },
  { value: 'recovered', label: 'Recovered' },
  { value: 'cancelled', label: 'Cancelled' },
];

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
      const filter = status === '__all' ? undefined : status;
      setRows(await listDunningCampaigns(filter));
      setError(null);
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load.');
    } finally {
      setLoading(false);
    }
  }, [status]);

  useEffect(() => { void load(); }, [load]);

  const columns: ColumnDef<DunningCampaignDto>[] = [
    {
      id: 'started',
      header: 'Started',
      cell: ({ row }) => new Date(row.original.startedAt).toLocaleString(),
    },
    {
      id: 'sub',
      header: 'Subscription',
      cell: ({ row }) => row.original.subscriptionId.slice(0, 12) + '…',
    },
    { id: 'user', accessorKey: 'userId', header: 'User' },
    {
      id: 'attempts',
      header: 'Attempts',
      cell: ({ row }) => row.original.attemptCount.toString(),
    },
    {
      id: 'next',
      header: 'Next attempt',
      cell: ({ row }) => new Date(row.original.nextAttemptAt).toLocaleString(),
    },
    {
      id: 'failure',
      header: 'Last failure',
      cell: ({ row }) => row.original.lastFailureCode ?? row.original.lastFailureReason ?? '-',
    },
    {
      id: 'steps',
      header: 'Steps done',
      cell: ({ row }) =>
        row.original.stepsCompletedCsv.split(',').filter(Boolean).length.toString(),
    },
    {
      id: 'status',
      header: 'Status',
      cell: ({ row }) => (
        <Badge variant={statusVariant(row.original.status)}>{row.original.status}</Badge>
      ),
    },
  ];

  return (
    <AdminTableLayout
      title="Dunning campaigns"
      description="Failed renewal recovery. Day 0 to 21 retry schedule plus access state."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Billing', href: '/admin/billing' },
        { label: 'Dunning' },
      ]}
      actions={
        <Button variant="ghost" onClick={() => void load()} startIcon={<RefreshCw className="h-4 w-4" />}>
          Refresh
        </Button>
      }
      banner={
        <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:gap-4">
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          <div className="flex items-end gap-2">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="dunning-status" className="text-xs text-admin-fg-muted">
                Status
              </Label>
              <Select value={status} onValueChange={setStatus}>
                <SelectTrigger id="dunning-status" className="w-[180px]">
                  <SelectValue placeholder="Status" />
                </SelectTrigger>
                <SelectContent>
                  {STATUS_OPTIONS.map((opt) => (
                    <SelectItem key={opt.value} value={opt.value}>
                      {opt.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
        </div>
      }
    >
      {!loading && rows.length === 0 ? (
        <div className="px-6 py-12">
          <EmptyState
            illustration={<CheckCircle2 />}
            title="No active dunning campaigns"
            description="Nothing to recover right now. All renewals are healthy."
          />
        </div>
      ) : (
        <DataTable
          columns={columns}
          data={rows}
          loading={loading}
          emptyMessage="No campaigns match this filter."
          searchPlaceholder="Search campaigns…"
        />
      )}
    </AdminTableLayout>
  );
}
