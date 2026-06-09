'use client';

import { type ChangeEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import {
  AlertTriangle,
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  FileText,
  Loader2,
  Trash2,
  Upload,
} from 'lucide-react';

import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import {
  attachPaperAsset,
  getContentPaper,
  removePaperAsset,
  uploadFileChunked,
  type ContentPaperAssetDto,
  type ContentPaperDto,
  type PaperAssetRole,
} from '@/lib/content-upload-api';
import {
  validateListeningStructure,
  type ListeningValidationReport,
} from '@/lib/listening-authoring-api';

// Required learner-facing question-paper parts. Mirrors the Reading module's
// Part A/B/C PDF slots; for Listening these print the on-screen question booklet
// shown next to the audio for each part.
type PartCode = 'A' | 'B' | 'C';
// Optional per-section overrides. When present a section PDF takes precedence
// over the shared Part PDF for that sub-section tab (A has two consultations,
// B has six short extracts, C has two presentations).
type SectionSlotCode = 'A1' | 'A2' | 'B1' | 'B2' | 'B3' | 'B4' | 'B5' | 'B6' | 'C1' | 'C2';
type SlotCode = PartCode | SectionSlotCode;
type ToastState = { message: string; variant: 'success' | 'error' };

const REQUIRED_PARTS: readonly PartCode[] = ['A', 'B', 'C'];
const A_SECTION_SLOTS: readonly SectionSlotCode[] = ['A1', 'A2'];
const B_SECTION_SLOTS: readonly SectionSlotCode[] = ['B1', 'B2', 'B3', 'B4', 'B5', 'B6'];
const C_SECTION_SLOTS: readonly SectionSlotCode[] = ['C1', 'C2'];
const ALL_SECTION_SLOTS: readonly SectionSlotCode[] = [
  ...A_SECTION_SLOTS,
  ...B_SECTION_SLOTS,
  ...C_SECTION_SLOTS,
];

const PDF_ACCEPT =
  'application/pdf,.pdf,image/png,image/jpeg,image/gif,image/webp,.png,.jpg,.jpeg,.gif,.webp';

function isPartCode(value: string | null | undefined): value is PartCode {
  return value === 'A' || value === 'B' || value === 'C';
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/** Primary asset for a given (role, part) pair, or null when none is attached. */
function primaryFor(
  assets: ContentPaperAssetDto[],
  role: PaperAssetRole,
  part: string | null,
): ContentPaperAssetDto | null {
  const matches = assets.filter((a) => a.role === role && (a.part ?? null) === part);
  return matches.find((a) => a.isPrimary) ?? null;
}

/** Per-part publish-blocker message from the validation report, if any. */
function requiredIssueForPart(report: ListeningValidationReport | null, partCode: PartCode): string | null {
  const issue = report?.issues.find((c) => c.code === `part_${partCode.toLowerCase()}_pdf_required`);
  return issue?.message ?? null;
}

export default function ListeningPdfAssetsPage() {
  const params = useParams<{ paperId: string }>();
  const paperId = params?.paperId ?? '';

  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [validation, setValidation] = useState<ListeningValidationReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  // Upload state is keyed by `${role}:${part}` so only the active card spins.
  const [uploadingSlot, setUploadingSlot] = useState<string | null>(null);
  const [uploadProgress, setUploadProgress] = useState<number | null>(null);
  const [removingAssetId, setRemovingAssetId] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState | null>(null);

  const load = useCallback(async () => {
    if (!paperId) return;
    setLoading(true);
    setError(null);
    try {
      const [paperData, report] = await Promise.all([
        getContentPaper(paperId),
        validateListeningStructure(paperId).catch(() => null),
      ]);
      setPaper(paperData);
      setValidation(report);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load listening PDF assets');
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => {
    void load();
  }, [load]);

  const assets = useMemo(() => paper?.assets ?? [], [paper?.assets]);

  const readyParts = REQUIRED_PARTS.filter((p) => primaryFor(assets, 'QuestionPaper', p));
  const errorCount = validation?.issues.filter((i) => i.severity === 'error').length ?? 0;
  const warningCount = validation?.issues.filter((i) => i.severity === 'warning').length ?? 0;

  const slotKey = (role: PaperAssetRole, part: string | null) => `${role}:${part ?? ''}`;

  const handleUpload = useCallback(
    async (role: PaperAssetRole, part: string | null, file: File): Promise<void> => {
      const looksLikePdf = file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf');
      const looksLikeAsset = looksLikePdf || file.type.startsWith('image/') || /\.(png|jpe?g|gif|webp|avif)$/i.test(file.name);
      if (!looksLikeAsset) {
        setToast({ message: 'Upload a PDF or image file for this slot.', variant: 'error' });
        return;
      }

      setUploadingSlot(slotKey(role, part));
      setUploadProgress(0);
      try {
        const result = await uploadFileChunked(file, role, (pct) => setUploadProgress(pct));
        // Stable display order: part slots first (in canonical order), then null-part roles last.
        const order = part
          ? [...REQUIRED_PARTS, ...ALL_SECTION_SLOTS].indexOf(part as SlotCode) + 1
          : 99;
        await attachPaperAsset(paperId, {
          role,
          mediaAssetId: result.mediaAssetId,
          part,
          title: file.name,
          displayOrder: order,
          makePrimary: true,
        });
        const label = part ? `${role} (${part})` : role;
        setToast({
          message: result.deduplicated ? `${label} attached from existing media.` : `${label} uploaded.`,
          variant: 'success',
        });
        await load();
      } catch (err) {
        setToast({ message: err instanceof Error ? err.message : 'Asset upload failed', variant: 'error' });
      } finally {
        setUploadingSlot(null);
        setUploadProgress(null);
      }
    },
    [paperId, load],
  );

  const handleRemove = useCallback(
    async (assetId: string): Promise<void> => {
      if (!window.confirm('Remove this PDF from the listening paper?')) return;
      setRemovingAssetId(assetId);
      try {
        await removePaperAsset(paperId, assetId);
        setToast({ message: 'PDF removed.', variant: 'success' });
        await load();
      } catch (err) {
        setToast({ message: err instanceof Error ? err.message : 'Failed to remove PDF', variant: 'error' });
      } finally {
        setRemovingAssetId(null);
      }
    },
    [paperId, load],
  );

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Listening', href: '/admin/content/listening' },
    { label: 'Paper', href: `/admin/content/papers/${paperId}` },
    { label: 'PDFs' },
  ];

  if (!paperId) {
    return (
      <AdminSettingsLayout title="Listening PDFs" breadcrumbs={breadcrumbs}>
        <SettingsSection title="Missing paper">
          <p className="text-sm text-admin-fg-muted">No paper ID provided.</p>
        </SettingsSection>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      title="Listening PDFs"
      description="Attach the learner question-paper PDFs (per part, with optional per-section overrides) plus the audio script and answer key — the same per-part pattern as the Reading module."
      eyebrow="Listening authoring"
      breadcrumbs={breadcrumbs}
    >
      <div className="space-y-6">
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <SettingsSection
          title="Question paper slots"
          description="Each Listening paper needs one primary QuestionPaper PDF (or image) for Part A, Part B, and Part C before it can publish — the learner sees the relevant part next to the audio."
        >
          {loading ? (
            <div className="grid gap-4 lg:grid-cols-3">
              {REQUIRED_PARTS.map((p) => (
                <Card key={p}>
                  <CardContent className="space-y-3 py-6">
                    <Skeleton variant="text" className="h-5 w-1/3" />
                    <Skeleton variant="card" />
                    <Skeleton variant="text" className="h-4 w-2/3" />
                  </CardContent>
                </Card>
              ))}
            </div>
          ) : (
            <div className="space-y-4">
              <div className="grid gap-3 md:grid-cols-3">
                <StatusTile label="PDF slots ready" value={`${readyParts.length}/3`} tone={readyParts.length === 3 ? 'success' : 'warning'} />
                <StatusTile label="Publish blockers" value={String(errorCount)} tone={errorCount > 0 ? 'danger' : 'success'} />
                <StatusTile label="Warnings" value={String(warningCount)} tone={warningCount > 0 ? 'warning' : 'success'} />
              </div>

              <div className="grid gap-4 lg:grid-cols-3">
                {REQUIRED_PARTS.map((partCode) => (
                  <PdfSlotCard
                    key={partCode}
                    title={`Part ${partCode}`}
                    description="Primary QuestionPaper PDF or image"
                    role="QuestionPaper"
                    part={partCode}
                    assets={assets}
                    requiredIssue={requiredIssueForPart(validation, partCode)}
                    uploadingSlot={uploadingSlot}
                    uploadProgress={uploadProgress}
                    removingAssetId={removingAssetId}
                    onUpload={handleUpload}
                    onRemove={handleRemove}
                  />
                ))}
              </div>
            </div>
          )}
        </SettingsSection>

        {!loading && (
          <SettingsSection
            title="Per-section PDFs (optional)"
            description="Part A (A1, A2 consultations), Part B (B1–B6 extracts) and Part C (C1, C2 presentations) can each have their own PDF or image. When present these override the shared Part PDF for that section tab. Leave slots empty to use the shared Part PDF."
          >
            <div className="space-y-4">
              <SectionGroup label="Part A sections" slots={A_SECTION_SLOTS} columns="sm:grid-cols-2" {...{ assets, uploadingSlot, uploadProgress, removingAssetId, onUpload: handleUpload, onRemove: handleRemove }} />
              <SectionGroup label="Part B sections" slots={B_SECTION_SLOTS} columns="sm:grid-cols-2 lg:grid-cols-3" {...{ assets, uploadingSlot, uploadProgress, removingAssetId, onUpload: handleUpload, onRemove: handleRemove }} />
              <SectionGroup label="Part C sections" slots={C_SECTION_SLOTS} columns="sm:grid-cols-2" {...{ assets, uploadingSlot, uploadProgress, removingAssetId, onUpload: handleUpload, onRemove: handleRemove }} />
            </div>
          </SettingsSection>
        )}

        {!loading && (
          <SettingsSection
            title="Reference PDFs"
            description="The full audio transcript and the official answer key. Both are required to publish a Listening paper."
          >
            <div className="grid gap-4 lg:grid-cols-2">
              <PdfSlotCard
                title="Audio script"
                description="Full transcript PDF (marker reference)"
                role="AudioScript"
                part={null}
                assets={assets}
                requiredIssue={null}
                uploadingSlot={uploadingSlot}
                uploadProgress={uploadProgress}
                removingAssetId={removingAssetId}
                onUpload={handleUpload}
                onRemove={handleRemove}
              />
              <PdfSlotCard
                title="Answer key"
                description="Official answer key PDF"
                role="AnswerKey"
                part={null}
                assets={assets}
                requiredIssue={null}
                uploadingSlot={uploadingSlot}
                uploadProgress={uploadProgress}
                removingAssetId={removingAssetId}
                onUpload={handleUpload}
                onRemove={handleRemove}
              />
            </div>
          </SettingsSection>
        )}

        <div className="flex items-center justify-between pt-2">
          <Button asChild variant="ghost" size="sm" startIcon={<ArrowLeft className="h-4 w-4" />}>
            <Link href={`/admin/content/papers/${paperId}`}>Back to paper</Link>
          </Button>
          <Button asChild variant="primary" size="sm" endIcon={<ArrowRight className="h-4 w-4" />}>
            <Link href={`/admin/content/listening/${paperId}/audio`}>Next: Audio &amp; timers</Link>
          </Button>
        </div>
      </div>

      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}
    </AdminSettingsLayout>
  );
}

function SectionGroup({
  label,
  slots,
  columns,
  assets,
  uploadingSlot,
  uploadProgress,
  removingAssetId,
  onUpload,
  onRemove,
}: {
  label: string;
  slots: readonly SectionSlotCode[];
  columns: string;
  assets: ContentPaperAssetDto[];
  uploadingSlot: string | null;
  uploadProgress: number | null;
  removingAssetId: string | null;
  onUpload: (role: PaperAssetRole, part: string | null, file: File) => Promise<void>;
  onRemove: (assetId: string) => Promise<void>;
}) {
  return (
    <div>
      <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">{label}</p>
      <div className={`grid gap-3 ${columns}`}>
        {slots.map((slotCode) => (
          <PdfSlotCard
            key={slotCode}
            title={slotCode}
            description="Optional section override"
            role="QuestionPaper"
            part={slotCode}
            assets={assets}
            requiredIssue={null}
            uploadingSlot={uploadingSlot}
            uploadProgress={uploadProgress}
            removingAssetId={removingAssetId}
            onUpload={onUpload}
            onRemove={onRemove}
          />
        ))}
      </div>
    </div>
  );
}

function StatusTile({ label, value, tone }: { label: string; value: string; tone: 'success' | 'warning' | 'danger' }) {
  return (
    <div className="rounded-admin-lg border border-admin-border bg-admin-bg-surface px-4 py-3">
      <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">{label}</p>
      <div className="mt-2 flex items-center justify-between gap-3">
        <p className="text-2xl font-semibold text-admin-fg-strong">{value}</p>
        <Badge variant={tone}>{tone === 'success' ? 'Ready' : tone === 'warning' ? 'Check' : 'Blocked'}</Badge>
      </div>
    </div>
  );
}

function PdfSlotCard({
  title,
  description,
  role,
  part,
  assets,
  requiredIssue,
  uploadingSlot,
  uploadProgress,
  removingAssetId,
  onUpload,
  onRemove,
}: {
  title: string;
  description: string;
  role: PaperAssetRole;
  part: string | null;
  assets: ContentPaperAssetDto[];
  requiredIssue: string | null;
  uploadingSlot: string | null;
  uploadProgress: number | null;
  removingAssetId: string | null;
  onUpload: (role: PaperAssetRole, part: string | null, file: File) => Promise<void>;
  onRemove: (assetId: string) => Promise<void>;
}) {
  const slotAssets = assets.filter((a) => a.role === role && (a.part ?? null) === part);
  const primaryAsset = slotAssets.find((a) => a.isPrimary) ?? null;
  const extraAssets = slotAssets.filter((a) => a.id !== primaryAsset?.id);
  const uploading = uploadingSlot === `${role}:${part ?? ''}`;
  const inputId = `listening-${role.toLowerCase()}-${(part ?? 'main').toLowerCase()}-pdf`;

  async function handleFileChange(event: ChangeEvent<HTMLInputElement>): Promise<void> {
    const file = event.currentTarget.files?.[0];
    if (!file) return;
    const input = event.currentTarget;
    await onUpload(role, part, file);
    input.value = '';
  }

  return (
    <Card>
      <CardHeader>
        <div className="min-w-0">
          <CardTitle className="flex items-center gap-2">
            <FileText className="h-4 w-4 text-admin-fg-muted" />
            {title}
          </CardTitle>
          <CardDescription>{description}</CardDescription>
        </div>
        <CardAction>
          {primaryAsset ? (
            <Badge variant="success" startIcon={<CheckCircle2 className="h-3.5 w-3.5" />}>Ready</Badge>
          ) : (
            <Badge variant="warning" startIcon={<AlertTriangle className="h-3.5 w-3.5" />}>Missing</Badge>
          )}
        </CardAction>
      </CardHeader>
      <CardContent className="space-y-4">
        {primaryAsset ? (
          <AssetSummary asset={primaryAsset} label="Primary PDF" removing={removingAssetId === primaryAsset.id} onRemove={onRemove} />
        ) : (
          <div className="rounded-admin-lg border border-dashed border-admin-border bg-admin-bg-subtle px-4 py-5 text-sm text-admin-fg-muted">
            {requiredIssue ?? `${title} has no primary PDF yet.`}
          </div>
        )}

        {extraAssets.length > 0 ? (
          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Additional assets</p>
            {extraAssets.map((asset) => (
              <AssetSummary key={asset.id} asset={asset} label="Secondary PDF" removing={removingAssetId === asset.id} onRemove={onRemove} />
            ))}
          </div>
        ) : null}

        <div className="space-y-2">
          <input
            id={inputId}
            type="file"
            accept={PDF_ACCEPT}
            className="sr-only"
            onChange={(event) => void handleFileChange(event)}
            disabled={uploading}
          />
          <Button asChild variant={primaryAsset ? 'secondary' : 'primary'} size="sm" className="w-full justify-center">
            <label htmlFor={inputId} aria-disabled={uploading} className="inline-flex items-center justify-center gap-2">
              {uploading ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Upload className="h-4 w-4" aria-hidden="true" />}
              {uploading ? 'Uploading...' : primaryAsset ? 'Replace' : 'Upload PDF / image'}
            </label>
          </Button>
          {uploading && uploadProgress !== null ? (
            <div className="h-2 overflow-hidden rounded-full bg-admin-bg-subtle" aria-label={`${title} upload progress`}>
              <div className="h-full bg-[var(--admin-primary)] transition-all" style={{ width: `${Math.round(uploadProgress * 100)}%` }} />
            </div>
          ) : null}
        </div>
      </CardContent>
    </Card>
  );
}

function AssetSummary({
  asset,
  label,
  removing,
  onRemove,
}: {
  asset: ContentPaperAssetDto;
  label: string;
  removing: boolean;
  onRemove: (assetId: string) => Promise<void>;
}) {
  const filename = asset.media?.originalFilename ?? asset.title ?? 'PDF';
  const size = asset.media ? formatBytes(asset.media.sizeBytes) : null;

  return (
    <div className="flex items-start justify-between gap-3 rounded-admin-lg border border-admin-border bg-admin-bg-subtle px-3 py-3">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={asset.isPrimary ? 'success' : 'muted'}>{label}</Badge>
          {asset.part ? <Badge variant="outline">{asset.part}</Badge> : null}
        </div>
        <p className="mt-2 truncate text-sm font-semibold text-admin-fg-strong">{filename}</p>
        <p className="mt-1 text-xs text-admin-fg-muted">
          {asset.media?.mimeType ?? 'application/pdf'}{size ? ` - ${size}` : ''}
        </p>
      </div>
      <Button
        type="button"
        variant="ghost"
        size="sm"
        onClick={() => void onRemove(asset.id)}
        disabled={removing}
        aria-label={`Remove ${filename}`}
        className="text-[var(--admin-danger)] hover:bg-[var(--admin-danger-tint)] hover:text-[var(--admin-danger)]"
      >
        {removing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" />}
      </Button>
    </div>
  );
}
