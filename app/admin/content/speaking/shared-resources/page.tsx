'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { Plus, RotateCcw, Sparkles, Upload as UploadIcon } from 'lucide-react';

import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';

import { Modal } from '@/components/ui/modal';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import {
  adminListSpeakingSharedResources,
  adminUploadSpeakingSharedResource,
  adminPublishSpeakingSharedResource,
  adminArchiveSpeakingSharedResource,
  adminDeleteSpeakingSharedResource,
  adminForceDeleteSpeakingSharedResource,
  type SpeakingSharedResourceDto,
  type SpeakingSharedResourceKind,
} from '@/lib/api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const KIND_OPTIONS: { value: SpeakingSharedResourceKind; label: string }[] = [
  { value: 'WarmUpQuestions', label: 'Warm-up questions' },
  { value: 'AssessmentCriteria', label: 'Assessment criteria' },
];

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Content', href: '/admin/content' },
  { label: 'Speaking', href: '/admin/content/speaking' },
  { label: 'Shared resources' },
];

export default function AdminSpeakingSharedResourcesPage() {
  const [items, setItems] = useState<SpeakingSharedResourceDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [filterKind, setFilterKind] = useState<string>('');

  const [uploadOpen, setUploadOpen] = useState(false);
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadForm, setUploadForm] = useState({
    kind: 'WarmUpQuestions' as SpeakingSharedResourceKind,
    title: '',
    professionId: '',
  });
  const [uploading, setUploading] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListSpeakingSharedResources({
        kind: (filterKind as SpeakingSharedResourceKind) || undefined,
      });
      setItems(data ?? []);
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setLoading(false);
    }
  }, [filterKind]);

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
    setUploading(true);
    try {
      const created = await adminUploadSpeakingSharedResource({
        file: uploadFile,
        kind: uploadForm.kind,
        title: uploadForm.title.trim(),
        professionId: uploadForm.professionId.trim() || null,
      });
      setToast({ variant: 'success', message: `Uploaded "${created.title}" as Draft.` });
      setUploadOpen(false);
      setUploadFile(null);
      setUploadForm({ kind: 'WarmUpQuestions', title: '', professionId: '' });
      if (fileRef.current) fileRef.current.value = '';
      await reload();
    } catch (err) {
      setToast({ variant: 'error', message: (err as Error).message });
    } finally {
      setUploading(false);
    }
  }

  async function handlePublish(row: SpeakingSharedResourceDto) {
    setBusyId(row.id);
    try {
      await adminPublishSpeakingSharedResource(row.id);
      setToast({ variant: 'success', message: `Published "${row.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  async function handleArchive(row: SpeakingSharedResourceDto) {
    if (!confirm(`Archive "${row.title}"?`)) return;
    setBusyId(row.id);
    try {
      await adminArchiveSpeakingSharedResource(row.id);
      setToast({ variant: 'success', message: `Archived "${row.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  async function handleDelete(row: SpeakingSharedResourceDto) {
    if (!confirm(`Soft-delete "${row.title}"?`)) return;
    setBusyId(row.id);
    try {
      await adminDeleteSpeakingSharedResource(row.id);
      setToast({ variant: 'success', message: `Removed "${row.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  async function handleForceDelete(row: SpeakingSharedResourceDto) {
    if (!confirm(`Permanently delete "${row.title}"? This removes it for good and cannot be undone.`)) return;
    setBusyId(row.id);
    try {
      await adminForceDeleteSpeakingSharedResource(row.id);
      setToast({ variant: 'success', message: `Permanently deleted "${row.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  const filtersNode = (
    <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
      <Select
        label="Kind"
        value={filterKind}
        onChange={(e) => setFilterKind(e.target.value)}
        options={[{ value: '', label: 'All kinds' }, ...KIND_OPTIONS]}
      />
      <div className="flex items-end">
        <Button variant="outline" onClick={() => void reload()}>
          <RotateCcw className="h-4 w-4 mr-1" /> Refresh
        </Button>
      </div>
      <div className="flex items-end justify-end text-xs text-admin-fg-muted">
        {items.length} resource(s)
      </div>
    </div>
  );

  return (
    <AdminCatalogLayout
      title="Speaking shared resources"
      description="Manage Warm-up Questions and Assessment Criteria PDFs that apply to every Speaking practice session for a profession."
      breadcrumbs={BREADCRUMBS}
      eyebrow="CMS"
      hideViewModeToggle
      filters={filtersNode}
      actions={
        <Button onClick={() => setUploadOpen(true)}>
          <Plus className="h-4 w-4 mr-1" /> Upload PDF
        </Button>
      }
    >
      {loading ? (
        <div className="col-span-full space-y-3">
          {[1, 2, 3].map((i) => <Skeleton key={i} className="h-20" />)}
        </div>
      ) : items.length === 0 ? (
        <div className="col-span-full">
          <EmptyState
            illustration={<Sparkles />}
            title="No shared resources yet"
            description="Click 'Upload PDF' to add Warm-up Questions or Assessment Criteria."
          />
        </div>
      ) : (
        <div className="col-span-full space-y-3">
          {items.map((row) => (
            <Card key={row.id}>
              <CardContent className="p-4">
                <div className="flex items-start justify-between flex-wrap gap-3">
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2 flex-wrap mb-1">
                      <h3 className="font-semibold truncate text-admin-fg-strong">{row.title}</h3>
                      <Badge variant={
                        row.status === 'Published' ? 'success'
                          : row.status === 'Draft' ? 'default'
                          : row.status === 'Archived' ? 'warning' : 'default'
                      } intensity="tinted">{row.status}</Badge>
                      <Badge variant="default" intensity="tinted">{row.kind}</Badge>
                      {row.professionId
                        ? <Badge variant="default" intensity="tinted">{row.professionId}</Badge>
                        : <Badge variant="default" intensity="tinted">all professions</Badge>}
                    </div>
                    <p className="text-xs text-admin-fg-muted mt-1">
                      {row.media?.originalFilename ?? 'no file'}
                      {row.media?.sizeBytes ? ` - ${(row.media.sizeBytes / 1024 / 1024).toFixed(2)} MB` : ''}
                      {' - '}Updated {new Date(row.updatedAt).toLocaleDateString()}
                    </p>
                  </div>
                  <div className="flex items-center gap-2 flex-wrap">
                    {row.status !== 'Published' && row.status !== 'Archived' ? (
                      <Button size="sm" onClick={() => void handlePublish(row)} disabled={busyId === row.id}>Publish</Button>
                    ) : null}
                    {row.status === 'Published' ? (
                      <Button size="sm" variant="outline" onClick={() => void handleArchive(row)} disabled={busyId === row.id}>Archive</Button>
                    ) : null}
                    <Button size="sm" variant="ghost" onClick={() => void handleDelete(row)} disabled={busyId === row.id}>Delete</Button>
                    {row.status === 'Archived' ? (
                      <Button size="sm" variant="ghost" className="text-red-600" onClick={() => void handleForceDelete(row)} disabled={busyId === row.id}>Force delete</Button>
                    ) : null}
                  </div>
                </div>
              </CardContent>
            </Card>
          ))}
        </div>
      )}

      {uploadOpen ? (
        <Modal open={uploadOpen} onClose={() => setUploadOpen(false)} title="Upload Speaking shared resource">
          <form className="space-y-3" onSubmit={(e) => void handleUpload(e)}>
            <div>
              <label className="block text-sm font-medium mb-1 text-admin-fg-strong">PDF file (max 50 MB)</label>
              <input
                ref={fileRef}
                type="file"
                accept=".pdf,application/pdf"
                onChange={(e) => setUploadFile(e.target.files?.[0] ?? null)}
                className="w-full text-sm"
              />
              {uploadFile ? (
                <p className="text-xs text-admin-fg-muted mt-1">
                  {uploadFile.name} - {(uploadFile.size / 1024 / 1024).toFixed(2)} MB
                </p>
              ) : null}
            </div>
            <Select
              label="Kind"
              value={uploadForm.kind}
              onChange={(e) => setUploadForm({ ...uploadForm, kind: e.target.value as SpeakingSharedResourceKind })}
              options={KIND_OPTIONS}
            />
            <Input
              label="Title"
              value={uploadForm.title}
              onChange={(e) => setUploadForm({ ...uploadForm, title: e.target.value })}
              placeholder='e.g. "Speaking Warm-up Questions v2"'
              required
            />
            <Input
              label="Profession (optional - blank = all)"
              value={uploadForm.professionId}
              onChange={(e) => setUploadForm({ ...uploadForm, professionId: e.target.value })}
              placeholder="e.g. medicine, nursing"
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
    </AdminCatalogLayout>
  );
}
