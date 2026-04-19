'use client';

import { useEffect, useRef, useState } from 'react';
import { AdminRouteSectionHeader, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchAdminContentInventory,
  adminBulkImportContent,
} from '@/lib/api';
import type {
  ContentInventoryItem,
  ImportResult,
  PaginatedResponse,
} from '@/lib/types/content-hierarchy';
import { Upload, RefreshCw, Package } from 'lucide-react';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
const PAGE_SIZE = 25;

export default function AdminContentImportPage() {
  const { isAuthenticated } = useAdminAuth();
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [items, setItems] = useState<ContentInventoryItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<ImportResult | null>(null);
  const [reloadNonce, setReloadNonce] = useState(0);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const raw = await fetchAdminContentInventory({ page, pageSize: PAGE_SIZE }) as PaginatedResponse<ContentInventoryItem>;
        if (cancelled) return;
        setItems(raw.items ?? []);
        setTotal(raw.total ?? 0);
        setPageStatus((raw.items?.length ?? 0) > 0 ? 'success' : 'empty');
      } catch {
        if (!cancelled) setPageStatus('error');
      }
    }
    load();
    return () => { cancelled = true; };
  }, [page, reloadNonce]);

  async function handleFileImport(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    if (!file) return;
    setImporting(true);
    try {
      const text = await file.text();
      const rows = JSON.parse(text);
      if (!Array.isArray(rows)) throw new Error('Expected a JSON array of content rows.');
      const result = await adminBulkImportContent(file.name, rows) as ImportResult;
      setImportResult(result);
      setToast({ variant: result.failed > 0 ? 'error' : 'success', message: `Imported ${result.created} items. ${result.failed} failed.` });
      setReloadNonce(n => n + 1);
    } catch (err) {
      setToast({ variant: 'error', message: `Import failed: ${err instanceof Error ? err.message : 'Unknown error'}` });
    } finally {
      setImporting(false);
    }
  }

  const columns: Column<ContentInventoryItem>[] = [
    { key: 'title', header: 'Title', render: (r) => <span className="font-medium text-sm">{r.title}</span> },
    { key: 'subtestCode', header: 'Subtest', render: (r) => <Badge variant="muted">{r.subtestCode}</Badge> },
    { key: 'difficulty', header: 'Difficulty', render: (r) => <Badge variant="muted">{r.difficulty}</Badge> },
    { key: 'sourceProvenance', header: 'Source', render: (r) => <span className="text-xs text-muted">{r.sourceProvenance}</span> },
    { key: 'status', header: 'Status', render: (r) => <Badge variant={r.status === 'Published' ? 'success' : 'muted'}>{r.status}</Badge> },
    { key: 'qualityScore', header: 'Quality', render: (r) => <span className="text-xs">{r.qualityScore}</span> },
    {
      key: 'flags', header: 'Flags', render: (r) => (
        <div className="flex gap-1">
          {r.isMockEligible && <Badge variant="muted" className="text-[10px]">Mock</Badge>}
          {r.isDiagnosticEligible && <Badge variant="muted" className="text-[10px]">Diag</Badge>}
          {r.isPreviewEligible && <Badge variant="muted" className="text-[10px]">Preview</Badge>}
        </div>
      ),
    },
  ];

  if (!isAuthenticated) return null;

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="Content Import & Inventory"
        description="Bulk import content and manage the content inventory."
      />

      <AdminRoutePanel title="Content Inventory">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            <input
              ref={fileInputRef}
              type="file"
              accept=".json"
              className="hidden"
              onChange={handleFileImport}
              disabled={importing}
              aria-label="Import JSON file"
            />
            <Button
              variant="primary"
              size="sm"
              disabled={importing}
              onClick={() => fileInputRef.current?.click()}
            >
              <Upload className="w-4 h-4 mr-1" /> {importing ? 'Importing…' : 'Import JSON'}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setReloadNonce(n => n + 1)}>
              <RefreshCw className="w-4 h-4 mr-1" /> Refresh
            </Button>
          </div>
          <span className="text-xs text-muted">{total} item{total !== 1 ? 's' : ''}</span>
        </div>

        {importResult && (
          <div className="mb-4 rounded-lg border p-3 text-sm bg-muted/50">
            <div className="flex items-center gap-2 mb-1">
              <Package className="w-4 h-4" />
              <strong>Last Import:</strong> Batch {importResult.batchId}
            </div>
            <div className="text-muted">
              Created: {importResult.created} | Failed: {importResult.failed}
              {importResult.errors.length > 0 && (
                <ul className="mt-1 list-disc list-inside text-danger">
                  {importResult.errors.slice(0, 5).map((e, i) => (
                    <li key={i}>Row {e.rowIndex}: {e.message}</li>
                  ))}
                  {importResult.errors.length > 5 && <li>…and {importResult.errors.length - 5} more</li>}
                </ul>
              )}
            </div>
          </div>
        )}

        <AsyncStateWrapper status={pageStatus} errorMessage="Failed to load inventory.">
          {pageStatus === 'empty' ? (
            <EmptyState icon={<Package className="w-8 h-8 text-muted" />} title="No content items yet" description="Import content using the button above." />
          ) : (
            <DataTable columns={columns} data={items} keyExtractor={(r) => r.id} onRowClick={(r) => window.open(`/admin/content?id=${r.id}`, '_self')} />
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
