'use client';

import { useCallback, useEffect, useState } from 'react';
import type { JSX } from 'react';
import { AdminRouteSectionHeader, AdminRouteWorkspace, AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Badge } from '@/components/ui/badge';
import { Toast } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { aiAssistantClient } from '@/lib/ai-assistant/client';
import type { ProviderConfigDto } from '@/lib/ai-assistant/types';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function AiAssistantProvidersPage(): JSX.Element {
  const [status, setStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<ProviderConfigDto[]>([]);
  const [toast, setToast] = useState<ToastState>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const data = await aiAssistantClient.listProviders();
      setRows(data);
      setStatus(data.length === 0 ? 'empty' : 'success');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const notImplemented = () =>
    setToast({ variant: 'error', message: 'Use the main AI provider admin surface to edit gateway provider rows.' });

  const columns: Column<ProviderConfigDto>[] = [
    { key: 'displayName', header: 'Provider', render: (r) => <span className="font-medium">{r.displayName}</span> },
    { key: 'kind', header: 'Kind', render: (r) => <Badge variant="info">{r.kind}</Badge> },
    { key: 'defaultModel', header: 'Model', render: (r) => <span className="text-xs text-admin-text-muted">{r.defaultModel || 'Provider default'}</span> },
    {
      key: 'isDefault',
      header: 'Default',
      render: (r) => (r.isDefault ? <Badge variant="success">Default</Badge> : <span className="text-xs text-admin-text-muted">—</span>),
    },
    {
      key: 'isEnabled',
      header: 'Enabled',
      render: (r) => (r.isEnabled ? <Badge variant="success">On</Badge> : <Badge variant="muted">Off</Badge>),
    },
    {
      key: 'hasSecret',
      header: 'Secret',
      render: (r) => (r.hasSecret ? <Badge variant="success">Configured</Badge> : <Badge variant="warning">Missing</Badge>),
    },
    {
      key: 'id',
      header: 'Actions',
      render: () => (
        <Button variant="ghost" size="sm" onClick={notImplemented}>Edit</Button>
      ),
    },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="AI Assistant — Providers"
        description="Canonical gateway provider registry for AI Assistant routing. This view is read-only."
        actions={<Button variant="primary" onClick={notImplemented}>Add provider</Button>}
      />

      {toast && <Toast variant={toast.variant === 'success' ? 'success' : 'error'} message={toast.message} onClose={() => setToast(null)} />}

      <AsyncStateWrapper status={status} errorMessage="Could not load providers." onRetry={() => void load()}>
        <AdminRoutePanel>
          <DataTable<ProviderConfigDto> data={rows} columns={columns} keyExtractor={(r) => r.id} />
        </AdminRoutePanel>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
