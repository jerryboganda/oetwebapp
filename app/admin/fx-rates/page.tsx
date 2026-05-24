'use client';

import { useCallback, useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';

import { fetchFxRates, refreshFxRates, type ExchangeRateDto } from '@/lib/api';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { DataTable } from '@/components/admin/ui/data-table';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/admin/ui/input';
import { Card, CardContent } from '@/components/admin/ui/card';

export default function AdminFxRatesPage() {
  const [rows, setRows] = useState<ExchangeRateDto[]>([]);
  const [from, setFrom] = useState('USD');
  const [to, setTo] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      setRows(await fetchFxRates(from || undefined, to || undefined));
    } catch (err: any) {
      setError(err?.userMessage ?? err?.message ?? 'Failed to load.');
    } finally {
      setLoading(false);
    }
  }, [from, to]);

  useEffect(() => {
    void load();
  }, [load]);

  async function handleRefresh() {
    try {
      const msg = await refreshFxRates();
      setToast({ variant: 'success', message: msg });
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? err?.message ?? 'Failed.' });
    }
  }

  const columns: ColumnDef<ExchangeRateDto>[] = [
    {
      accessorKey: 'fromCurrency',
      header: 'From',
      cell: ({ row }) => (
        <span className="font-medium text-admin-fg-strong">{row.original.fromCurrency}</span>
      ),
    },
    {
      accessorKey: 'toCurrency',
      header: 'To',
      cell: ({ row }) => (
        <span className="font-medium text-admin-fg-strong">{row.original.toCurrency}</span>
      ),
    },
    {
      accessorKey: 'rate',
      header: 'Rate',
      cell: ({ row }) => (
        <span className="font-mono tabular-nums text-admin-fg-default">
          {row.original.rate.toFixed(6)}
        </span>
      ),
    },
    {
      accessorKey: 'effectiveFrom',
      header: 'Effective from',
      cell: ({ row }) => (
        <span className="text-sm text-admin-fg-muted">
          {new Date(row.original.effectiveFrom).toLocaleString()}
        </span>
      ),
    },
    {
      accessorKey: 'source',
      header: 'Source',
      cell: ({ row }) => (
        <span className="text-sm text-admin-fg-muted">{row.original.source}</span>
      ),
    },
  ];

  const banner = (
    <Card>
      <CardContent>
        {error && (
          <div className="mb-3 rounded-admin border border-[var(--admin-danger-tint-strong)] bg-[var(--admin-danger-tint)] px-3 py-2 text-sm text-[var(--admin-danger)]">
            {error}
          </div>
        )}
        {toast && (
          <div
            className={`mb-3 rounded-admin border px-3 py-2 text-sm ${
              toast.variant === 'success'
                ? 'border-[var(--admin-success-tint-strong)] bg-[var(--admin-success-tint)] text-[var(--admin-success)]'
                : 'border-[var(--admin-danger-tint-strong)] bg-[var(--admin-danger-tint)] text-[var(--admin-danger)]'
            }`}
          >
            {toast.message}
          </div>
        )}
        <div className="flex flex-wrap items-end gap-3">
          <div className="w-40">
            <Input
              label="From currency"
              value={from}
              onChange={(e) => setFrom(e.target.value.toUpperCase())}
              maxLength={3}
            />
          </div>
          <div className="w-40">
            <Input
              label="To currency"
              value={to}
              onChange={(e) => setTo(e.target.value.toUpperCase())}
              maxLength={3}
            />
          </div>
          <Button
            variant="outline"
            size="md"
            onClick={() => void load()}
            startIcon={<RefreshCw className="h-4 w-4" />}
          >
            Reload
          </Button>
          <Button onClick={handleRefresh}>Refresh from provider</Button>
        </div>
      </CardContent>
    </Card>
  );

  return (
    <AdminTableLayout
      title="FX rates"
      description="Live foreign-exchange rates used by dynamic-pricing experiments and multi-currency invoicing."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'FX rates' },
      ]}
      banner={banner}
    >
      <DataTable
        columns={columns as ColumnDef<ExchangeRateDto, unknown>[]}
        data={rows}
        loading={loading}
        emptyMessage="No FX rate rows. Click ‘Refresh from provider’."
        searchPlaceholder="Search currencies…"
      />
    </AdminTableLayout>
  );
}
