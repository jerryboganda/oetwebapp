'use client';

import { useCallback, useState } from 'react';
import { Loader2, RefreshCw, UploadCloud, CheckCircle2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { uploadFileChunked, type PaperAssetRole } from '@/lib/mock-wizard/upload';
import { attachPaperAsset } from '@/lib/mock-wizard/api';

export interface UploadSlotProps {
  /** Paper to attach to once the upload completes. If null, only uploads. */
  paperId: string | null;
  role: PaperAssetRole;
  label: string;
  /** Comma-separated `accept` attribute, e.g. "audio/mpeg,application/pdf". */
  accept: string;
  hint?: string;
  /** Existing media asset id, if one is already attached. */
  currentAssetId?: string | null;
  onAttached?: (mediaAssetId: string) => void;
  /** When true, uploads only and lets the caller attach later. */
  deferAttach?: boolean;
  /** Cached media id — show "attached" without an attach call. */
  attachedAssetId?: string | null;
}

type UploadState =
  | { kind: 'idle' }
  | { kind: 'uploading'; progress: number; filename: string }
  | { kind: 'attaching'; filename: string }
  | { kind: 'done'; mediaAssetId: string; filename: string }
  | { kind: 'error'; message: string };

export function UploadSlot({
  paperId,
  role,
  label,
  accept,
  hint,
  currentAssetId,
  onAttached,
  deferAttach = false,
  attachedAssetId,
}: UploadSlotProps) {
  const [state, setState] = useState<UploadState>(
    attachedAssetId
      ? { kind: 'done', mediaAssetId: attachedAssetId, filename: 'previously uploaded' }
      : { kind: 'idle' },
  );
  const [pendingFile, setPendingFile] = useState<File | null>(null);

  const runUpload = useCallback(
    async (file: File) => {
      setPendingFile(file);
      setState({ kind: 'uploading', progress: 0, filename: file.name });
      try {
        const result = await uploadFileChunked(file, role, (pct) => {
          setState({ kind: 'uploading', progress: pct, filename: file.name });
        });
        if (deferAttach || !paperId) {
          setState({ kind: 'done', mediaAssetId: result.mediaAssetId, filename: file.name });
          onAttached?.(result.mediaAssetId);
          return;
        }
        setState({ kind: 'attaching', filename: file.name });
        await attachPaperAsset(paperId, {
          role,
          mediaAssetId: result.mediaAssetId,
          displayOrder: 0,
          makePrimary: true,
        });
        setState({ kind: 'done', mediaAssetId: result.mediaAssetId, filename: file.name });
        onAttached?.(result.mediaAssetId);
      } catch (err) {
        setState({
          kind: 'error',
          message: err instanceof Error ? err.message : 'Upload failed.',
        });
      }
    },
    [deferAttach, onAttached, paperId, role],
  );

  return (
    <div className="rounded-2xl border border-border bg-background-light p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-sm font-bold text-navy">{label}</p>
          <p className="text-xs text-muted">Role: {role}</p>
          {hint ? <p className="mt-1 text-xs text-muted">{hint}</p> : null}
        </div>
        {state.kind === 'done' ? (
          <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2 py-0.5 text-xs font-bold text-emerald-700">
            <CheckCircle2 className="h-3 w-3" /> Attached
          </span>
        ) : null}
      </div>

      <label className="mt-3 inline-flex cursor-pointer items-center gap-2 rounded-xl border border-border bg-surface px-3 py-2 text-sm font-bold text-navy hover:bg-background-light">
        <UploadCloud className="h-4 w-4" />
        <span>{state.kind === 'idle' ? 'Choose file' : 'Replace file'}</span>
        <input
          type="file"
          accept={accept}
          className="hidden"
          onChange={(e) => {
            const f = e.target.files?.[0];
            if (f) void runUpload(f);
            e.target.value = '';
          }}
        />
      </label>

      {state.kind === 'uploading' ? (
        <div className="mt-3">
          <div className="flex items-center gap-2 text-xs text-muted">
            <Loader2 className="h-3 w-3 animate-spin" />
            <span>Uploading {state.filename}…</span>
            <span className="ml-auto font-bold text-navy">
              {Math.round(state.progress * 100)}%
            </span>
          </div>
          <div className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-gray-200">
            <div
              className="h-full bg-primary transition-[width] duration-200"
              style={{ width: `${state.progress * 100}%` }}
            />
          </div>
        </div>
      ) : null}

      {state.kind === 'attaching' ? (
        <p className="mt-3 inline-flex items-center gap-2 text-xs text-muted">
          <Loader2 className="h-3 w-3 animate-spin" /> Attaching to paper…
        </p>
      ) : null}

      {state.kind === 'done' ? (
        <p className="mt-3 break-all text-xs text-muted">
          {state.filename} → media id <code>{state.mediaAssetId}</code>
        </p>
      ) : null}

      {state.kind === 'error' ? (
        <div className="mt-3 flex items-center justify-between gap-2 rounded-lg bg-red-50 px-3 py-2 text-xs text-red-700">
          <span>{state.message}</span>
          {pendingFile ? (
            <Button variant="outline" size="sm" onClick={() => void runUpload(pendingFile)}>
              <RefreshCw className="mr-1 h-3 w-3" /> Retry
            </Button>
          ) : null}
        </div>
      ) : null}

      {currentAssetId && state.kind === 'idle' ? (
        <p className="mt-2 text-xs text-muted">
          A {role} is already attached. Choose a new file to replace it (a new primary will be set).
        </p>
      ) : null}
    </div>
  );
}
