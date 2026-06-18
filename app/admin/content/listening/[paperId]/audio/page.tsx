'use client';

import { type ChangeEvent, useCallback, useEffect, useMemo, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import {
  AlertTriangle,
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  Headphones,
  ListChecks,
  Loader2,
  Save,
  Timer,
  Trash2,
  Upload,
} from 'lucide-react';

import { AdminSettingsLayout, SettingsSection } from '@/components/admin/layout/admin-settings-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  attachPaperAsset,
  getContentPaper,
  removePaperAsset,
  uploadFileChunked,
  type ContentPaperAssetDto,
  type ContentPaperDto,
} from '@/lib/content-upload-api';
import {
  getListeningExtracts,
  setListeningSubSectionTimer,
  type ListeningSubSectionCode,
} from '@/lib/listening-authoring-api';

type ToastState = { message: string; variant: 'success' | 'error' };

const AUDIO_ACCEPT = 'audio/mpeg,audio/wav,audio/mp4,audio/ogg,.mp3,.wav,.m4a,.ogg';

/**
 * The five learner-facing Listening sections, each with its own audio upload.
 * Part B's six questions share ONE audio (stored under part code "B"); Part A is
 * always per-subsection (A1, A2). Mirrors the player's LISTENING_SECTION_SEQUENCE.
 */
const AUDIO_SECTION_CODES = ['A1', 'A2', 'B', 'C1', 'C2'] as const;
type AudioSectionCode = (typeof AUDIO_SECTION_CODES)[number];

const SECTION_LABEL: Record<AudioSectionCode, string> = {
  A1: 'Part A — Extract 1 (A1)',
  A2: 'Part A — Extract 2 (A2)',
  B: 'Part B — single audio (plays across all six questions)',
  C1: 'Part C — Extract 1 (C1)',
  C2: 'Part C — Extract 2 (C2)',
};

// Part-asset codes that belong to a section. Part B owns its single "B" upload
// plus any legacy per-question B1..B6 audio (still shown so admins can clear
// leftovers before the per-section migration runs). Every other section owns
// its own code only.
function partCodesForSection(section: AudioSectionCode): readonly string[] {
  return section === 'B' ? ['B', 'B1', 'B2', 'B3', 'B4', 'B5', 'B6'] : [section];
}

// The ListeningPart row that carries a section's countdown timer. Timers live on
// the enum-coded B1..B6 parts (there is no bare "B"), so Part B's timer uses B1.
function timerCodeForSection(section: AudioSectionCode): ListeningSubSectionCode {
  return section === 'B' ? 'B1' : (section as ListeningSubSectionCode);
}

function isListeningAudioAsset(asset: ContentPaperAssetDto): boolean {
  if (asset.role !== 'Audio') return false;
  const code = (asset.part ?? '').toUpperCase();
  return AUDIO_SECTION_CODES.some((section) => partCodesForSection(section).includes(code));
}

function looksLikeAudio(file: File): boolean {
  if (file.type.startsWith('audio/')) return true;
  return /\.(mp3|wav|m4a|ogg)$/i.test(file.name);
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

function sectionAssets(assets: ContentPaperAssetDto[], section: AudioSectionCode): ContentPaperAssetDto[] {
  const codes = partCodesForSection(section);
  return assets.filter((a) => a.role === 'Audio' && codes.includes((a.part ?? '').toUpperCase()));
}

// The audio that actually plays for a section. For Part B, prefer the canonical
// "B" upload, then a legacy "B1" file, then any B* asset (mirrors the backend
// resolver in ListeningLearnerService).
function primaryFor(assets: ContentPaperAssetDto[], section: AudioSectionCode): ContentPaperAssetDto | null {
  const matches = sectionAssets(assets, section);
  if (matches.length === 0) return null;
  if (section === 'B') {
    const code = (a: ContentPaperAssetDto) => (a.part ?? '').toUpperCase();
    return matches.find((a) => code(a) === 'B' && a.isPrimary)
      ?? matches.find((a) => code(a) === 'B')
      ?? matches.find((a) => code(a) === 'B1')
      ?? matches.find((a) => a.isPrimary)
      ?? matches[0];
  }
  return matches.find((a) => a.isPrimary) ?? matches[0];
}

/**
 * Prefer the backend problem payload's prose (stashed on `err.detail` by the
 * listening API client, which otherwise only sets `HTTP <status>`). The
 * content-upload client already lifts prose into `.message`, so this also
 * handles the upload/attach/remove path uniformly.
 */
function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'detail' in err) {
    const detail = (err as { detail?: unknown }).detail;
    if (detail && typeof detail === 'object') {
      const msg = (detail as { message?: unknown }).message;
      if (typeof msg === 'string' && msg.trim()) return msg;
    }
  }
  return err instanceof Error && err.message ? err.message : fallback;
}

export default function ListeningAudioTimersPage() {
  const params = useParams<{ paperId?: string | string[] }>();
  const paperId = Array.isArray(params?.paperId) ? params?.paperId[0] : params?.paperId ?? '';
  const { isAuthenticated, role } = useAdminAuth();

  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [timers, setTimers] = useState<Record<string, number | null>>({});
  const [timerDraft, setTimerDraft] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [uploadingCode, setUploadingCode] = useState<AudioSectionCode | null>(null);
  const [uploadProgress, setUploadProgress] = useState<number | null>(null);
  const [removingAssetId, setRemovingAssetId] = useState<string | null>(null);
  const [savingTimer, setSavingTimer] = useState<AudioSectionCode | null>(null);
  const [toast, setToast] = useState<ToastState | null>(null);

  const load = useCallback(async () => {
    if (!paperId) return;
    setLoading(true);
    setError(null);
    try {
      const [paperData, extractList] = await Promise.all([
        getContentPaper(paperId),
        getListeningExtracts(paperId).catch(() => ({ extracts: [] })),
      ]);
      setPaper(paperData);
      const nextTimers: Record<string, number | null> = {};
      const nextDraft: Record<string, string> = {};
      for (const extract of extractList.extracts) {
        const code = String(extract.partCode).toUpperCase();
        // Collapse Part B's six sub-parts to one "B" section, represented by B1.
        const section = code.startsWith('B') ? 'B' : code;
        if (section === 'B' && code !== 'B1') continue;
        nextTimers[section] = extract.timeLimitSeconds ?? null;
        nextDraft[section] = extract.timeLimitSeconds != null ? String(extract.timeLimitSeconds) : '';
      }
      setTimers(nextTimers);
      setTimerDraft(nextDraft);
    } catch (err) {
      setError(errorMessage(err, 'Failed to load listening audio assets'));
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    void load();
  }, [isAuthenticated, role, load]);

  const audioAssets = useMemo(
    () => (paper?.assets ?? []).filter(isListeningAudioAsset),
    [paper?.assets],
  );

  const activeCodes = AUDIO_SECTION_CODES;

  const readyCount = useMemo(
    () => activeCodes.filter((code) =>
      primaryFor(audioAssets, code) && (timers[code] ?? 0) > 0,
    ).length,
    [activeCodes, audioAssets, timers],
  );

  const audioCount = useMemo(
    () => activeCodes.filter((code) => primaryFor(audioAssets, code)).length,
    [activeCodes, audioAssets],
  );

  async function handleUpload(code: AudioSectionCode, file: File): Promise<void> {
    if (!looksLikeAudio(file)) {
      setToast({ message: 'Upload an audio file (.mp3, .wav, .m4a, .ogg).', variant: 'error' });
      return;
    }
    setUploadingCode(code);
    setUploadProgress(0);
    try {
      const result = await uploadFileChunked(file, 'Audio', (pct) => setUploadProgress(pct));
      await attachPaperAsset(paperId, {
        role: 'Audio',
        mediaAssetId: result.mediaAssetId,
        part: code,
        title: file.name,
        displayOrder: AUDIO_SECTION_CODES.indexOf(code) + 1,
        makePrimary: true,
      });
      setToast({
        message: result.deduplicated
          ? `${code} audio attached from existing media.`
          : `${code} audio uploaded.`,
        variant: 'success',
      });
      await load();
    } catch (err) {
      setToast({ message: errorMessage(err, 'Audio upload failed'), variant: 'error' });
    } finally {
      setUploadingCode(null);
      setUploadProgress(null);
    }
  }

  async function handleRemove(assetId: string): Promise<void> {
    if (!window.confirm('Remove this audio file from the sub-section?')) return;
    setRemovingAssetId(assetId);
    try {
      await removePaperAsset(paperId, assetId);
      setToast({ message: 'Audio removed.', variant: 'success' });
      await load();
    } catch (err) {
      setToast({ message: errorMessage(err, 'Failed to remove audio'), variant: 'error' });
    } finally {
      setRemovingAssetId(null);
    }
  }

  async function handleSaveTimer(code: AudioSectionCode): Promise<void> {
    const raw = (timerDraft[code] ?? '').trim();
    const parsed = raw === '' ? null : Number(raw);
    if (parsed != null && (!Number.isFinite(parsed) || parsed < 0 || !Number.isInteger(parsed))) {
      setToast({ message: 'Timer must be a whole number of seconds (or blank to clear).', variant: 'error' });
      return;
    }
    setSavingTimer(code);
    try {
      const timerCode = timerCodeForSection(code);
      const result = await setListeningSubSectionTimer(paperId, timerCode, parsed && parsed > 0 ? parsed : null);
      const updated = result.extracts.find((e) => String(e.partCode).toUpperCase() === timerCode);
      const value = updated?.timeLimitSeconds ?? null;
      setTimers((prev) => ({ ...prev, [code]: value }));
      setTimerDraft((prev) => ({ ...prev, [code]: value != null ? String(value) : '' }));
      setToast({
        message: value != null ? `${code} timer set to ${value}s.` : `${code} timer cleared.`,
        variant: 'success',
      });
    } catch (err) {
      setToast({ message: errorMessage(err, 'Failed to save timer'), variant: 'error' });
    } finally {
      setSavingTimer(null);
    }
  }

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Listening', href: '/admin/content/listening' },
    { label: 'Paper', href: `/admin/content/listening/${paperId}/structure` },
    { label: 'Audio & timers' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Listening: audio & timers" breadcrumbs={breadcrumbs}>
        <SettingsSection title="Admin access required">
          <p className="text-sm text-admin-fg-muted">You need admin access to author listening audio.</p>
        </SettingsSection>
      </AdminSettingsLayout>
    );
  }

  if (!paperId) {
    return (
      <AdminSettingsLayout title="Listening: audio & timers" breadcrumbs={breadcrumbs}>
        <SettingsSection title="Missing paper">
          <p className="text-sm text-admin-fg-muted">No paper ID provided.</p>
        </SettingsSection>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      eyebrow="Listening authoring"
      icon={<Headphones className="h-5 w-5" />}
      title="Audio & timers"
      description="Upload an audio file and set a countdown timer for each of the 5 listening sections: A1, A2, one shared Part B audio, C1, C2."
      breadcrumbs={breadcrumbs}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="ghost" size="sm" asChild>
            <Link href="/admin/content/listening">
              <ArrowLeft className="h-4 w-4 mr-1.5" />
              Back to papers
            </Link>
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/structure`}>
              <ListChecks className="h-4 w-4 mr-1.5" />
              Structure
            </Link>
          </Button>
        </div>
      }
    >
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <SettingsSection
        title="Section audio + countdown"
        description="Parts A and C have a separate audio file per section (A1, A2, C1, C2). Part B has one shared audio that plays across all six Part B questions. Each section also has its own countdown (seconds) and is ready once both are set."
      >
        {loading ? (
          <div className="grid gap-4 lg:grid-cols-2">
            {AUDIO_SECTION_CODES.map((code) => (
              <Card key={code}>
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
              <StatusTile label="Audio uploaded" value={`${audioCount}/${activeCodes.length}`} tone={audioCount === activeCodes.length ? 'success' : 'warning'} />
              <StatusTile label="Fully ready (audio + timer)" value={`${readyCount}/${activeCodes.length}`} tone={readyCount === activeCodes.length ? 'success' : 'warning'} />
              <StatusTile label="Sections" value={String(activeCodes.length)} tone="info" />
            </div>

            <div className="grid gap-4 lg:grid-cols-2">
              {activeCodes.map((code) => {
                const codeAssets = sectionAssets(audioAssets, code);
                const primaryAsset = primaryFor(audioAssets, code);
                return (
                  <AudioSlotCard
                    key={code}
                    code={code}
                    label={SECTION_LABEL[code]}
                    assets={codeAssets}
                    primaryAsset={primaryAsset}
                    timerValue={timers[code] ?? null}
                    timerDraft={timerDraft[code] ?? ''}
                    uploading={uploadingCode === code}
                    uploadProgress={uploadProgress}
                    removingAssetId={removingAssetId}
                    savingTimer={savingTimer === code}
                    onUpload={handleUpload}
                    onRemove={handleRemove}
                    onTimerDraftChange={(value) => setTimerDraft((prev) => ({ ...prev, [code]: value }))}
                    onSaveTimer={handleSaveTimer}
                  />
                );
              })}
            </div>
          </div>
        )}
      </SettingsSection>

      <div className="flex items-center justify-between pt-2">
        <Button asChild variant="ghost" size="sm" startIcon={<ArrowLeft className="h-4 w-4" />}>
          <Link href={`/admin/content/listening/${paperId}/structure`}>Back to Structure</Link>
        </Button>
        <Button asChild variant="primary" size="sm" endIcon={<ArrowRight className="h-4 w-4" />}>
          <Link href={`/admin/content/listening/${paperId}/questions`}>Next: Questions</Link>
        </Button>
      </div>

      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}
    </AdminSettingsLayout>
  );
}

function StatusTile({ label, value, tone }: { label: string; value: string; tone: 'success' | 'warning' | 'danger' | 'info' }) {
  const badgeLabel = tone === 'success' ? 'Ready' : tone === 'warning' ? 'Check' : tone === 'danger' ? 'Blocked' : 'Info';
  return (
    <div className="rounded-admin-lg border border-admin-border bg-admin-bg-surface px-4 py-3">
      <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">{label}</p>
      <div className="mt-2 flex items-center justify-between gap-3">
        <p className="text-2xl font-semibold text-admin-fg-strong tabular-nums">{value}</p>
        <Badge variant={tone}>{badgeLabel}</Badge>
      </div>
    </div>
  );
}

function AudioSlotCard({
  code,
  label,
  assets,
  primaryAsset,
  timerValue,
  timerDraft,
  uploading,
  uploadProgress,
  removingAssetId,
  savingTimer,
  onUpload,
  onRemove,
  onTimerDraftChange,
  onSaveTimer,
}: {
  code: AudioSectionCode;
  label?: string;
  assets: ContentPaperAssetDto[];
  primaryAsset: ContentPaperAssetDto | null;
  timerValue: number | null;
  timerDraft: string;
  uploading: boolean;
  uploadProgress: number | null;
  removingAssetId: string | null;
  savingTimer: boolean;
  onUpload: (code: AudioSectionCode, file: File) => Promise<void>;
  onRemove: (assetId: string) => Promise<void>;
  onTimerDraftChange: (value: string) => void;
  onSaveTimer: (code: AudioSectionCode) => Promise<void>;
}) {
  const inputId = `listening-audio-${code.toLowerCase()}`;
  const extraAssets = assets.filter((a) => a.id !== primaryAsset?.id);
  const hasTimer = (timerValue ?? 0) > 0;
  const ready = Boolean(primaryAsset) && hasTimer;
  const timerDirty = (timerDraft.trim() === '' ? null : Number(timerDraft.trim())) !== (timerValue ?? null);

  async function handleFileChange(event: ChangeEvent<HTMLInputElement>): Promise<void> {
    const file = event.currentTarget.files?.[0];
    if (!file) return;
    const input = event.currentTarget;
    await onUpload(code, file);
    input.value = '';
  }

  return (
    <Card>
      <CardHeader>
        <div className="min-w-0">
          <CardTitle className="flex items-center gap-2">
            <Headphones className="h-4 w-4 text-admin-fg-muted" />
            {label ?? `Section ${code}`}
          </CardTitle>
          <CardDescription>Audio file + countdown timer</CardDescription>
        </div>
        <CardAction>
          {ready ? (
            <Badge variant="success" startIcon={<CheckCircle2 className="h-3.5 w-3.5" />}>Ready</Badge>
          ) : (
            <Badge variant="warning" startIcon={<AlertTriangle className="h-3.5 w-3.5" />}>Incomplete</Badge>
          )}
        </CardAction>
      </CardHeader>
      <CardContent className="space-y-4">
        {primaryAsset ? (
          <AssetSummary
            asset={primaryAsset}
            removing={removingAssetId === primaryAsset.id}
            onRemove={onRemove}
          />
        ) : (
          <div className="rounded-admin-lg border border-dashed border-admin-border bg-admin-bg-subtle px-4 py-5 text-sm text-admin-fg-muted">
            No audio uploaded for {code} yet.
          </div>
        )}

        {extraAssets.length > 0 ? (
          <div className="space-y-2">
            <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Additional audio</p>
            {extraAssets.map((asset) => (
              <AssetSummary
                key={asset.id}
                asset={asset}
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
            accept={AUDIO_ACCEPT}
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
              {uploading ? 'Uploading...' : primaryAsset ? 'Replace audio' : 'Upload audio'}
            </label>
          </Button>
          {uploading && uploadProgress !== null ? (
            <div className="h-2 overflow-hidden rounded-full bg-admin-bg-subtle" aria-label={`${code} upload progress`}>
              <div className="h-full bg-[var(--admin-primary)] transition-[width]" style={{ width: `${Math.round(uploadProgress * 100)}%` }} />
            </div>
          ) : null}
        </div>

        <div className="space-y-2 border-t border-admin-border pt-4">
          <label htmlFor={`${inputId}-timer`} className="flex items-center gap-1.5 text-sm font-semibold text-admin-fg-strong">
            <Timer className="h-4 w-4 text-admin-fg-muted" />
            Countdown timer (seconds)
          </label>
          <div className="flex items-end gap-2">
            <input
              id={`${inputId}-timer`}
              type="number"
              min={0}
              inputMode="numeric"
              value={timerDraft}
              onChange={(e) => onTimerDraftChange(e.target.value)}
              placeholder="e.g. 300"
              className="w-32 rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong focus:outline-none focus:ring-2 focus:ring-[var(--admin-primary)]"
            />
            <Button
              variant="primary"
              size="sm"
              onClick={() => void onSaveTimer(code)}
              loading={savingTimer}
              loadingText="Saving…"
              disabled={!timerDirty}
              startIcon={<Save className="h-3.5 w-3.5" />}
            >
              Save timer
            </Button>
            {hasTimer ? (
              <Badge variant="info" className="mb-1.5">{timerValue}s set</Badge>
            ) : (
              <Badge variant="muted" className="mb-1.5">No timer</Badge>
            )}
          </div>
        </div>
      </CardContent>
    </Card>
  );
}

function AssetSummary({
  asset,
  removing,
  onRemove,
}: {
  asset: ContentPaperAssetDto;
  removing: boolean;
  onRemove: (assetId: string) => Promise<void>;
}) {
  const filename = asset.media?.originalFilename ?? asset.title ?? 'Audio file';
  const size = asset.media ? formatBytes(asset.media.sizeBytes) : null;
  const duration = asset.media?.durationSeconds != null
    ? `${Math.round(asset.media.durationSeconds)}s`
    : null;

  return (
    <div className="flex items-start justify-between gap-3 rounded-admin-lg border border-admin-border bg-admin-bg-subtle px-3 py-3">
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={asset.isPrimary ? 'success' : 'muted'}>{asset.isPrimary ? 'Primary audio' : 'Secondary'}</Badge>
          {asset.part ? <Badge variant="outline">{asset.part}</Badge> : null}
          <Badge variant="success" startIcon={<CheckCircle2 className="h-3.5 w-3.5" />}>audio ready</Badge>
        </div>
        <p className="mt-2 truncate text-sm font-semibold text-admin-fg-strong">{filename}</p>
        <p className="mt-1 text-xs text-admin-fg-muted">
          {asset.media?.mimeType ?? 'audio'}{size ? ` · ${size}` : ''}{duration ? ` · ${duration}` : ''}
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
