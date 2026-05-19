'use client';

import { use, useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  ArrowLeft,
  CheckCircle2,
  FileText,
  Mic2,
  Trash2,
  Upload,
} from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { Toast } from '@/components/ui/alert';
import { SpeakingStructureEditor } from '@/components/domain/SpeakingStructureEditor';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  getContentPaper,
  attachPaperAsset,
  removePaperAsset,
  uploadFileChunked,
  type ContentPaperDto,
  type PaperAssetRole,
} from '@/lib/content-upload-api';
import {
  adminArchiveSpeakingPaper,
  adminPublishPaperWithWarnings,
} from '@/lib/api';

type Tab = 'structure' | 'assets' | 'publish';
type ToastState = { variant: 'success' | 'error' | 'warning'; message: string } | null;

const SPEAKING_ROLES: { role: PaperAssetRole; label: string; required: boolean }[] = [
  { role: 'RoleCard', label: 'Role card', required: true },
  { role: 'AssessmentCriteria', label: 'Assessment criteria', required: true },
  { role: 'WarmUpQuestions', label: 'Warm-up questions', required: false },
  { role: 'Audio', label: 'Example audio (optional)', required: false },
  { role: 'Supplementary', label: 'Supplementary', required: false },
];

export default function AdminSpeakingPaperWorkspacePage({
  params,
}: {
  params: Promise<{ paperId: string }>;
}) {
  const { paperId } = use(params);
  const router = useRouter();
  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const canPublish = hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);

  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [tab, setTab] = useState<Tab>('structure');
  const [toast, setToast] = useState<ToastState>(null);
  const [uploadingRole, setUploadingRole] = useState<PaperAssetRole | null>(null);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [publishing, setPublishing] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await getContentPaper(paperId);
      setPaper(data);
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to load paper.' });
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => {
    queueMicrotask(() => void load());
  }, [load]);

  const handleUpload = async (role: PaperAssetRole, file: File | null) => {
    if (!file || !canWrite || !paper) return;
    setUploadingRole(role);
    setUploadProgress(0);
    try {
      const upload = await uploadFileChunked(file, role, (p) => setUploadProgress(p));
      await attachPaperAsset(paper.id, {
        role,
        mediaAssetId: upload.mediaAssetId,
        displayOrder: (paper.assets ?? []).filter((a) => a.role === role).length,
        makePrimary: true,
        title: file.name,
      });
      setToast({
        variant: upload.deduplicated ? 'warning' : 'success',
        message: upload.deduplicated ? `Attached (deduplicated existing file).` : 'Asset uploaded.',
      });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Upload failed.' });
    } finally {
      setUploadingRole(null);
      setUploadProgress(0);
    }
  };

  const handleRemoveAsset = async (assetId: string) => {
    if (!canWrite || !paper) return;
    if (!confirm('Remove this asset from the paper?')) return;
    try {
      await removePaperAsset(paper.id, assetId);
      setToast({ variant: 'success', message: 'Asset removed.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Remove failed.' });
    }
  };

  const handlePublish = async () => {
    if (!canPublish || !paper) return;
    if (!confirm('Publish this speaking paper? Soft publish gate — warnings are allowed.')) return;
    setPublishing(true);
    try {
      const res = await adminPublishPaperWithWarnings(paper.id);
      const msg = res.warnings && res.warnings.length > 0
        ? `Published with ${res.warnings.length} warning(s).`
        : 'Published.';
      setToast({ variant: res.warnings?.length ? 'warning' : 'success', message: msg });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Publish failed.' });
    } finally {
      setPublishing(false);
    }
  };

  const handleArchive = async () => {
    if (!canWrite || !paper) return;
    if (!confirm('Archive this speaking paper?')) return;
    try {
      await adminArchiveSpeakingPaper(paper.id);
      setToast({ variant: 'success', message: 'Paper archived.' });
      router.push('/admin/content/speaking');
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Archive failed.' });
    }
  };

  return (
    <AdminRouteWorkspace role="main" aria-label="Speaking workspace">
      <AdminRouteHero
        eyebrow={paper ? `${paper.subtestCode} · ${paper.status}` : 'Speaking'}
        icon={Mic2}
        accent="navy"
        title={paper?.title ?? 'Loading…'}
        description={paper ? `Difficulty: ${paper.difficulty} · Slug: ${paper.slug}` : 'Loading speaking paper…'}
        aside={(
          <div className="flex flex-wrap gap-2">
            <Button variant="ghost" asChild>
              <Link href="/admin/content/speaking">
                <ArrowLeft className="mr-1 h-4 w-4" /> Back to list
              </Link>
            </Button>
          </div>
        )}
      />

      <div className="flex gap-2 border-b border-border">
        {(['structure', 'assets', 'publish'] as Tab[]).map((t) => (
          <button
            key={t}
            type="button"
            onClick={() => setTab(t)}
            className={`px-4 py-2 text-sm font-medium capitalize ${
              tab === t ? 'border-b-2 border-primary text-primary' : 'text-muted hover:text-foreground'
            }`}
          >
            {t}
          </button>
        ))}
      </div>

      {loading ? (
        <Skeleton className="h-64 w-full rounded-2xl" />
      ) : !paper ? (
        <Card className="p-8 text-center text-sm text-muted">Paper not found.</Card>
      ) : tab === 'structure' ? (
        <SpeakingStructureEditor paperId={paper.id} />
      ) : tab === 'assets' ? (
        <AdminRoutePanel
          title="Assets"
          description="Speaking papers may attach a role card, assessment criteria, and warm-up questions as files."
        >
          <div className="space-y-4">
            {SPEAKING_ROLES.map(({ role, label, required }) => {
              const existing = (paper.assets ?? []).filter((a) => a.role === role);
              return (
                <div key={role} className="rounded-2xl border border-border p-4">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div>
                      <div className="text-sm font-semibold">
                        {label}{' '}
                        {required && <Badge variant="warning">Required</Badge>}{' '}
                        {existing.length > 0 && <Badge variant="success">{existing.length}</Badge>}
                      </div>
                      <div className="text-xs text-muted">role: {role}</div>
                    </div>
                    {canWrite && (
                      <label className="inline-flex cursor-pointer items-center gap-2 rounded-lg border border-border bg-background-light px-3 py-1.5 text-xs hover:bg-background">
                        <Upload className="h-3.5 w-3.5" /> Upload
                        <input
                          type="file"
                          className="sr-only"
                          disabled={uploadingRole !== null}
                          onChange={(e) => void handleUpload(role, e.target.files?.[0] ?? null)}
                        />
                      </label>
                    )}
                  </div>
                  {uploadingRole === role && (
                    <div className="mt-2 h-1 w-full overflow-hidden rounded-full bg-background">
                      <div
                        className="h-full bg-primary transition-all"
                        style={{ width: `${Math.round(uploadProgress * 100)}%` }}
                      />
                    </div>
                  )}
                  {existing.length > 0 && (
                    <ul className="mt-3 space-y-1 text-xs">
                      {existing.map((a) => (
                        <li
                          key={a.id}
                          className="flex items-center justify-between rounded-lg bg-background-light px-3 py-2"
                        >
                          <span className="flex items-center gap-2">
                            <FileText className="h-3.5 w-3.5" />
                            <span className="truncate">{a.title ?? a.media?.originalFilename ?? a.mediaAssetId}</span>
                            {a.isPrimary && <Badge variant="muted">primary</Badge>}
                          </span>
                          {canWrite && (
                            <button
                              type="button"
                              onClick={() => void handleRemoveAsset(a.id)}
                              className="text-danger hover:text-danger/80"
                              aria-label={`Remove asset ${a.title ?? a.id}`}
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </button>
                          )}
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              );
            })}
          </div>
        </AdminRoutePanel>
      ) : (
        <AdminRoutePanel
          title="Publish"
          description="Speaking publish gate is soft — warnings are surfaced but do not block."
        >
          <Card className="space-y-4 p-4">
            <div className="text-sm">
              Current status: <Badge variant={paper.status === 'Published' ? 'success' : 'muted'}>{paper.status}</Badge>
            </div>
            <div className="flex flex-wrap gap-2">
              {canPublish && paper.status !== 'Published' && paper.status !== 'Archived' && (
                <Button onClick={() => void handlePublish()} disabled={publishing} className="inline-flex items-center gap-2">
                  <CheckCircle2 className="h-4 w-4" /> {publishing ? 'Publishing…' : 'Publish (soft gate)'}
                </Button>
              )}
              {canWrite && paper.status !== 'Archived' && (
                <Button variant="outline" onClick={() => void handleArchive()}>
                  Archive
                </Button>
              )}
            </div>
          </Card>
        </AdminRoutePanel>
      )}

      {toast && (
        <Toast
          variant={toast.variant === 'error' ? 'error' : toast.variant === 'warning' ? 'warning' : 'success'}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </AdminRouteWorkspace>
  );
}
