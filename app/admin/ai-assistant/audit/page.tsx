'use client';

import { useCallback, useEffect, useState } from 'react';
import type { JSX } from 'react';
import { AdminRouteSectionHeader, AdminRouteWorkspace, AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { aiAssistantClient } from '@/lib/ai-assistant/client';

interface AuditRow {
  id: string;
  actorUserId: string | null;
  action: string;
  metadataJson: string | null;
  ipAddress: string | null;
  occurredAt: string;
}

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

const PAGE_SIZE = 100;

export default function AiAssistantAuditPage(): JSX.Element {
  const [status, setStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<AuditRow[]>([]);
  const [skip, setSkip] = useState(0);

  const load = useCallback(async (offset: number) => {
    setStatus('loading');
    try {
      const data = await aiAssistantClient.getAuditLog(PAGE_SIZE, offset);
      const list = data as AuditRow[];
      setRows(list);
      setStatus(list.length === 0 ? 'empty' : 'success');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => { queueMicrotask(() => { void load(skip); }); }, [load, skip]);

  const columns: Column<AuditRow>[] = [
    {
      key: 'occurredAt',
      header: 'When',
      render: (r) => <span className="text-xs tabular-nums">{new Date(r.occurredAt).toLocaleString()}</span>,
    },
    {
      key: 'action',
      header: 'Action',
      render: (r) => <Badge variant="info">{r.action}</Badge>,
    },
    { key: 'actorUserId', header: 'Actor', render: (r) => <span className="text-xs font-mono">{r.actorUserId ?? '—'}</span> },
    { key: 'ipAddress', header: 'IP', render: (r) => <span className="text-xs font-mono">{r.ipAddress ?? '—'}</span> },
    {
      key: 'metadataJson',
      header: 'Metadata',
      render: (r) => (
        <code className="block max-w-md truncate text-xs text-admin-text-muted">{r.metadataJson ?? '—'}</code>
      ),
    },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="AI Assistant — Audit"
        description="Immutable audit log of admin AI Assistant actions: kill-switch toggles, message sends, tool approvals."
        actions={
          <>
            <Button variant="ghost" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - PAGE_SIZE))}>Prev</Button>
            <Button variant="ghost" disabled={rows.length < PAGE_SIZE} onClick={() => setSkip(skip + PAGE_SIZE)}>Next</Button>
          </>
        }
      />
      <AsyncStateWrapper status={status} errorMessage="Could not load audit log." onRetry={() => void load(skip)}>
        <AdminRoutePanel>
          <DataTable<AuditRow> data={rows} columns={columns} keyExtractor={(r) => r.id} />
        </AdminRoutePanel>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
