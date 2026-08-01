'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { Image as ImageIcon, Plus, RotateCcw, Upload as UploadIcon } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Modal } from '@/components/ui/modal';
import { Input, Textarea } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';
import {
  adminListResultTemplates,
  adminUploadResultTemplate,
  adminUpdateResultTemplate,
  adminActivateResultTemplate,
  adminDeactivateResultTemplate,
  adminDeleteResultTemplate,
  adminForceDeleteResultTemplate,
  fetchAuthorizedObjectUrl,
  type ResultTemplateDto,
} from '@/lib/api';
import { titleFromFilename, slugifyFilename, uniqueSlug } from '@/lib/filename-utils';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

function AuthorizedTemplateImage({ mediaId, title }: { mediaId: string; title: string }) {
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    let objectUrl: string | null = null;
    let cancelled = false;
    fetchAuthorizedObjectUrl(`/v1/media/${encodeURIComponent(mediaId)}/content`)
      .then((nextUrl) => {
        objectUrl = nextUrl;
        if (!cancelled) setUrl(nextUrl);
      })
      .catch(() => {
        if (!cancelled) setUrl(null);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [mediaId]);

  if (!url) return <ImageIcon className="h-10 w-10 opacity-30" />;

  // eslint-disable-next-line @next/next/no-img-element
  return <img src={url} alt={title} className="w-full h-full object-cover" loading="lazy" />;
}

export default function AdminResultTemplatesPage() {
  const [items, setItems] = useState<ResultTemplateDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);
  const [filterProfession, setFilterProfession] = useState('');

  const [uploadOpen, setUploadOpen] = useState(false);
  const [uploadFiles, setUploadFiles] = useState<File[]>([]);
  const [uploadForm, setUploadForm] = useState({
    templateKey: '',
    title: '',
    description: '',
    professionId: '',
    sortOrder: 0,
  });
  const [uploading, setUploading] = useState(false);
  const fileRef = useRef<HTMLInputElement>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const data = await adminListResultTemplates(filterProfession.trim() || undefined);
      setItems(data ?? []);
    } catch (e) {
      setToast({ variant: 'error', message: `Failed to load templates: ${(e as Error).message}` });
    } finally {
      setLoading(false);
    }
  }, [filterProfession]);

  useEffect(() => { void reload(); }, [reload]);

  async function handleUpload(e?: React.FormEvent) {
    e?.preventDefault();
    if (uploadFiles.length === 0) {
      setToast({ variant: 'error', message: 'Pick an image file first (JPG, PNG, or WebP).' });
      return;
    }
    const isBatch = uploadFiles.length > 1;
    if (!isBatch) {
      if (!uploadForm.templateKey.trim()) {
        setToast({ variant: 'error', message: 'Template key is required (slug-like, unique).' });
        return;
      }
      if (!uploadForm.title.trim()) {
        setToast({ variant: 'error', message: 'Title is required.' });
        return;
      }
    }
    setUploading(true);
    try {
      if (isBatch) {
        const usedKeys = new Set<string>();
        const failures: string[] = [];
        for (const file of uploadFiles) {
          const key = uniqueSlug(slugifyFilename(file.name), usedKeys);
          try {
            await adminUploadResultTemplate({
              file,
              templateKey: key,
              title: titleFromFilename(file.name),
              description: uploadForm.description.trim() || null,
              professionId: uploadForm.professionId.trim() || null,
              sortOrder: uploadForm.sortOrder,
            });
          } catch (err) {
            failures.push(`${file.name} (${(err as Error).message})`);
          }
        }
        const addedCount = uploadFiles.length - failures.length;
        setToast(
          failures.length === 0
            ? { variant: 'success', message: `Uploaded ${addedCount} template${addedCount === 1 ? '' : 's'} (inactive).` }
            : { variant: 'error', message: `Uploaded ${addedCount} of ${uploadFiles.length}. Failed: ${failures.join('; ')}` },
        );
      } else {
        const created = await adminUploadResultTemplate({
          file: uploadFiles[0],
          templateKey: uploadForm.templateKey.trim(),
          title: uploadForm.title.trim(),
          description: uploadForm.description.trim() || null,
          professionId: uploadForm.professionId.trim() || null,
          sortOrder: uploadForm.sortOrder,
        });
        setToast({ variant: 'success', message: `Uploaded "${created.title}" (inactive).` });
      }
      setUploadOpen(false);
      setUploadFiles([]);
      setUploadForm({ templateKey: '', title: '', description: '', professionId: '', sortOrder: 0 });
      if (fileRef.current) fileRef.current.value = '';
      await reload();
    } catch (err) {
      setToast({ variant: 'error', message: (err as Error).message });
    } finally {
      setUploading(false);
    }
  }

  async function toggleActive(row: ResultTemplateDto) {
    setBusyId(row.id);
    try {
      if (row.isActive) await adminDeactivateResultTemplate(row.id);
      else await adminActivateResultTemplate(row.id);
      setToast({ variant: 'success', message: `${row.isActive ? 'Deactivated' : 'Activated'} "${row.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  async function handleDelete(row: ResultTemplateDto) {
    if (!confirm(`Permanently delete "${row.title}"? This cannot be undone.`)) return;
    setBusyId(row.id);
    try {
      await adminForceDeleteResultTemplate(row.id);
      setToast({ variant: 'success', message: `Deleted "${row.title}".` });
      await reload();
    } catch (e) {
      setToast({ variant: 'error', message: (e as Error).message });
    } finally {
      setBusyId(null);
    }
  }

  return (
    <AdminCatalogLayout
      title="Result Templates"
      description="Manage the score-report visual templates shown on learner mock-result pages. Upload JPG/PNG, activate the ones you want learners to see."
      eyebrow="CMS"
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Result Templates' },
      ]}
      actions={
        <Button onClick={() => setUploadOpen(true)} startIcon={<Plus className="h-4 w-4" />}>
          Upload image
        </Button>
      }
      filters={
        <div className="flex items-end gap-3 w-full">
          <div className="flex-1 max-w-xs">
            <Input
              label="Profession filter (optional)"
              value={filterProfession}
              onChange={(e) => setFilterProfession(e.target.value)}
              placeholder="e.g. medicine; blank = all"
            />
          </div>
          <Button variant="outline" onClick={() => void reload()} startIcon={<RotateCcw className="h-4 w-4" />}>
            Refresh
          </Button>
          <div className="ml-auto text-xs text-admin-fg-muted">{items.length} template(s)</div>
        </div>
      }
      hideViewModeToggle
      itemsClassName="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4"
    >
      {loading ? (
        <>{[1, 2, 3, 4, 5, 6].map((i) => <Skeleton key={i} className="h-56 rounded-admin-lg" />)}</>
      ) : items.length === 0 ? (
        <div className="col-span-full">
          <EmptyState
            icon={<ImageIcon className="h-10 w-10 text-admin-fg-muted" />}
            title="No templates yet"
            description="Click Upload image to add the first one."
            primaryAction={{ label: 'Upload image', onClick: () => setUploadOpen(true) }}
          />
        </div>
      ) : (
        <>
          {items.map((row) => {
            return (
              <Card key={row.id} className="overflow-hidden">
                <div className="aspect-[4/3] bg-admin-bg-subtle flex items-center justify-center overflow-hidden">
                  {row.media?.id ? (
                    <AuthorizedTemplateImage mediaId={row.media.id} title={row.title} />
                  ) : (
                    <ImageIcon className="h-10 w-10 opacity-30" />
                  )}
                </div>
                <CardContent className="space-y-2 pt-3">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <h3 className="font-semibold truncate text-admin-fg-strong">{row.title}</h3>
                      <p className="text-xs text-admin-fg-muted truncate">key: <code>{row.templateKey}</code></p>
                    </div>
                    <Badge variant={row.isActive ? 'success' : 'default'}>
                      {row.isActive ? 'active' : 'inactive'}
                    </Badge>
                  </div>
                  {row.description ? (
                    <p className="text-xs text-admin-fg-muted line-clamp-2">{row.description}</p>
                  ) : null}
                  <div className="flex items-center justify-between flex-wrap gap-2 pt-1">
                    <span className="text-xs text-admin-fg-muted">
                      {row.professionId ?? 'all'} · order {row.sortOrder}
                    </span>
                    <div className="flex gap-1">
                      <Button size="sm" variant={row.isActive ? 'outline' : 'primary'} disabled={busyId === row.id} onClick={() => void toggleActive(row)}>
                        {row.isActive ? 'Deactivate' : 'Activate'}
                      </Button>
                      <Button size="sm" variant="ghost" disabled={busyId === row.id} onClick={() => void handleDelete(row)}>Delete</Button>
                    </div>
                  </div>
                </CardContent>
              </Card>
            );
          })}
        </>
      )}

      {uploadOpen ? (
        <Modal open={uploadOpen} onClose={() => setUploadOpen(false)} title="Upload result-template image">
          <form className="space-y-3" onSubmit={(e) => void handleUpload(e)}>
            <div>
              <label className="block text-sm font-medium mb-1">
                Image(s) (JPG/PNG/WebP, max 10 MB each) — select multiple to upload them all at once
              </label>
              <input
                ref={fileRef}
                type="file"
                multiple
                accept=".jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp"
                onChange={(e) => setUploadFiles(Array.from(e.target.files ?? []))}
                className="w-full text-sm"
              />
              {uploadFiles.length === 1 ? (
                <p className="text-xs text-muted mt-1">
                  {uploadFiles[0].name} - {(uploadFiles[0].size / 1024 / 1024).toFixed(2)} MB
                </p>
              ) : null}
              {uploadFiles.length > 1 ? (
                <ul className="mt-1 max-h-28 space-y-0.5 overflow-y-auto text-xs text-muted list-disc pl-4">
                  {uploadFiles.map((f, i) => (
                    <li key={`${f.name}-${i}`}>{f.name} - {(f.size / 1024 / 1024).toFixed(2)} MB</li>
                  ))}
                </ul>
              ) : null}
            </div>
            {uploadFiles.length <= 1 ? (
              <>
                <Input
                  label="Template key (slug, unique)"
                  value={uploadForm.templateKey}
                  onChange={(e) => setUploadForm({ ...uploadForm, templateKey: e.target.value })}
                  placeholder='e.g. "ielts-style-band-chart"'
                  required
                />
                <Input
                  label="Title"
                  value={uploadForm.title}
                  onChange={(e) => setUploadForm({ ...uploadForm, title: e.target.value })}
                  placeholder='e.g. "Standard OET Score Report"'
                  required
                />
              </>
            ) : (
              <p className="text-xs text-muted">
                Uploading <span className="font-medium text-admin-fg">{uploadFiles.length} templates</span> — each will get
                its own key/title derived from its filename.
              </p>
            )}
            <Textarea
              label="Description (optional)"
              value={uploadForm.description}
              onChange={(e) => setUploadForm({ ...uploadForm, description: e.target.value })}
              rows={2}
            />
            <Input
              label="Profession (optional - blank = all)"
              value={uploadForm.professionId}
              onChange={(e) => setUploadForm({ ...uploadForm, professionId: e.target.value })}
              placeholder="e.g. medicine, nursing"
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
                {uploading ? 'Uploading...' : (
                  <><UploadIcon className="h-4 w-4 mr-1" />{uploadFiles.length > 1 ? `Upload ${uploadFiles.length}` : 'Upload'}</>
                )}
              </Button>
            </div>
          </form>
        </Modal>
      ) : null}

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </AdminCatalogLayout>
  );
}
