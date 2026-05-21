'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { Library, Plus, RotateCcw, Upload as UploadIcon } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Modal } from '@/components/ui/modal';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import {
  adminListRecallDocuments,
  adminUploadRecallDocument,
  adminPublishRecallDocument,
  adminArchiveRecallDocument,
  adminUnarchiveRecallDocument,
  adminDeleteRecallDocument,
  type RecallDocumentDto,
  type RecallSubtest,
  type RecallDocumentStatus,
} from '@/lib/api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const SUBTEST_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'All' },
  { value: 'listening', label: 'Listening' },
  { value: 'reading', label: 'Reading' },
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
  { value: 'cross', label: 'Cross-cutting' },
];

const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'All' },
  { value: 'Draft', label: 'Draft' },
  { value: 'Published', label: 'Published' },
  { value: 'Archived', label: 'Archived' },
];

export default function AdminRecallsLibraryPage() {
  const [items, setItems] = useState<RecallDocumentDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [filterSubtest, setFilterSubtest] = useState<string>('');
  const [filterStatus, setFilterStatus] = useState<string>('');
  const [page, setPage] = useState(1);
  const [pageSize] = useState(50);
  const [total, setTotal] = useState(0);

  const [uploadOpen, setUploadOpen] = useState(false);
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadForm, setUploadForm] = useState({
    title: '',
    subtestCode: 'listening' as RecallSubtest,
    periodLabel: '',
    professionId: '',
    descriptionMarkdown: '',
    sortOrder: 0,
  });
  const [uploading, setUploading] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListRecallDocuments({
        subtest: (filterSubtest as RecallSubtest) || undefined,
        status: (filterStatus as RecallDocumentStatus) || undefined,
        page,
        pageSize,
      });
      setItems(data.items ?? []);
      setTotal(data.total ?? 0);
    } catch (e) {
      setToast({ variant: 'error', message: `Failed to load recalls: ${(e as Error).message}` });
    } finally {
      setLoading(false);
    }
  }, [filterSubtest, filterStatus, page, pageSize]);

  useEffect(() => { void reload(); }, [reload]);

  async function handleUpload(e?: React.FormEvent) {
    e?.preventDefault();
    if (!uploadFile) {
      setToast({ variant: 'error', message: 'Pick a PDF file first.' });
      return;
    }
    if (!uploadForm.title.trim()) {
      setToast({ variant: 'error', message: 'Title is required.' });
      return;
    }
    if (!uploadForm.periodLabel.trim()) {
      setToast({ variant: 'error', message: 'Period label is required (e.g. "2026 Q1" or "2023-2025").' });
      return;
    }
    setUploading(true);
    try {
      const created = await adminUploadRecallDocument({
        file: uploadFile,
        title: uploadForm.title.trim(),
        subtestCode: uploadForm.subtestCode,
        periodLabel: uploadForm.periodLabel.trim(),
        professionId: uploadForm.professionId.trim() || null,
        descriptionMarkdown: uploadForm.descriptionMarkdown.trim() || null,
        sortOrder: uploadForm.sortOrder,
      });
      setToast({ variant: 'success', message: `Uploaded "${created.title}" as Draft.` });
      setUploadOpen(false);
      setUploadFile(null);
      setUploadForm({ title: '', subtestCode: 'listening', periodLabel: '', professionId: '', descriptionMarkdown: '', sortOrder: 0 });
      if (fileRef.current) fileRef.current.value = '';
      await reload();
    } catch (err) {
      setToast({ variant: 'error', message: (err as Error).message });
    } finally {
      setUploading(false);
    }
  }

  async function handlePublish(doc: RecallDocumentDto) {
    if (!confirm(`Publish "${doc.title}"? Learners will be able to see and download it.`)) return;
    setBusyId(doc.id);
    try {
      await adminPublishRecallDocument(doc.id);
      setToast({ variant: 'success', message: `Published "${doc.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  async function handleArchive(doc: RecallDocumentDto) {
    if (!confirm(`Archive "${doc.title}"? Learners will no longer see it.`)) return;
    setBusyId(doc.id);
    try {
      await adminArchiveRecallDocument(doc.id);
      setToast({ variant: 'success', message: `Archived "${doc.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  async function handleUnarchive(doc: RecallDocumentDto) {
    setBusyId(doc.id);
    try {
      await adminUnarchiveRecallDocument(doc.id);
      setToast({ variant: 'success', message: `Restored "${doc.title}" to Draft.` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  async function handleDelete(doc: RecallDocumentDto) {
    if (!confirm(`Soft-delete "${doc.title}"? It will be archived (reversible from this UI).`)) return;
    setBusyId(doc.id);
    try {
      await adminDeleteRecallDocument(doc.id);
      setToast({ variant: 'success', message: `Removed "${doc.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  const totalPages = Math.max(1, Math.ceil(total / pageSize));

  return (
    <AdminRouteWorkspace role="main" aria-label="Recalls library">
      <AdminRouteHero
        eyebrow="CMS"
        icon={Library}
        accent="navy"
        title="Recalls Library"
        description="Upload exam recall PDFs (monthly / yearly digests) for learners to read and download. Separate from per-learner spaced-repetition recalls."
        aside={(
          <div className="rounded-2xl border border-border bg-background-light p-4 shadow-sm">
            <Button onClick={() => setUploadOpen(true)}>
              <Plus className="h-4 w-4 mr-1" /> Upload PDF
            </Button>
          </div>
        )}
      />

      <AdminRoutePanel>
        <div className="grid grid-cols-1 sm:grid-cols-4 gap-3">
          <Select
            label="Subtest"
            value={filterSubtest}
            onChange={(e) => { setPage(1); setFilterSubtest(e.target.value); }}
            options={SUBTEST_OPTIONS}
          />
          <Select
            label="Status"
            value={filterStatus}
            onChange={(e) => { setPage(1); setFilterStatus(e.target.value); }}
            options={STATUS_OPTIONS}
          />
          <div className="flex items-end">
            <Button variant="outline" onClick={() => void reload()}>
              <RotateCcw className="h-4 w-4 mr-1" /> Refresh
            </Button>
          </div>
          <div className="flex items-end justify-end text-xs text-admin-text-muted">
            {total} total - page {page} / {totalPages}
          </div>
        </div>
      </AdminRoutePanel>

      {loading ? (
        <div className="space-y-3">
          {[1, 2, 3].map((i) => <Skeleton key={i} className="h-24" />)}
        </div>
      ) : items.length === 0 ? (
        <Card className="p-8 text-center text-muted">
          No recall documents found. Click <strong>Upload PDF</strong> to add the first one.
        </Card>
      ) : (
        <div className="space-y-3">
          {items.map((doc) => (
            <Card key={doc.id} className="p-4">
              <div className="flex items-start justify-between flex-wrap gap-3">
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-2 flex-wrap mb-1">
                    <h3 className="font-semibold truncate">{doc.title}</h3>
                    <Badge
                      variant={
                        doc.status === 'Published' ? 'success'
                          : doc.status === 'Draft' ? 'muted'
                          : doc.status === 'Archived' ? 'outline' : 'outline'
                      }
                    >
                      {doc.status}
                    </Badge>
                    <Badge variant="outline">{doc.subtestCode}</Badge>
                    <Badge variant="outline">{doc.periodLabel}</Badge>
                    {doc.professionId
                      ? <Badge variant="outline">{doc.professionId}</Badge>
                      : <Badge variant="outline">all professions</Badge>}
                  </div>
                  {doc.descriptionMarkdown ? (
                    <p className="text-sm text-muted line-clamp-2">{doc.descriptionMarkdown}</p>
                  ) : null}
                  <p className="text-xs text-muted mt-1">
                    {doc.media?.originalFilename ?? 'no file'}
                    {doc.media?.sizeBytes ? ` - ${(doc.media.sizeBytes / 1024 / 1024).toFixed(2)} MB` : ''}
                    {' - '}Updated {new Date(doc.updatedAt).toLocaleDateString()}
                  </p>
                </div>
                <div className="flex items-center gap-2 flex-wrap">
                  {doc.status === 'Draft' || doc.status === 'InReview' ? (
                    <Button size="sm" onClick={() => void handlePublish(doc)} disabled={busyId === doc.id}>Publish</Button>
                  ) : null}
                  {doc.status === 'Published' ? (
                    <Button size="sm" variant="outline" onClick={() => void handleArchive(doc)} disabled={busyId === doc.id}>Archive</Button>
                  ) : null}
                  {doc.status === 'Archived' ? (
                    <Button size="sm" variant="outline" onClick={() => void handleUnarchive(doc)} disabled={busyId === doc.id}>Unarchive</Button>
                  ) : null}
                  <Button size="sm" variant="ghost" onClick={() => void handleDelete(doc)} disabled={busyId === doc.id}>Delete</Button>
                </div>
              </div>
            </Card>
          ))}

          {totalPages > 1 ? (
            <div className="flex items-center justify-center gap-2 mt-4">
              <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))}>Previous</Button>
              <span className="text-xs text-muted">{page} / {totalPages}</span>
              <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => setPage((p) => Math.min(totalPages, p + 1))}>Next</Button>
            </div>
          ) : null}
        </div>
      )}

      {uploadOpen ? (
        <Modal open={uploadOpen} onClose={() => setUploadOpen(false)} title="Upload recall PDF">
          <form className="space-y-3" onSubmit={(e) => void handleUpload(e)}>
            <div>
              <label className="block text-sm font-medium mb-1">PDF file (max 50 MB)</label>
              <input
                ref={fileRef}
                type="file"
                accept=".pdf,application/pdf"
                onChange={(e) => setUploadFile(e.target.files?.[0] ?? null)}
                className="w-full text-sm"
              />
              {uploadFile ? (
                <p className="text-xs text-muted mt-1">
                  {uploadFile.name} - {(uploadFile.size / 1024 / 1024).toFixed(2)} MB
                </p>
              ) : null}
            </div>
            <Input
              label="Title"
              value={uploadForm.title}
              onChange={(e) => setUploadForm({ ...uploadForm, title: e.target.value })}
              placeholder='e.g. "2026 Listening Recalls (Jan-Mar)"'
              required
            />
            <Select
              label="Subtest"
              value={uploadForm.subtestCode}
              onChange={(e) => setUploadForm({ ...uploadForm, subtestCode: e.target.value as RecallSubtest })}
              options={SUBTEST_OPTIONS.filter((o) => o.value !== '')}
            />
            <Input
              label="Period label"
              value={uploadForm.periodLabel}
              onChange={(e) => setUploadForm({ ...uploadForm, periodLabel: e.target.value })}
              placeholder='e.g. "2026 Q1" or "2023-2025"'
              required
            />
            <Input
              label="Profession (optional - leave blank for all)"
              value={uploadForm.professionId}
              onChange={(e) => setUploadForm({ ...uploadForm, professionId: e.target.value })}
              placeholder="e.g. medicine, nursing, dentistry"
            />
            <Textarea
              label="Description (markdown, optional)"
              value={uploadForm.descriptionMarkdown}
              onChange={(e) => setUploadForm({ ...uploadForm, descriptionMarkdown: e.target.value })}
              rows={3}
            />
            <Input
              type="number"
              label="Sort order (lower shows first)"
              value={String(uploadForm.sortOrder)}
              onChange={(e) => setUploadForm({ ...uploadForm, sortOrder: Number.parseInt(e.target.value || '0', 10) })}
            />
            <div className="flex justify-end gap-2 pt-2">
              <Button type="button" variant="outline" onClick={() => setUploadOpen(false)} disabled={uploading}>Cancel</Button>
              <Button type="submit" disabled={uploading}>
                {uploading ? 'Uploading...' : (<><UploadIcon className="h-4 w-4 mr-1" />Upload as Draft</>)}
              </Button>
            </div>
          </form>
        </Modal>
      ) : null}

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </AdminRouteWorkspace>
  );
}
