'use client';

import { useEffect, useState } from 'react';
import { AdminRouteSectionHeader, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { fetchAdminMediaAssets, fetchAdminMediaAudit, adminProcessMediaAsset } from '@/lib/api';
import type { MediaAsset, MediaAuditResult, PaginatedResponse } from '@/lib/types/content-hierarchy';
import { Film, RefreshCw, AlertTriangle } from 'lucide-react';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
const PAGE_SIZE = 25;

export default function AdminMediaPage() {
  const { isAuthenticated } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [assets, setAssets] = useState<MediaAsset[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [audit, setAudit] = useState<MediaAuditResult | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [reloadNonce, setReloadNonce] = useState(0);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const raw = await fetchAdminMediaAssets({ page, pageSize: PAGE_SIZE }) as PaginatedResponse<MediaAsset>;
        if (cancelled) return;
        setAssets(raw.items ?? []);
        setTotal(raw.total ?? 0);
        setPageStatus((raw.items?.length ?? 0) > 0 ? 'success' : 'empty');
      } catch {
        if (!cancelled) setPageStatus('error');
      }
    }
    load();
    return () => { cancelled = true; };
  }, [page, reloadNonce]);

  async function handleAudit() {
    try {
      const result = await fetchAdminMediaAudit() as MediaAuditResult;
      setAudit(result);
    } catch {
      setToast({ variant: 'error', message: 'Media audit failed.' });
    }
  }

  async function handleProcess(assetId: string) {
    try {
      await adminProcessMediaAsset(assetId);
      setToast({ variant: 'success', message: 'Processing enqueued.' });
      setReloadNonce(n => n + 1);
    } catch {
      setToast({ variant: 'error', message: 'Failed to enqueue processing.' });
    }
  }

  const columns: Column<MediaAsset>[] = [
    { key: 'originalFilename', header: 'Filename', render: (r) => <span className="font-medium text-sm truncate max-w-[200px] block">{r.originalFilename}</span> },
    { key: 'mimeType', header: 'Type', render: (r) => <Badge variant="muted" className="text-[10px]">{r.mimeType}</Badge> },
    { key: 'format', header: 'Format', render: (r) => <span className="text-xs">{r.format}</span> },
    { key: 'sizeBytes', header: 'Size', render: (r) => <span className="text-xs">{(r.sizeBytes / 1024 / 1024).toFixed(1)} MB</span> },
    {
      key: 'status', header: 'Status', render: (r) => (
        <Badge variant={r.status === 'Ready' ? 'default' : r.status === 'Processing' ? 'muted' : 'danger'}>
          {r.status}
        </Badge>
      ),
    },
    {
      key: 'actions', header: '', render: (r) => r.status !== 'Ready' ? (
        <Button variant="outline" size="sm" onClick={(e) => { e.stopPropagation(); handleProcess(r.id); }}>
          Process
        </Button>
      ) : null,
    },
  ];

  if (!isAuthenticated) return null;

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="Media Asset Manager"
        description="View, audit, and process media assets across the content library."
      />

      <AdminRoutePanel title="Media Assets">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            <Button variant="primary" size="sm" onClick={handleAudit}>
              <AlertTriangle className="w-4 h-4 mr-1" /> Run Audit
            </Button>
            <Button variant="outline" size="sm" onClick={() => setReloadNonce(n => n + 1)}>
              <RefreshCw className="w-4 h-4 mr-1" /> Refresh
            </Button>
          </div>
          <span className="text-xs text-muted">{total} asset{total !== 1 ? 's' : ''}</span>
        </div>

        {audit && (
          <div className="mb-4 rounded-lg border p-3 text-sm bg-muted/50">
            <div className="grid grid-cols-4 gap-4 mb-2">
              <div><strong>Total:</strong> {audit.totalAssets}</div>
              <div><strong>Ready:</strong> {audit.readyCount}</div>
              <div><strong>Processing:</strong> {audit.processingCount}</div>
              <div className="text-destructive"><strong>Failed:</strong> {audit.failedCount}</div>
            </div>
            {audit.missingThumbnails.length > 0 && (
              <div className="text-xs text-amber-600">⚠ {audit.missingThumbnails.length} missing thumbnails</div>
            )}
            {audit.missingTranscripts.length > 0 && (
              <div className="text-xs text-amber-600">⚠ {audit.missingTranscripts.length} missing transcripts</div>
            )}
          </div>
        )}

        <AsyncStateWrapper status={pageStatus} errorMessage="Failed to load media assets.">
          {pageStatus === 'empty' ? (
            <EmptyState icon={<Film className="w-8 h-8 text-muted" />} title="No media assets" description="Media assets will appear here when content is imported." />
          ) : (
            <DataTable columns={columns} data={assets} keyExtractor={(r) => r.id} />
          )}
        </AsyncStateWrapper>

        {total > PAGE_SIZE && (
          <div className="flex justify-center gap-2 mt-4">
            <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(p => p - 1)}>Previous</Button>
            <span className="text-sm text-muted self-center">Page {page} of {Math.ceil(total / PAGE_SIZE)}</span>
            <Button variant="outline" size="sm" disabled={page >= Math.ceil(total / PAGE_SIZE)} onClick={() => setPage(p => p + 1)}>Next</Button>
          </div>
        )}
      </AdminRoutePanel>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
