'use client';

import { useCallback, useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { fetchFxRates, refreshFxRates, type ExchangeRateDto } from '@/lib/api';

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

  useEffect(() => { void load(); }, [load]);

  async function handleRefresh() {
    try {
      const msg = await refreshFxRates();
      setToast({ variant: 'success', message: msg });
      await load();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? err?.message ?? 'Failed.' });
    }
  }

  const columns: Column<ExchangeRateDto>[] = [
    { key: 'from', header: 'From', render: (r) => r.fromCurrency },
    { key: 'to', header: 'To', render: (r) => r.toCurrency },
    { key: 'rate', header: 'Rate', render: (r) => r.rate.toFixed(6) },
    { key: 'effective', header: 'Effective from', render: (r) => new Date(r.effectiveFrom).toLocaleString() },
    { key: 'source', header: 'Source', render: (r) => r.source },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader title="FX rates" description="Live foreign-exchange rates used by dynamic-pricing experiments and multi-currency invoicing." />

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRoutePanel>
        {error && <InlineAlert variant="error">{error}</InlineAlert>}
        <div className="flex items-end gap-3">
          <Input label="From currency" value={from} onChange={(e) => setFrom(e.target.value.toUpperCase())} maxLength={3} />
          <Input label="To currency" value={to} onChange={(e) => setTo(e.target.value.toUpperCase())} maxLength={3} />
          <Button variant="ghost" onClick={() => void load()}>
            <RefreshCw className="h-4 w-4" />
          </Button>
          <Button onClick={handleRefresh}>Refresh from provider</Button>
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel>
        {loading ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : (
          <DataTable data={rows} columns={columns} keyExtractor={(r) => r.id} emptyMessage="No FX rate rows. Click ‘Refresh from provider’." />
        )}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
