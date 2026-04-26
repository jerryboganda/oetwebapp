'use client';

import { useEffect, useState, useRef, useCallback } from 'react';
import { AdminRouteSectionHeader, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Pagination } from '@/components/ui/pagination';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { fetchAdminMediaAssets, fetchAdminMediaAudit, adminProcessMediaAsset, uploadMedia, deleteMedia } from '@/lib/api';
import type { MediaAsset, MediaAuditResult, PaginatedResponse } from '@/lib/types/content-hierarchy';
import { Film, RefreshCw, AlertTriangle, Upload, Trash2, FileImage, FileText } from 'lucide-react';

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
        <div className="w-10 h-10 rounded flex items-center justify-center bg-surface-alt shrink-0">
          {isImage(r.mimeType) ? (
            <FileImage className="w-5 h-5 text-muted" />
          ) : (
            <FileText className="w-5 h-5 text-muted" />
          )}
        </div>
      ),
    },
    { key: 'originalFilename', header: 'Filename', render: (r) => <span className="font-medium text-sm truncate max-w-[200px] block">{r.originalFilename}</span> },
    { key: 'mimeType', header: 'Type', render: (r) => <Badge variant="muted" className="text-[10px]">{r.mimeType}</Badge> },
    { key: 'format', header: 'Format', render: (r) => <span className="text-xs">{r.format}</span> },
    { key: 'sizeBytes', header: 'Size', render: (r) => <span className="text-xs">{formatFileSize(r.sizeBytes)}</span> },
    {
      key: 'status', header: 'Status', render: (r) => (
        <Badge variant={r.status === 'Ready' ? 'default' : r.status === 'Processing' ? 'muted' : 'danger'}>
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
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="Media Asset Manager"
        description="Upload, view, audit, and manage media assets across the content library."
      />

      {/* Upload Drop Zone */}
      <AdminRoutePanel title="Upload Media">
        <div
          className={`border-2 border-dashed rounded-lg p-8 text-center transition-colors ${
            dragOver ? 'border-primary bg-primary/5' : 'border-border'
          }`}
          onDrop={handleDrop}
          onDragOver={handleDragOver}
          onDragLeave={handleDragLeave}
        >
          <Upload className="w-8 h-8 text-muted mx-auto mb-2" />
          <p className="text-sm text-muted mb-2">
            Drag & drop files here, or click to select
          </p>
          <p className="text-xs text-muted mb-3">
            Allowed: JPG, PNG, GIF, WebP, PDF — Max 10 MB per file
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
            onClick={() => fileInputRef.current?.click()}
          >
            {uploading ? 'Uploading…' : 'Select Files'}
          </Button>
        </div>
      </AdminRoutePanel>

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
            <EmptyState icon={<Film className="w-8 h-8 text-muted" />} title="No media assets" description="Upload files or import content to see media assets here." />
          ) : (
            <DataTable columns={columns} data={assets} keyExtractor={(r) => r.id} />
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
      </AdminRoutePanel>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
