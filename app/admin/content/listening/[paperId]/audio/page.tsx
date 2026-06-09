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
  getPartAAudioMode,
  setListeningSubSectionTimer,
  setPartAAudioMode,
  LISTENING_SUB_SECTION_CODES,
  type ListeningPartAAudioMode,
  type ListeningSubSectionCode,
} from '@/lib/listening-authoring-api';

type ToastState = { message: string; variant: 'success' | 'error' };

const AUDIO_ACCEPT = 'audio/mpeg,audio/wav,audio/mp4,audio/ogg,.mp3,.wav,.m4a,.ogg';

/**
 * Sub-sections shown when Part A is in "single audio" mode: the A1 slot doubles
 * as the single Part A upload (it plays across both consultations) and A2 is
 * hidden. Part B/C are unaffected.
 */
const PART_A_SINGLE_CODES: readonly ListeningSubSectionCode[] = [
  'A1', 'B1', 'B2', 'B3', 'B4', 'B5', 'B6', 'C1', 'C2',
];

function isListeningAudioAsset(asset: ContentPaperAssetDto): boolean {
  if (asset.role !== 'Audio') return false;
  const code = (asset.part ?? '').toUpperCase();
  return (LISTENING_SUB_SECTION_CODES as readonly string[]).includes(code);
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

function primaryFor(assets: ContentPaperAssetDto[], code: ListeningSubSectionCode): ContentPaperAssetDto | null {
  const matches = assets.filter((a) => a.role === 'Audio' && (a.part ?? '').toUpperCase() === code);
  return matches.find((a) => a.isPrimary) ?? matches[0] ?? null;
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
  const [uploadingCode, setUploadingCode] = useState<ListeningSubSectionCode | null>(null);
  const [uploadProgress, setUploadProgress] = useState<number | null>(null);
  const [removingAssetId, setRemovingAssetId] = useState<string | null>(null);
  const [savingTimer, setSavingTimer] = useState<ListeningSubSectionCode | null>(null);
  const [toast, setToast] = useState<ToastState | null>(null);
  const [mode, setMode] = useState<ListeningPartAAudioMode>('per_subsection');
  const [modeSaving, setModeSaving] = useState(false);

  const load = useCallback(async () => {
    if (!paperId) return;
    setLoading(true);
    setError(null);
    try {
      const [paperData, extractList, modeResult] = await Promise.all([
        getContentPaper(paperId),
        getListeningExtracts(paperId).catch(() => ({ extracts: [] })),
        getPartAAudioMode(paperId).catch(() => ({ mode: 'per_subsection' as const })),
      ]);
      setPaper(paperData);
      setMode(modeResult.mode);
      const nextTimers: Record<string, number | null> = {};
      const nextDraft: Record<string, string> = {};
      for (const extract of extractList.extracts) {
        const code = String(extract.partCode).toUpperCase();
        nextTimers[code] = extract.timeLimitSeconds ?? null;
        nextDraft[code] = extract.timeLimitSeconds != null ? String(extract.timeLimitSeconds) : '';
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

  // Sub-sections to show: single Part A mode collapses A1+A2 into the A1 slot
  // (which doubles as the single Part A upload) and hides A2.
  const activeCodes = useMemo<readonly ListeningSubSectionCode[]>(
    () => (mode === 'single' ? PART_A_SINGLE_CODES : LISTENING_SUB_SECTION_CODES),
    [mode],
  );

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

  async function handleSetMode(next: ListeningPartAAudioMode): Promise<void> {
    if (next === mode || modeSaving) return;
    setModeSaving(true);
    try {
      const result = await setPartAAudioMode(paperId, next);
      setMode(result.mode);
      setToast({
        message: result.mode === 'single'
          ? 'Part A now uses a single audio across both consultations.'
          : 'Part A now uses separate A1 / A2 audio.',
        variant: 'success',
      });
    } catch (err) {
      setToast({ message: errorMessage(err, 'Failed to update Part A audio mode'), variant: 'error' });
    } finally {
      setModeSaving(false);
    }
  }

  async function handleUpload(code: ListeningSubSectionCode, file: File): Promise<void> {
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
        displayOrder: LISTENING_SUB_SECTION_CODES.indexOf(code) + 1,
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

  async function handleSaveTimer(code: ListeningSubSectionCode): Promise<void> {
    const raw = (timerDraft[code] ?? '').trim();
    const parsed = raw === '' ? null : Number(raw);
    if (parsed != null && (!Number.isFinite(parsed) || parsed < 0 || !Number.isInteger(parsed))) {
      setToast({ message: 'Timer must be a whole number of seconds (or blank to clear).', variant: 'error' });
      return;
    }
    setSavingTimer(code);
    try {
      const result = await setListeningSubSectionTimer(paperId, code, parsed && parsed > 0 ? parsed : null);
      const updated = result.extracts.find((e) => String(e.partCode).toUpperCase() === code);
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
      description="Upload a separate audio file and set a countdown timer for each of the 10 listening sub-sections (A1, A2, B1–B6, C1, C2)."
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
        title="Part A audio mode"
        description="Choose whether Part A uses one audio across both consultations (single) or separate A1 / A2 files. Single mode plays the A1 upload for both consultations and hides the A2 slot."
      >
        <div className="flex flex-wrap items-center gap-2" role="radiogroup" aria-label="Part A audio mode">
          {([
            { value: 'single', label: 'Single audio (whole Part A)' },
            { value: 'per_subsection', label: 'Separate A1 / A2 audio' },
          ] as const).map((opt) => {
            const active = mode === opt.value;
            return (
              <button
                key={opt.value}
                type="button"
                role="radio"
                aria-checked={active}
                disabled={modeSaving || loading}
                onClick={() => void handleSetMode(opt.value)}
                className={`rounded-admin border px-4 py-2 text-sm font-semibold transition disabled:cursor-not-allowed disabled:opacity-60 ${
                  active
                    ? 'border-[var(--admin-primary)] bg-[var(--admin-primary)] text-white'
                    : 'border-admin-border bg-admin-bg-surface text-admin-fg-strong hover:border-admin-border-hover'
                }`}
              >
                {opt.label}
              </button>
            );
          })}
          {modeSaving ? <Loader2 className="h-4 w-4 animate-spin text-admin-fg-muted" aria-hidden="true" /> : null}
        </div>
      </SettingsSection>

      <SettingsSection
        title="Sub-section audio + countdown"
        description="Each sub-section is independent: its own uploaded audio file and its own per-sub-section countdown (seconds). A sub-section is ready once both are set."
      >
        {loading ? (
          <div className="grid gap-4 lg:grid-cols-2">
            {LISTENING_SUB_SECTION_CODES.map((code) => (
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
              <StatusTile label="Sub-sections" value={String(activeCodes.length)} tone="info" />
            </div>

            <div className="grid gap-4 lg:grid-cols-2">
              {activeCodes.map((code) => {
                const codeAssets = audioAssets.filter((a) => (a.part ?? '').toUpperCase() === code);
                const primaryAsset = primaryFor(audioAssets, code);
                const label = mode === 'single' && code === 'A1'
                  ? 'Part A (single audio — plays across A1 + A2)'
                  : undefined;
                return (
                  <AudioSlotCard
                    key={code}
                    code={code}
                    label={label}
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
  code: ListeningSubSectionCode;
  label?: string;
  assets: ContentPaperAssetDto[];
  primaryAsset: ContentPaperAssetDto | null;
  timerValue: number | null;
  timerDraft: string;
  uploading: boolean;
  uploadProgress: number | null;
  removingAssetId: string | null;
  savingTimer: boolean;
  onUpload: (code: ListeningSubSectionCode, file: File) => Promise<void>;
  onRemove: (assetId: string) => Promise<void>;
  onTimerDraftChange: (value: string) => void;
  onSaveTimer: (code: ListeningSubSectionCode) => Promise<void>;
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
            {label ?? `Sub-section ${code}`}
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
