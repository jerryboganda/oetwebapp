'use client';

import { use, useCallback, useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, CloudUpload, Loader2, Trash2 } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { ReadingStructureEditor } from '@/components/domain/ReadingStructureEditor';
import { DEFAULT_CONTENT_SOURCE_PROVENANCE } from '@/lib/content-upload-defaults';
import {
  attachPaperAsset,
  getContentPaper,
  getRequiredRoles,
  publishContentPaper,
  removePaperAsset,
  updateContentPaper,
  uploadFileChunked,
  type ContentPaperAssetDto,
  type ContentPaperDto,
  type PaperAssetRole,
} from '@/lib/content-upload-api';

type PageStatus = 'loading' | 'success' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

const ROLE_OPTIONS: { value: PaperAssetRole; label: string; accept: string }[] = [
  { value: 'Audio', label: 'Audio (MP3)', accept: 'audio/mpeg,audio/mp4,.mp3,.m4a' },
  { value: 'QuestionPaper', label: 'Question Paper (PDF)', accept: 'application/pdf,.pdf' },
  { value: 'AudioScript', label: 'Audio Script (PDF)', accept: 'application/pdf,.pdf' },
  { value: 'AnswerKey', label: 'Answer Key (PDF)', accept: 'application/pdf,.pdf' },
  { value: 'CaseNotes', label: 'Case Notes (PDF)', accept: 'application/pdf,.pdf' },
  { value: 'ModelAnswer', label: 'Model Answer (PDF)', accept: 'application/pdf,.pdf' },
  { value: 'RoleCard', label: 'Role Card (PDF)', accept: 'application/pdf,.pdf' },
  { value: 'AssessmentCriteria', label: 'Assessment Criteria (PDF)', accept: 'application/pdf,.pdf' },
  { value: 'WarmUpQuestions', label: 'Warm-up Questions (PDF)', accept: 'application/pdf,.pdf' },
  { value: 'Supplementary', label: 'Supplementary', accept: 'application/pdf,.pdf' },
];

export default function ContentPaperEditorPage({ params }: { params: Promise<{ paperId: string }> }) {
  const { paperId } = use(params);
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [requiredRoles, setRequiredRoles] = useState<PaperAssetRole[]>([]);
  const [toast, setToast] = useState<ToastState>(null);
  const [saving, setSaving] = useState(false);
  const [publishing, setPublishing] = useState(false);
  const requestSeqRef = useRef(0);

  const [uploadRole, setUploadRole] = useState<PaperAssetRole>('QuestionPaper');
  const [uploadPart, setUploadPart] = useState('');
  const [uploadProgress, setUploadProgress] = useState<number | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const updatePaper = useCallback((updater: (current: ContentPaperDto) => ContentPaperDto) => {
    setPaper((current) => (current ? updater(current) : current));
  }, []);

  const load = useCallback(async () => {
    const requestSeq = ++requestSeqRef.current;
    setStatus('loading');
    setPaper(null);
    setRequiredRoles([]);
    try {
      const p = await getContentPaper(paperId);
      if (requestSeq !== requestSeqRef.current) return;
      setPaper(p);
      const req = await getRequiredRoles(p.subtestCode);
      if (requestSeq !== requestSeqRef.current) return;
      setRequiredRoles(req.required);
      setStatus('success');
    } catch (e) {
      if (requestSeq !== requestSeqRef.current) return;
      setStatus('error');
      setToast({ variant: 'error', message: `${(e as Error).message}` });
    }
  }, [paperId]);

  useEffect(() => {
    void load();
  }, [load]);

  const saveMetadata = async () => {
    if (!paper) return;
    setSaving(true);
    try {
      const updated = await updateContentPaper(paper.id, {
        title: paper.title,
        difficulty: paper.difficulty,
        estimatedDurationMinutes: paper.estimatedDurationMinutes,
        appliesToAllProfessions: paper.appliesToAllProfessions,
        professionId: paper.professionId,
        cardType: paper.cardType,
        letterType: paper.letterType,
        priority: paper.priority,
        tagsCsv: paper.tagsCsv,
        sourceProvenance: paper.sourceProvenance,
      });
      setPaper(updated);
      setToast({ variant: 'success', message: 'Saved.' });
    } catch (e) {
      setToast({ variant: 'error', message: `Save failed: ${(e as Error).message}` });
    } finally { setSaving(false); }
  };

  const uploadFile = async (file: File) => {
    setUploadProgress(0);
    try {
      const result = await uploadFileChunked(file, uploadRole, (pct) => setUploadProgress(pct));
      await attachPaperAsset(paperId, {
        role: uploadRole,
        mediaAssetId: result.mediaAssetId,
        part: uploadPart || null,
        displayOrder: (paper?.assets?.length ?? 0),
        makePrimary: true,
      });
      setToast({ variant: 'success', message: result.deduplicated ? 'Attached (deduplicated).' : 'Uploaded + attached.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: `Upload failed: ${(e as Error).message}` });
    } finally {
      setUploadProgress(null);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  };

  const removeAsset = async (assetId: string) => {
    try {
      await removePaperAsset(paperId, assetId);
      setToast({ variant: 'success', message: 'Asset removed.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: `${(e as Error).message}` });
    }
  };

  const publish = async () => {
    setPublishing(true);
    try {
      await publishContentPaper(paperId);
      setToast({ variant: 'success', message: 'Paper published.' });
      await load();
    } catch (e) {
      const detail = (e as Error & { detail?: { error?: string } }).detail;
      setToast({ variant: 'error', message: detail?.error ?? (e as Error).message });
    } finally { setPublishing(false); }
  };

  if (!isAuthenticated || role !== 'admin') {
    return <AdminRouteWorkspace><p className="text-sm text-muted">Admin access required.</p></AdminRouteWorkspace>;
  }

  const missingRoles = paper
    ? requiredRoles.filter((r) => !paper.assets?.some((a) => a.role === r && a.isPrimary))
    : [];

  return (
    <AdminRouteWorkspace>
      <Link href="/admin/content-papers" className="inline-flex items-center gap-2 text-sm text-muted hover:text-navy">
        <ArrowLeft className="w-4 h-4" /> Back to papers
      </Link>

      <AsyncStateWrapper status={status}>
        {paper && (
          <>
            <AdminRouteSectionHeader
              icon={<CloudUpload className="w-6 h-6" />}
              title={paper.title}
              description={`${paper.subtestCode.toUpperCase()} · ${paper.appliesToAllProfessions ? 'All professions' : (paper.professionId ?? '—')} · ${paper.status}`}
            />

            {missingRoles.length > 0 && paper.status !== 'Published' && (
              <InlineAlert variant="warning">
                Missing required roles for {paper.subtestCode}: {missingRoles.join(', ')}. Publish is blocked until all required roles have a primary asset.
              </InlineAlert>
            )}

            <AdminRoutePanel title="Metadata">
              <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
                <Input label="Title" value={paper.title} onChange={(e) => updatePaper((current) => ({ ...current, title: e.target.value }))} />
                <Input label="Difficulty" value={paper.difficulty} onChange={(e) => updatePaper((current) => ({ ...current, difficulty: e.target.value }))} />
                <Input
                  type="number"
                  label="Estimated duration (minutes)"
                  value={paper.estimatedDurationMinutes}
                  min={0}
                  onChange={(e) => {
                    const next = e.target.value;
                    if (next === '') return;
                    const parsed = Number(next);
                    if (!Number.isFinite(parsed)) return;
                    updatePaper((current) => ({ ...current, estimatedDurationMinutes: parsed }));
                  }}
                />
                <Input
                  type="number"
                  label="Priority"
                  value={paper.priority}
                  min={0}
                  onChange={(e) => {
                    const next = e.target.value;
                    if (next === '') return;
                    const parsed = Number(next);
                    if (!Number.isFinite(parsed)) return;
                    updatePaper((current) => ({ ...current, priority: parsed }));
                  }}
                />
                <label className="flex items-center gap-2 lg:col-span-2">
                  <input type="checkbox" checked={paper.appliesToAllProfessions}
                    onChange={(e) => updatePaper((current) => ({ ...current, appliesToAllProfessions: e.target.checked, professionId: e.target.checked ? null : current.professionId }))} />
                  Applies to all professions
                </label>
                {!paper.appliesToAllProfessions && (
                  <Input label="Profession ID" value={paper.professionId ?? ''}
                    onChange={(e) => updatePaper((current) => ({ ...current, professionId: e.target.value || null }))} />
                )}
                {paper.subtestCode === 'writing' && (
                  <Input label="Letter type" value={paper.letterType ?? ''}
                    onChange={(e) => updatePaper((current) => ({ ...current, letterType: e.target.value || null }))}
                    placeholder="routine_referral | urgent_referral | transfer_letter | …" />
                )}
                {paper.subtestCode === 'speaking' && (
                  <Input label="Card type" value={paper.cardType ?? ''}
                    onChange={(e) => updatePaper((current) => ({ ...current, cardType: e.target.value || null }))}
                    placeholder="already_known_pt | examination | first_visit_emergency | …" />
                )}
                <Input label="Tags CSV" value={paper.tagsCsv}
                  onChange={(e) => updatePaper((current) => ({ ...current, tagsCsv: e.target.value }))}
                  placeholder="dermatology,acne" />
                <Input
                  label="Source provenance (required to publish)"
                  value={paper.sourceProvenance ?? ''}
                  onChange={(e) => updatePaper((current) => ({ ...current, sourceProvenance: e.target.value }))}
                  placeholder={DEFAULT_CONTENT_SOURCE_PROVENANCE} />
              </div>
              <div className="flex gap-3 mt-4">
                <Button variant="primary" onClick={saveMetadata} loading={saving}>Save metadata</Button>
                <Button variant="secondary" onClick={publish} loading={publishing}
                  disabled={missingRoles.length > 0 || !paper.sourceProvenance || paper.status === 'Published'}>
                  Publish
                </Button>
              </div>
            </AdminRoutePanel>

            <AdminRoutePanel title="Assets">
              <div className="grid grid-cols-1 md:grid-cols-4 gap-3 mb-4 items-end">
                <Select
                  label="Role"
                  value={uploadRole}
                  onChange={(e) => setUploadRole(e.target.value as PaperAssetRole)}
                  options={ROLE_OPTIONS.map((r) => ({ value: r.value, label: r.label }))}
                />
                <Input label="Part (optional)" value={uploadPart} onChange={(e) => setUploadPart(e.target.value)} placeholder='A | B+C | Section1' />
                <div className="md:col-span-2">
                  <label className="block text-sm font-medium mb-1">File</label>
                  <input
                    ref={fileInputRef}
                    type="file"
                    accept={ROLE_OPTIONS.find((r) => r.value === uploadRole)?.accept ?? ''}
                    onChange={(e) => {
                      const f = e.target.files?.[0];
                      if (f) void uploadFile(f);
                    }}
                    disabled={uploadProgress !== null}
                    className="block w-full text-sm"
                  />
                  {uploadProgress !== null && (
                    <div className="mt-2 flex items-center gap-2 text-sm text-muted">
                      <Loader2 className="w-4 h-4 animate-spin" />
                      Uploading… {Math.round(uploadProgress * 100)}%
                    </div>
                  )}
                </div>
              </div>

              <AssetList assets={paper.assets ?? []} onRemove={removeAsset} />
            </AdminRoutePanel>

            {paper.subtestCode === 'reading' && (
              <ReadingStructureEditor paperId={paper.id} />
            )}
          </>
        )}
      </AsyncStateWrapper>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}

function AssetList({ assets, onRemove }: { assets: ContentPaperAssetDto[]; onRemove: (id: string) => void }) {
  if (assets.length === 0) {
    return <p className="text-sm text-muted">No assets yet. Upload a file above.</p>;
  }
  return (
    <ul className="divide-y divide-gray-100">
      {assets.map((a) => (
        <li key={a.id} className="py-3 flex items-center justify-between gap-4">
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              <Badge variant="info">{a.role}</Badge>
              {a.part && <Badge variant="muted">Part {a.part}</Badge>}
              {a.isPrimary && <Badge variant="success">Primary</Badge>}
              <span className="font-medium truncate">{a.media?.originalFilename ?? '(no file)'}</span>
            </div>
            <div className="text-xs text-muted mt-1">
              {a.media?.mediaKind} · {a.media ? formatBytes(a.media.sizeBytes) : '—'}
              {a.media?.sha256 && ` · SHA ${a.media.sha256.slice(0, 10)}…`}
            </div>
          </div>
          <Button variant="ghost" size="sm" onClick={() => onRemove(a.id)}>
            <Trash2 className="w-4 h-4" />
          </Button>
        </li>
      ))}
    </ul>
  );
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  if (n < 1024 * 1024 * 1024) return `${(n / 1024 / 1024).toFixed(1)} MB`;
  return `${(n / 1024 / 1024 / 1024).toFixed(2)} GB`;
}
