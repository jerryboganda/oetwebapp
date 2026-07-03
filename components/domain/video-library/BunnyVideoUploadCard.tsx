'use client';

/**
 * Direct browser → Bunny Stream TUS upload card for the video wizard.
 *
 * Drives the pure `uploadCardReducer` state machine:
 *   idle → requesting-session → uploading → uploaded → polling-encode
 *        → ready | encode-failed
 * The video file NEVER passes through our backend — we only mint a presigned
 * authorization (`upload-authorization`), push bytes straight to Bunny via
 * tus, then poll our API while Bunny transcodes. `onChanged` is invoked on
 * persisted transitions so the wizard refreshes its entity snapshot.
 */

import { useCallback, useEffect, useReducer, useRef, useState } from 'react';
import { Loader2, Pause, Play, RefreshCw, UploadCloud, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/admin/ui/alert-dialog';
import { buttonVariants } from '@/components/admin/ui/button';
import { createBunnyUpload, type BunnyUploadHandle } from '@/lib/video/bunny-tus-upload';
import {
  adminGetVideo,
  adminRefreshVideoStatus,
  adminRequestUploadAuthorization,
  adminResetVideoUpload,
  type AdminVideoDetail,
} from '@/lib/api/video-library';
import { initialUploadCardState, uploadCardReducer } from './upload-card-reducer';
import { EncodeStatusBadge } from './EncodeStatusBadge';

export const VIDEO_FILE_ACCEPT = 'video/mp4,video/quicktime,video/webm,video/x-matroska';
export const MAX_VIDEO_BYTES = 5 * 1024 * 1024 * 1024; // 5 GB client-side cap

const POLL_INTERVAL_MS = 5_000;
const POLL_BACKOFF_AFTER_MS = 2 * 60_000;
const POLL_BACKOFF_INTERVAL_MS = 15_000;

export interface BunnyVideoUploadCardProps {
  videoId: string;
  video: AdminVideoDetail;
  canWrite: boolean;
  /** Called after persisted transitions (upload registered, encode terminal, reset). */
  onChanged: () => void;
}

export function BunnyVideoUploadCard({ videoId, video, canWrite, onChanged }: BunnyVideoUploadCardProps) {
  const [state, dispatch] = useReducer(uploadCardReducer, video, initialUploadCardState);
  const [pickError, setPickError] = useState<string | null>(null);
  const [replaceOpen, setReplaceOpen] = useState(false);
  const [resetting, setResetting] = useState(false);

  const handleRef = useRef<BunnyUploadHandle | null>(null);
  const activeRef = useRef(true);
  const pollTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const pollStartRef = useRef(0);

  useEffect(() => {
    activeRef.current = true;
    return () => {
      activeRef.current = false;
      if (pollTimerRef.current) clearTimeout(pollTimerRef.current);
    };
  }, []);

  // Warn before leaving the page while bytes are in flight.
  const uploadingNow =
    state.kind === 'requesting-session' || (state.kind === 'uploading' && !state.paused);
  useEffect(() => {
    if (!uploadingNow) return;
    const guard = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = '';
    };
    window.addEventListener('beforeunload', guard);
    return () => window.removeEventListener('beforeunload', guard);
  }, [uploadingNow]);

  const stopPolling = useCallback(() => {
    if (pollTimerRef.current) {
      clearTimeout(pollTimerRef.current);
      pollTimerRef.current = null;
    }
  }, []);

  const pollEncode = useCallback(async () => {
    if (!activeRef.current) return;
    try {
      const detail = await adminGetVideo(videoId);
      if (!activeRef.current) return;
      dispatch({
        type: 'encode-status',
        status: detail.encodeStatus,
        progress: detail.encodeProgress,
        error: detail.encodeError,
      });
      if (detail.encodeStatus === 'ready' || detail.encodeStatus === 'failed') {
        stopPolling();
        onChanged();
        return;
      }
    } catch {
      // Transient poll failure — keep the cadence and try again.
    }
    const elapsed = Date.now() - pollStartRef.current;
    const delay = elapsed > POLL_BACKOFF_AFTER_MS ? POLL_BACKOFF_INTERVAL_MS : POLL_INTERVAL_MS;
    pollTimerRef.current = setTimeout(() => void pollEncode(), delay);
  }, [videoId, onChanged, stopPolling]);

  const startPolling = useCallback(() => {
    stopPolling();
    pollStartRef.current = Date.now();
    pollTimerRef.current = setTimeout(() => void pollEncode(), POLL_INTERVAL_MS);
  }, [pollEncode, stopPolling]);

  // Hydrated mid-encode (queued/processing/encoding) — resume polling.
  useEffect(() => {
    if (state.kind === 'polling-encode' && !pollTimerRef.current) {
      startPolling();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleUploadSuccess = useCallback(async () => {
    if (!activeRef.current) return;
    dispatch({ type: 'upload-succeeded' });
    try {
      // Tell the backend the bytes are up so it snapshots Bunny's status.
      const detail = await adminRefreshVideoStatus(videoId);
      if (!activeRef.current) return;
      dispatch({
        type: 'encode-status',
        status: detail.encodeStatus,
        progress: detail.encodeProgress,
        error: detail.encodeError,
      });
      onChanged();
      if (detail.encodeStatus !== 'ready' && detail.encodeStatus !== 'failed') {
        startPolling();
      }
    } catch {
      if (!activeRef.current) return;
      // Refresh failed — assume Bunny queued it and let polling recover.
      dispatch({ type: 'encode-status', status: 'queued', progress: null });
      startPolling();
    }
  }, [videoId, onChanged, startPolling]);

  const beginUpload = useCallback(
    async (file: File) => {
      dispatch({ type: 'select-file', file });
      let session;
      try {
        session = await adminRequestUploadAuthorization(videoId);
      } catch (err) {
        if (!activeRef.current) return;
        dispatch({
          type: 'session-failed',
          message: err instanceof Error ? err.message : 'Could not authorize the upload.',
        });
        return;
      }
      if (!activeRef.current) return;

      const handle = createBunnyUpload(file, session, {
        onProgress: (sent, total) => {
          if (activeRef.current) dispatch({ type: 'progress', progress: total > 0 ? sent / total : 0 });
        },
        onSuccess: () => void handleUploadSuccess(),
        onError: (error) => {
          if (activeRef.current) {
            dispatch({ type: 'upload-failed', message: error.message || 'Upload failed.' });
          }
        },
        // Signature expired mid-upload → fresh authorization for the SAME bunnyVideoId.
        refreshSession: () => adminRequestUploadAuthorization(videoId),
      });
      handleRef.current = handle;
      dispatch({ type: 'session-granted' });
      await handle.start();
    },
    [videoId, handleUploadSuccess],
  );

  function handlePick(file: File) {
    setPickError(null);
    const acceptedTypes = VIDEO_FILE_ACCEPT.split(',');
    if (file.type && !acceptedTypes.includes(file.type)) {
      setPickError('Unsupported file type. Use MP4, MOV, WebM or MKV.');
      return;
    }
    if (file.size > MAX_VIDEO_BYTES) {
      setPickError('File is larger than the 5 GB limit.');
      return;
    }
    void beginUpload(file);
  }

  function handlePause() {
    dispatch({ type: 'pause' });
    void handleRef.current?.pause();
  }

  function handleResume() {
    dispatch({ type: 'resume' });
    handleRef.current?.resume();
  }

  function handleCancel() {
    void handleRef.current?.abort();
    handleRef.current = null;
    dispatch({ type: 'reset' });
  }

  function handleRetry() {
    const file = state.kind === 'session-error' || state.kind === 'upload-error' ? state.file : null;
    dispatch({ type: 'retry' });
    if (file) void beginUpload(file);
  }

  async function handleReplaceConfirm() {
    setResetting(true);
    setPickError(null);
    try {
      await adminResetVideoUpload(videoId);
      stopPolling();
      dispatch({ type: 'reset' });
      onChanged();
    } catch (err) {
      setPickError(err instanceof Error ? err.message : 'Could not reset the video upload.');
    } finally {
      setResetting(false);
      setReplaceOpen(false);
    }
  }

  const showPicker = state.kind === 'idle';
  const showReplace = state.kind === 'ready' || state.kind === 'encode-failed';

  return (
    <div className="rounded-2xl border border-border bg-background-light p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-sm font-bold text-navy">Video file</p>
          <p className="text-xs text-muted">
            Uploads go directly from your browser to Bunny Stream (MP4, MOV, WebM or MKV — max 5 GB).
          </p>
        </div>
        <EncodeStatusBadge status={video.encodeStatus} />
      </div>

      {pickError ? (
        <div className="mt-3">
          <InlineAlert variant="error">{pickError}</InlineAlert>
        </div>
      ) : null}

      {showPicker ? (
        <label
          className={`mt-3 inline-flex items-center gap-2 rounded-xl border border-border bg-surface px-3 py-2 text-sm font-bold text-navy ${canWrite ? 'cursor-pointer hover:bg-background-light' : 'cursor-not-allowed opacity-60'}`}
        >
          <UploadCloud className="h-4 w-4" />
          <span>Choose video file</span>
          <input
            type="file"
            accept={VIDEO_FILE_ACCEPT}
            className="hidden"
            disabled={!canWrite}
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) handlePick(f);
              e.target.value = '';
            }}
          />
        </label>
      ) : null}

      {state.kind === 'requesting-session' ? (
        <p className="mt-3 inline-flex items-center gap-2 text-xs text-muted">
          <Loader2 className="h-3 w-3 animate-spin" /> Authorizing upload for {state.file.name}…
        </p>
      ) : null}

      {state.kind === 'uploading' ? (
        <div className="mt-3 space-y-2">
          <div className="flex items-center gap-2 text-xs text-muted">
            {state.paused ? <Pause className="h-3 w-3" /> : <Loader2 className="h-3 w-3 animate-spin" />}
            <span>
              {state.paused ? 'Paused' : 'Uploading'} {state.file.name}
            </span>
            <span className="ml-auto font-bold text-navy">{Math.round(state.progress * 100)}%</span>
          </div>
          <div className="h-1.5 w-full overflow-hidden rounded-full bg-muted">
            <div
              className="h-full bg-primary transition-[width] duration-200"
              style={{ width: `${state.progress * 100}%` }}
            />
          </div>
          <div className="flex items-center gap-2">
            {state.paused ? (
              <Button type="button" variant="outline" size="sm" onClick={handleResume}>
                <Play className="mr-1 h-3.5 w-3.5" /> Resume
              </Button>
            ) : (
              <Button type="button" variant="outline" size="sm" onClick={handlePause}>
                <Pause className="mr-1 h-3.5 w-3.5" /> Pause
              </Button>
            )}
            <Button type="button" variant="ghost" size="sm" onClick={handleCancel}>
              <X className="mr-1 h-3.5 w-3.5" /> Cancel
            </Button>
          </div>
        </div>
      ) : null}

      {state.kind === 'uploaded' ? (
        <p className="mt-3 inline-flex items-center gap-2 text-xs text-muted">
          <Loader2 className="h-3 w-3 animate-spin" /> Upload complete — registering with Bunny…
        </p>
      ) : null}

      {state.kind === 'polling-encode' ? (
        <div className="mt-3 space-y-1">
          <p className="inline-flex items-center gap-2 text-xs text-muted">
            <Loader2 className="h-3 w-3 animate-spin" />
            Bunny is transcoding ({state.status}
            {state.progress != null ? ` · ${Math.round(state.progress)}%` : ''}) — this can take a few
            minutes. You can keep working; the status updates automatically.
          </p>
        </div>
      ) : null}

      {state.kind === 'ready' ? (
        <div className="mt-3 space-y-1">
          <p className="text-xs text-emerald-700">
            Video is encoded and ready to stream
            {video.durationSeconds != null ? ` (${formatDuration(video.durationSeconds)})` : ''}.
          </p>
        </div>
      ) : null}

      {state.kind === 'encode-failed' ? (
        <div className="mt-3">
          <InlineAlert variant="error">
            Bunny could not encode this video{state.message ? `: ${state.message}` : '.'} Replace the file
            to try again.
          </InlineAlert>
        </div>
      ) : null}

      {state.kind === 'session-error' || state.kind === 'upload-error' ? (
        <div className="mt-3 flex items-center justify-between gap-2 rounded-lg bg-red-50 px-3 py-2 text-xs text-red-700">
          <span>{state.message}</span>
          <Button type="button" variant="outline" size="sm" onClick={handleRetry}>
            <RefreshCw className="mr-1 h-3 w-3" /> Retry
          </Button>
        </div>
      ) : null}

      {showReplace && canWrite ? (
        <div className="mt-4 border-t border-border pt-3">
          <Button type="button" variant="outline" size="sm" onClick={() => setReplaceOpen(true)}>
            <UploadCloud className="mr-1 h-3.5 w-3.5" /> Replace video
          </Button>
        </div>
      ) : null}

      <AlertDialog open={replaceOpen} onOpenChange={setReplaceOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Replace this video?</AlertDialogTitle>
            <AlertDialogDescription>
              The current Bunny video and its encoded renditions will be discarded and you will upload a
              new file from scratch. Learners lose access to the old stream immediately. This cannot be
              undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={resetting}>Cancel</AlertDialogCancel>
            <AlertDialogAction
              className={buttonVariants({ variant: 'destructive' })}
              disabled={resetting}
              onClick={(e) => {
                e.preventDefault();
                void handleReplaceConfirm();
              }}
            >
              {resetting ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : null}
              Replace video
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

function formatDuration(totalSeconds: number): string {
  const s = Math.max(0, Math.round(totalSeconds));
  const m = Math.floor(s / 60);
  const rest = s % 60;
  return `${m}:${String(rest).padStart(2, '0')}`;
}
