'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Archive,
  CheckCircle2,
  Download,
  FileUp,
  Loader2,
  Package,
  Search,
  Tag,
  Upload,
  XCircle,
} from 'lucide-react';
import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { DataTable, type Column } from '@/components/ui/data-table';
import { BulkActionBar } from '@/components/ui/bulk-action-bar';
import { Input } from '@/components/ui/form-controls';
import { Modal } from '@/components/ui/modal';
import { Toast } from '@/components/ui/alert';
import { TableSkeleton } from '@/components/admin/ui/skeleton';
import { apiClient, fetchAuthorizedBlob } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface ContentPaper {
  id: string;
  title: string;
  subSkill: string;
  profession: string;
  status: 'draft' | 'published' | 'retired';
  updatedAt: string;
  tags: string[];
}

/**
 * Wire shape of GET /v1/admin/papers (see ContentPapersAdminEndpoints.ProjectPaper).
 * The route returns a BARE ARRAY — the total lives in the X-Total-Count header —
 * and uses different field names to this page's display model, so responses are
 * normalised through `toContentPaper` rather than consumed directly.
 */
interface AdminPaperWire {
  id: string;
  title: string;
  subtestCode?: string | null;
  professionId?: string | null;
  appliesToAllProfessions?: boolean;
  status?: string | null;
  updatedAt?: string | null;
  tagsCsv?: string | null;
}

// Backend ContentStatus has no "retired" member; Archived is the retired state.
function normalizePaperStatus(status: string | null | undefined): ContentPaper['status'] {
  switch ((status ?? '').toLowerCase()) {
    case 'published':
      return 'published';
    case 'archived':
      return 'retired';
    default:
      return 'draft';
  }
}

function toContentPaper(row: AdminPaperWire): ContentPaper {
  return {
    id: row.id,
    title: row.title,
    subSkill: row.subtestCode ?? '—',
    profession: row.appliesToAllProfessions ? 'All' : (row.professionId ?? '—'),
    status: normalizePaperStatus(row.status),
    updatedAt: row.updatedAt ?? '',
    tags: (row.tagsCsv ?? '')
      .split(',')
      .map((tag) => tag.trim())
      .filter(Boolean),
  };
}

interface BulkResult {
  succeeded: number;
  failed: number;
  errors: string[];
}

export default function AdminBulkOperationsPage() {
  const { isAuthenticated, isLoading: authLoading } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [papers, setPapers] = useState<ContentPaper[]>([]);
  const [filter, setFilter] = useState('');
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());
  const [toast, setToast] = useState<ToastState>(null);

  // Bulk operation state
  const [isBulkRunning, setIsBulkRunning] = useState(false);
  const [bulkProgress, setBulkProgress] = useState<string | null>(null);
  const [bulkResult, setBulkResult] = useState<BulkResult | null>(null);

  // Tag modal
  const [isTagModalOpen, setIsTagModalOpen] = useState(false);
  const [tagInput, setTagInput] = useState('');

  // Upload modal
  const [isUploadModalOpen, setIsUploadModalOpen] = useState(false);
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [isUploading, setIsUploading] = useState(false);

  // Confirm modal
  const [confirmAction, setConfirmAction] = useState<{
    type: 'publish' | 'retire';
    count: number;
  } | null>(null);

  const loadPapers = useCallback(async () => {
    setPageStatus('loading');
    try {
      const rows = await apiClient.get<AdminPaperWire[]>('/v1/admin/papers?pageSize=500');
      const items = (Array.isArray(rows) ? rows : []).map(toContentPaper);
      setPapers(items);
      setPageStatus(items.length ? 'success' : 'empty');
    } catch {
      setPageStatus('error');
      setToast({ variant: 'error', message: 'Failed to load content papers.' });
    }
  }, []);

  useEffect(() => {
    if (!authLoading && isAuthenticated) {
      loadPapers();
    }
  }, [authLoading, isAuthenticated, loadPapers]);

  const filtered = useMemo(
    () =>
      papers.filter(
        (p) =>
          p.title.toLowerCase().includes(filter.toLowerCase()) ||
          p.subSkill.toLowerCase().includes(filter.toLowerCase()) ||
          p.profession.toLowerCase().includes(filter.toLowerCase())
      ),
    [papers, filter]
  );

  const metrics = useMemo(() => {
    const draft = papers.filter((p) => p.status === 'draft').length;
    const published = papers.filter((p) => p.status === 'published').length;
    const retired = papers.filter((p) => p.status === 'retired').length;
    return { total: papers.length, draft, published, retired };
  }, [papers]);

  // --- Bulk actions ---

  const runBulkAction = useCallback(
    async (action: 'publish' | 'retire', ids: string[]) => {
      setIsBulkRunning(true);
      setBulkProgress(`Processing ${ids.length} papers…`);
      setBulkResult(null);
      try {
        // One unified route: POST /v1/admin/papers/bulk {action, ids, reason}.
        // "retire" is this page's label for the backend's `archive` action —
        // ContentStatus has no Retired member.
        const result = await apiClient.post<BulkResult>('/v1/admin/papers/bulk', {
          action: action === 'retire' ? 'archive' : 'publish',
          ids,
        });
        setBulkResult(result);
        setToast({
          variant: result.failed > 0 ? 'error' : 'success',
          message: `${action === 'publish' ? 'Published' : 'Retired'}: ${result.succeeded} succeeded, ${result.failed} failed.`,
        });
        setSelectedKeys(new Set());
        loadPapers();
      } catch (err: any) {
        setToast({ variant: 'error', message: err?.userMessage ?? `Bulk ${action} failed.` });
      } finally {
        setIsBulkRunning(false);
        setBulkProgress(null);
      }
    },
    [loadPapers]
  );

  const handleBulkPublish = () => {
    setConfirmAction({ type: 'publish', count: selectedKeys.size });
  };

  const handleBulkRetire = () => {
    setConfirmAction({ type: 'retire', count: selectedKeys.size });
  };

  const handleConfirmAction = () => {
    if (!confirmAction) return;
    runBulkAction(confirmAction.type, Array.from(selectedKeys));
    setConfirmAction(null);
  };

  const handleBulkTag = async () => {
    const tags = tagInput
      .split(',')
      .map((t) => t.trim())
      .filter(Boolean);
    if (tags.length === 0) return;

    const ids = Array.from(selectedKeys);
    setIsBulkRunning(true);
    setBulkProgress(`Tagging ${ids.length} papers…`);
    try {
      // There is no bulk-tag route; tags are applied per paper through
      // PUT /v1/admin/papers/{id}, which is a partial patch (only non-null
      // fields are written), so sending tagsCsv alone touches nothing else.
      // Tags are merged into the paper's existing set rather than replacing it.
      let failed = 0;
      for (const [index, id] of ids.entries()) {
        setBulkProgress(`Tagging ${index + 1} of ${ids.length}…`);
        const existing = papers.find((paper) => paper.id === id)?.tags ?? [];
        const merged = Array.from(new Set([...existing, ...tags]));
        try {
          await apiClient.put(`/v1/admin/papers/${encodeURIComponent(id)}`, {
            tagsCsv: merged.join(','),
          });
        } catch {
          failed += 1;
        }
      }
      setToast(
        failed > 0
          ? { variant: 'error', message: `Tagged ${ids.length - failed} of ${ids.length}; ${failed} failed.` }
          : { variant: 'success', message: `Tagged ${ids.length} papers.` }
      );
      setSelectedKeys(new Set());
      setIsTagModalOpen(false);
      setTagInput('');
      loadPapers();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? 'Bulk tag failed.' });
    } finally {
      setIsBulkRunning(false);
      setBulkProgress(null);
    }
  };

  const handleBulkExport = async (format: 'json' | 'csv') => {
    setIsBulkRunning(true);
    setBulkProgress(`Exporting ${selectedKeys.size} papers as ${format.toUpperCase()}…`);
    try {
      // apiClient JSON-parses every response, so a file download has to go
      // through the authorized-blob helper instead.
      const blob = await fetchAuthorizedBlob(
        `/v1/admin/papers/export?format=${format}&ids=${encodeURIComponent(Array.from(selectedKeys).join(','))}`
      );
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `content-papers-export.${format}`;
      link.click();
      URL.revokeObjectURL(url);
      setToast({ variant: 'success', message: `Exported ${selectedKeys.size} papers.` });
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? 'Export failed.' });
    } finally {
      setIsBulkRunning(false);
      setBulkProgress(null);
    }
  };

  const handleZipUpload = async () => {
    if (!uploadFile) return;
    setIsUploading(true);
    try {
      const formData = new FormData();
      formData.append('file', uploadFile);
      await apiClient.postForm('/v1/admin/imports/zip', formData);
      setToast({ variant: 'success', message: 'ZIP import started successfully.' });
      setIsUploadModalOpen(false);
      setUploadFile(null);
      loadPapers();
    } catch (err: any) {
      setToast({ variant: 'error', message: err?.userMessage ?? 'ZIP import failed.' });
    } finally {
      setIsUploading(false);
    }
  };

  // --- Table columns ---

  const columns: Column<ContentPaper>[] = [
    {
      key: 'title',
      header: 'Title',
      render: (paper) => (
        <div className="min-w-0">
          <p className="truncate font-semibold text-admin-fg-strong">{paper.title}</p>
          <p className="mt-0.5 text-xs text-admin-fg-muted">
            {paper.subSkill} · {paper.profession}
          </p>
        </div>
      ),
    },
    {
      key: 'status',
      header: 'Status',
      render: (paper) => (
        <Badge
          variant={
            paper.status === 'published'
              ? 'success'
              : paper.status === 'retired'
                ? 'danger'
                : 'warning'
          }
        >
          {paper.status}
        </Badge>
      ),
    },
    {
      key: 'tags',
      header: 'Tags',
      render: (paper) =>
        paper.tags.length > 0 ? (
          <div className="flex flex-wrap gap-1">
            {paper.tags.slice(0, 3).map((tag) => (
              <Badge key={tag} variant="default">
                {tag}
              </Badge>
            ))}
            {paper.tags.length > 3 && (
              <span className="text-xs text-admin-fg-muted">+{paper.tags.length - 3}</span>
            )}
          </div>
        ) : (
          <span className="text-xs text-admin-fg-muted">-</span>
        ),
      hideOnMobile: true,
    },
    {
      key: 'updatedAt',
      header: 'Updated',
      render: (paper) => (
        <span className="text-sm tabular-nums text-admin-fg-muted">
          {new Date(paper.updatedAt).toLocaleDateString()}
        </span>
      ),
      hideOnMobile: true,
    },
  ];

  // --- Bulk action bar config ---

  const bulkActions = useMemo(
    () => [
      {
        key: 'publish',
        label: 'Publish',
        icon: <CheckCircle2 className="h-4 w-4" />,
        variant: 'primary' as const,
        disabled: isBulkRunning,
        onClick: handleBulkPublish,
      },
      {
        key: 'retire',
        label: 'Retire',
        icon: <Archive className="h-4 w-4" />,
        variant: 'danger' as const,
        disabled: isBulkRunning,
        onClick: handleBulkRetire,
      },
      {
        key: 'tag',
        label: 'Tag',
        icon: <Tag className="h-4 w-4" />,
        variant: 'secondary' as const,
        disabled: isBulkRunning,
        onClick: () => setIsTagModalOpen(true),
      },
      {
        key: 'export-json',
        label: 'Export JSON',
        icon: <Download className="h-4 w-4" />,
        variant: 'secondary' as const,
        disabled: isBulkRunning,
        onClick: () => handleBulkExport('json'),
      },
      {
        key: 'export-csv',
        label: 'Export CSV',
        icon: <Download className="h-4 w-4" />,
        variant: 'secondary' as const,
        disabled: isBulkRunning,
        onClick: () => handleBulkExport('csv'),
      },
    ],
    [isBulkRunning]
  );

  return (
    <AdminRouteWorkspace>
      <AdminTableLayout
        title="Bulk Operations"
        description="Perform bulk actions on content papers: publish, retire, tag, export, or import."
        eyebrow="Content"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Bulk Operations' },
        ]}
        actions={
          <Button variant="primary" onClick={() => setIsUploadModalOpen(true)}>
            <Upload className="h-4 w-4" />
            Import ZIP
          </Button>
        }
        banner={
          <div className="space-y-4">
            <KpiStrip>
              <KpiTile label="Total Papers" value={metrics.total} />
              <KpiTile label="Draft" value={metrics.draft} />
              <KpiTile label="Published" value={metrics.published} />
              <KpiTile label="Retired" value={metrics.retired} />
            </KpiStrip>

            <div className="flex flex-col gap-3 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-3 shadow-admin-sm sm:flex-row sm:items-center sm:justify-between">
              <div className="text-sm text-admin-fg-muted">
                {filtered.length} papers · {selectedKeys.size} selected
              </div>
              <div className="relative w-full min-w-[240px] sm:w-80">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-admin-fg-muted" />
                <Input
                  placeholder="Search by title, skill, or profession…"
                  value={filter}
                  onChange={(e) => setFilter(e.target.value)}
                  className="pl-9"
                />
              </div>
            </div>

            {bulkProgress && (
              <div className="flex items-center gap-2 rounded-admin border border-admin-border bg-admin-bg-surface p-3 text-sm text-admin-fg-default">
                <Loader2 className="h-4 w-4 animate-spin text-[var(--admin-primary)]" />
                {bulkProgress}
              </div>
            )}
          </div>
        }
      >
        {pageStatus === 'loading' ? (
          <TableSkeleton rows={8} columns={4} />
        ) : pageStatus === 'error' ? (
          <EmptyState
            variant="error"
            illustration={<Package className="h-10 w-10" />}
            title="Could not load content papers"
            description="Check your connection and try again."
          />
        ) : pageStatus === 'empty' ? (
          <EmptyState
            variant="default"
            illustration={<Package className="h-10 w-10" />}
            title="No content papers"
            description="Import papers via ZIP or create them from the content library."
          />
        ) : (
          <>
            <DataTable
              columns={columns}
              data={filtered}
              keyExtractor={(paper) => paper.id}
              emptyMessage="No papers match the current filter."
              aria-label="Content papers"
              selectable
              selectedKeys={selectedKeys}
              onSelectionChange={setSelectedKeys}
            />
            <BulkActionBar
              selectedCount={selectedKeys.size}
              actions={bulkActions}
              onClearSelection={() => setSelectedKeys(new Set())}
              totalCount={filtered.length}
              onSelectAll={() => setSelectedKeys(new Set(filtered.map((p) => p.id)))}
            />
          </>
        )}
      </AdminTableLayout>

      {/* Confirm Publish/Retire Modal */}
      {confirmAction && (
        <Modal
          open
          onClose={() => setConfirmAction(null)}
          title={`Confirm Bulk ${confirmAction.type === 'publish' ? 'Publish' : 'Retire'}`}
        >
          <div className="space-y-4 p-4">
            <p className="text-sm text-admin-fg-default">
              Are you sure you want to {confirmAction.type}{' '}
              <strong>{confirmAction.count}</strong> content paper
              {confirmAction.count === 1 ? '' : 's'}?
            </p>
            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={() => setConfirmAction(null)}>
                Cancel
              </Button>
              <Button
                variant={confirmAction.type === 'retire' ? 'destructive' : 'primary'}
                onClick={handleConfirmAction}
                disabled={isBulkRunning}
              >
                {isBulkRunning && <Loader2 className="h-4 w-4 animate-spin" />}
                {confirmAction.type === 'publish' ? 'Publish All' : 'Retire All'}
              </Button>
            </div>
          </div>
        </Modal>
      )}

      {/* Tag Modal */}
      {isTagModalOpen && (
        <Modal open onClose={() => setIsTagModalOpen(false)} title="Add Tags to Selected Papers">
          <div className="space-y-4 p-4">
            <p className="text-sm text-admin-fg-muted">
              Enter tags separated by commas. They will be added to{' '}
              <strong>{selectedKeys.size}</strong> selected paper
              {selectedKeys.size === 1 ? '' : 's'}.
            </p>
            <Input
              placeholder="e.g. nursing, cardiology, part-a"
              value={tagInput}
              onChange={(e) => setTagInput(e.target.value)}
            />
            <div className="flex justify-end gap-2">
              <Button variant="secondary" onClick={() => setIsTagModalOpen(false)}>
                Cancel
              </Button>
              <Button
                variant="primary"
                onClick={handleBulkTag}
                disabled={isBulkRunning || !tagInput.trim()}
              >
                {isBulkRunning && <Loader2 className="h-4 w-4 animate-spin" />}
                Apply Tags
              </Button>
            </div>
          </div>
        </Modal>
      )}

      {/* ZIP Upload Modal */}
      {isUploadModalOpen && (
        <Modal open onClose={() => setIsUploadModalOpen(false)} title="Import Content from ZIP">
          <div className="space-y-4 p-4">
            <p className="text-sm text-admin-fg-muted">
              Upload a ZIP file containing content papers and their assets. The import runs
              asynchronously; progress appears in the import log.
            </p>
            <div className="flex flex-col items-center gap-3 rounded-admin-lg border-2 border-dashed border-admin-border bg-admin-bg-subtle p-8">
              <FileUp className="h-8 w-8 text-admin-fg-muted" />
              <label className="cursor-pointer text-sm font-medium text-[var(--admin-primary)] hover:underline">
                Choose ZIP file
                <input
                  type="file"
                  accept=".zip"
                  className="hidden"
                  onChange={(e) => setUploadFile(e.target.files?.[0] ?? null)}
                />
              </label>
              {uploadFile && (
                <p className="text-sm text-admin-fg-default">{uploadFile.name}</p>
              )}
            </div>
            <div className="flex justify-end gap-2">
              <Button
                variant="secondary"
                onClick={() => {
                  setIsUploadModalOpen(false);
                  setUploadFile(null);
                }}
              >
                Cancel
              </Button>
              <Button
                variant="primary"
                onClick={handleZipUpload}
                disabled={!uploadFile || isUploading}
              >
                {isUploading && <Loader2 className="h-4 w-4 animate-spin" />}
                Upload & Import
              </Button>
            </div>
          </div>
        </Modal>
      )}

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}
