'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { Loader2, Upload, RefreshCw, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Select } from '@/components/ui/form-controls';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import {
  attachPaperAsset,
  getContentPaper,
  removePaperAsset,
  uploadFileChunked,
  type ContentPaperDto,
  type PaperAssetRole,
} from '@/lib/content-upload-api';

interface Props {
  paperId: string;
  onToast: (variant: 'success' | 'error', message: string) => void;
}

const ROLES: { value: PaperAssetRole; label: string }[] = [
  { value: 'Audio', label: 'Audio (per-part: A1/A2/B/C1/C2)' },
  { value: 'QuestionPaper', label: 'QuestionPaper (PDF)' },
  { value: 'AudioScript', label: 'AudioScript (PDF)' },
  { value: 'AnswerKey', label: 'AnswerKey (PDF)' },
];

const PARTS = [
  { value: '', label: '(no part)' },
  { value: 'A1', label: 'A1' },
  { value: 'A2', label: 'A2' },
  { value: 'B', label: 'B' },
  { value: 'C1', label: 'C1' },
  { value: 'C2', label: 'C2' },
];

export function AssetsTab({ paperId, onToast }: Props) {
  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [role, setRole] = useState<PaperAssetRole>('Audio');
  const [part, setPart] = useState('');
  const [uploading, setUploading] = useState(false);
  const [progress, setProgress] = useState(0);
  const fileRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const p = await getContentPaper(paperId);
      setPaper(p);
    } catch (e) {
      onToast('error', `Load paper failed: ${(e as Error).message}`);
    } finally {
      setLoading(false);
    }
  }, [onToast, paperId]);

  useEffect(() => {
    void load();
  }, [load]);

  const upload = useCallback(async () => {
    const f = fileRef.current?.files?.[0];
    if (!f) {
      onToast('error', 'Pick a file first.');
      return;
    }
    setUploading(true);
    setProgress(0);
    try {
      const result = await uploadFileChunked(f, role, (p) => setProgress(p));
      const displayOrder = (paper?.assets?.filter((a) => a.role === role).length ?? 0) + 1;
      await attachPaperAsset(paperId, {
        role,
        mediaAssetId: result.mediaAssetId,
        part: part || null,
        title: f.name,
        displayOrder,
        makePrimary: true,
      });
      onToast('success', `Uploaded ${f.name} as ${role}${part ? ` (${part})` : ''}.`);
      if (fileRef.current) fileRef.current.value = '';
      await load();
    } catch (e) {
      onToast('error', `Upload failed: ${(e as Error).message}`);
    } finally {
      setUploading(false);
      setProgress(0);
    }
  }, [load, onToast, paper, paperId, part, role]);

  const remove = useCallback(
    async (assetId: string) => {
      if (!confirm('Remove this asset attachment?')) return;
      try {
        await removePaperAsset(paperId, assetId);
        onToast('success', 'Asset removed.');
        await load();
      } catch (e) {
        onToast('error', `Remove failed: ${(e as Error).message}`);
      }
    },
    [load, onToast, paperId],
  );

  if (loading) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted">
        <Loader2 className="h-4 w-4 animate-spin" /> Loading…
      </div>
    );
  }

  const assets = paper?.assets ?? [];

  return (
    <div className="space-y-4">
      <AdminRoutePanel title="Upload asset">
        <div className="grid gap-2 md:grid-cols-4">
          <Select
            label="Role"
            value={role}
            onChange={(e) => setRole(e.target.value as PaperAssetRole)}
            options={ROLES}
          />
          <Select
            label="Part (Audio only)"
            value={part}
            onChange={(e) => setPart(e.target.value)}
            options={PARTS}
          />
          <div className="md:col-span-2">
            <label className="mb-1 block text-sm font-semibold text-navy">File</label>
            <input
              ref={fileRef}
              type="file"
              className="block w-full rounded-2xl border border-border bg-background-light px-4 py-2 text-sm"
            />
          </div>
        </div>
        <div className="mt-3 flex items-center gap-3">
          <Button variant="primary" onClick={() => void upload()} disabled={uploading}>
            {uploading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Upload className="h-4 w-4" />}
            Upload & attach
          </Button>
          {uploading && (
            <div className="text-xs text-muted">{Math.round(progress * 100)}%</div>
          )}
          <Button variant="ghost" onClick={() => void load()}>
            <RefreshCw className="h-3 w-3" /> Refresh
          </Button>
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel title={`Attached assets (${assets.length})`}>
        {assets.length === 0 ? (
          <p className="text-sm text-muted">No assets attached.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border text-left text-xs uppercase tracking-wide text-muted">
                  <th className="py-2">Role</th>
                  <th className="py-2">Part</th>
                  <th className="py-2">File</th>
                  <th className="py-2">Size</th>
                  <th className="py-2">Primary</th>
                  <th className="py-2"></th>
                </tr>
              </thead>
              <tbody>
                {assets.map((a) => (
                  <tr key={a.id} className="border-b border-border/50">
                    <td className="py-2"><Badge variant="info">{a.role}</Badge></td>
                    <td className="py-2 font-mono text-xs">{a.part ?? '—'}</td>
                    <td className="py-2">{a.media?.originalFilename ?? a.title ?? '—'}</td>
                    <td className="py-2 text-xs text-muted">
                      {a.media ? `${Math.round(a.media.sizeBytes / 1024)} KB` : '—'}
                    </td>
                    <td className="py-2">
                      {a.isPrimary ? <Badge variant="success">primary</Badge> : <Badge variant="muted">—</Badge>}
                    </td>
                    <td className="py-2 text-right">
                      <Button variant="ghost" size="sm" onClick={() => void remove(a.id)}>
                        <Trash2 className="h-3 w-3" /> Remove
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </AdminRoutePanel>
    </div>
  );
}
