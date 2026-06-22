'use client';

import { use, useCallback, useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, CloudUpload, Eye, Loader2, Trash2 } from 'lucide-react';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { ReadingStructureEditor } from '@/components/domain/ReadingStructureEditor';
import { ListeningStructureEditor } from '@/components/domain/ListeningStructureEditor';
import { SpeakingStructureEditor } from '@/components/domain/SpeakingStructureEditor';
import { WritingStructureEditor } from '@/components/domain/WritingStructureEditor';
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

/**
 * Reads the current access tier ("free" or "premium") encoded in a paper's
 * tagsCsv. Defaults to "premium" — subscription required — when no access:*
 * token is present, matching the backend ContentEntitlementService gate.
 */
function readAccessTier(tagsCsv: string | null | undefined): 'free' | 'premium' {
  const tokens = (tagsCsv ?? '').split(',').map((t) => t.trim().toLowerCase());
  if (tokens.includes('access:free')) return 'free';
  return 'premium';
}

/**
 * Returns a new tagsCsv string with the access:* token replaced. Preserves all
 * other tags and their original casing/order. Strips empty entries.
 */
function writeAccessTier(tagsCsv: string | null | undefined, tier: 'free' | 'premium'): string {
  const kept = (tagsCsv ?? '')
    .split(',')
    .map((t) => t.trim())
    .filter((t) => t.length > 0 && !t.toLowerCase().startsWith('access:'));
  kept.push(`access:${tier}`);
  return kept.join(',');
}

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

/**
 * Part options for LISTENING Question-Paper PDFs. One slot per section: Part A
 * is its two consultations (A1, A2), Part B is a SINGLE booklet (the six MCQs
 * Q25–Q30 all reference it — no per-question PDFs), Part C is its two
 * presentations (C1, C2). Values map 1:1 to ContentPaperAsset.Part (uppercased;
 * resolved by `questionPaperByPart` in ListeningLearnerService). The part is
 * optional — nothing is compulsory. Audio is a single Part A file ("A"),
 * handled in the Audio tab.
 */
const LISTENING_PART_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'No part / unassigned' },
  { value: 'A1', label: 'Part A — Extract 1 (A1)' },
  { value: 'A2', label: 'Part A — Extract 2 (A2)' },
  { value: 'B', label: 'Part B — question booklet (Q25–30)' },
  { value: 'C1', label: 'Part C — C1 (Q31–36)' },
  { value: 'C2', label: 'Part C — C2 (Q37–42)' },
];

/** Content-type tabs for the Listening Assets card. Each tab is one asset role. */
const LISTENING_ASSET_TABS: { id: PaperAssetRole; label: string }[] = [
  { id: 'Audio', label: 'Audio' },
  { id: 'QuestionPaper', label: 'Question Papers' },
  { id: 'AudioScript', label: 'Audio Script' },
  { id: 'AnswerKey', label: 'Answer Key' },
  { id: 'Supplementary', label: 'Supplementary' },
];

/** Roles that have their own Listening tab; everything else lands in Supplementary. */
const LISTENING_PRIMARY_TAB_ROLES: PaperAssetRole[] = ['Audio', 'QuestionPaper', 'AudioScript', 'AnswerKey'];

/** Assets belonging to a given content-type tab (Supplementary = any other role). */
function listeningAssetsForTab(assets: ContentPaperAssetDto[], tabId: PaperAssetRole): ContentPaperAssetDto[] {
  if (tabId === 'Supplementary') {
    return assets.filter((a) => !LISTENING_PRIMARY_TAB_ROLES.includes(a.role));
  }
  return assets.filter((a) => a.role === tabId);
}

const WRITING_LETTER_TYPE_OPTIONS = [
  { value: '', label: 'Select letter type' },
  { value: 'routine_referral', label: 'Routine referral' },
  { value: 'urgent_referral', label: 'Urgent referral' },
  { value: 'non_medical_referral', label: 'Referral to non-medical professional' },
  { value: 'update_discharge', label: 'Update and discharge' },
  { value: 'update_referral_specialist_to_gp', label: 'Specialist update / referral to GP or dentist' },
  { value: 'transfer_letter', label: 'Transfer letter' },
];

export default function ContentPaperEditorPage({ params }: { params: Promise<{ paperId: string }> }) {
  const { paperId } = use(params);
  const { isAuthenticated, isLoading: isAdminLoading, role } = useAdminAuth();
  const { user, isLoading: isUserLoading } = useCurrentUser();
  const canReadContent = hasPermission(user?.adminPermissions, AdminPermission.ContentRead);
  const canWriteContent = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const canPublishContent = hasPermission(user?.adminPermissions, AdminPermission.ContentPublish);
  const canViewContent = canReadContent || canWriteContent || canPublishContent;
  const [status, setStatus] = useState<PageStatus>('loading');
  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [requiredRoles, setRequiredRoles] = useState<PaperAssetRole[]>([]);
  const [toast, setToast] = useState<ToastState>(null);
  const [saving, setSaving] = useState(false);
  const [publishing, setPublishing] = useState(false);

  const [uploadRole, setUploadRole] = useState<PaperAssetRole>('QuestionPaper');
  const [uploadPart, setUploadPart] = useState('');
  const [uploadProgress, setUploadProgress] = useState<number | null>(null);
  const [readingStructureVersion, setReadingStructureVersion] = useState(0);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    if (!canViewContent) return;
    setStatus('loading');
    try {
      const p = await getContentPaper(paperId);
      setPaper(p);
      if (p.subtestCode === 'writing') setUploadRole('CaseNotes');
      else if (p.subtestCode === 'listening') setUploadRole('Audio');
      const req = await getRequiredRoles(p.subtestCode);
      setRequiredRoles(req.required);
      setStatus('success');
    } catch (e) {
      setStatus('error');
      setToast({ variant: 'error', message: `${(e as Error).message}` });
    }
  }, [canViewContent, paperId]);

  useEffect(() => {
    if (!canViewContent) return;
    queueMicrotask(() => { void load(); });
  }, [canViewContent, load]);

  const saveMetadata = async () => {
    if (!paper || !canWriteContent) return;
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
    if (!canWriteContent) return;
    // Listening audio is always the single Part A file (its per-section split is
    // set on the Audio page). Every other Listening asset's part is OPTIONAL —
    // nothing is compulsory, so we never block the upload on a missing part.
    const isListening = paper?.subtestCode === 'listening';
    const effectivePart = isListening && uploadRole === 'Audio' ? 'A' : (uploadPart || null);
    setUploadProgress(0);
    try {
      const result = await uploadFileChunked(file, uploadRole, (pct) => setUploadProgress(pct));
      await attachPaperAsset(paperId, {
        role: uploadRole,
        mediaAssetId: result.mediaAssetId,
        part: effectivePart,
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
    if (!canWriteContent) return;
    try {
      await removePaperAsset(paperId, assetId);
      setToast({ variant: 'success', message: 'Asset removed.' });
      await load();
    } catch (e) {
      setToast({ variant: 'error', message: `${(e as Error).message}` });
    }
  };

  const publish = async () => {
    if (!canPublishContent) return;
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

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Papers', href: '/admin/content/papers' },
    { label: paper?.title ?? 'Paper' },
  ];

  if (isAdminLoading || isUserLoading) return null;

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Paper editor" breadcrumbs={breadcrumbs}>
        <Card><CardContent className="p-6"><p className="text-sm text-admin-fg-muted">Admin access required.</p></CardContent></Card>
      </AdminSettingsLayout>
    );
  }

  if (!canViewContent) {
    return (
      <AdminSettingsLayout title="Paper editor" breadcrumbs={breadcrumbs}>
        <Card><CardContent className="p-6"><p className="text-sm text-admin-fg-muted">Content read, write, or publish permission is required.</p></CardContent></Card>
      </AdminSettingsLayout>
    );
  }


  return (
    <AdminSettingsLayout
      eyebrow="CMS"
      icon={<CloudUpload className="w-5 h-5" />}
      title={paper?.title ?? 'Paper editor'}
      description={paper ? `${paper.subtestCode.toUpperCase()} · ${paper.appliesToAllProfessions ? 'All professions' : (paper.professionId ?? '-')} · ${paper.status}` : undefined}
      breadcrumbs={breadcrumbs}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="ghost" size="sm" asChild>
            <Link href="/admin/content/papers"><ArrowLeft className="w-4 h-4 mr-1.5" /> Back</Link>
          </Button>
          {paper?.subtestCode === 'reading' && (
            <Button variant="outline" size="sm" asChild>
              <Link href={`/admin/content/papers/${paperId}/preview`}>
                <Eye className="w-4 h-4 mr-1.5" /> Preview
              </Link>
            </Button>
          )}
        </div>
      }
    >
      <AsyncStateWrapper status={status}>
        {paper && (
          <div className="space-y-6">
            <Card>
              <CardHeader><CardTitle>Metadata</CardTitle></CardHeader>
              <CardContent>
              <div className="grid grid-cols-2 gap-4">
                <Input label="Title" value={paper.title} onChange={(e) => setPaper({ ...paper, title: e.target.value })} disabled={!canWriteContent} />
                <Input label="Difficulty" value={paper.difficulty} onChange={(e) => setPaper({ ...paper, difficulty: e.target.value })} disabled={!canWriteContent} />
                <Input type="number" label="Estimated duration (minutes)" value={paper.estimatedDurationMinutes}
                  onChange={(e) => setPaper({ ...paper, estimatedDurationMinutes: Number(e.target.value) })} disabled={!canWriteContent} />
                <Input type="number" label="Priority" value={paper.priority}
                  onChange={(e) => setPaper({ ...paper, priority: Number(e.target.value) })} disabled={!canWriteContent} />
                <label className="flex items-center gap-2 col-span-2">
                  <input type="checkbox" checked={paper.appliesToAllProfessions}
                    onChange={(e) => setPaper({ ...paper, appliesToAllProfessions: e.target.checked, professionId: e.target.checked ? null : paper.professionId })}
                    disabled={!canWriteContent} />
                  Applies to all professions
                </label>
                {!paper.appliesToAllProfessions && (
                  <Input label="Profession ID" value={paper.professionId ?? ''}
                    onChange={(e) => setPaper({ ...paper, professionId: e.target.value || null })} disabled={!canWriteContent} />
                )}
                {paper.subtestCode === 'writing' && (
                  <Select
                    label="Letter type"
                    value={paper.letterType ?? ''}
                    onChange={(e) => setPaper({ ...paper, letterType: e.target.value || null })}
                    options={WRITING_LETTER_TYPE_OPTIONS}
                    disabled={!canWriteContent} />
                )}
                {paper.subtestCode === 'speaking' && (
                  <Input label="Card type" value={paper.cardType ?? ''}
                    onChange={(e) => setPaper({ ...paper, cardType: e.target.value || null })}
                    placeholder="already_known_pt | examination | first_visit_emergency | …"
                    disabled={!canWriteContent} />
                )}
                <Select
                  label="Access tier"
                  value={readAccessTier(paper.tagsCsv)}
                  onChange={(e) => setPaper({ ...paper, tagsCsv: writeAccessTier(paper.tagsCsv, e.target.value as 'free' | 'premium') })}
                  disabled={!canWriteContent}
                  options={[
                    { value: 'premium', label: 'Premium (subscription required)' },
                    { value: 'free', label: 'Free preview (no subscription required)' },
                  ]}
                />
                <Input label="Tags CSV" value={paper.tagsCsv}
                  onChange={(e) => setPaper({ ...paper, tagsCsv: e.target.value })}
                  placeholder="dermatology,acne"
                  disabled={!canWriteContent} />
                <Input label={paper.subtestCode === 'listening' ? 'Source provenance (optional)' : 'Source provenance (required to publish)'}
                  value={paper.sourceProvenance ?? ''}
                  onChange={(e) => setPaper({ ...paper, sourceProvenance: e.target.value })}
                  placeholder={DEFAULT_CONTENT_SOURCE_PROVENANCE}
                  disabled={!canWriteContent} />
              </div>
              <div className="flex gap-3 mt-4">
                {canWriteContent ? <Button variant="primary" onClick={saveMetadata} loading={saving} loadingText="Saving…">Save metadata</Button> : null}
                {canPublishContent ? (
                  <Button variant="outline" onClick={publish} loading={publishing} loadingText="Publishing…"
                    disabled={paper.status === 'Published'}>
                    Publish
                  </Button>
                ) : null}
              </div>
              </CardContent>
            </Card>

            <Card>
              <CardHeader><CardTitle>Assets</CardTitle></CardHeader>
              <CardContent>
              {paper.subtestCode === 'listening' ? (
                <ListeningAssetTabs
                  assets={paper.assets ?? []}
                  paperId={paper.id}
                  activeRole={uploadRole}
                  onActiveRoleChange={(r) => { setUploadRole(r); setUploadPart(''); }}
                  uploadPart={uploadPart}
                  onUploadPartChange={setUploadPart}
                  canWrite={canWriteContent}
                  uploadProgress={uploadProgress}
                  fileInputRef={fileInputRef}
                  onPickFile={(f) => void uploadFile(f)}
                  onRemove={removeAsset}
                />
              ) : (
                <>
                  {canWriteContent ? (
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
                          <div className="mt-2 flex items-center gap-2 text-sm text-admin-text-muted">
                            <Loader2 className="w-4 h-4 animate-spin" />
                            Uploading… {Math.round(uploadProgress * 100)}%
                          </div>
                        )}
                      </div>
                    </div>
                  ) : null}

                  <AssetList assets={paper.assets ?? []} onRemove={removeAsset} canRemove={canWriteContent} />
                </>
              )}
              </CardContent>
            </Card>

            {canWriteContent && paper.subtestCode === 'reading' && (
              <ReadingStructureEditor key={readingStructureVersion} paperId={paper.id} />
            )}

            {canWriteContent && paper.subtestCode === 'listening' && (
              <>
                <Card>
                  <CardHeader><CardTitle>Listening tools</CardTitle></CardHeader>
                  <CardContent>
                    <div className="flex flex-wrap gap-2">
                      <Button variant="outline" size="sm" asChild>
                        <Link href={`/admin/content/listening/${paper.id}/part-a`}>Part A notes (exam format)</Link>
                      </Button>
                      <Button variant="outline" size="sm" asChild>
                        <Link href={`/admin/content/listening/${paper.id}/audio`}>Audio &amp; timers</Link>
                      </Button>
                      <Button variant="outline" size="sm" asChild>
                        <Link href={`/admin/content/listening/${paper.id}/extractions`}>AI extraction</Link>
                      </Button>
                    </div>
                    <p className="mt-2 text-xs text-admin-text-muted">
                      Things the Assets tabs above can&rsquo;t do: author the Part A note-completion in the exact exam format, set per-section audio &amp; timers, or auto-fill the Part A notes from the question-paper + answer-key PDFs with AI. (Per-part question-paper PDFs live in the Assets tabs above.)
                    </p>
                  </CardContent>
                </Card>
                <ListeningStructureEditor paperId={paper.id} />
              </>
            )}

            {canWriteContent && paper.subtestCode === 'speaking' && (
              <SpeakingStructureEditor paperId={paper.id} />
            )}

            {canWriteContent && paper.subtestCode === 'writing' && (
              <WritingStructureEditor paperId={paper.id} />
            )}
          </div>
        )}
      </AsyncStateWrapper>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminSettingsLayout>
  );
}

function AssetList({ assets, onRemove, canRemove }: { assets: ContentPaperAssetDto[]; onRemove: (id: string) => void; canRemove: boolean }) {
  if (assets.length === 0) {
    return <p className="text-sm text-muted">No assets yet. Upload a file above.</p>;
  }
  return (
    <ul className="divide-y divide-admin-border">
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
              {a.media?.mediaKind} · {a.media ? formatBytes(a.media.sizeBytes) : '-'}
              {a.media?.sha256 && ` · SHA ${a.media.sha256.slice(0, 10)}…`}
            </div>
          </div>
          {canRemove ? (
            <Button
              variant="ghost"
              size="sm"
              onClick={() => onRemove(a.id)}
              aria-label={`Remove ${a.role} asset ${a.media?.originalFilename ?? a.id}`}
            >
              <Trash2 className="w-4 h-4" />
            </Button>
          ) : null}
        </li>
      ))}
    </ul>
  );
}

/**
 * Listening Assets, organised into one tab per content type (Audio, Question
 * Papers, Audio Script, Answer Key, Supplementary). The active tab IS the upload
 * role, so each tab shows a role-scoped upload control + the filtered list for
 * that role. Nothing is compulsory — every slot can stay empty and the paper
 * still publishes.
 */
function ListeningAssetTabs({
  assets,
  paperId,
  activeRole,
  onActiveRoleChange,
  uploadPart,
  onUploadPartChange,
  canWrite,
  uploadProgress,
  fileInputRef,
  onPickFile,
  onRemove,
}: {
  assets: ContentPaperAssetDto[];
  paperId: string;
  activeRole: PaperAssetRole;
  onActiveRoleChange: (role: PaperAssetRole) => void;
  uploadPart: string;
  onUploadPartChange: (part: string) => void;
  canWrite: boolean;
  uploadProgress: number | null;
  fileInputRef: { current: HTMLInputElement | null };
  onPickFile: (file: File) => void;
  onRemove: (id: string) => void;
}) {
  const accept = ROLE_OPTIONS.find((r) => r.value === activeRole)?.accept ?? '';
  const isAudio = activeRole === 'Audio';
  const isQuestionPaper = activeRole === 'QuestionPaper';
  const tabAssets = listeningAssetsForTab(assets, activeRole);

  return (
    <div className="space-y-4">
      <div role="tablist" aria-label="Asset content types" className="flex flex-wrap gap-1 border-b border-admin-border">
        {LISTENING_ASSET_TABS.map((tab) => {
          const count = listeningAssetsForTab(assets, tab.id).length;
          const active = tab.id === activeRole;
          return (
            <button
              key={tab.id}
              type="button"
              role="tab"
              aria-selected={active}
              onClick={() => onActiveRoleChange(tab.id)}
              className={`-mb-px inline-flex items-center gap-2 border-b-2 px-3 py-2 text-sm font-medium transition-colors ${
                active
                  ? 'border-[var(--admin-primary)] text-[var(--admin-primary)]'
                  : 'border-transparent text-admin-fg-muted hover:text-admin-fg-strong'
              }`}
            >
              {tab.label}
              <span className="rounded-full bg-admin-bg-subtle px-1.5 py-0.5 text-xs text-admin-fg-muted">{count}</span>
            </button>
          );
        })}
      </div>

      {canWrite ? (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3 items-end">
          {isAudio ? (
            <div className="md:col-span-3">
              <label className="block text-sm font-medium mb-1">Part</label>
              <div className="rounded-admin border border-admin-border bg-admin-bg-subtle px-3 py-2 text-xs text-admin-text-muted">
                Part A (single audio). Split per section on the{' '}
                <Link href={`/admin/content/listening/${paperId}/audio`} className="underline">Audio page</Link>.
              </div>
            </div>
          ) : isQuestionPaper ? (
            <Select
              label="Part (optional)"
              value={uploadPart}
              onChange={(e) => onUploadPartChange(e.target.value)}
              options={LISTENING_PART_OPTIONS}
            />
          ) : null}

          <div className={isQuestionPaper ? 'md:col-span-2' : 'md:col-span-3'}>
            <label className="block text-sm font-medium mb-1">File</label>
            <input
              ref={fileInputRef}
              type="file"
              accept={accept}
              onChange={(e) => {
                const f = e.target.files?.[0];
                if (f) onPickFile(f);
              }}
              disabled={uploadProgress !== null}
              className="block w-full text-sm"
            />
            {uploadProgress !== null && (
              <div className="mt-2 flex items-center gap-2 text-sm text-admin-text-muted">
                <Loader2 className="w-4 h-4 animate-spin" />
                Uploading… {Math.round(uploadProgress * 100)}%
              </div>
            )}
          </div>
        </div>
      ) : null}

      {tabAssets.length === 0 ? (
        <p className="text-sm text-muted">No files in this tab yet.</p>
      ) : (
        <AssetList assets={tabAssets} onRemove={onRemove} canRemove={canWrite} />
      )}
    </div>
  );
}

function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`;
  if (n < 1024 * 1024 * 1024) return `${(n / 1024 / 1024).toFixed(1)} MB`;
  return `${(n / 1024 / 1024 / 1024).toFixed(2)} GB`;
}
