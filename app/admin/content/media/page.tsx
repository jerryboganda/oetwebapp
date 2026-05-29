'use client';

import { useEffect, useState, useRef, useCallback } from 'react';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Pagination } from '@/components/ui/pagination';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { fetchAdminMediaAssets, fetchAdminMediaAudit, adminProcessMediaAsset, uploadMedia, deleteMedia } from '@/lib/api';
import type { MediaAsset, MediaAuditResult, PaginatedResponse } from '@/lib/types/content-hierarchy';
import { Film, RefreshCw, AlertTriangle, Upload, Trash2, FileImage, FileText } from 'lucide-react';
import { BulkActionBar } from '@/components/ui/bulk-action-bar';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
const MAX_FILE_SIZE = 10 * 1024 * 1024;
const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/gif', 'image/webp', 'application/pdf'];

function isImage(mimeType: string) {
  return mimeType.startsWith('image/');
}

function formatFileSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

export default function AdminMediaPage() {
  const { isAuthenticated } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [assets, setAssets] = useState<MediaAsset[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [audit, setAudit] = useState<MediaAuditResult | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [reloadNonce, setReloadNonce] = useState(0);
  const [uploading, setUploading] = useState(false);
  const [dragOver, setDragOver] = useState(false);
  const [deleteConfirmId, setDeleteConfirmId] = useState<string | null>(null);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const raw = await fetchAdminMediaAssets({ page, pageSize }) as PaginatedResponse<MediaAsset>;
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
  }, [page, pageSize, reloadNonce]);

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

  const handleUploadFiles = useCallback(async (files: FileList | File[]) => {
    const fileArray = Array.from(files);
    if (fileArray.length === 0) return;

    for (const file of fileArray) {
      if (file.size > MAX_FILE_SIZE) {
        setToast({ variant: 'error', message: `${file.name} exceeds 10 MB limit.` });
        return;
      }
      if (!ALLOWED_TYPES.includes(file.type)) {
        setToast({ variant: 'error', message: `${file.name} has an unsupported file type.` });
        return;
      }
    }

    setUploading(true);
    let successCount = 0;
    for (const file of fileArray) {
      try {
        await uploadMedia(file);
        successCount++;
      } catch (err) {
        const msg = err instanceof Error ? err.message : 'Upload failed';
        setToast({ variant: 'error', message: `Failed to upload ${file.name}: ${msg}` });
      }
    }
    setUploading(false);
    if (successCount > 0) {
      setToast({ variant: 'success', message: `${successCount} file${successCount > 1 ? 's' : ''} uploaded.` });
      setReloadNonce(n => n + 1);
    }
  }, []);

  async function handleDelete(assetId: string) {
    try {
      await deleteMedia(assetId);
      setToast({ variant: 'success', message: 'Media deleted.' });
      setDeleteConfirmId(null);
      setReloadNonce(n => n + 1);
    } catch (err) {
      const msg = err instanceof Error ? err.message : 'Delete failed';
      setToast({ variant: 'error', message: msg });
    }
  }

  function handleDrop(e: React.DragEvent) {
    e.preventDefault();
    setDragOver(false);
    if (e.dataTransfer.files.length > 0) {
      handleUploadFiles(e.dataTransfer.files);
    }
  }

  function handleDragOver(e: React.DragEvent) {
    e.preventDefault();
    setDragOver(true);
  }

  function handleDragLeave(e: React.DragEvent) {
    e.preventDefault();
    setDragOver(false);
  }

  const columns: Column<MediaAsset>[] = [
    {
      key: 'thumbnail', header: '', render: (r) => (
        <div className="w-10 h-10 rounded-admin-sm flex items-center justify-center bg-admin-bg-subtle shrink-0">
          {isImage(r.mimeType) ? (
            <FileImage className="w-5 h-5 text-admin-fg-muted" />
          ) : (
            <FileText className="w-5 h-5 text-admin-fg-muted" />
          )}
        </div>
      ),
    },
    { key: 'originalFilename', header: 'Filename', render: (r) => <span className="font-medium text-sm truncate max-w-[200px] block text-admin-fg-strong">{r.originalFilename}</span> },
    { key: 'mimeType', header: 'Type', render: (r) => <Badge variant="default" size="sm">{r.mimeType}</Badge> },
    { key: 'format', header: 'Format', render: (r) => <span className="text-xs">{r.format}</span> },
    { key: 'sizeBytes', header: 'Size', render: (r) => <span className="text-xs">{formatFileSize(r.sizeBytes)}</span> },
    {
      key: 'status', header: 'Status', render: (r) => (
        <Badge variant={r.status === 'Ready' ? 'success' : r.status === 'Processing' ? 'default' : 'danger'}>
          {r.status}
        </Badge>
      ),
    },
    {
      key: 'actions', header: '', render: (r) => (
        <div className="flex items-center gap-1">
          {r.status !== 'Ready' && (
            <Button variant="outline" size="sm" onClick={(e) => { e.stopPropagation(); handleProcess(r.id); }}>
              Process
            </Button>
          )}
          {deleteConfirmId === r.id ? (
            <div className="flex items-center gap-1">
              <Button variant="destructive" size="sm" onClick={(e) => { e.stopPropagation(); handleDelete(r.id); }}>
                Confirm
              </Button>
              <Button variant="outline" size="sm" onClick={(e) => { e.stopPropagation(); setDeleteConfirmId(null); }}>
                Cancel
              </Button>
            </div>
          ) : (
            <Button variant="outline" size="sm" onClick={(e) => { e.stopPropagation(); setDeleteConfirmId(r.id); }}>
              <Trash2 className="w-3 h-3" />
            </Button>
          )}
        </div>
      ),
    },
  ];

  if (!isAuthenticated) return null;

  return (
    <>
      <AdminTableLayout
        title="Media Asset Manager"
        description="Upload, view, audit, and manage media assets across the content library."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Media' },
        ]}
        actions={
          <div className="flex items-center gap-2">
            <Button variant="primary" size="sm" onClick={handleAudit} startIcon={<AlertTriangle className="w-4 h-4" />}>
              Run Audit
            </Button>
            <Button variant="outline" size="sm" onClick={() => setReloadNonce(n => n + 1)} startIcon={<RefreshCw className="w-4 h-4" />}>
              Refresh
            </Button>
          </div>
        }
        banner={
          <div className="space-y-4">
            <Card>
              <CardHeader><CardTitle>Upload Media</CardTitle></CardHeader>
              <CardContent>
                <div
                  className={`border-2 border-dashed rounded-admin-lg p-8 text-center transition-colors ${
                    dragOver ? 'border-[var(--admin-primary)] bg-[var(--admin-primary-tint)]' : 'border-admin-border'
                  }`}
                  onDrop={handleDrop}
                  onDragOver={handleDragOver}
                  onDragLeave={handleDragLeave}
                >
                  <Upload className="w-8 h-8 text-admin-fg-muted mx-auto mb-2" />
                  <p className="text-sm text-admin-fg-default mb-2">
                    Drag &amp; drop files here, or click to select
                  </p>
                  <p className="text-xs text-admin-fg-muted mb-3">
                    Allowed: JPG, PNG, GIF, WebP, PDF. Max 10 MB per file
                  </p>
                  <input
                    ref={fileInputRef}
                    type="file"
                    accept=".jpg,.jpeg,.png,.gif,.webp,.pdf"
                    multiple
                    className="hidden"
                    onChange={(e) => {
                      if (e.target.files) handleUploadFiles(e.target.files);
                      e.target.value = '';
                    }}
                  />
                  <Button
                    variant="primary"
                    size="sm"
                    disabled={uploading}
                    loading={uploading}
                    onClick={() => fileInputRef.current?.click()}
                  >
                    {uploading ? 'Uploading…' : 'Select Files'}
                  </Button>
                </div>
              </CardContent>
            </Card>

            {audit && (
              <InlineAlert
                variant={audit.failedCount > 0 ? 'warning' : 'info'}
                title="Media inventory snapshot"
              >
                <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
                  <div><strong>Total:</strong> {audit.totalAssets}</div>
                  <div><strong>Ready:</strong> {audit.readyCount}</div>
                  <div><strong>Processing:</strong> {audit.processingCount}</div>
                  <div className="text-danger"><strong>Failed:</strong> {audit.failedCount}</div>
                </div>
                {audit.missingThumbnails.length > 0 && (
                  <div className="mt-2 text-xs text-warning">⚠ {audit.missingThumbnails.length} missing thumbnails</div>
                )}
                {audit.missingTranscripts.length > 0 && (
                  <div className="mt-1 text-xs text-warning">⚠ {audit.missingTranscripts.length} missing transcripts</div>
                )}
              </InlineAlert>
            )}
          </div>
        }
      >
        <div className="p-4 sm:p-5">
          <div className="flex items-center justify-end mb-4">
            <span className="text-xs text-admin-fg-muted">{total} asset{total !== 1 ? 's' : ''}</span>
          </div>

          <AsyncStateWrapper status={pageStatus} errorMessage="Failed to load media assets.">
            {pageStatus === 'empty' ? (
              <EmptyState icon={<Film className="w-8 h-8 text-admin-fg-muted" />} title="No media assets" description="Upload files or import content to see media assets here." />
            ) : (
              <>
                <DataTable columns={columns} data={assets} keyExtractor={(r) => r.id} selectable selectedKeys={selectedKeys} onSelectionChange={setSelectedKeys} />
                <BulkActionBar
                  selectedCount={selectedKeys.size}
                  onClearSelection={() => setSelectedKeys(new Set())}
                  actions={[
                    { key: 'delete', label: 'Delete selected', variant: 'danger', onClick: () => setToast({ variant: 'error', message: 'Bulk delete coming soon.' }) },
                  ]}
                />
              </>
            )}
          </AsyncStateWrapper>

          <div className="mt-4">
            <Pagination
              page={page}
              pageSize={pageSize}
              total={total}
              onPageChange={setPage}
              onPageSizeChange={setPageSize}
              itemLabel="asset"
              itemLabelPlural="assets"
            />
          </div>
        </div>
      </AdminTableLayout>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </>
  );
}
