'use client';

import { useCallback, useEffect, useState } from 'react';
import type { JSX } from 'react';
import { Database } from 'lucide-react';
import { AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace, AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Toast } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { aiAssistantClient } from '@/lib/ai-assistant/client';

interface IndexingStatus {
  state: string;
  indexedChunkCount: number;
}

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function AiAssistantIndexingPage(): JSX.Element {
  const [status, setStatus] = useState<PageStatus>('loading');
  const [info, setInfo] = useState<IndexingStatus | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      setInfo(await aiAssistantClient.getIndexingStatus());
      setStatus('success');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const onReindex = useCallback(async () => {
    try {
      await aiAssistantClient.triggerReindex(true);
      setToast({ variant: 'success', message: 'Reindex triggered.' });
    } catch (err) {
      setToast({
        variant: 'error',
        message: err instanceof Error ? err.message : 'Reindex unavailable in this build.',
      });
    }
  }, []);

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="AI Assistant — Codebase Indexing"
        description="Status of the pgvector-backed codebase index used for retrieval. Phase 2 — deferred until DBA approves CREATE EXTENSION vector on production Postgres."
        actions={<Button variant="primary" onClick={() => void onReindex()}>Reindex now</Button>}
      />

      {toast && <Toast variant={toast.variant === 'success' ? 'success' : 'error'} message={toast.message} onClose={() => setToast(null)} />}

      <AsyncStateWrapper status={status} errorMessage="Could not load indexing status." onRetry={() => void load()}>
        <div className="grid gap-3 md:grid-cols-2">
          <AdminRouteSummaryCard
            label="Index state"
            value={info?.state ?? '—'}
            tone={info?.state === 'ready' ? 'success' : 'warning'}
            icon={Database}
          />
          <AdminRouteSummaryCard
            label="Indexed chunks"
            value={info?.indexedChunkCount ?? 0}
          />
        </div>
        <AdminRoutePanel
          title="Phase 2 status — deferred"
          description="pgvector requires a new migration plus CREATE EXTENSION IF NOT EXISTS vector on the production database. Opt-in via AI_ASSISTANT__ENABLE_PGVECTOR=true once approved. See docs/AI-ASSISTANT-PROGRESS.md."
        >
          <p className="text-xs text-admin-text-muted">
            The Reindex button currently returns HTTP 501 by design. No data is destroyed by clicking it.
          </p>
        </AdminRoutePanel>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
