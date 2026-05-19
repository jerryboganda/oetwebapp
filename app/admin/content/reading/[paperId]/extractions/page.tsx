'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import {
  ArrowLeft,
  Check,
  FileSearch,
  Plus,
  RotateCcw,
  X,
} from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input, Textarea } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { Toast } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  adminReadingApproveExtraction,
  adminReadingCreateExtraction,
  adminReadingGetExtraction,
  adminReadingListExtractions,
  adminReadingRejectExtraction,
} from '@/lib/api';
import type {
  ReadingExtractionDraft,
  ReadingExtractionStatus,
} from '@/lib/types/admin/reading-authoring';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

function resolveParam(raw: string | string[] | undefined): string | null {
  if (!raw) return null;
  if (Array.isArray(raw)) return raw[0] ?? null;
  return raw;
}

function statusVariant(status: ReadingExtractionStatus): 'success' | 'muted' | 'warning' | 'danger' {
  if (status === 'Approved') return 'success';
  if (status === 'Rejected' || status === 'Failed') return 'danger';
  return 'warning';
}

export default function ReadingExtractionsPage() {
  const params = useParams();
  const paperId = resolveParam(params?.paperId as string | string[] | undefined);

  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [drafts, setDrafts] = useState<ReadingExtractionDraft[]>([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const [selected, setSelected] = useState<ReadingExtractionDraft | null>(null);
  const [rejectReason, setRejectReason] = useState('');
  const [mediaAssetId, setMediaAssetId] = useState('');

  const load = useCallback(async () => {
    if (!paperId) return;
    setLoading(true);
    try {
      const list = await adminReadingListExtractions(paperId);
      setDrafts(list);
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to load extraction queue.' });
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => {
    queueMicrotask(() => void load());
  }, [load]);

  const handleCreate = async () => {
    if (!paperId || !canWrite) return;
    setBusy(true);
    try {
      const draft = await adminReadingCreateExtraction(paperId, mediaAssetId.trim() || null);
      setToast({ variant: 'success', message: draft.isStub ? 'Stub extraction created (no media asset).' : 'Extraction draft created.' });
      setMediaAssetId('');
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Create extraction failed.' });
    } finally {
      setBusy(false);
    }
  };

  const openDetail = async (draft: ReadingExtractionDraft) => {
    if (!paperId) return;
    try {
      const full = await adminReadingGetExtraction(paperId, draft.id);
      setSelected(full);
      setRejectReason('');
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to load extraction.' });
    }
  };

  const handleApprove = async () => {
    if (!paperId || !selected || !canWrite) return;
    if (!confirm('Approve this extraction? The proposed manifest will be applied to the paper.')) return;
    setBusy(true);
    try {
      await adminReadingApproveExtraction(paperId, selected.id);
      setToast({ variant: 'success', message: 'Extraction approved.' });
      setSelected(null);
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Approve failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handleReject = async () => {
    if (!paperId || !selected || !canWrite) return;
    setBusy(true);
    try {
      await adminReadingRejectExtraction(paperId, selected.id, rejectReason || undefined);
      setToast({ variant: 'success', message: 'Extraction rejected.' });
      setSelected(null);
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Reject failed.' });
    } finally {
      setBusy(false);
    }
  };

  if (!paperId) {
    return (
      <AdminRouteWorkspace role="main">
        <Card className="p-8 text-center text-sm text-muted">Invalid paper id.</Card>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Reading extraction queue">
      <AdminRouteHero
        eyebrow="AI extraction"
        icon={FileSearch}
        accent="navy"
        title="Reading PDF extraction queue"
        description="AI-extracted reading structure proposals awaiting admin approval. Approving applies the manifest to the paper structure."
        aside={(
          <div className="flex flex-wrap gap-2">
            <Button variant="ghost" asChild>
              <Link href={`/admin/content/reading/${paperId}`}>
                <ArrowLeft className="mr-1 h-4 w-4" /> Back
              </Link>
            </Button>
            <Button variant="outline" size="sm" onClick={() => void load()} disabled={busy}>
              <RotateCcw className="h-4 w-4" />
            </Button>
          </div>
        )}
      />

      {canWrite && (
        <AdminRoutePanel>
          <div className="flex flex-wrap items-end gap-2">
            <div className="flex-1 min-w-[280px]">
              <Input
                label="Media asset id (optional — leave blank for stub)"
                value={mediaAssetId}
                onChange={(e) => setMediaAssetId(e.target.value)}
                placeholder="Upload the PDF via /admin/content/papers and paste its MediaAsset id here"
              />
            </div>
            <Button size="sm" onClick={() => void handleCreate()} disabled={busy}>
              <Plus className="mr-1 h-4 w-4" /> Request extraction
            </Button>
          </div>
          <p className="mt-2 text-xs text-muted">
            Note: this surface accepts an existing <code>MediaAssetId</code>. To run AI extraction on a new PDF,
            first upload the file using the standard content-upload flow under{' '}
            <Link href="/admin/content/papers" className="underline">Admin → Content → Papers</Link>, then paste the
            resulting media asset id here. With no id, a stub draft is created for manual review.
          </p>
        </AdminRoutePanel>
      )}

      {loading ? (
        <div className="space-y-2">
          {Array.from({ length: 3 }).map((_, i) => (<Skeleton key={i} className="h-16 rounded-xl" />))}
        </div>
      ) : drafts.length === 0 ? (
        <Card className="p-8 text-center text-sm text-muted">No extraction drafts for this paper.</Card>
      ) : (
        <Card className="overflow-hidden">
          <table className="min-w-full text-sm">
            <thead className="bg-background-light text-left text-xs uppercase tracking-[0.15em] text-muted">
              <tr>
                <th className="px-4 py-2">Status</th>
                <th className="px-4 py-2">Media</th>
                <th className="px-4 py-2">Created</th>
                <th className="px-4 py-2">Resolved</th>
                <th className="px-4 py-2 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {drafts.map((d) => (
                <tr key={d.id} className="border-t border-border hover:bg-background-light/40">
                  <td className="px-4 py-2">
                    <Badge variant={statusVariant(d.status)}>{d.status}</Badge>
                    {d.isStub && <Badge variant="muted" className="ml-1">stub</Badge>}
                  </td>
                  <td className="px-4 py-2 font-mono text-[10px]">{d.mediaAssetId ?? '—'}</td>
                  <td className="px-4 py-2 text-xs">{new Date(d.createdAt).toLocaleString()}</td>
                  <td className="px-4 py-2 text-xs">{d.resolvedAt ? new Date(d.resolvedAt).toLocaleString() : '—'}</td>
                  <td className="px-4 py-2 text-right">
                    <Button variant="ghost" size="sm" onClick={() => void openDetail(d)}>
                      View
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}

      {/* Detail panel */}
      {selected && (
        <Card className="p-4">
          <div className="mb-2 flex items-center justify-between">
            <div className="flex items-center gap-2 text-sm font-medium">
              <FileSearch className="h-4 w-4" /> Extraction detail
              <Badge variant={statusVariant(selected.status)}>{selected.status}</Badge>
              {selected.isStub && <Badge variant="muted">stub</Badge>}
            </div>
            <Button variant="ghost" size="sm" onClick={() => setSelected(null)}>
              Close
            </Button>
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <Textarea
              label="Proposed manifest (JSON)"
              value={selected.extractedManifestJson ?? ''}
              readOnly
              rows={16}
              spellCheck={false}
              className="font-mono text-xs"
            />
            <Textarea
              label="Raw AI response"
              value={selected.rawAiResponseJson ?? ''}
              readOnly
              rows={16}
              spellCheck={false}
              className="font-mono text-xs"
            />
          </div>
          {selected.notes && (
            <p className="mt-2 text-xs text-muted">Notes: {selected.notes}</p>
          )}
          {canWrite && selected.status === 'Pending' && (
            <div className="mt-3 space-y-2">
              <Input
                label="Reject reason (optional)"
                value={rejectReason}
                onChange={(e) => setRejectReason(e.target.value)}
              />
              <div className="flex flex-wrap justify-end gap-2">
                <Button size="sm" variant="outline" onClick={() => void handleReject()} disabled={busy}>
                  <X className="mr-1 h-4 w-4" /> Reject
                </Button>
                <Button size="sm" onClick={() => void handleApprove()} disabled={busy}>
                  <Check className="mr-1 h-4 w-4" /> Approve & apply
                </Button>
              </div>
            </div>
          )}
        </Card>
      )}

      {toast && (
        <Toast
          variant={toast.variant === 'error' ? 'error' : 'success'}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </AdminRouteWorkspace>
  );
}
