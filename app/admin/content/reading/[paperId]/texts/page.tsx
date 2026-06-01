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
import { ReadingWizardSteps } from '@/components/domain/admin/reading/ReadingWizardSteps';
import {
  attachPaperAsset,
  getContentPaper,
  removePaperAsset,
  uploadFileChunked,
  type ContentPaperAssetDto,
  type ContentPaperDto,
} from '@/lib/content-upload-api';
import {
  validateReadingPaper,
  type ReadingValidationReport,
} from '@/lib/reading-authoring-api';

type PartCode = 'A' | 'B' | 'C';
type ToastState = { message: string; variant: 'success' | 'error' };

const REQUIRED_PARTS: readonly PartCode[] = ['A', 'B', 'C'];

function isPartCode(value: string | null | undefined): value is PartCode {
  return value === 'A' || value === 'B' || value === 'C';
}

function isReadingQuestionPaperAsset(asset: ContentPaperAssetDto): boolean {
  return asset.role === 'QuestionPaper' && isPartCode(asset.part);
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function primaryForPart(assets: ContentPaperAssetDto[], partCode: PartCode): ContentPaperAssetDto | null {
  const partAssets = assets.filter((asset) => asset.role === 'QuestionPaper' && asset.part === partCode);
  return partAssets.find((asset) => asset.isPrimary) ?? null;
}

function requiredIssueForPart(report: ReadingValidationReport | null, partCode: PartCode): string | null {
  const issue = report?.issues.find((candidate) => candidate.code === `part_${partCode.toLowerCase()}_pdf_required`);
  return issue?.message ?? null;
}

export default function ReadingPdfAssetsPage() {
  const params = useParams<{ paperId: string }>();
  const paperId = params?.paperId ?? '';

  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [validation, setValidation] = useState<ReadingValidationReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [uploadingPart, setUploadingPart] = useState<PartCode | null>(null);
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
        validateReadingPaper(paperId).catch(() => null),
      ]);
      setPaper(paperData);
      setValidation(report);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load reading PDF assets');
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => {
    void load();
  }, [load]);

  const readingQuestionPaperAssets = useMemo(
    () => (paper?.assets ?? []).filter(isReadingQuestionPaperAsset),
    [paper?.assets],
  );

  const readyParts = REQUIRED_PARTS.filter((partCode) => primaryForPart(readingQuestionPaperAssets, partCode));
  const errorCount = validation?.issues.filter((issue) => issue.severity === 'error').length ?? 0;
  const warningCount = validation?.issues.filter((issue) => issue.severity === 'warning').length ?? 0;

  async function handleUpload(partCode: PartCode, file: File): Promise<void> {
    const looksLikePdf = file.type === 'application/pdf' || file.name.toLowerCase().endsWith('.pdf');
    if (!looksLikePdf) {
      setToast({ message: 'Upload a PDF file for the question paper slot.', variant: 'error' });
      return;
    }

    setUploadingPart(partCode);
    setUploadProgress(0);
    try {
      const result = await uploadFileChunked(file, 'QuestionPaper', (pct) => setUploadProgress(pct));
      await attachPaperAsset(paperId, {
        role: 'QuestionPaper',
        mediaAssetId: result.mediaAssetId,
        part: partCode,
        title: file.name,
        displayOrder: REQUIRED_PARTS.indexOf(partCode) + 1,
        makePrimary: true,
      });
      setToast({
        message: result.deduplicated ? `Part ${partCode} PDF attached from existing media.` : `Part ${partCode} PDF uploaded.`,
        variant: 'success',
      });
      await load();
    } catch (err) {
      setToast({ message: err instanceof Error ? err.message : 'PDF upload failed', variant: 'error' });
    } finally {
      setUploadingPart(null);
      setUploadProgress(null);
    }
  }

  async function handleRemove(assetId: string): Promise<void> {
    if (!window.confirm('Remove this PDF from the reading paper?')) return;
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
  }

  if (!paperId) {
    return (
      <AdminSettingsLayout
        title="Reading PDFs"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Reading', href: '/admin/content/reading' },
          { label: 'PDFs' },
        ]}
      >
        <SettingsSection title="Missing paper">
          <p className="text-sm text-admin-fg-muted">No paper ID provided.</p>
        </SettingsSection>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      title="Reading PDFs"
      description="Attach the three primary question paper PDFs used by the learner Reading player."
      eyebrow="Reading authoring"
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Reading', href: '/admin/content/reading' },
        { label: 'Paper', href: `/admin/content/reading/${paperId}` },
        { label: 'PDFs' },
      ]}
    >
      <div className="space-y-6">
        <ReadingWizardSteps paperId={paperId} currentStep="texts" />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <SettingsSection
          title="Question paper slots"
          description="Each Reading paper needs one primary QuestionPaper PDF for Part A, Part B, and Part C before it can publish."
        >
          {loading ? (
            <div className="grid gap-4 lg:grid-cols-3">
              {REQUIRED_PARTS.map((partCode) => (
                <Card key={partCode}>
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
                {REQUIRED_PARTS.map((partCode) => {
                  const assets = readingQuestionPaperAssets.filter((asset) => asset.part === partCode);
                  const primaryAsset = primaryForPart(readingQuestionPaperAssets, partCode);
                  return (
                    <PdfSlotCard
                      key={partCode}
                      partCode={partCode}
                      assets={assets}
                      primaryAsset={primaryAsset}
                      requiredIssue={requiredIssueForPart(validation, partCode)}
                      uploading={uploadingPart === partCode}
                      uploadProgress={uploadProgress}
                      removingAssetId={removingAssetId}
                      onUpload={handleUpload}
                      onRemove={handleRemove}
                    />
                  );
                })}
              </div>
            </div>
          )}
        </SettingsSection>

        <div className="flex items-center justify-between pt-2">
          <Button asChild variant="ghost" size="sm" startIcon={<ArrowLeft className="h-4 w-4" />}>
            <Link href={`/admin/content/reading/${paperId}`}>Back to Overview</Link>
          </Button>

          <Button asChild variant="primary" size="sm" endIcon={<ArrowRight className="h-4 w-4" />}>
            <Link href={`/admin/content/reading/${paperId}/questions`}>Next: Questions</Link>
          </Button>
        </div>
      </div>

      {toast ? (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      ) : null}
    </AdminSettingsLayout>
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
  partCode,
  assets,
  primaryAsset,
  requiredIssue,
  uploading,
  uploadProgress,
  removingAssetId,
  onUpload,
  onRemove,
}: {
  partCode: PartCode;
  assets: ContentPaperAssetDto[];
  primaryAsset: ContentPaperAssetDto | null;
  requiredIssue: string | null;
  uploading: boolean;
  uploadProgress: number | null;
  removingAssetId: string | null;
  onUpload: (partCode: PartCode, file: File) => Promise<void>;
  onRemove: (assetId: string) => Promise<void>;
}) {
  const inputId = `reading-part-${partCode.toLowerCase()}-pdf`;
  const extraAssets = assets.filter((asset) => asset.id !== primaryAsset?.id);

  async function handleFileChange(event: ChangeEvent<HTMLInputElement>): Promise<void> {
    const file = event.currentTarget.files?.[0];
    if (!file) return;
      const input = event.currentTarget;
      await onUpload(partCode, file);
      input.value = '';
  }

  return (
    <Card>
      <CardHeader>
        <div className="min-w-0">
          <CardTitle className="flex items-center gap-2">
            <FileText className="h-4 w-4 text-admin-fg-muted" />
            Part {partCode}
          </CardTitle>
          <CardDescription>Primary QuestionPaper PDF</CardDescription>
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
          <AssetSummary
            asset={primaryAsset}
            label="Primary PDF"
            removing={removingAssetId === primaryAsset.id}
            onRemove={onRemove}
          />
        ) : (
          <div className="rounded-admin-lg border border-dashed border-admin-border bg-admin-bg-subtle px-4 py-5 text-sm text-admin-fg-muted">
            {requiredIssue ?? `Part ${partCode} has no primary question paper PDF.`}
          </div>
        )}

        {extraAssets.length > 0 ? (
          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Additional assets</p>
            {extraAssets.map((asset) => (
              <AssetSummary
                key={asset.id}
                asset={asset}
                label="Secondary PDF"
                removing={removingAssetId === asset.id}
                onRemove={onRemove}
              />
            ))}
          </div>
        ) : null}

        <div className="space-y-2">
          <input
            id={inputId}
            type="file"
            accept="application/pdf,.pdf"
            className="sr-only"
            onChange={(event) => void handleFileChange(event)}
            disabled={uploading}
          />
          <Button
            asChild
            variant={primaryAsset ? 'secondary' : 'primary'}
            size="sm"
            className="w-full justify-center"
          >
            <label htmlFor={inputId} aria-disabled={uploading} className="inline-flex items-center justify-center gap-2">
              {uploading ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Upload className="h-4 w-4" aria-hidden="true" />}
              {uploading ? 'Uploading...' : primaryAsset ? 'Replace primary PDF' : 'Upload PDF'}
            </label>
          </Button>
          {uploading && uploadProgress !== null ? (
            <div className="h-2 overflow-hidden rounded-full bg-admin-bg-subtle" aria-label={`Part ${partCode} upload progress`}>
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
  const filename = asset.media?.originalFilename ?? asset.title ?? 'Question paper PDF';
  const size = asset.media ? formatBytes(asset.media.sizeBytes) : null;

  return (
    <div className="flex items-start justify-between gap-3 rounded-admin-lg border border-admin-border bg-admin-bg-subtle px-3 py-3">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={asset.isPrimary ? 'success' : 'muted'}>{label}</Badge>
          {asset.part ? <Badge variant="outline">Part {asset.part}</Badge> : null}
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
