'use client';

import { useCallback, useEffect, useState } from 'react';
import type { JSX } from 'react';
import { AdminRouteSectionHeader, AdminRouteWorkspace, AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { aiAssistantClient } from '@/lib/ai-assistant/client';
import type { ChatThreadDto } from '@/lib/ai-assistant/types';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
const PAGE_SIZE = 100;

interface AdminThreadRow extends ChatThreadDto {
  ownerUserId?: string | null;
}

export default function AiAssistantThreadsPage(): JSX.Element {
  const [status, setStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<AdminThreadRow[]>([]);
  const [skip, setSkip] = useState(0);

  const load = useCallback(async (offset: number) => {
    setStatus('loading');
    try {
      const data = await aiAssistantClient.listAllThreads(PAGE_SIZE, offset);
      setRows(data as AdminThreadRow[]);
      setStatus(data.length === 0 ? 'empty' : 'success');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => { queueMicrotask(() => { void load(skip); }); }, [load, skip]);

  const columns: Column<AdminThreadRow>[] = [
    {
      key: 'updatedAt',
      header: 'Updated',
      render: (r) => <span className="text-xs tabular-nums">{new Date(r.updatedAt).toLocaleString()}</span>,
    },
    { key: 'title', header: 'Title', render: (r) => <span className="font-medium">{r.title || '(untitled)'}</span> },
    {
      key: 'ownerUserId',
      header: 'Owner',
      render: (r) => <span className="text-xs font-mono">{r.ownerUserId ?? '—'}</span>,
    },
    {
      key: 'messageCount',
      header: 'Messages',
      render: (r) => <span className="tabular-nums">{r.messageCount}</span>,
    },
    {
      key: 'isArchived',
      header: 'State',
      render: (r) => (r.isArchived ? <Badge variant="muted">Archived</Badge> : <Badge variant="success">Active</Badge>),
    },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="AI Assistant — All Threads"
        description="Cross-admin view of every chat thread. For your own threads only, open the assistant from the sidebar."
        actions={
          <>
            <Button variant="ghost" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - PAGE_SIZE))}>Prev</Button>
            <Button variant="ghost" disabled={rows.length < PAGE_SIZE} onClick={() => setSkip(skip + PAGE_SIZE)}>Next</Button>
          </>
        }
      />
      <AsyncStateWrapper status={status} errorMessage="Could not load threads." onRetry={() => void load(skip)}>
        <AdminRoutePanel>
          <DataTable<AdminThreadRow> data={rows} columns={columns} keyExtractor={(r) => r.id} />
        </AdminRoutePanel>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
